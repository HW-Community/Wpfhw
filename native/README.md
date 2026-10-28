# Wpfhw Native

native 工程编译后产出 `native\bin\Release\Wpfhw.exe`，同时会被嵌入 WPF 主程序，作为内置托盘/CLI 组件发布。

需要 Visual Studio 2022 或 2026（工作负载：使用 C++ 的桌面开发）。在仓库根目录或 `native` 目录运行：

```bat
build.bat
```

指定配置：

```bat
build.bat Release
build.bat Debug
```

脚本会自动用 vswhere 定位 MSBuild，不必先开「x64 Native Tools Command Prompt」。工程使用 `$(DefaultPlatformToolset)`，VS 2022（v143）和 VS 2026（v145）都能直接编译。

产物：`native\bin\Release\Wpfhw.exe`

```bat
Wpfhw.exe
Wpfhw.exe search sodium
Wpfhw.exe download fabric-api
```

无参数驻留托盘（项目 logo、圆角右键菜单、跟随系统深浅色），并带单实例互斥。`search` / `download` 走命令行，请求头 `User-Agent: HW-Community/Wpfhw-cli/1.0.0`。大文件下载内置多线程，静态链进同一个 exe。

WPF 主程序启动时会把该 exe 释放到 `%LOCALAPPDATA%\Wpfhw\native\`，并以 `--tray <WPF exe 路径>` 启动，因此托盘菜单的「打开主界面」能重新拉起 GUI。根目录 `build.bat` 会同时产出三种 WPF 发布形态（`self` / `fx-single` / `fx-full`）；`native` 目录下的 `build.bat` 会转调根目录脚本，因此也会发布 WPF。
