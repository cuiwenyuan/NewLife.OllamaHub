@echo off
chcp 65001 >nul
rem 安装为 Windows 服务（需要管理员权限）
rem 用法：右键“以管理员身份运行”本文件

setlocal
cd /d "%~dp0"
cd ..

if not exist "NewLife.OllamaHub.exe" (
    echo 未找到 NewLife.OllamaHub.exe，请先构建或解压 Release。
    pause
    exit /b 1
)

echo 正在安装 NewLife.OllamaHub 服务（开机自启）...
NewLife.OllamaHub.exe -i
if errorlevel 1 (
    echo 安装失败，请确认以管理员身份运行。
    pause
    exit /b 1
)

echo 安装完成。可用 -status 查看状态，或在 services.msc 中管理。
pause
endlocal
