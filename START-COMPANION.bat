@echo off
set "APP=C:\Program Files (x86)\Steam\steamapps\common\Schedule I\ScheduleICompanion\ScheduleICompanion.App.exe"
if not exist "%APP%" (
  echo Companion not found at:
  echo %APP%
  pause
  exit /b 1
)
start "Schedule I Companion" "%APP%"
