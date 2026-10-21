#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE

#include <windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <dwmapi.h>

#include "resource.h"

#include <string>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "ole32.lib")

namespace {

constexpr UINT WM_TRAY = WM_APP + 1;
constexpr UINT ID_TRAY = 1;
constexpr UINT ID_OPEN_MAIN = 1001;
constexpr UINT ID_EXIT = 1002;
constexpr UINT IDM_OPEN = 1;
constexpr UINT IDM_EXIT = 2;

HWND g_hwnd = nullptr;
HWND g_menu_hwnd = nullptr;
HICON g_icon = nullptr;
NOTIFYICONDATAW g_nid{};
bool g_dark_theme = false;
int g_dpi = 96;

int scale(int px)
{
    return MulDiv(px, g_dpi, 96);
}

bool is_system_dark()
{
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER,
                      L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", 0,
                      KEY_READ, &key) != ERROR_SUCCESS) {
        return false;
    }
    DWORD value = 1;
    DWORD size = sizeof(value);
    const LSTATUS status =
        RegQueryValueExW(key, L"SystemUsesLightTheme", nullptr, nullptr,
                         reinterpret_cast<LPBYTE>(&value), &size);
    RegCloseKey(key);
    if (status != ERROR_SUCCESS) {
        return false;
    }
    return value == 0;
}

std::wstring module_dir()
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring full = path;
    const auto slash = full.find_last_of(L"\\/");
    if (slash == std::wstring::npos) {
        return L".";
    }
    return full.substr(0, slash);
}

HICON load_app_icon(HINSTANCE instance)
{
    HICON icon = LoadIconW(instance, MAKEINTRESOURCEW(IDI_APPICON));
    if (icon) {
        return icon;
    }
    const std::wstring dir = module_dir();
    const std::wstring ico = dir + L"\\logo.ico";
    icon = static_cast<HICON>(LoadImageW(nullptr, ico.c_str(), IMAGE_ICON, 0, 0,
                                         LR_LOADFROMFILE | LR_DEFAULTSIZE));
    if (icon) {
        return icon;
    }
    return LoadIconW(nullptr, IDI_APPLICATION);
}

void add_tray_icon(HWND hwnd)
{
    ZeroMemory(&g_nid, sizeof(g_nid));
    g_nid.cbSize = sizeof(g_nid);
    g_nid.hWnd = hwnd;
    g_nid.uID = ID_TRAY;
    g_nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_nid.uCallbackMessage = WM_TRAY;
    g_nid.hIcon = g_icon;
    lstrcpynW(g_nid.szTip, L"Wpfhw Tray", ARRAYSIZE(g_nid.szTip));
    Shell_NotifyIconW(NIM_ADD, &g_nid);
}

void remove_tray_icon()
{
    Shell_NotifyIconW(NIM_DELETE, &g_nid);
}

void open_main_window()
{
    HWND existing = FindWindowW(L"wpfhw.MainWindow", nullptr);
    if (!existing) {
        existing = FindWindowW(nullptr, L"Wpfhw Downloader");
    }
    const std::wstring dir = module_dir();
    const wchar_t* names[] = {L"\\wpfhw.exe", L"\\Wpfhw.exe", L"\\..\\wpfhw.exe"};
    if (existing) {
        ShowWindow(existing, SW_RESTORE);
        SetForegroundWindow(existing);
        return;
    }
    for (const wchar_t* name : names) {
        const std::wstring exe = dir + name;
        if (GetFileAttributesW(exe.c_str()) == INVALID_FILE_ATTRIBUTES) {
            continue;
        }
        ShellExecuteW(nullptr, L"open", exe.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
        return;
    }
    MessageBoxW(nullptr, L"未找到 Wpfhw 主程序。", L"Wpfhw Tray", MB_OK | MB_ICONINFORMATION);
}

COLORREF theme_bg()
{
    return g_dark_theme ? RGB(32, 32, 32) : RGB(248, 248, 248);
}

COLORREF theme_fg()
{
    return g_dark_theme ? RGB(245, 245, 245) : RGB(24, 24, 24);
}

COLORREF theme_hover()
{
    return g_dark_theme ? RGB(58, 58, 58) : RGB(232, 232, 232);
}

HRGN rounded_rgn(int w, int h, int r)
{
    return CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
}

int hit_test_item(int y)
{
    const int item_h = scale(36);
    const int pad = scale(8);
    if (y < pad) {
        return 0;
    }
    const int index = (y - pad) / item_h;
    if (index == 0) {
        return IDM_OPEN;
    }
    if (index == 1) {
        return IDM_EXIT;
    }
    return 0;
}

void paint_menu(HWND hwnd)
{
    PAINTSTRUCT ps;
    HDC hdc = BeginPaint(hwnd, &ps);
    RECT rc;
    GetClientRect(hwnd, &rc);
    HDC mem = CreateCompatibleDC(hdc);
    HBITMAP bmp = CreateCompatibleBitmap(hdc, rc.right, rc.bottom);
    HGDIOBJ old_bmp = SelectObject(mem, bmp);

    HBRUSH bg = CreateSolidBrush(theme_bg());
    FillRect(mem, &rc, bg);
    DeleteObject(bg);

    const int pad = scale(8);
    const int item_h = scale(36);
    POINT cursor;
    GetCursorPos(&cursor);
    ScreenToClient(hwnd, &cursor);
    const int hover = hit_test_item(cursor.y);

    HFONT font = CreateFontW(scale(16), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                             OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                             DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    HGDIOBJ old_font = SelectObject(mem, font);
    SetBkMode(mem, TRANSPARENT);
    SetTextColor(mem, theme_fg());

    auto draw_item = [&](int id, const wchar_t* text, int row) {
        RECT item{scale(6), pad + row * item_h, rc.right - scale(6), pad + (row + 1) * item_h};
        if (hover == id) {
            HBRUSH hover_br = CreateSolidBrush(theme_hover());
            const int rr = scale(10);
            HRGN item_rgn = CreateRoundRectRgn(item.left, item.top, item.right, item.bottom, rr, rr);
            FillRgn(mem, item_rgn, hover_br);
            DeleteObject(item_rgn);
            DeleteObject(hover_br);
        }
        RECT text_rc = item;
        text_rc.left += scale(14);
        DrawTextW(mem, text, -1, &text_rc, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    };
    draw_item(IDM_OPEN, L"打开主界面", 0);
    draw_item(IDM_EXIT, L"退出", 1);

    SelectObject(mem, old_font);
    DeleteObject(font);
    BitBlt(hdc, 0, 0, rc.right, rc.bottom, mem, 0, 0, SRCCOPY);
    SelectObject(mem, old_bmp);
    DeleteObject(bmp);
    DeleteDC(mem);
    EndPaint(hwnd, &ps);
}

void destroy_menu()
{
    if (g_menu_hwnd) {
        DestroyWindow(g_menu_hwnd);
        g_menu_hwnd = nullptr;
    }
}

LRESULT CALLBACK menu_proc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    switch (msg) {
    case WM_CREATE: {
        const int w = scale(176);
        const int h = scale(88);
        SetWindowPos(hwnd, nullptr, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER);
        HRGN rgn = rounded_rgn(w, h, scale(12));
        SetWindowRgn(hwnd, rgn, TRUE);
        return 0;
    }
    case WM_NCACTIVATE:
    case WM_NCPAINT:
        return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:
        paint_menu(hwnd);
        return 0;
    case WM_MOUSEMOVE:
        InvalidateRect(hwnd, nullptr, FALSE);
        return 0;
    case WM_LBUTTONUP: {
        const int id = hit_test_item(GET_Y_LPARAM(lparam));
        destroy_menu();
        if (id == IDM_OPEN) {
            PostMessageW(g_hwnd, WM_COMMAND, ID_OPEN_MAIN, 0);
        } else if (id == IDM_EXIT) {
            PostMessageW(g_hwnd, WM_COMMAND, ID_EXIT, 0);
        }
        return 0;
    }
    case WM_ACTIVATE:
        if (LOWORD(wparam) == WA_INACTIVE) {
            destroy_menu();
        }
        return 0;
    case WM_KILLFOCUS:
        destroy_menu();
        return 0;
    default:
        return DefWindowProcW(hwnd, msg, wparam, lparam);
    }
}

void show_menu()
{
    destroy_menu();
    g_dark_theme = is_system_dark();
    g_dpi = static_cast<int>(GetDpiForWindow(g_hwnd));
    if (g_dpi <= 0) {
        g_dpi = 96;
    }
    POINT pt;
    GetCursorPos(&pt);
    const int w = scale(176);
    const int h = scale(88);
    g_menu_hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, L"WpfhwTrayMenu", L"", WS_POPUP,
                                  pt.x - w + scale(8), pt.y - h - scale(8), w, h, g_hwnd, nullptr,
                                  GetModuleHandleW(nullptr), nullptr);
    if (!g_menu_hwnd) {
        return;
    }
    BOOL dark = g_dark_theme ? TRUE : FALSE;
    DwmSetWindowAttribute(g_menu_hwnd, 20, &dark, sizeof(dark));
    ShowWindow(g_menu_hwnd, SW_SHOWNA);
    SetForegroundWindow(g_menu_hwnd);
    InvalidateRect(g_menu_hwnd, nullptr, FALSE);
}

LRESULT CALLBACK wnd_proc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    switch (msg) {
    case WM_CREATE:
        add_tray_icon(hwnd);
        return 0;
    case WM_TRAY:
        if (LOWORD(lparam) == WM_RBUTTONUP || LOWORD(lparam) == WM_CONTEXTMENU) {
            show_menu();
        } else if (LOWORD(lparam) == WM_LBUTTONDBLCLK) {
            open_main_window();
        }
        return 0;
    case WM_COMMAND:
        if (LOWORD(wparam) == ID_OPEN_MAIN) {
            open_main_window();
        } else if (LOWORD(wparam) == ID_EXIT) {
            DestroyWindow(hwnd);
        }
        return 0;
    case WM_DESTROY:
        destroy_menu();
        remove_tray_icon();
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(hwnd, msg, wparam, lparam);
    }
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    g_icon = load_app_icon(instance);
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = wnd_proc;
    wc.hInstance = instance;
    wc.hIcon = g_icon;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.lpszClassName = L"WpfhwTrayHidden";
    RegisterClassExW(&wc);

    WNDCLASSEXW menu_wc{};
    menu_wc.cbSize = sizeof(menu_wc);
    menu_wc.lpfnWndProc = menu_proc;
    menu_wc.hInstance = instance;
    menu_wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    menu_wc.hbrBackground = nullptr;
    menu_wc.lpszClassName = L"WpfhwTrayMenu";
    RegisterClassExW(&menu_wc);

    g_hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, L"WpfhwTrayHidden", L"Wpfhw Tray", WS_POPUP, 0, 0, 0,
                             0, nullptr, nullptr, instance, nullptr);
    if (!g_hwnd) {
        return 1;
    }
    ShowWindow(g_hwnd, SW_HIDE);
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    if (g_icon) {
        DestroyIcon(g_icon);
    }
    return static_cast<int>(msg.wParam);
}
