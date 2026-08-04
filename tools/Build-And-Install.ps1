$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Schedule I'
$ModsDir = Join-Path $GameDir 'Mods'
$AppDir = Join-Path $GameDir 'ScheduleICompanion'
$BackupRoot = Join-Path $GameDir 'ScheduleICompanion Backups'
$Stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$BackupDir = Join-Path $BackupRoot $Stamp

function Step($Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }
function Require-Path($Path, $Description) { if (-not (Test-Path $Path)) { throw "$Description not found: $Path" } }

Step 'Checking installation and build requirements'
Require-Path $GameDir 'Schedule I folder'
Require-Path (Join-Path $GameDir 'MelonLoader') 'MelonLoader folder'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is not installed or is not on PATH.' }
$sdk = dotnet --list-sdks
if (-not $sdk) { throw 'No .NET SDK was found. Install the .NET 8 SDK.' }
$GameRunning = $null -ne (Get-Process -Name 'Schedule I' -ErrorAction SilentlyContinue)
if ($GameRunning) {
    Write-Host 'Schedule I is running. The companion and reloadable runtime will be updated live.' -ForegroundColor Yellow
    Write-Host 'The stable bootstrap DLL will be left untouched until the next update with the game closed.' -ForegroundColor Yellow
}

Step 'Stopping the companion application if it is running'
Get-Process -Name 'ScheduleICompanion.App' -ErrorAction SilentlyContinue | Stop-Process -Force

Step 'Updating the project game path'
$props = Join-Path $ProjectRoot 'Directory.Build.props'
[xml]$xml = Get-Content $props
$xml.Project.PropertyGroup.ScheduleIGameDirectory = $GameDir
$xml.Save($props)

$il2cppCore = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies\Il2Cppmscorlib.dll'
$il2cppFallback = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies\mscorlib.dll'
if (-not (Test-Path $il2cppCore) -and -not (Test-Path $il2cppFallback)) {
    throw 'Neither Il2Cppmscorlib.dll nor mscorlib.dll was found under MelonLoader\Il2CppAssemblies.'
}

Step 'Building the companion application'
dotnet publish (Join-Path $ProjectRoot 'ScheduleICompanion.App\ScheduleICompanion.App.csproj') -c Release -r win-x64 --self-contained false
if ($LASTEXITCODE -ne 0) { throw "Companion build failed with exit code $LASTEXITCODE." }

Step 'Building managed Companion mods'
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.Backpack\ScheduleICompanion.Backpack.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Backpack build failed with exit code $LASTEXITCODE." }

Step 'Building the MelonLoader bridge'
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.Mod\ScheduleICompanion.Mod.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Mod build failed with exit code $LASTEXITCODE." }

Step 'Building the reloadable game runtime'
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.Runtime\ScheduleICompanion.Runtime.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Runtime build failed with exit code $LASTEXITCODE." }

$PublishDir = Join-Path $ProjectRoot 'ScheduleICompanion.App\bin\Release\net8.0-windows\win-x64\publish'
$ModDll = Join-Path $ProjectRoot 'ScheduleICompanion.Mod\bin\Release\net6.0\ScheduleICompanion.Mod.dll'
$RuntimeDir = Join-Path $ProjectRoot 'ScheduleICompanion.Runtime\bin\Release\net6.0'
$RuntimeDll = Join-Path $RuntimeDir 'ScheduleICompanion.Runtime.dll'
$BackpackDll = Join-Path $ProjectRoot 'ScheduleICompanion.Backpack\bin\Release\net6.0\ScheduleICompanion.Backpack.dll'
Require-Path (Join-Path $PublishDir 'ScheduleICompanion.App.exe') 'Published companion executable'
Require-Path $ModDll 'Built bridge DLL'
Require-Path $RuntimeDll 'Built reloadable runtime DLL'
Require-Path $BackpackDll 'Built backpack DLL'

Step 'Backing up the installed companion'
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
if (Test-Path $AppDir) { Copy-Item $AppDir (Join-Path $BackupDir 'ScheduleICompanion') -Recurse -Force }
$InstalledMod = Join-Path $ModsDir 'ScheduleICompanion.Mod.dll'
if (Test-Path $InstalledMod) { New-Item -ItemType Directory -Force -Path (Join-Path $BackupDir 'Mods') | Out-Null; Copy-Item $InstalledMod (Join-Path $BackupDir 'Mods\ScheduleICompanion.Mod.dll') -Force }
$InstalledBackpackMod = Join-Path $ModsDir 'ScheduleICompanion.Backpack.dll'
if (Test-Path $InstalledBackpackMod) { New-Item -ItemType Directory -Force -Path (Join-Path $BackupDir 'Mods') | Out-Null; Copy-Item $InstalledBackpackMod (Join-Path $BackupDir 'Mods\ScheduleICompanion.Backpack.dll') -Force }
Set-Content -Path (Join-Path $BackupRoot 'latest.txt') -Value $BackupDir

Step 'Installing the companion application'
New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
$preserve = @('Maps')
Get-ChildItem $AppDir -Force | Where-Object { $preserve -notcontains $_.Name } | Remove-Item -Recurse -Force
Copy-Item (Join-Path $PublishDir '*') $AppDir -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $AppDir 'ModPackages') | Out-Null
Copy-Item $BackpackDll (Join-Path $AppDir 'ModPackages\ScheduleICompanion.Backpack.dll') -Force
if (-not (Test-Path (Join-Path $AppDir 'Maps'))) { New-Item -ItemType Directory -Path (Join-Path $AppDir 'Maps') | Out-Null }
if (Test-Path (Join-Path $ProjectRoot 'ScheduleICompanion.App\Maps')) { Copy-Item (Join-Path $ProjectRoot 'ScheduleICompanion.App\Maps\*') (Join-Path $AppDir 'Maps') -Force -ErrorAction SilentlyContinue }

Step 'Installing the reloadable game runtime'
$InstalledRuntimeDir = Join-Path $AppDir 'Runtime'
New-Item -ItemType Directory -Force -Path $InstalledRuntimeDir | Out-Null
Copy-Item $RuntimeDll (Join-Path $InstalledRuntimeDir 'ScheduleICompanion.Runtime.dll') -Force
$RuntimePdb = Join-Path $RuntimeDir 'ScheduleICompanion.Runtime.pdb'
if (Test-Path $RuntimePdb) { Copy-Item $RuntimePdb (Join-Path $InstalledRuntimeDir 'ScheduleICompanion.Runtime.pdb') -Force }

Step 'Installing the MelonLoader bridge'
New-Item -ItemType Directory -Force -Path $ModsDir | Out-Null
if ($GameRunning) {
    $BootstrapChanged = -not (Test-Path $InstalledMod) -or
        ((Get-FileHash $ModDll -Algorithm SHA256).Hash -ne (Get-FileHash $InstalledMod -Algorithm SHA256).Hash)
    if ($BootstrapChanged) {
        Copy-Item $ModDll (Join-Path $AppDir 'ScheduleICompanion.Mod.pending-install.dll') -Force
        Write-Host 'Bootstrap update staged. Run this installer with the game closed before relying on bootstrap changes.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Stable bootstrap is unchanged; only the live runtime was replaced.' -ForegroundColor Green
    }
}
else {
    Copy-Item $ModDll $InstalledMod -Force
    $StagedMod = Join-Path $AppDir 'ScheduleICompanion.Mod.pending-install.dll'
    if (Test-Path $StagedMod) { Remove-Item $StagedMod -Force }
}

Step 'Starting the companion application'
Start-Process -FilePath (Join-Path $AppDir 'ScheduleICompanion.App.exe') -WorkingDirectory $AppDir
Write-Host "`nInstallation complete." -ForegroundColor Green
Write-Host "Backup: $BackupDir"
