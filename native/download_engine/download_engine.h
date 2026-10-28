#pragma once

#ifdef DOWNLOAD_ENGINE_EXPORTS
#define DOWNLOAD_ENGINE_API __declspec(dllexport)
#else
#define DOWNLOAD_ENGINE_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

DOWNLOAD_ENGINE_API int __cdecl DownloadFile(const char* url, const char* save_path);

#ifdef __cplusplus
}
#endif
