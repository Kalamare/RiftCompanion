@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0.tools\dotnet\dotnet.exe" set "DOTNET_ROOT=%~dp0.tools\dotnet"
if not exist "artifacts\native-profile-v34\RiftCompanion.exe" (
  echo Compile d'abord l'application avec native\Build.ps1.
  pause
  exit /b 1
)
start "" "artifacts\native-profile-v34\RiftCompanion.exe" %*
