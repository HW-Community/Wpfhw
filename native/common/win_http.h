#pragma once

#include <cstdint>
#include <string>
#include <vector>

struct HttpResponse {
    int status = 0;
    std::string body;
    std::string error;
    std::int64_t content_length = -1;
    bool accept_ranges = false;
};

HttpResponse http_get(const std::string& url, const std::string& user_agent);
bool http_probe_length(const std::string& url, const std::string& user_agent,
                       std::int64_t& content_length, bool& accept_ranges, std::string& error);
bool http_download_range(const std::string& url, const std::string& user_agent,
                         std::int64_t start, std::int64_t end_inclusive,
                         const std::wstring& file_path, std::int64_t file_offset,
                         std::string& error);
bool http_download_file(const std::string& url, const std::string& user_agent,
                        const std::wstring& file_path, std::string& error);
std::wstring utf8_to_wide(const std::string& utf8);
std::string url_encode(const std::string& value);
