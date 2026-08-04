$ErrorActionPreference = 'Stop'
$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Schedule I'
$BackupRoot = Join-Path $GameDir 'ScheduleICompanion Backups'
$LatestFile = Join-Path $BackupRoot 'latest.txt'
if (-not (Test-Path $LatestFile)) { throw 'No previous backup was recorded.' }
$BackupDir = (Get-Content $LatestFile -Raw).Trim()
if (-not (Test-Path $BackupDir)) { throw "Backup folder not found: $BackupDir" }
if (Get-Process -Name 'Schedule I' -ErrorAction SilentlyContinue) { throw 'Close Schedule I before rolling back.' }
Get-Process -Name 'ScheduleICompanion.App' -ErrorAction SilentlyContinue | Stop-Process -Force
$AppBackup = Join-Path $BackupDir 'ScheduleICompanion'
$ModBackup = Join-Path $BackupDir 'Mods\ScheduleICompanion.Mod.dll'
$AppDir = Join-Path $GameDir 'ScheduleICompanion'
$ModPath = Join-Path $GameDir 'Mods\ScheduleICompanion.Mod.dll'
if (Test-Path $AppBackup) { if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }; Copy-Item $AppBackup $AppDir -Recurse -Force }
if (Test-Path $ModBackup) { Copy-Item $ModBackup $ModPath -Force }
Write-Host "Rollback complete: $BackupDir" -ForegroundColor Green
if (Test-Path (Join-Path $AppDir 'ScheduleICompanion.App.exe')) { Start-Process (Join-Path $AppDir 'ScheduleICompanion.App.exe') -WorkingDirectory $AppDir }
