#include "tray.h"
#include "resource.h"

#include <windowsx.h>
#include <shellapi.h>
#include <dwmapi.h>
#include <string>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")

namespace {

constexpr UINT WM_TRAY = WM_APP + 1;
constexpr UINT ID_TRAY = 1;
constexpr UINT ID_OPEN_MAIN = 1001;
constexpr UINT ID_EXIT = 1002;
constexpr UINT IDM_OPEN = 1;
constexpr UINT IDM_EXIT = 2;

#ifndef DWMWA_USE_IMMERSIVE_DARK_MODE
#define DWMWA_USE_IMMERSIVE_DARK_MODE 20
#endif

HWND g_hwnd = nullptr;
HWND g_menu_hwnd = nullptr;
HICON g_icon = nullptr;
bool g_icon_owned = false;
NOTIFYICONDATAW g_nid{};
bool g_dark_theme = false;
int g_dpi = 96;
int g_hover = 0;

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
    return status == ERROR_SUCCESS && value == 0;
}

HICON load_app_icon(HINSTANCE instance, bool& owned)
{
    HICON icon = LoadIconW(instance, MAKEINTRESOURCEW(IDI_APPICON));
    if (icon) {
        owned = true;
        return icon;
    }
    owned = false;
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
    lstrcpynW(g_nid.szTip, L"Wpfhw", ARRAYSIZE(g_nid.szTip));
    Shell_NotifyIconW(NIM_ADD, &g_nid);
}

void remove_tray_icon()
{
    Shell_NotifyIconW(NIM_DELETE, &g_nid);
}

void open_main_window()
{
    HWND existing = FindWindowW(nullptr, L"Wpfhw Downloader");
    if (existing) {
        ShowWindow(existing, SW_RESTORE);
        SetForegroundWindow(existing);
        return;
    }
    MessageBoxW(nullptr, L"主界面尚未打开。双击本程序无参数运行即可驻留托盘。", L"Wpfhw",
                MB_OK | MB_ICONINFORMATION);
}

COLORREF theme_bg() { return g_dark_theme ? RGB(32, 32, 32) : RGB(248, 248, 248); }
COLORREF theme_fg() { return g_dark_theme ? RGB(245, 245, 245) : RGB(24, 24, 24); }
COLORREF theme_hover() { return g_dark_theme ? RGB(58, 58, 58) : RGB(232, 232, 232); }

HRGN rounded_rgn(int w, int h, int r)
{
    return CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
}

int hit_test_item(int x, int y)
{
    const int item_h = scale(36);
    const int pad = scale(8);
    const int w = scale(176);
    if (x < scale(6) || x > w - scale(6) || y < pad) {
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
    const int hover = g_hover;
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
            HRGN item_rgn = CreateRoundRectRgn(item.left, item.top, item.right, item.bottom,
                                               scale(10), scale(10));
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
    HWND hwnd = g_menu_hwnd;
    g_menu_hwnd = nullptr;
    g_hover = 0;
    if (hwnd) {
        DestroyWindow(hwnd);
    }
}

LRESULT CALLBACK menu_proc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    switch (msg) {
    case WM_CREATE: {
        const int w = scale(176);
        const int h = scale(88);
        SetWindowPos(hwnd, nullptr, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER);
        SetWindowRgn(hwnd, rounded_rgn(w, h, scale(12)), TRUE);
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
    case WM_MOUSEMOVE: {
        TRACKMOUSEEVENT tme{};
        tme.cbSize = sizeof(tme);
        tme.dwFlags = TME_LEAVE;
        tme.hwndTrack = hwnd;
        TrackMouseEvent(&tme);
        const int hover = hit_test_item(GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam));
        if (hover != g_hover) {
            g_hover = hover;
            InvalidateRect(hwnd, nullptr, FALSE);
        }
        return 0;
    }
    case WM_MOUSELEAVE:
        if (g_hover != 0) {
            g_hover = 0;
            InvalidateRect(hwnd, nullptr, FALSE);
        }
        return 0;
    case WM_LBUTTONUP: {
        const int id = hit_test_item(GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam));
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
    int x = pt.x - w + scale(8);
    int y = pt.y - h - scale(8);
    HMONITOR monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{};
    mi.cbSize = sizeof(mi);
    if (GetMonitorInfoW(monitor, &mi)) {
        const RECT& wa = mi.rcWork;
        if (x + w > wa.right) {
            x = wa.right - w;
        }
        if (y + h > wa.bottom) {
            y = wa.bottom - h;
        }
        if (x < wa.left) {
            x = wa.left;
        }
        if (y < wa.top) {
            y = wa.top;
        }
    }
    g_hover = 0;
    g_menu_hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, L"WpfhwTrayMenu", L"", WS_POPUP,
                                  x, y, w, h, g_hwnd, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!g_menu_hwnd) {
        return;
    }
    BOOL dark = g_dark_theme ? TRUE : FALSE;
    DwmSetWindowAttribute(g_menu_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(dark));
    ShowWindow(g_menu_hwnd, SW_SHOWNA);
    SetForegroundWindow(g_menu_hwnd);
    PostMessageW(g_menu_hwnd, WM_NULL, 0, 0);
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

int run_tray(HINSTANCE instance)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    g_icon = load_app_icon(instance, g_icon_owned);
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
    menu_wc.lpszClassName = L"WpfhwTrayMenu";
    RegisterClassExW(&menu_wc);
    g_hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, L"WpfhwTrayHidden", L"Wpfhw", WS_POPUP, 0, 0, 0, 0,
                             nullptr, nullptr, instance, nullptr);
    if (!g_hwnd) {
        return 1;
    }
    ShowWindow(g_hwnd, SW_HIDE);
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    if (g_icon && g_icon_owned) {
        DestroyIcon(g_icon);
    }
    return static_cast<int>(msg.wParam);
}
