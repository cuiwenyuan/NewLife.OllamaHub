@echo off
chcp 65001 >nul
rem 卸载 Windows 服务（需要管理员权限）
setlocal
cd /d "%~dp0"
cd ..

if not exist "NewLife.OllamaHub.exe" (
    echo 未找到 NewLife.OllamaHub.exe。
    pause
    exit /b 1
)

echo 正在卸载 NewLife.OllamaHub 服务...
NewLife.OllamaHub.exe -u
pause
endlocal
