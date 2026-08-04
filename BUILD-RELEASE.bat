@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-Release.ps1"
if errorlevel 1 (
  echo.
  echo Release build failed.
  pause
)
