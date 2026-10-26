@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "CONFIG=%~1"
if "!CONFIG!"=="" set "CONFIG=Release"

set "ROOT=%~dp0"
if "!ROOT:~-1!"=="\" set "ROOT=!ROOT:~0,-1!"

echo =====================================
echo 构建 Wpfhw.exe (!CONFIG! x64)
echo =====================================

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

echo 未找到 MSBuild。
echo 请安装 Visual Studio 2022（工作负载：使用 C++ 的桌面开发），
echo 或在「x64 Native Tools Command Prompt for VS」中运行本脚本。
exit /b 1

:have_msbuild
echo 使用 MSBuild: !MSBUILD!

"!MSBUILD!" "!ROOT!\native\wpfhw-native.sln" /p:Configuration=!CONFIG! /p:Platform=x64 /m /v:minimal
if errorlevel 1 (
    echo 构建失败。
    exit /b 1
)

set "OUT_DIR=!ROOT!\native\bin\!CONFIG!"
set "OUT_EXE=!OUT_DIR!\Wpfhw.exe"
if not exist "!OUT_EXE!" if exist "!OUT_DIR!\wpfhw.exe" (
    copy /Y "!OUT_DIR!\wpfhw.exe" "!OUT_EXE!" >nul
)
if not exist "!OUT_EXE!" (
    echo 构建完成，但未找到 Wpfhw.exe。
    exit /b 1
)

echo =====================================
echo 构建完成
echo 产物: !OUT_EXE!
echo =====================================
endlocal
exit /b 0
