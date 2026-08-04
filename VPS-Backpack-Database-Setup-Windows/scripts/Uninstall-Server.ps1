. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    Write-Host "`n[1] Stop/disable the Backpack server but keep all data" -ForegroundColor Cyan
    Write-Host "[2] Remove the API, HTTPS service and backpacks database" -ForegroundColor Yellow
    Write-Host "PostgreSQL and Python remain installed because other software may use them."
    $choice = (Read-Host "Choose 1 or 2").Trim()
    if ($choice -notin @("1", "2")) { Write-Host "Cancelled."; exit 0 }

    Stop-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
    Disable-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue | Out-Null
    & sc.exe stop $script:CaddyServiceName 2>$null | Out-Null

    if ($choice -eq "1") {
        Write-Host "Backpack API and HTTPS service disabled. Data was kept." -ForegroundColor Green
        Write-Host "Run START-SERVER.bat to enable it again."
        exit 0
    }

    $confirm = (Read-Host "Permanent removal deletes every stored backpack. Type DELETE ALL BACKPACKS").Trim()
    if ($confirm -cne "DELETE ALL BACKPACKS") { Write-Host "Cancelled; services remain disabled."; exit 0 }

    $postgresPassword = Read-PrivateText "Enter the PostgreSQL 'postgres' account password"
    $pgBin = Get-PostgresBin
    $env:PGPASSWORD = $postgresPassword
    & (Join-Path $pgBin "dropdb.exe") -h 127.0.0.1 -U postgres --if-exists --force backpacks
    if ($LASTEXITCODE -ne 0) { throw "Could not delete the database. No application files were removed." }
    & (Join-Path $pgBin "dropuser.exe") -h 127.0.0.1 -U postgres --if-exists backpack_api
    if ($LASTEXITCODE -ne 0) { throw "Could not delete the backpack_api database account." }
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue

    Unregister-ScheduledTask -TaskName $script:TaskName -Confirm:$false -ErrorAction SilentlyContinue
    & sc.exe delete $script:CaddyServiceName 2>$null | Out-Null
    Remove-NetFirewallRule -DisplayName "Schedule I Backpack HTTPS (TCP)" -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName "Schedule I Backpack HTTPS (QUIC)" -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $script:InstallRoot -Recurse -Force
    Write-Host "Backpack server and database removed." -ForegroundColor Green
    Write-Host "PostgreSQL 18 and Python 3.13 were deliberately left installed."
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
