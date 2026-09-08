@echo off
cd /d "%~dp0"
python serve-engineering-mode.py %*
echo.
echo === 伺服器已結束 ===
pause
