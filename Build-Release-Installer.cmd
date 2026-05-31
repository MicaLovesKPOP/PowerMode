@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo Power Mode release installer build
echo ----------------------------------
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release-Installer.ps1"
set "BUILD_EXIT=%ERRORLEVEL%"

echo.
if not "%BUILD_EXIT%"=="0" (
  echo Build failed with exit code %BUILD_EXIT%.
  echo Check the log in:
  echo   %~dp0dist\build-release-installer.log
) else (
  echo Build completed successfully.
  echo Output folder:
  echo   %~dp0dist
)

echo.
pause
exit /b %BUILD_EXIT%
