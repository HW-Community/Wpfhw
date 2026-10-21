@echo off
chcp 65001 >nul
setlocal

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

echo =====================================
echo Building Wpfhw native toolchain (%CONFIG% x64)
echo =====================================

where msbuild >nul 2>nul
if errorlevel 1 (
    echo MSBuild not found in PATH.
    echo Open "x64 Native Tools Command Prompt for VS" and run this script again.
    exit /b 1
)

msbuild "%~dp0wpfhw-native.sln" /p:Configuration=%CONFIG% /p:Platform=x64 /m
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo.
echo Output: %~dp0bin\%CONFIG%\
echo   wpfhw-cli.exe
echo   download_engine.dll
echo   wpfhw-tray.exe
endlocal
