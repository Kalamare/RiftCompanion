@echo off
setlocal
cd /d "%~dp0"
where node >nul 2>nul
if errorlevel 1 (
  echo Installe Node.js 24 LTS puis relance ce fichier.
  pause
  exit /b 1
)
if not exist node_modules\electron\dist\electron.exe (
  call npm ci
  if errorlevel 1 goto :failure
)
call npm run build
if errorlevel 1 goto :failure
call npm start
if errorlevel 1 goto :failure
exit /b 0
:failure
echo Echec. Copie le message d'erreur ci-dessus sans aucune cle secrete.
pause
exit /b 1
