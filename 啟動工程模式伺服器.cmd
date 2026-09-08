@echo off
chcp 65001 >nul
cd /d "%~dp0"

where python >nul 2>nul
if errorlevel 1 (
  echo 找不到 python，請先安裝 Python 3。
  pause
  exit /b 1
)

python serve-engineering-mode.py %*

echo.
echo 伺服器已結束。
pause
