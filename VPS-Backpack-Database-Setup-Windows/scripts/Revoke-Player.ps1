. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    $steamId = Get-SteamId "SteamID64 to revoke"
    $answer = (Read-Host "Revoke every backpack token for $steamId? Type REVOKE").Trim()
    if ($answer -cne "REVOKE") { Write-Host "Cancelled."; exit 0 }
    Invoke-ApiAdmin -Arguments @("revoke-all", $steamId)
    Write-Host "Player tokens revoked." -ForegroundColor Green
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
