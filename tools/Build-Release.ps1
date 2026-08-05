$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Version = '1.7.16'
$DistRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'dist'))
$PackageRoot = Join-Path $DistRoot "ScheduleICompanion-v$Version"
$PayloadRoot = Join-Path $PackageRoot 'Payload'
$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Schedule I'

function Step($Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }
function Require-Path($Path, $Description) { if (-not (Test-Path $Path)) { throw "$Description not found: $Path" } }

if (-not $DistRoot.StartsWith([IO.Path]::GetFullPath($ProjectRoot), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to package outside the project directory: $DistRoot"
}
if (Test-Path $PackageRoot) { Remove-Item -LiteralPath $PackageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PayloadRoot | Out-Null

Step 'Checking build requirements'
Require-Path $GameDir 'Local Schedule I folder used for compile-time references'
Require-Path (Join-Path $GameDir 'MelonLoader') 'MelonLoader compile-time references'

Step 'Bundling the version-matched IL2CPP interop cache'
$InteropSource = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'
$InteropConfig = Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Config.cfg'
Require-Path $InteropSource 'Generated IL2CPP assembly cache'
Require-Path $InteropConfig 'IL2CPP assembly generator configuration'
$InteropPayload = Join-Path $PayloadRoot 'InteropCache'
New-Item -ItemType Directory -Force -Path $InteropPayload | Out-Null
Copy-Item $InteropSource (Join-Path $InteropPayload 'Il2CppAssemblies') -Recurse -Force
Copy-Item $InteropConfig (Join-Path $InteropPayload 'Config.cfg') -Force

Step 'Building managed Companion mods'
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.Backpack\ScheduleICompanion.Backpack.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Backpack build failed with exit code $LASTEXITCODE." }
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.ClonalCultivation\ScheduleICompanion.ClonalCultivation.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Clonal Cultivation build failed with exit code $LASTEXITCODE." }
$BackpackDll = Join-Path $ProjectRoot 'ScheduleICompanion.Backpack\bin\Release\net6.0\ScheduleICompanion.Backpack.dll'
$ClonalCultivationDll = Join-Path $ProjectRoot 'ScheduleICompanion.ClonalCultivation\bin\Release\net6.0\ScheduleICompanion.ClonalCultivation.dll'

Step 'Publishing self-contained Companion application'
$AppProject = Join-Path $ProjectRoot 'ScheduleICompanion.App\ScheduleICompanion.App.csproj'
$AppPublish = Join-Path $ProjectRoot 'ScheduleICompanion.App\bin\Release\net8.0-windows\win-x64\publish'
dotnet publish $AppProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Companion publish failed with exit code $LASTEXITCODE." }
Copy-Item $AppPublish (Join-Path $PayloadRoot 'Companion') -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $PayloadRoot 'Companion\ModPackages') | Out-Null
Copy-Item $BackpackDll (Join-Path $PayloadRoot 'Companion\ModPackages\ScheduleICompanion.Backpack.dll') -Force
Copy-Item $ClonalCultivationDll (Join-Path $PayloadRoot 'Companion\ModPackages\ScheduleICompanion.ClonalCultivation.dll') -Force

Step 'Building MelonLoader bootstrap and reloadable runtime'
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.Mod\ScheduleICompanion.Mod.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Bootstrap build failed with exit code $LASTEXITCODE." }
dotnet build (Join-Path $ProjectRoot 'ScheduleICompanion.Runtime\ScheduleICompanion.Runtime.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Runtime build failed with exit code $LASTEXITCODE." }
New-Item -ItemType Directory -Force -Path (Join-Path $PayloadRoot 'Mods') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PayloadRoot 'Runtime') | Out-Null
Copy-Item (Join-Path $ProjectRoot 'ScheduleICompanion.Mod\bin\Release\net6.0\ScheduleICompanion.Mod.dll') (Join-Path $PayloadRoot 'Mods') -Force
Copy-Item (Join-Path $ProjectRoot 'ScheduleICompanion.Runtime\bin\Release\net6.0\ScheduleICompanion.Runtime.dll') (Join-Path $PayloadRoot 'Runtime') -Force
$RuntimePdb = Join-Path $ProjectRoot 'ScheduleICompanion.Runtime\bin\Release\net6.0\ScheduleICompanion.Runtime.pdb'
if (Test-Path $RuntimePdb) { Copy-Item $RuntimePdb (Join-Path $PayloadRoot 'Runtime') -Force }

Step 'Publishing self-contained setup application'
$SetupProject = Join-Path $ProjectRoot 'ScheduleICompanion.Installer\ScheduleICompanion.Installer.csproj'
$SetupPublish = Join-Path $ProjectRoot 'ScheduleICompanion.Installer\bin\Release\net8.0-windows\win-x64\publish'
dotnet publish $SetupProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed with exit code $LASTEXITCODE." }
Copy-Item (Join-Path $SetupPublish 'ScheduleICompanion.Setup.exe') (Join-Path $PackageRoot 'ScheduleICompanion-Setup.exe') -Force
Copy-Item (Join-Path $ProjectRoot 'packaging\README-INSTALL.txt') $PackageRoot -Force
Copy-Item (Join-Path $ProjectRoot 'packaging\catalog.vps.example.json') $PackageRoot -Force

Step 'Creating shareable release archive'
$Archive = Join-Path $DistRoot "ScheduleICompanion-v$Version.zip"
if (Test-Path $Archive) { Remove-Item -LiteralPath $Archive -Force }
Compress-Archive -Path $PackageRoot -DestinationPath $Archive -CompressionLevel Optimal
$Hash = (Get-FileHash $Archive -Algorithm SHA256).Hash
$ChecksumFile = "$Archive.sha256"
Set-Content -LiteralPath $ChecksumFile -Encoding ASCII -Value "$Hash *$([IO.Path]::GetFileName($Archive))"
Write-Host "`nRelease ready:" -ForegroundColor Green
Write-Host $Archive
Write-Host "SHA-256: $Hash"
