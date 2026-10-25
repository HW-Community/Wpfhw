@echo off
chcp 65001 >nul
echo ============================================
echo  Wpfhw - Build single native EXE
echo  (CLI + system tray + multi-thread download)
echo ============================================
echo.
call "%~dp0native\build.bat" %*
