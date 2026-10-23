#include "cli.h"
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

constexpr const char* kUserAgent = "HW-Community/Wpfhw-cli/1.0.0";
constexpr const char* kApiBase = "https://api.modrinth.com/v2";

void print_usage()
{
    std::cout << "Wpfhw 1.0.0\n"
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
    const size_t count = (std::min)(hits.size(), static_cast<size_t>(5));
    for (size_t i = 0; i < count; ++i) {
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
    std::string lowered = name;
    std::transform(lowered.begin(), lowered.end(), lowered.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    for (const auto& hit : hits) {
        std::string slug = json_object_string(hit.raw, "slug");
        std::string title = json_object_string(hit.raw, "title");
        std::string slug_l = slug;
        std::string title_l = title;
        std::transform(slug_l.begin(), slug_l.end(), slug_l.begin(), [](unsigned char c) {
            return static_cast<char>(std::tolower(c));
        });
        std::transform(title_l.begin(), title_l.end(), title_l.begin(), [](unsigned char c) {
            return static_cast<char>(std::tolower(c));
        });
        if (slug_l == lowered || title_l == lowered) {
            return slug.empty() ? json_object_string(hit.raw, "project_id") : slug;
        }
    }
    const std::string slug = json_object_string(hits.front().raw, "slug");
    return slug.empty() ? json_object_string(hits.front().raw, "project_id") : slug;
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
    const auto& latest = versions.front().raw;
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

std::wstring desktop_path()
{
    PWSTR path = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Desktop, 0, nullptr, &path)) && path) {
        std::wstring result = path;
        CoTaskMemFree(path);
        return result;
    }
    wchar_t fallback[MAX_PATH];
    if (GetEnvironmentVariableW(L"USERPROFILE", fallback, MAX_PATH) > 0) {
        return std::wstring(fallback) + L"\\Desktop";
    }
    return L".";
}

std::string wide_to_utf8(const std::wstring& wide)
{
    if (wide.empty()) {
        return {};
    }
    const int size = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()),
                                         nullptr, 0, nullptr, nullptr);
    std::string out(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()), out.data(), size,
                        nullptr, nullptr);
    return out;
}

int cmd_download(const std::string& name)
{
    std::string project = name;
    const std::string probe_url = std::string(kApiBase) + "/project/" + url_encode(name);
    const HttpResponse probe = http_get(probe_url, kUserAgent);
    if (probe.status < 200 || probe.status >= 300) {
        std::cout << "resolving project name...\n";
        project = pick_project_slug(name);
        if (project.empty()) {
            std::cerr << "project not found: " << name << "\n";
            return 1;
        }
        std::cout << "using project: " << project << "\n";
    }
    const DownloadTarget target = pick_latest_file(project);
    if (target.url.empty() || target.filename.empty()) {
        std::cerr << "no downloadable file for " << project << "\n";
        return 1;
    }
    const std::wstring save_path = desktop_path() + L"\\" + utf8_to_wide(target.filename);
    std::cout << "latest version: " << (target.version.empty() ? "?" : target.version) << "\n";
    std::cout << "downloading " << target.filename << " ...\n";
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
