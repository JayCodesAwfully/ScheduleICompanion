. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    Read-ServerConfig | Out-Null
    Enable-ScheduledTask -TaskName $script:TaskName | Out-Null
    Start-ScheduledTask -TaskName $script:TaskName
    Set-Service -Name $script:CaddyServiceName -StartupType Automatic
    Start-Service -Name $script:CaddyServiceName
    Write-Host "Backpack API and HTTPS service enabled and started." -ForegroundColor Green
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
