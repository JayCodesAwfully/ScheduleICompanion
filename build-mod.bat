@echo off
setlocal
cd /d "%~dp0"
echo Building MelonLoader mod only...
dotnet build ".\ScheduleICompanion.Mod\ScheduleICompanion.Mod.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Mod build failed. The error is shown above.
  pause
  exit /b 1
)
echo Building reloadable runtime...
dotnet build ".\ScheduleICompanion.Runtime\ScheduleICompanion.Runtime.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Runtime build failed. The error is shown above.
  pause
  exit /b 1
)
echo.
echo Mod and reloadable runtime build completed successfully.
echo Output: ScheduleICompanion.Mod\bin\Release\net6.0\ScheduleICompanion.Mod.dll
echo Output: ScheduleICompanion.Runtime\bin\Release\net6.0\ScheduleICompanion.Runtime.dll
pause
