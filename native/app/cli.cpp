#include "cli.h"
#include "../common/app_info.h"
#include "../common/download_file.h"
#include "../common/json_mini.h"
#include "../common/win_http.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <iostream>
#include <string>
#include <vector>

namespace {

constexpr const char* kApiBase = "https://api.modrinth.com/v2";

void print_usage()
{
    std::cout << "Wpfhw " << kAppVersion << "\n"
              << "Usage:\n"
              << "  wpfhw.exe\n"
              << "  wpfhw.exe search <keyword>\n"
              << "  wpfhw.exe download <project>\n";
}

std::string format_downloads(std::int64_t count)
{
    char buf[64];
    if (count >= 100000000) {
        std::snprintf(buf, sizeof(buf), "%.1f亿", count / 100000000.0);
    } else if (count >= 10000) {
        std::snprintf(buf, sizeof(buf), "%.1f万", count / 10000.0);
    } else if (count >= 1000) {
        std::snprintf(buf, sizeof(buf), "%.1f千", count / 1000.0);
    } else {
        std::snprintf(buf, sizeof(buf), "%lld", static_cast<long long>(count));
    }
    return buf;
}

std::string to_lower(std::string s)
{
    std::transform(s.begin(), s.end(), s.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return s;
}

int cmd_search(const std::string& keyword)
{
    const std::string url = std::string(kApiBase) + "/search?query=" + url_encode(keyword) +
                            "&limit=5&index=downloads";
    const HttpResponse response = http_get(url, kUserAgent);
    if (response.status == 0) {
        std::cerr << "search failed: " << response.error << "\n";
        return 1;
    }
    if (response.status < 200 || response.status >= 300) {
        std::cerr << "search HTTP " << response.status << "\n";
        return 1;
    }
    const auto hits = json_object_array_objects(response.body, "hits");
    if (hits.empty()) {
        std::cout << "no results\n";
        return 0;
    }
    for (size_t i = 0; i < hits.size(); ++i) {
        const auto& hit = hits[i].raw;
        const std::string title = json_object_string(hit, "title");
        const std::string slug = json_object_string(hit, "slug");
        const std::int64_t downloads = json_object_int64(hit, "downloads");
        std::cout << (i + 1) << ". " << title;
        if (!slug.empty()) {
            std::cout << "  [" << slug << "]";
        }
        std::cout << "  downloads=" << format_downloads(downloads) << " (" << downloads << ")\n";
    }
    return 0;
}

std::string project_id_from_hit(std::string_view hit)
{
    std::string slug = json_object_string(hit, "slug");
    if (!slug.empty()) {
        return slug;
    }
    return json_object_string(hit, "project_id");
}

std::string pick_project_slug(const std::string& name)
{
    const std::string url = std::string(kApiBase) + "/search?query=" + url_encode(name) +
                            "&limit=5&index=relevance";
    const HttpResponse response = http_get(url, kUserAgent);
    if (response.status < 200 || response.status >= 300) {
        return {};
    }
    const auto hits = json_object_array_objects(response.body, "hits");
    if (hits.empty()) {
        return {};
    }
    const std::string lowered = to_lower(name);
    for (const auto& hit : hits) {
        const std::string slug = json_object_string(hit.raw, "slug");
        const std::string title = json_object_string(hit.raw, "title");
        if (to_lower(slug) == lowered || to_lower(title) == lowered) {
            const std::string id = project_id_from_hit(hit.raw);
            if (!id.empty()) {
                return id;
            }
        }
    }
    return project_id_from_hit(hits.front().raw);
}

struct DownloadTarget {
    std::string url;
    std::string filename;
    std::string version;
};

DownloadTarget pick_latest_file(const std::string& project)
{
    const std::string url = std::string(kApiBase) + "/project/" + url_encode(project) + "/version";
    const HttpResponse response = http_get(url, kUserAgent);
    DownloadTarget target;
    if (response.status < 200 || response.status >= 300) {
        return target;
    }
    const auto versions = json_root_array_objects(response.body);
    if (versions.empty()) {
        return target;
    }
    size_t latest_index = 0;
    std::string latest_date = json_object_string(versions.front().raw, "date_published");
    for (size_t i = 1; i < versions.size(); ++i) {
        const std::string date = json_object_string(versions[i].raw, "date_published");
        if (date > latest_date) {
            latest_date = date;
            latest_index = i;
        }
    }
    const auto& latest = versions[latest_index].raw;
    target.version = json_object_string(latest, "version_number");
    const auto files = json_object_array_objects(latest, "files");
    if (files.empty()) {
        return target;
    }
    size_t chosen = 0;
    for (size_t i = 0; i < files.size(); ++i) {
        if (json_object_bool(files[i].raw, "primary", false)) {
            chosen = i;
            break;
        }
    }
    target.url = json_object_string(files[chosen].raw, "url");
    target.filename = json_object_string(files[chosen].raw, "filename");
    return target;
}

std::wstring known_folder(REFKNOWNFOLDERID id)
{
    PWSTR path = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &path)) && path) {
        std::wstring result = path;
        CoTaskMemFree(path);
        return result;
    }
    return {};
}

std::wstring desktop_path()
{
    std::wstring path = known_folder(FOLDERID_Desktop);
    if (!path.empty()) {
        return path;
    }
    path = known_folder(FOLDERID_Downloads);
    if (!path.empty()) {
        return path;
    }
    wchar_t temp[MAX_PATH];
    const DWORD n = GetTempPathW(MAX_PATH, temp);
    if (n > 0 && n < MAX_PATH) {
        return std::wstring(temp, n);
    }
    return {};
}

std::wstring safe_file_name(const std::string& name)
{
    std::wstring wide = utf8_to_wide(name);
    std::wstring out;
    out.reserve(wide.size());
    for (wchar_t c : wide) {
        if (c == L'\\' || c == L'/' || c == L':' || c == L'*' || c == L'?' || c == L'"' ||
            c == L'<' || c == L'>' || c == L'|' || c == L'\0') {
            continue;
        }
        out.push_back(c);
    }
    while (!out.empty() && (out.front() == L'.' || out.front() == L' ')) {
        out.erase(out.begin());
    }
    if (out.empty()) {
        return L"download.bin";
    }
    if (out.size() > 180) {
        out.resize(180);
    }
    return out;
}

bool url_is_https(const std::string& url)
{
    return url.size() >= 8 && url.compare(0, 8, "https://") == 0;
}

int cmd_download(const std::string& name)
{
    std::string project = pick_project_slug(name);
    if (project.empty()) {
        std::cerr << "project not found: " << name << "\n";
        return 1;
    }
    std::cout << "using project: " << project << "\n";
    const DownloadTarget target = pick_latest_file(project);
    if (target.url.empty() || target.filename.empty()) {
        std::cerr << "no downloadable file for " << project << "\n";
        return 1;
    }
    if (!url_is_https(target.url)) {
        std::cerr << "refusing non-https download url\n";
        return 1;
    }
    const std::wstring dir = desktop_path();
    if (dir.empty()) {
        std::cerr << "cannot resolve save directory\n";
        return 1;
    }
    const std::wstring save_path = dir + L"\\" + safe_file_name(target.filename);
    std::cout << "latest version: " << (target.version.empty() ? "?" : target.version) << "\n";
    std::cout << "downloading " << wide_to_utf8(safe_file_name(target.filename)) << " ...\n";
    if (DownloadFile(target.url.c_str(), wide_to_utf8(save_path).c_str()) != 0) {
        std::cerr << "download failed\n";
        return 1;
    }
    std::cout << "saved: " << wide_to_utf8(save_path) << "\n";
    return 0;
}

} // namespace

int run_cli(const std::vector<std::string>& args)
{
    if (args.empty()) {
        print_usage();
        return 0;
    }
    const std::string& cmd = args[0];
    if (cmd == "search") {
        if (args.size() < 2) {
            std::cerr << "missing keyword\n";
            print_usage();
            return 1;
        }
        std::string keyword = args[1];
        for (size_t i = 2; i < args.size(); ++i) {
            keyword.append(" ").append(args[i]);
        }
        return cmd_search(keyword);
    }
    if (cmd == "download") {
        if (args.size() < 2) {
            std::cerr << "missing project name\n";
            print_usage();
            return 1;
        }
        std::string name = args[1];
        for (size_t i = 2; i < args.size(); ++i) {
            name.append(" ").append(args[i]);
        }
        return cmd_download(name);
    }
    std::cerr << "unknown command: " << cmd << "\n";
    print_usage();
    return 1;
}
