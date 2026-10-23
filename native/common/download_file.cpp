#include "download_file.h"
#include "win_http.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <cstdint>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr const char* kUserAgent = "HW-Community/Wpfhw-cli/1.0.0";
constexpr int kMaxThreads = 8;
constexpr std::int64_t kMinChunkBytes = 256 * 1024;

bool preallocate_file(const std::wstring& path, std::int64_t size)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        return false;
    }
    LARGE_INTEGER li;
    li.QuadPart = size;
    if (!SetFilePointerEx(file, li, nullptr, FILE_BEGIN) || !SetEndOfFile(file)) {
        CloseHandle(file);
        return false;
    }
    CloseHandle(file);
    return true;
}

int thread_count_for(std::int64_t size)
{
    if (size <= kMinChunkBytes) {
        return 1;
    }
    int threads = static_cast<int>(size / kMinChunkBytes);
    if (threads < 2) {
        threads = 2;
    }
    if (threads > kMaxThreads) {
        threads = kMaxThreads;
    }
    return threads;
}

int download_single(const std::string& url, const std::wstring& path)
{
    std::string error;
    return http_download_file(url, kUserAgent, path, error) ? 0 : 1;
}

} // namespace

int DownloadFile(const char* url, const char* save_path)
{
    if (!url || !save_path || url[0] == '\0' || save_path[0] == '\0') {
        return 1;
    }
    const std::string url_str = url;
    const std::wstring path = utf8_to_wide(save_path);
    std::int64_t content_length = -1;
    bool accept_ranges = false;
    std::string error;
    if (!http_probe_length(url_str, kUserAgent, content_length, accept_ranges, error) ||
        content_length <= 0 || !accept_ranges) {
        return download_single(url_str, path);
    }
    const int threads = thread_count_for(content_length);
    if (threads <= 1) {
        return download_single(url_str, path);
    }
    if (!preallocate_file(path, content_length)) {
        return 1;
    }
    struct Chunk {
        std::int64_t start = 0;
        std::int64_t end = 0;
    };
    std::vector<Chunk> chunks(static_cast<size_t>(threads));
    const std::int64_t chunk_size = content_length / threads;
    for (int i = 0; i < threads; ++i) {
        chunks[i].start = static_cast<std::int64_t>(i) * chunk_size;
        chunks[i].end = (i == threads - 1) ? (content_length - 1)
                                           : (chunks[i].start + chunk_size - 1);
    }
    std::vector<std::thread> workers;
    std::vector<int> results(static_cast<size_t>(threads), 0);
    workers.reserve(static_cast<size_t>(threads));
    for (int i = 0; i < threads; ++i) {
        workers.emplace_back([&, i]() {
            std::string local_error;
            if (!http_download_range(url_str, kUserAgent, chunks[i].start, chunks[i].end, path,
                                     chunks[i].start, local_error)) {
                results[static_cast<size_t>(i)] = 1;
            }
        });
    }
    for (auto& worker : workers) {
        worker.join();
    }
    for (int result : results) {
        if (result != 0) {
            return 1;
        }
    }
    return 0;
}
