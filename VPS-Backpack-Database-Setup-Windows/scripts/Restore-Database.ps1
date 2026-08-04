. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    $config = Read-ServerConfig
    Set-PostgresClientPassword $config
    $backupRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "Backups"
    $backups = @(Get-ChildItem -LiteralPath $backupRoot -Filter "*.dump" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    if ($backups.Count -eq 0) { throw "No .dump backups exist in $backupRoot" }
    Write-Host "Available backups:`n"
    for ($i = 0; $i -lt $backups.Count; $i++) {
        Write-Host "[$($i + 1)] $($backups[$i].Name)  $($backups[$i].Length) bytes"
    }
    $selectionText = (Read-Host "Backup number to restore").Trim()
    $selection = 0
    if (-not [int]::TryParse($selectionText, [ref]$selection) -or $selection -lt 1 -or $selection -gt $backups.Count) {
        throw "Invalid backup number."
    }
    $chosen = $backups[$selection - 1]
    $confirm = (Read-Host "This replaces current backpack data. Type RESTORE").Trim()
    if ($confirm -cne "RESTORE") { Write-Host "Cancelled."; exit 0 }
    $pgRestore = Join-Path (Get-PostgresBin) "pg_restore.exe"
    & $pgRestore --clean --if-exists --no-owner -h 127.0.0.1 -p 5432 -U backpack_api -d backpacks $chosen.FullName
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed. The database may require manual review." }
    Write-Host "Database restored from $($chosen.Name)." -ForegroundColor Green
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
