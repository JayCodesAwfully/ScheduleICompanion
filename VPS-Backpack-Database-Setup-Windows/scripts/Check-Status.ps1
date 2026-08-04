. "$PSScriptRoot\Common.ps1"

try {
    Assert-Administrator
    $config = Read-ServerConfig
    Write-Host "`nSchedule I Backpack Server" -ForegroundColor Cyan
    Write-Host "Public URL: $($config['PUBLIC_URL'])"

    $postgres = Get-Service -Name "postgresql-x64-18" -ErrorAction SilentlyContinue
    if ($null -eq $postgres) { Write-Host "PostgreSQL: MISSING" -ForegroundColor Red }
    else { Write-Host "PostgreSQL: $($postgres.Status)" -ForegroundColor $(if ($postgres.Status -eq 'Running') {'Green'} else {'Red'}) }

    $task = Get-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) { Write-Host "Backpack API: MISSING" -ForegroundColor Red }
    else {
        $info = Get-ScheduledTaskInfo -TaskName $script:TaskName
        Write-Host "Backpack API task: $($task.State) (last result $($info.LastTaskResult))" -ForegroundColor $(if ($task.State -eq 'Running') {'Green'} else {'Yellow'})
    }

    $caddy = Get-Service -Name $script:CaddyServiceName -ErrorAction SilentlyContinue
    if ($null -eq $caddy) { Write-Host "HTTPS service: MISSING" -ForegroundColor Red }
    else { Write-Host "HTTPS service: $($caddy.Status)" -ForegroundColor $(if ($caddy.Status -eq 'Running') {'Green'} else {'Red'}) }

    try {
        $local = Invoke-RestMethod -Uri "http://127.0.0.1:8080/health" -TimeoutSec 5
        Write-Host "Local API health: $($local.status)" -ForegroundColor Green
    } catch { Write-Host "Local API health: FAILED - $($_.Exception.Message)" -ForegroundColor Red }

    try {
        $public = Invoke-RestMethod -Uri "$($config['PUBLIC_URL'])/health" -TimeoutSec 15
        Write-Host "Public HTTPS health: $($public.status)" -ForegroundColor Green
    } catch {
        Write-Host "Public HTTPS health: FAILED" -ForegroundColor Red
        if ($config['HOST_IS_IP'] -eq 'True') {
            Write-Host "Check that this is the VPS public IP and inbound TCP 80/443 are permitted." -ForegroundColor Yellow
        } else {
            Write-Host "Check the DNS A record and that inbound TCP 80/443 are permitted." -ForegroundColor Yellow
        }
    }

    Write-Host "`nListening ports (5432 and 8080 should only use 127.0.0.1):"
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in @(80,443,5432,8080) } |
        Sort-Object LocalPort | Format-Table LocalAddress, LocalPort, OwningProcess -AutoSize
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
