#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <string>

int run_tray(HINSTANCE instance, const std::wstring& main_exe_path);
