@echo off
setlocal
title Schedule I Backpack Server - Windows Server Setup
cd /d "%~dp0"

fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting Administrator access...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-WindowsServer.ps1"
set "EXITCODE=%ERRORLEVEL%"
echo.
if not "%EXITCODE%"=="0" echo Setup stopped with error code %EXITCODE%.
pause
exit /b %EXITCODE%
