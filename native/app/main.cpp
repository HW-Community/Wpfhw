#include "cli.h"
#include "tray.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <shellapi.h>
#include <cstdio>
#include <string>
#include <vector>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")

namespace {

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

std::vector<std::string> args_from_command_line()
{
    int argc = 0;
    LPWSTR* argvw = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::vector<std::string> args;
    if (!argvw) {
        return args;
    }
    for (int i = 1; i < argc; ++i) {
        args.push_back(wide_to_utf8(argvw[i]));
    }
    LocalFree(argvw);
    return args;
}

void attach_console()
{
    if (!AttachConsole(ATTACH_PARENT_PROCESS)) {
        AllocConsole();
    }
    FILE* fp = nullptr;
    freopen_s(&fp, "CONOUT$", "w", stdout);
    freopen_s(&fp, "CONOUT$", "w", stderr);
    freopen_s(&fp, "CONIN$", "r", stdin);
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const auto args = args_from_command_line();
    if (args.empty()) {
        return run_tray(instance);
    }
    attach_console();
    const int code = run_cli(args);
    CoUninitialize();
    return code;
}
