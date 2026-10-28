@echo off
rem ============================================================
rem Wpfhw build script
rem
rem Usage:
rem   build.bat [Release|Debug] [all|self|fx-single|fx-full]
rem
rem   all       = build all three publish flavors (default)
rem   self      = self-contained single-file exe (no .NET needed)
rem   fx-single = framework-dependent single-file exe
rem   fx-full   = framework-dependent full directory
rem
rem The script requires:
rem   - .NET 10 SDK (x64)
rem   - Visual Studio 2022/2026 with "Desktop development with C++"
rem   - Windows 10/11 SDK
rem ============================================================
setlocal EnableDelayedExpansion

set "CONFIG=%~1"
if "!CONFIG!"=="" set "CONFIG=Release"
if /I not "!CONFIG!"=="Release" if /I not "!CONFIG!"=="Debug" (
    echo [ERROR] Unknown configuration: !CONFIG!
    echo Usage: build.bat [Release^|Debug] [all^|self^|fx-single^|fx-full]
    exit /b 1
)

set "MODE=%~2"
if "!MODE!"=="" set "MODE=all"
if /I not "!MODE!"=="all" if /I not "!MODE!"=="self" if /I not "!MODE!"=="fx-single" if /I not "!MODE!"=="fx-full" (
    echo [ERROR] Unknown publish mode: !MODE!
    echo Usage: build.bat [Release^|Debug] [all^|self^|fx-single^|fx-full]
    exit /b 1
)

set "MODES=!MODE!"
if /I "!MODE!"=="all" set "MODES=self fx-single fx-full"

set "ROOT=%~dp0"
if "!ROOT:~-1!"=="\" set "ROOT=!ROOT:~0,-1!"

echo =====================================
echo Build Wpfhw (!CONFIG! x64)
echo =====================================

rem ---------- Environment checks ----------
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet command not found. .NET 10 SDK is required.
    echo   Download page: https://dotnet.microsoft.com/download/dotnet/10.0
    echo   Direct link:   https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe
    exit /b 1
)
dotnet --list-sdks 2>nul | findstr /r /c:"^10\." >nul
if errorlevel 1 (
    echo [ERROR] .NET 10 SDK not found. Installed SDKs:
    dotnet --list-sdks
    echo   Download page: https://dotnet.microsoft.com/download/dotnet/10.0
    echo   Direct link:   https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe
    exit /b 1
)

set "MSBUILD="
where msbuild >nul 2>nul
if not errorlevel 1 (
    for /f "delims=" %%I in ('where msbuild') do (
        set "MSBUILD=%%I"
        goto :have_msbuild
    )
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "!VSWHERE!" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "!VSWHERE!" (
    for /f "usebackq delims=" %%I in (`"!VSWHERE!" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        set "MSBUILD=%%I"
        goto :have_msbuild
    )
    for /f "usebackq delims=" %%I in (`"!VSWHERE!" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do (
        if exist "%%I\MSBuild\Current\Bin\MSBuild.exe" (
            set "MSBUILD=%%I\MSBuild\Current\Bin\MSBuild.exe"
            goto :have_msbuild
        )
        if exist "%%I\MSBuild\Current\Bin\amd64\MSBuild.exe" (
            set "MSBUILD=%%I\MSBuild\Current\Bin\amd64\MSBuild.exe"
            goto :have_msbuild
        )
    )
)

echo [ERROR] MSBuild / Visual Studio C++ build tools not found.
echo   Install Visual Studio 2022 or 2026 with the workload:
echo   "Desktop development with C++".
echo   VS downloads:        https://visualstudio.microsoft.com/downloads/
echo   VS 2022 Build Tools: https://aka.ms/vs/17/release/vs_buildtools.exe
echo   VS 2026 Community:   https://aka.ms/vs/18/stable/vs_community.exe
echo   Select: MSVC v143/v145 x64 build tools + Windows 10/11 SDK.
exit /b 1

:have_msbuild
echo Using MSBuild: !MSBUILD!

if exist "!VSWHERE!" (
    set "VCTOOLS="
    for /f "usebackq delims=" %%I in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCTOOLS=%%I"
    if "!VCTOOLS!"=="" (
        echo [ERROR] Visual Studio is missing the C++ build tools component.
        echo   Select "Desktop development with C++" in Visual Studio Installer.
        echo   VS downloads:        https://visualstudio.microsoft.com/downloads/
        echo   VS 2022 Build Tools: https://aka.ms/vs/17/release/vs_buildtools.exe
        echo   VS 2026 Community:   https://aka.ms/vs/18/stable/vs_community.exe
        exit /b 1
    )
)

if not exist "%ProgramFiles(x86)%\Windows Kits\10\Include" (
    echo [ERROR] Windows 10/11 SDK not found.
    echo   Download: https://developer.microsoft.com/windows/downloads/windows-sdk/
    exit /b 1
)

rem ---------- Build native ----------
echo.
echo [1/2] Building native C++ (Wpfhw.exe)
"!MSBUILD!" "!ROOT!\native\wpfhw-native.sln" -p:Configuration=!CONFIG! -p:Platform=x64 -m -v:minimal -nologo
if errorlevel 1 goto :fail

set "NATIVE_EXE=!ROOT!\native\bin\!CONFIG!\Wpfhw.exe"
if not exist "!NATIVE_EXE!" (
    echo [ERROR] Native build finished but !NATIVE_EXE! was not found.
    goto :fail
)
echo Native output: !NATIVE_EXE!

rem ---------- Publish WPF ----------
echo.
echo [2/2] Publishing WPF application
set "DIST=!ROOT!\dist\!CONFIG!"
if not exist "!DIST!" mkdir "!DIST!"

for %%M in (!MODES!) do (
    call :publish_one %%M
    if errorlevel 1 goto :fail
)

echo.
echo =====================================
echo Build complete
echo =====================================
echo Native standalone tool: !NATIVE_EXE!
echo Publish root:          !DIST!
if exist "!DIST!\self-contained-single\Wpfhw.exe" echo   self-contained single: !DIST!\self-contained-single\Wpfhw.exe
if exist "!DIST!\framework-single\Wpfhw.exe" echo   framework single:      !DIST!\framework-single\Wpfhw.exe
if exist "!DIST!\framework-full\Wpfhw.exe" echo   framework full:        !DIST!\framework-full\
echo.
echo Notes:
echo   - The WPF exe embeds the native tray/CLI component. On first run it is
echo     extracted to LocalAppData\Wpfhw\native\.
echo   - framework-* outputs require .NET 10 Desktop Runtime x64 on the target:
echo     https://dotnet.microsoft.com/download/dotnet/10.0/runtime
echo     Direct link: https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe
endlocal
exit /b 0

:publish_one
set "FLAVOR=%~1"
set "OUT_NAME="
set "SELF=false"
set "SINGLE=false"
if /I "!FLAVOR!"=="self" (
    set "OUT_NAME=self-contained-single"
    set "SELF=true"
    set "SINGLE=true"
)
if /I "!FLAVOR!"=="fx-single" (
    set "OUT_NAME=framework-single"
    set "SELF=false"
    set "SINGLE=true"
)
if /I "!FLAVOR!"=="fx-full" (
    set "OUT_NAME=framework-full"
    set "SELF=false"
    set "SINGLE=false"
)
if "!OUT_NAME!"=="" (
    echo [ERROR] Unknown publish flavor: !FLAVOR!
    exit /b 1
)

set "OUT_DIR=!DIST!\!OUT_NAME!"
if exist "!OUT_DIR!" rmdir /s /q "!OUT_DIR!"

echo.
echo --- Publishing !OUT_NAME! ---
dotnet publish "!ROOT!\wpfhw\wpfhw.csproj" -c !CONFIG! -r win-x64 --self-contained !SELF! -p:PublishSingleFile=!SINGLE! -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o "!OUT_DIR!" -v:minimal -nologo
if errorlevel 1 exit /b 1

if not exist "!OUT_DIR!\Wpfhw.exe" (
    echo [ERROR] !OUT_DIR!\Wpfhw.exe was not generated.
    exit /b 1
)
exit /b 0

:fail
echo.
echo Build failed.
endlocal
exit /b 1
