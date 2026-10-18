@echo off
chcp 65001
echo =====================================
echo 开始构建全部3个发布包
echo =====================================
echo.
echo [1/3] 正在构建：便携单exe（不含.NET框架）
dotnet publish -p:PublishProfile=Portable-SingleFile
echo.
echo [2/3] 正在构建：单exe完整版（自带.NET）
dotnet publish -p:PublishProfile=SingleFull
echo.
echo [3/3] 正在构建：散文件完整版（一堆dll）
dotnet publish -p:PublishProfile=FullFiles
echo.
echo =====================================
echo 全部构建完成！
echo 输出目录：bin\Release\net10.0-windows\publish
echo =====================================
pause
