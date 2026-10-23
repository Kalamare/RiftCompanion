@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0native\Start-Client.ps1" %*
if errorlevel 1 pause
