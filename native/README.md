# Wpfhw Native

编译后只产出一个 `wpfhw.exe`。

```bat
cd native
build.bat Release
```

产物：`native\bin\Release\wpfhw.exe`

```bat
wpfhw.exe
wpfhw.exe search sodium
wpfhw.exe download fabric-api
```

无参数驻留托盘（项目 logo、圆角右键菜单、跟随系统深浅色）。`search` / `download` 走命令行，请求头 `User-Agent: HW-Community/Wpfhw-cli/1.0.0`。大文件下载内置多线程，静态链进同一个 exe。
