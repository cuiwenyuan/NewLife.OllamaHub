@echo off
chcp 65001 >nul
rem 升级到 GitHub Release 最新版本（自替换并重启服务）
setlocal
cd /d "%~dp0"
cd ..

if not exist "NewLife.OllamaHub.exe" (
    echo 未找到 NewLife.OllamaHub.exe。
    pause
    exit /b 1
)

NewLife.OllamaHub.exe upgrade
pause
endlocal
