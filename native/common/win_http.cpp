#include "win_http.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <winhttp.h>

#include <algorithm>
#include <cstdio>
#include <sstream>
#include <vector>

#pragma comment(lib, "winhttp.lib")

namespace {

constexpr DWORD kConnectTimeoutMs = 15000;
constexpr DWORD kSendTimeoutMs = 15000;
constexpr DWORD kReceiveTimeoutMs = 60000;
constexpr DWORD kReadBufferSize = 64 * 1024;

std::string last_error_message(DWORD code)
{
    std::ostringstream oss;
    oss << "WinHTTP error " << code;
    return oss.str();
}

struct UrlParts {
    bool https = true;
    std::wstring host;
    INTERNET_PORT port = INTERNET_DEFAULT_HTTPS_PORT;
    std::wstring path;
};

struct WinHttpSession {
    HINTERNET session = nullptr;
    HINTERNET connect = nullptr;
    HINTERNET request = nullptr;

    ~WinHttpSession()
    {
        close();
    }

    void close()
    {
        if (request) {
            WinHttpCloseHandle(request);
            request = nullptr;
        }
        if (connect) {
            WinHttpCloseHandle(connect);
            connect = nullptr;
        }
        if (session) {
            WinHttpCloseHandle(session);
            session = nullptr;
        }
    }
};

bool crack_url(const std::string& url, UrlParts& out, std::string& error)
{
    const std::wstring wide = utf8_to_wide(url);
    URL_COMPONENTS uc{};
    uc.dwStructSize = sizeof(uc);
    wchar_t scheme[16];
    wchar_t host[256];
    wchar_t path[4096];
    wchar_t extra[2048];
    uc.lpszScheme = scheme;
    uc.dwSchemeLength = 16;
    uc.lpszHostName = host;
    uc.dwHostNameLength = 256;
    uc.lpszUrlPath = path;
    uc.dwUrlPathLength = 4096;
    uc.lpszExtraInfo = extra;
    uc.dwExtraInfoLength = 2048;
    if (!WinHttpCrackUrl(wide.c_str(), 0, 0, &uc)) {
        error = last_error_message(GetLastError());
        return false;
    }
    out.https = (uc.nScheme == INTERNET_SCHEME_HTTPS);
    out.host.assign(uc.lpszHostName, uc.dwHostNameLength);
    out.port = uc.nPort;
    out.path.assign(uc.lpszUrlPath, uc.dwUrlPathLength);
    if (uc.dwExtraInfoLength > 0) {
        out.path.append(uc.lpszExtraInfo, uc.dwExtraInfoLength);
    }
    if (out.path.empty()) {
        out.path = L"/";
    }
    return true;
}

bool open_request(WinHttpSession& http, const UrlParts& url, const std::string& user_agent,
                  const wchar_t* method, std::string& error)
{
    const std::wstring ua = utf8_to_wide(user_agent);
    http.session = WinHttpOpen(ua.c_str(), WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                               WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!http.session) {
        error = last_error_message(GetLastError());
        return false;
    }
    WinHttpSetTimeouts(http.session, kConnectTimeoutMs, kConnectTimeoutMs, kSendTimeoutMs,
                       kReceiveTimeoutMs);
    http.connect = WinHttpConnect(http.session, url.host.c_str(), url.port, 0);
    if (!http.connect) {
        error = last_error_message(GetLastError());
        return false;
    }
    const DWORD flags = url.https ? WINHTTP_FLAG_SECURE : 0;
    http.request = WinHttpOpenRequest(http.connect, method, url.path.c_str(), nullptr,
                                      WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!http.request) {
        error = last_error_message(GetLastError());
        return false;
    }
    return true;
}

bool query_status(HINTERNET request, int& status, std::string& error)
{
    DWORD code = 0;
    DWORD size = sizeof(code);
    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                             WINHTTP_HEADER_NAME_BY_INDEX, &code, &size, WINHTTP_NO_HEADER_INDEX)) {
        error = last_error_message(GetLastError());
        return false;
    }
    status = static_cast<int>(code);
    return true;
}

std::wstring query_header(HINTERNET request, DWORD header_id)
{
    DWORD size = 0;
    WinHttpQueryHeaders(request, header_id, WINHTTP_HEADER_NAME_BY_INDEX, nullptr, &size,
                        WINHTTP_NO_HEADER_INDEX);
    if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || size == 0) {
        return {};
    }
    std::wstring value(size / sizeof(wchar_t), L'\0');
    if (!WinHttpQueryHeaders(request, header_id, WINHTTP_HEADER_NAME_BY_INDEX, value.data(), &size,
                             WINHTTP_NO_HEADER_INDEX)) {
        return {};
    }
    while (!value.empty() && (value.back() == L'\0' || value.back() == L'\r' || value.back() == L'\n')) {
        value.pop_back();
    }
    return value;
}

std::int64_t parse_content_length(HINTERNET request)
{
    const std::wstring header = query_header(request, WINHTTP_QUERY_CONTENT_LENGTH);
    if (header.empty()) {
        return -1;
    }
    return _wtoi64(header.c_str());
}

std::int64_t parse_content_range_total(const std::wstring& header)
{
    const auto slash = header.find(L'/');
    if (slash == std::wstring::npos || slash + 1 >= header.size()) {
        return -1;
    }
    const std::wstring total = header.substr(slash + 1);
    if (total == L"*") {
        return -1;
    }
    return _wtoi64(total.c_str());
}

bool read_body(HINTERNET request, std::string& body, std::string& error)
{
    std::string buffer;
    DWORD available = 0;
    do {
        if (!WinHttpQueryDataAvailable(request, &available)) {
            error = last_error_message(GetLastError());
            return false;
        }
        if (available == 0) {
            break;
        }
        const size_t offset = buffer.size();
        buffer.resize(offset + available);
        DWORD read = 0;
        if (!WinHttpReadData(request, buffer.data() + offset, available, &read)) {
            error = last_error_message(GetLastError());
            return false;
        }
        buffer.resize(offset + read);
    } while (available > 0);
    body.swap(buffer);
    return true;
}

bool write_all(HANDLE file, const char* data, DWORD size)
{
    DWORD written = 0;
    while (written < size) {
        DWORD chunk = 0;
        if (!WriteFile(file, data + written, size - written, &chunk, nullptr)) {
            return false;
        }
        if (chunk == 0) {
            return false;
        }
        written += chunk;
    }
    return true;
}

bool send_and_receive(HINTERNET request, std::string& error)
{
    if (!WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0,
                            0) ||
        !WinHttpReceiveResponse(request, nullptr)) {
        error = last_error_message(GetLastError());
        return false;
    }
    return true;
}

} // namespace

std::wstring utf8_to_wide(const std::string& utf8)
{
    if (utf8.empty()) {
        return {};
    }
    const int size = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()),
                                         nullptr, 0);
    std::wstring out(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), out.data(), size);
    return out;
}

std::string url_encode(const std::string& value)
{
    std::string out;
    out.reserve(value.size() * 3);
    for (unsigned char c : value) {
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
            c == '-' || c == '_' || c == '.' || c == '~') {
            out.push_back(static_cast<char>(c));
        } else {
            char buf[8];
            std::snprintf(buf, sizeof(buf), "%%%02X", c);
            out.append(buf);
        }
    }
    return out;
}

HttpResponse http_get(const std::string& url, const std::string& user_agent)
{
    HttpResponse response;
    UrlParts parts;
    if (!crack_url(url, parts, response.error)) {
        return response;
    }
    WinHttpSession http;
    if (!open_request(http, parts, user_agent, L"GET", response.error)) {
        return response;
    }
    if (!send_and_receive(http.request, response.error)) {
        return response;
    }
    if (!query_status(http.request, response.status, response.error)) {
        return response;
    }
    response.content_length = parse_content_length(http.request);
    const std::wstring accept = query_header(http.request, WINHTTP_QUERY_ACCEPT_RANGES);
    response.accept_ranges = (accept.find(L"bytes") != std::wstring::npos);
    read_body(http.request, response.body, response.error);
    return response;
}

bool http_probe_length(const std::string& url, const std::string& user_agent,
                       std::int64_t& content_length, bool& accept_ranges, std::string& error)
{
    content_length = -1;
    accept_ranges = false;
    UrlParts parts;
    if (!crack_url(url, parts, error)) {
        return false;
    }
    WinHttpSession http;
    if (!open_request(http, parts, user_agent, L"GET", error)) {
        return false;
    }
    if (!WinHttpAddRequestHeaders(http.request, L"Range: bytes=0-0", static_cast<DWORD>(-1),
                                  WINHTTP_ADDREQ_FLAG_ADD)) {
        error = last_error_message(GetLastError());
        return false;
    }
    if (!send_and_receive(http.request, error)) {
        return false;
    }
    int status = 0;
    if (!query_status(http.request, status, error)) {
        return false;
    }
    const std::wstring accept = query_header(http.request, WINHTTP_QUERY_ACCEPT_RANGES);
    const std::wstring range = query_header(http.request, WINHTTP_QUERY_CONTENT_RANGE);
    if (status == 206) {
        accept_ranges = true;
        content_length = parse_content_range_total(range);
        if (content_length < 0) {
            content_length = parse_content_length(http.request);
        }
        return content_length >= 0;
    }
    if (status >= 200 && status < 300) {
        content_length = parse_content_length(http.request);
        accept_ranges = (accept.find(L"bytes") != std::wstring::npos);
        return content_length >= 0;
    }
    error = "HTTP status " + std::to_string(status);
    return false;
}

bool http_download_range(const std::string& url, const std::string& user_agent,
                         std::int64_t start, std::int64_t end_inclusive,
                         const std::wstring& file_path, std::int64_t file_offset,
                         std::string& error)
{
    UrlParts parts;
    if (!crack_url(url, parts, error)) {
        return false;
    }
    WinHttpSession http;
    if (!open_request(http, parts, user_agent, L"GET", error)) {
        return false;
    }
    wchar_t range_header[128];
    _snwprintf_s(range_header, _TRUNCATE, L"Range: bytes=%lld-%lld", static_cast<long long>(start),
                 static_cast<long long>(end_inclusive));
    if (!WinHttpAddRequestHeaders(http.request, range_header, static_cast<DWORD>(-1),
                                  WINHTTP_ADDREQ_FLAG_ADD)) {
        error = last_error_message(GetLastError());
        return false;
    }
    if (!send_and_receive(http.request, error)) {
        return false;
    }
    int status = 0;
    if (!query_status(http.request, status, error)) {
        return false;
    }
    if (status != 206) {
        error = "HTTP status " + std::to_string(status);
        return false;
    }
    HANDLE file = CreateFileW(file_path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        error = "failed to open file";
        return false;
    }
    LARGE_INTEGER pos;
    pos.QuadPart = file_offset;
    if (!SetFilePointerEx(file, pos, nullptr, FILE_BEGIN)) {
        error = "failed to seek file";
        CloseHandle(file);
        return false;
    }
    std::vector<char> buffer(kReadBufferSize);
    DWORD available = 0;
    bool ok = true;
    do {
        if (!WinHttpQueryDataAvailable(http.request, &available)) {
            error = last_error_message(GetLastError());
            ok = false;
            break;
        }
        if (available == 0) {
            break;
        }
        const DWORD to_read = (std::min)(available, kReadBufferSize);
        DWORD read = 0;
        if (!WinHttpReadData(http.request, buffer.data(), to_read, &read)) {
            error = last_error_message(GetLastError());
            ok = false;
            break;
        }
        if (read == 0) {
            break;
        }
        if (!write_all(file, buffer.data(), read)) {
            error = "failed to write file";
            ok = false;
            break;
        }
    } while (available > 0);
    CloseHandle(file);
    return ok;
}

bool http_download_file(const std::string& url, const std::string& user_agent,
                        const std::wstring& file_path, std::string& error)
{
    UrlParts parts;
    if (!crack_url(url, parts, error)) {
        return false;
    }
    WinHttpSession http;
    if (!open_request(http, parts, user_agent, L"GET", error)) {
        return false;
    }
    if (!send_and_receive(http.request, error)) {
        return false;
    }
    int status = 0;
    if (!query_status(http.request, status, error)) {
        return false;
    }
    if (status < 200 || status >= 300) {
        error = "HTTP status " + std::to_string(status);
        return false;
    }
    HANDLE file = CreateFileW(file_path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        error = "failed to create file";
        return false;
    }
    std::vector<char> buffer(kReadBufferSize);
    DWORD available = 0;
    bool ok = true;
    do {
        if (!WinHttpQueryDataAvailable(http.request, &available)) {
            error = last_error_message(GetLastError());
            ok = false;
            break;
        }
        if (available == 0) {
            break;
        }
        const DWORD to_read = (std::min)(available, kReadBufferSize);
        DWORD read = 0;
        if (!WinHttpReadData(http.request, buffer.data(), to_read, &read)) {
            error = last_error_message(GetLastError());
            ok = false;
            break;
        }
        if (read == 0) {
            break;
        }
        if (!write_all(file, buffer.data(), read)) {
            error = "failed to write file";
            ok = false;
            break;
        }
    } while (available > 0);
    CloseHandle(file);
    return ok;
}
