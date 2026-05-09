@echo off
REM Windows 客户端构建脚本

echo 正在构建 RemoteCore 客户端...

cd client

REM 恢复依赖
echo 恢复 NuGet 包...
dotnet restore

REM 发布可执行文件
echo 发布可执行文件...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish

echo.
echo ========================================
echo 构建完成！
echo 可执行文件位于: client\publish\RemoteCore.exe
echo ========================================
