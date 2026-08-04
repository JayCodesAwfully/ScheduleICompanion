. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    $config = Read-ServerConfig
    $packageRoot = Split-Path -Parent $PSScriptRoot
    $apiSource = Join-Path $packageRoot "server\api"
    if (-not (Test-Path -LiteralPath $apiSource)) { throw "Packaged API files are missing." }
    $apiTarget = Join-Path $script:InstallRoot "api"
    Copy-Item -Recurse -Force (Join-Path $apiSource "app") $apiTarget
    Copy-Item -Force (Join-Path $apiSource "requirements.txt") $apiTarget
    $python = Get-PythonExe
    & $python -m pip install --disable-pip-version-check --upgrade -r (Join-Path $apiTarget "requirements.txt")
    if ($LASTEXITCODE -ne 0) { throw "Dependency update failed." }
    Set-ApiEnvironment $config
    Push-Location $apiTarget
    try {
        & $python -m app.selftest
        if ($LASTEXITCODE -ne 0) { throw "The updated API failed its database self-test." }
    } finally { Pop-Location }
    Stop-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $script:TaskName
    Write-Host "Backpack API updated and restarted." -ForegroundColor Green
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
