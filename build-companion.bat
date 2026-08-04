@echo off
setlocal
cd /d "%~dp0"
echo Building companion application only...
dotnet publish ".\ScheduleICompanion.App\ScheduleICompanion.App.csproj" -c Release -r win-x64 --self-contained false
if errorlevel 1 (
  echo.
  echo Companion build failed. The error is shown above.
  pause
  exit /b 1
)
echo.
echo Companion build completed successfully.
echo Output: ScheduleICompanion.App\bin\Release\net8.0-windows\win-x64\publish
pause
