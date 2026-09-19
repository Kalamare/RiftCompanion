@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0.tools\dotnet\dotnet.exe" set "DOTNET_ROOT=%~dp0.tools\dotnet"
start "" "artifacts\native-diagnostic\RiftCompanion.exe" %*
