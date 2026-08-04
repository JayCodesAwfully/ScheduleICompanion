. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    $config = Read-ServerConfig
    Set-PostgresClientPassword $config
    $pgDump = Join-Path (Get-PostgresBin) "pg_dump.exe"
    $backupRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "Backups"
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $target = Join-Path $backupRoot "backpacks-$stamp.dump"
    & $pgDump --format=custom --compress=9 "--file=$target" -h 127.0.0.1 -p 5432 -U backpack_api backpacks
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed." }
    Write-Host "`nBackup complete:" -ForegroundColor Green
    Write-Host $target -ForegroundColor Cyan
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
