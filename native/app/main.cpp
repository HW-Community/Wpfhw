#include "cli.h"
#include "tray.h"
#include "../common/win_http.h"

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

struct ComInit {
    bool ok = false;
    ComInit()
    {
        const HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        ok = SUCCEEDED(hr);
    }
    ~ComInit()
    {
        if (ok) {
            CoUninitialize();
        }
    }
};

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
    if (freopen_s(&fp, "CONOUT$", "w", stdout) != 0 ||
        freopen_s(&fp, "CONOUT$", "w", stderr) != 0 ||
        freopen_s(&fp, "CONIN$", "r", stdin) != 0) {
        return;
    }
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    ComInit com;
    const auto args = args_from_command_line();
    if (args.empty()) {
        return run_tray(instance);
    }
    attach_console();
    return run_cli(args);
}
