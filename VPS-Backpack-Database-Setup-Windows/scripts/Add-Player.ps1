. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    $steamId = Get-SteamId
    $label = (Read-Host "Player name or device label").Trim()
    if ([string]::IsNullOrWhiteSpace($label)) { $label = "Windows companion" }
    $output = Invoke-ApiAdmin -Arguments @("create-token", $steamId, $label) | Out-String
    $tokenLine = ($output -split "`r?`n" | Where-Object { $_ -like "PLAYER_TOKEN=*" } | Select-Object -First 1)
    if (-not $tokenLine) { throw "The server did not return a player token." }
    $token = $tokenLine.Substring("PLAYER_TOKEN=".Length)
    $target = Join-Path (Split-Path -Parent $PSScriptRoot) "PLAYER-TOKEN-$steamId.txt"
    Write-Utf8NoBom -Path $target -Content @"
Schedule I Backpack player token
SteamID64: $steamId
Label: $label
Server: $((Read-ServerConfig)["PUBLIC_URL"])

$token

Keep this token private. Give it only to this player.
"@
    Write-Host "`nPlayer added." -ForegroundColor Green
    Write-Host "Token saved to: $target" -ForegroundColor Cyan
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
