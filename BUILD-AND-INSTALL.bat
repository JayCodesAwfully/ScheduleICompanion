@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process PowerShell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0tools\Build-And-Install.ps1""'"
if errorlevel 1 (
  echo.
  echo Installation did not complete.
  pause
)
