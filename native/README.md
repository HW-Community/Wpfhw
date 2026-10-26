# Wpfhw Native

编译后只产出一个 `Wpfhw.exe`。

需要 Visual Studio 2022（工作负载：使用 C++ 的桌面开发）。在仓库根目录或 `native` 目录运行：

```bat
build.bat
```

指定配置：

```bat
build.bat Release
build.bat Debug
```

脚本会自动用 vswhere 定位 MSBuild，不必先开「x64 Native Tools Command Prompt」。

产物：`native\bin\Release\Wpfhw.exe`

```bat
Wpfhw.exe
Wpfhw.exe search sodium
Wpfhw.exe download fabric-api
```

无参数驻留托盘（项目 logo、圆角右键菜单、跟随系统深浅色）。`search` / `download` 走命令行，请求头 `User-Agent: HW-Community/Wpfhw-cli/1.0.0`。大文件下载内置多线程，静态链进同一个 exe。
