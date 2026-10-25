@echo off
chcp 65001 >nul
setlocal

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

echo ============================================
echo  Building wpfhw.exe (native, %CONFIG% x64)
echo  Single EXE with CLI + tray + download engine
echo ============================================

rem --- Locate MSBuild -------------------------------------------------------
set "MSBUILD="
where msbuild >nul 2>nul
if not errorlevel 1 (
    for /f "delims=" %%i in ('where msbuild 2^>nul') do set "MSBUILD=%%i"
)
if not defined MSBUILD (
    set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
    if exist "%VSWHERE%" (
        for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2^>nul`) do set "MSBUILD=%%i"
    )
)
if not defined MSBUILD (
    echo [ERROR] MSBuild not found.
    echo.
    echo Options:
    echo   1. Install Visual Studio 2022 with "Desktop development with C++".
    echo   2. Run this script from "x64 Native Tools Command Prompt for VS 2022".
    echo   3. Add MSBuild to PATH manually.
    exit /b 1
)

echo Using MSBuild: %MSBUILD%
echo.

rem --- Build the native solution --------------------------------------------
"%MSBUILD%" "%~dp0wpfhw-native.sln" /p:Configuration=%CONFIG% /p:Platform=x64 /m /v:minimal
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. Check the errors above.
    exit /b 1
)

echo.
echo ============================================
echo  Build succeeded.
echo  Output: %~dp0bin\%CONFIG%\wpfhw.exe
echo ============================================
echo.
echo Usage:
echo   wpfhw.exe                ^(no args^)  - stay in system tray
echo   wpfhw.exe search keyword              - search Modrinth
echo   wpfhw.exe download project_name       - download latest file
endlocal
