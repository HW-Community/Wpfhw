# Wpfhw C++ 工具链

给 Wpfhw 用的 Windows 原生扩展：Modrinth 命令行、给 WPF 调用的多线程下载 DLL、右下角托盘监控。

## 目录

```
native/
  common/              WinHTTP 与 JSON 辅助代码
  cli/                 wpfhw-cli.exe
  download_engine/     download_engine.dll
  tray/                wpfhw-tray.exe
  wpfhw-native.sln
  build.bat
```

## 编译

需要 Visual Studio 2022，并安装「使用 C++ 的桌面开发」工作负载（MSVC v143、Windows SDK）。

```bat
cd native
build.bat Release
```

产物在 `native/bin/Release/`。

## 1. CLI 命令行

```bat
wpfhw-cli search sodium
wpfhw-cli download fabric-api
```

- `search <关键词>`：请求 Modrinth API，打印前 5 条结果（名字、下载量）
- `download <项目名>`：解析项目、取最新版本主文件，保存到桌面
- 所有请求头都带 `User-Agent: HW-Community/Wpfhw-cli/1.0.0`

## 2. 极速下载引擎 DLL

C# 调用方式：

```csharp
[DllImport("download_engine.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern int DownloadFile(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);
```

`DownloadFile` 成功返回 `0`。服务器支持 `Accept-Ranges: bytes` 时，大文件会按最多 8 个线程分片下载。

WPF 侧已提供 `wpfhw/NativeDownload.cs`。

## 3. 托盘监控

`wpfhw-tray.exe` 无主窗口，只在通知区域显示项目 `logo.ico`。

右键菜单带圆角，并按系统浅色/深色主题切换：

- 打开主界面
- 退出
