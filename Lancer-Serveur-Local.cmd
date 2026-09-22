@echo off
setlocal
cd /d "%~dp0"
set "RIFT_SERVER_URL=http://127.0.0.1:5080/"
set "RIFT_SERVER_ACCESS_KEY_FILE=%~dp0server\.secrets\access-key"
if not exist "%RIFT_SERVER_ACCESS_KEY_FILE%" (
  echo Initialise d'abord le backend avec server\Initialize-Dev.ps1 -Demo.
  pause
  exit /b 1
)
call Lancer-Natif.cmd %*
