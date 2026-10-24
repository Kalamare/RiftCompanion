@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server\Start-Local.ps1" %*
if errorlevel 1 pause
