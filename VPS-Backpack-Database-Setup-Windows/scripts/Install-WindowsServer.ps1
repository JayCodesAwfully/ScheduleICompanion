$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Common.ps1"

$packageRoot = Split-Path -Parent $PSScriptRoot
$downloadRoot = Join-Path $env:TEMP "ScheduleIBackpackSetup"
$postgresVersion = "18.4-1"
$pythonVersion = "3.13.14"
$completionMarker = Join-Path $script:InstallRoot "install.complete"

function New-SafeSecret {
    param([int]$Bytes = 36)
    $buffer = New-Object byte[] $Bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return [Convert]::ToBase64String($buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-VerifiedInstaller {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$ExpectedSigner
    )
    Write-Host "Downloading $([IO.Path]::GetFileName($Destination))..." -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Destination
    $signature = Get-AuthenticodeSignature -FilePath $Destination
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "The downloaded installer has no valid digital signature: $Destination"
    }
    if ($signature.SignerCertificate.Subject -notmatch $ExpectedSigner) {
        throw "Unexpected installer publisher: $($signature.SignerCertificate.Subject)"
    }
}

function Wait-Api {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $response = Invoke-RestMethod -Uri "http://127.0.0.1:8080/health" -TimeoutSec 2
            if ($response.status -eq "healthy") { return }
        } catch { Start-Sleep -Seconds 1 }
    }
    throw "The Backpack API did not become healthy. Run CHECK-STATUS.bat for details."
}

function Test-PublicIPv4 {
    param([string]$Value)
    $address = $null
    if (-not [Net.IPAddress]::TryParse($Value, [ref]$address)) { return $false }
    if ($address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { return $false }
    $bytes = $address.GetAddressBytes()
    if ($bytes[0] -in @(0, 10, 127) -or $bytes[0] -ge 224) { return $false }
    if ($bytes[0] -eq 169 -and $bytes[1] -eq 254) { return $false }
    if ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) { return $false }
    if ($bytes[0] -eq 192 -and $bytes[1] -eq 168) { return $false }
    if ($bytes[0] -eq 100 -and $bytes[1] -ge 64 -and $bytes[1] -le 127) { return $false }
    return $true
}

function Enable-WindowsInstaller {
    $service = Get-Service -Name "msiserver" -ErrorAction SilentlyContinue
    if ($null -eq $service) { throw "The Windows Installer service (msiserver) is missing from this VPS." }
    try {
        Set-Service -Name "msiserver" -StartupType Manual
        Start-Service -Name "msiserver" -ErrorAction Stop
    } catch {
        Write-Host "Repairing Windows Installer registration..." -ForegroundColor Yellow
        $msiexec = Join-Path $env:SystemRoot "System32\msiexec.exe"
        & $msiexec /unregister
        & $msiexec /regserver
        Set-Service -Name "msiserver" -StartupType Manual
        Start-Service -Name "msiserver" -ErrorAction Stop
    }
    $service = Get-Service -Name "msiserver"
    if ($service.Status -ne 'Running') { throw "The Windows Installer service could not be started." }
}

try {
    Assert-Administrator
    $os = Get-CimInstance Win32_OperatingSystem
    $build = [int]$os.BuildNumber
    if ($os.ProductType -eq 1) { throw "This package is for Windows Server, not a desktop edition of Windows." }
    if ($build -lt 26100) { throw "Windows Server 2025 build 26100 or newer is required. Detected build: $build" }

    Write-Host "`nSchedule I Backpack Server" -ForegroundColor Green
    Write-Host "$($os.Caption), build $build"
    Write-Host "This installs PostgreSQL, Python, the Backpack API and Caddy HTTPS locally."
    Write-Host "No Docker, WSL, Hyper-V or reboot is required.`n"

    if (Test-Path -LiteralPath $completionMarker) {
        throw "A server is already configured at $script:InstallRoot. Use UPDATE-SERVER.bat or UNINSTALL-SERVER.bat."
    }
    if (Test-Path -LiteralPath $script:ConfigPath) {
        Write-Host "An incomplete Backpack configuration was found and will be repaired." -ForegroundColor Yellow
        Remove-Item -LiteralPath $script:ConfigPath -Force
    }

    $serverHost = (Read-Host "Public hostname or VPS IPv4 address").Trim().ToLowerInvariant()
    $isPublicIp = Test-PublicIPv4 $serverHost
    $isDomain = $serverHost -match '^(?=.{4,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$'
    if (-not $isPublicIp -and -not $isDomain) {
        throw "Enter a public IPv4 address or hostname such as backpack.example.com."
    }
    $email = (Read-Host "Email address for HTTPS certificate notices").Trim()
    if ($email -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw "Enter a valid email address." }
    $firstSteamId = Get-SteamId "Your SteamID64"
    if ($isDomain) {
        Write-Host "`nBefore continuing, the DNS A record for $serverHost must point to this VPS." -ForegroundColor Yellow
    } else {
        Write-Host "`nIP mode selected. Ports 80 and 443 must reach $serverHost directly." -ForegroundColor Yellow
        Write-Host "The installer will request an automatically renewed six-day IP certificate."
    }
    $confirm = (Read-Host "Type INSTALL to continue").Trim()
    if ($confirm -cne "INSTALL") { Write-Host "Cancelled."; exit 0 }

    New-Item -ItemType Directory -Force -Path $downloadRoot, $script:InstallRoot | Out-Null
    $postgresService = Get-Service -Name "postgresql-x64-18" -ErrorAction SilentlyContinue
    $resetPostgresPassword = $false
    if ($null -eq $postgresService) {
        $postgresInstaller = Join-Path $downloadRoot "postgresql-$postgresVersion-windows-x64.exe"
        Get-VerifiedInstaller `
            -Url "https://get.enterprisedb.com/postgresql/postgresql-$postgresVersion-windows-x64.exe" `
            -Destination $postgresInstaller `
            -ExpectedSigner 'EnterpriseDB|EDB'
        $postgresSuperPassword = New-SafeSecret 42
        Write-Host "Installing PostgreSQL 18..." -ForegroundColor Cyan
        $pgArguments = @(
            "--mode", "unattended",
            "--unattendedmodeui", "minimal",
            "--superpassword", $postgresSuperPassword,
            "--serverport", "5432",
            "--servicename", "postgresql-x64-18"
        )
        $process = Start-Process -FilePath $postgresInstaller -ArgumentList $pgArguments -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "PostgreSQL installer returned $($process.ExitCode)." }
    } else {
        Write-Host "PostgreSQL 18 is already installed; it will be reused." -ForegroundColor DarkGray
        Write-Host "If the previous Backpack setup stopped after installing PostgreSQL, type RESET." -ForegroundColor Yellow
        $existingChoice = (Read-Host "Type RESET for interrupted-setup recovery, or press Enter to provide the existing password").Trim()
        if ($existingChoice -ceq "RESET") {
            $postgresSuperPassword = New-SafeSecret 42
            $resetPostgresPassword = $true
        } else {
            $postgresSuperPassword = Read-PrivateText "Enter the existing PostgreSQL 'postgres' account password"
            if ([string]::IsNullOrWhiteSpace($postgresSuperPassword)) { throw "The PostgreSQL password is required." }
        }
    }

    $pgBin = Get-PostgresBin
    $psql = Join-Path $pgBin "psql.exe"
    $pgServiceInfo = Get-CimInstance Win32_Service -Filter "Name='postgresql-x64-18'"
    if ($pgServiceInfo.PathName -notmatch '(?i)-D\s+"([^"]+)"|-D\s+([^\s]+)') {
        throw "Could not find the PostgreSQL data directory."
    }
    $pgData = if ($matches[1]) { $matches[1] } else { $matches[2] }
    $postgresConfig = Join-Path $pgData "postgresql.conf"
    if ($resetPostgresPassword) {
        $hbaPath = Join-Path $pgData "pg_hba.conf"
        $originalHba = Get-Content -LiteralPath $hbaPath -Raw
        Write-Host "Recovering the interrupted PostgreSQL installation..." -ForegroundColor Cyan
        try {
            $temporaryHba = "host all postgres 127.0.0.1/32 trust`r`nhost all postgres ::1/128 trust`r`n" + $originalHba
            Write-Utf8NoBom -Path $hbaPath -Content $temporaryHba
            Restart-Service -Name "postgresql-x64-18" -Force
            & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -c "ALTER ROLE postgres PASSWORD '$postgresSuperPassword';"
            if ($LASTEXITCODE -ne 0) { throw "Could not reset the PostgreSQL administrator password." }
        } finally {
            Write-Utf8NoBom -Path $hbaPath -Content $originalHba
            Restart-Service -Name "postgresql-x64-18" -Force
        }
        Write-Host "Interrupted installation recovered." -ForegroundColor Green
    }
    $postgresText = Get-Content -LiteralPath $postgresConfig -Raw
    if ($postgresText -match '(?m)^\s*#?\s*listen_addresses\s*=.*$') {
        $postgresText = $postgresText -replace '(?m)^\s*#?\s*listen_addresses\s*=.*$', "listen_addresses = '127.0.0.1'"
    } else { $postgresText += "`r`nlisten_addresses = '127.0.0.1'`r`n" }
    Write-Utf8NoBom -Path $postgresConfig -Content $postgresText
    Restart-Service -Name "postgresql-x64-18" -Force

    $apiDbPassword = New-SafeSecret 42
    $env:PGPASSWORD = $postgresSuperPassword
    $roleExists = & $psql -h 127.0.0.1 -U postgres -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='backpack_api'"
    if ($LASTEXITCODE -ne 0) { throw "Could not sign in to PostgreSQL. Check the postgres account password." }
    if ([string]$roleExists -ne "1") {
        & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE ROLE backpack_api LOGIN PASSWORD '$apiDbPassword';"
        if ($LASTEXITCODE -ne 0) { throw "Could not create the Backpack database account." }
    } else {
        & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -c "ALTER ROLE backpack_api PASSWORD '$apiDbPassword';"
        if ($LASTEXITCODE -ne 0) { throw "Could not update the Backpack database account." }
    }
    $databaseExists = & $psql -h 127.0.0.1 -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='backpacks'"
    if ($LASTEXITCODE -ne 0) { throw "Could not check the backpacks database." }
    if ([string]$databaseExists -ne "1") {
        & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE backpacks OWNER backpack_api;"
        if ($LASTEXITCODE -ne 0) { throw "Could not create the backpacks database." }
    }
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue

    $systemPython = "C:\Program Files\Python313\python.exe"
    if (-not (Test-Path -LiteralPath $systemPython)) {
        Write-Host "Checking the Windows Installer service..." -ForegroundColor Cyan
        Enable-WindowsInstaller
        $pythonInstaller = Join-Path $downloadRoot "python-$pythonVersion-amd64.exe"
        $pythonInstallLog = Join-Path $downloadRoot "python-install.log"
        Get-VerifiedInstaller `
            -Url "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-amd64.exe" `
            -Destination $pythonInstaller `
            -ExpectedSigner 'Python Software Foundation'
        Write-Host "Installing Python $pythonVersion..." -ForegroundColor Cyan
        $pyArguments = @(
            "/quiet", "/log", $pythonInstallLog,
            "InstallAllUsers=1", "PrependPath=0", "Include_test=0",
            "Include_launcher=0", 'TargetDir="C:\Program Files\Python313"'
        )
        $process = Start-Process -FilePath $pythonInstaller -ArgumentList $pyArguments -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Python installer returned $($process.ExitCode). Log: $pythonInstallLog" }
        if (-not (Test-Path -LiteralPath $systemPython)) { throw "Python reported success but python.exe was not installed." }
    }

    $apiTarget = Join-Path $script:InstallRoot "api"
    New-Item -ItemType Directory -Force -Path $apiTarget | Out-Null
    Copy-Item -Recurse -Force (Join-Path $packageRoot "server\api\app") $apiTarget
    Copy-Item -Force (Join-Path $packageRoot "server\api\requirements.txt") $apiTarget
    $venvPython = Join-Path $script:InstallRoot "venv\Scripts\python.exe"
    if (-not (Test-Path -LiteralPath $venvPython)) { & $systemPython -m venv (Join-Path $script:InstallRoot "venv") }
    Write-Host "Installing the Backpack API..." -ForegroundColor Cyan
    & $venvPython -m pip install --disable-pip-version-check --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw "Could not update pip." }
    & $venvPython -m pip install --disable-pip-version-check -r (Join-Path $apiTarget "requirements.txt")
    if ($LASTEXITCODE -ne 0) { throw "Could not install API dependencies." }

    $tokenPepper = New-SafeSecret 64
    $configText = @"
DATABASE_URL=postgresql://backpack_api:$apiDbPassword@127.0.0.1:5432/backpacks
TOKEN_PEPPER=$tokenPepper
PUBLIC_URL=https://$serverHost
SERVER_HOST=$serverHost
HOST_IS_IP=$isPublicIp
CERTIFICATE_EMAIL=$email
"@
    Write-Utf8NoBom -Path $script:ConfigPath -Content $configText
    Protect-ServerFile -Path $script:ConfigPath

    $runner = Join-Path $script:InstallRoot "Run-Api.ps1"
    Write-Utf8NoBom -Path $runner -Content @'
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($line in Get-Content (Join-Path $root "server.env")) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) { continue }
    $parts = $line.Split(@('='), 2)
    if ($parts.Count -eq 2) { [Environment]::SetEnvironmentVariable($parts[0], $parts[1], "Process") }
}
Set-Location (Join-Path $root "api")
& (Join-Path $root "venv\Scripts\python.exe") -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --workers 1
exit $LASTEXITCODE
'@
    Protect-ServerFile -Path $runner

    $config = Read-ServerConfig
    Set-ApiEnvironment $config
    Push-Location $apiTarget
    try {
        & $venvPython -m app.selftest
        if ($LASTEXITCODE -ne 0) { throw "Database self-test failed." }
    } finally { Pop-Location }

    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$runner`"" -WorkingDirectory $script:InstallRoot
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $script:TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $script:TaskName
    Wait-Api

    $caddyRoot = Join-Path $script:InstallRoot "caddy"
    New-Item -ItemType Directory -Force -Path $caddyRoot | Out-Null
    $caddyZip = Join-Path $downloadRoot "caddy-windows-amd64.zip"
    Write-Host "Downloading Caddy HTTPS server..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri "https://caddyserver.com/api/download?os=windows&arch=amd64" -OutFile $caddyZip
    try { Expand-Archive -LiteralPath $caddyZip -DestinationPath $caddyRoot -Force }
    catch { throw "The Caddy download was not a valid ZIP archive: $($_.Exception.Message)" }
    $caddyExe = Get-ChildItem $caddyRoot -Filter caddy.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
    if (-not $caddyExe) { throw "caddy.exe was not found in the official download." }
    $caddyFile = Join-Path $caddyRoot "Caddyfile"
    $tlsConfiguration = ""
    if ($isPublicIp) {
        $tlsConfiguration = @"
    tls {
        issuer acme {
            ca https://acme-v02.api.letsencrypt.org/directory
            profile shortlived
        }
    }
"@
    }
    Write-Utf8NoBom -Path $caddyFile -Content @"
{
    email $email
}

$serverHost {
$tlsConfiguration
    encode zstd gzip
    header {
        -Server
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
    }
    reverse_proxy 127.0.0.1:8080
}
"@
    & $caddyExe validate --config $caddyFile --adapter caddyfile
    if ($LASTEXITCODE -ne 0) { throw "Caddy rejected the generated HTTPS configuration." }
    & sc.exe stop $script:CaddyServiceName 2>$null | Out-Null
    & sc.exe delete $script:CaddyServiceName 2>$null | Out-Null
    Start-Sleep -Seconds 1
    $caddyCommand = '"{0}" run --config "{1}" --adapter caddyfile' -f $caddyExe, $caddyFile
    & sc.exe create $script:CaddyServiceName binPath= $caddyCommand start= auto DisplayName= "Schedule I Backpack HTTPS" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create the Caddy Windows service." }
    & sc.exe failure $script:CaddyServiceName reset= 0 actions= restart/5000/restart/15000/restart/30000 | Out-Null
    & sc.exe start $script:CaddyServiceName | Out-Null
    Start-Sleep -Seconds 2
    $caddyStatus = Get-Service -Name $script:CaddyServiceName -ErrorAction SilentlyContinue
    if ($null -eq $caddyStatus -or $caddyStatus.Status -ne 'Running') {
        throw "The Caddy HTTPS service did not remain running."
    }

    foreach ($rule in @(
        @{ Name = "Schedule I Backpack HTTPS (TCP)"; Protocol = "TCP"; Ports = @(80,443) },
        @{ Name = "Schedule I Backpack HTTPS (QUIC)"; Protocol = "UDP"; Ports = @(443) }
    )) {
        Remove-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
        New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Action Allow -Protocol $rule.Protocol -LocalPort $rule.Ports -Profile Any | Out-Null
    }

    $firstLabel = "$env:COMPUTERNAME initial player"
    $tokenOutput = Invoke-ApiAdmin -Arguments @("create-token", $firstSteamId, $firstLabel) | Out-String
    $tokenLine = ($tokenOutput -split "`r?`n" | Where-Object { $_ -like "PLAYER_TOKEN=*" } | Select-Object -First 1)
    if (-not $tokenLine) { throw "The initial player token could not be created." }
    $playerToken = $tokenLine.Substring("PLAYER_TOKEN=".Length)
    $tokenFile = Join-Path $packageRoot "PLAYER-TOKEN-$firstSteamId.txt"
    Write-Utf8NoBom -Path $tokenFile -Content @"
Schedule I Backpack player token
SteamID64: $firstSteamId
Server: https://$serverHost

$playerToken

Keep this token private. Enter it only in this player's Companion.
"@
    Write-Utf8NoBom -Path $completionMarker -Content "Installed $(Get-Date -Format o)`r`n"
    Protect-ServerFile -Path $completionMarker

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host "BACKPACK SERVER INSTALLED" -ForegroundColor Green
    Write-Host "Server URL: https://$serverHost"
    Write-Host "Player token: $tokenFile"
    Write-Host "PostgreSQL: localhost only"
    Write-Host "API: localhost only; HTTPS is public on ports 80 and 443"
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "`nCaddy may need a minute to obtain the first HTTPS certificate."
    if ($isPublicIp) { Write-Host "IP certificates renew automatically and Caddy must remain running." }
    Write-Host "Run CHECK-STATUS.bat to verify it."
} catch {
    Write-Host "`nSETUP ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.InvocationInfo.ScriptLineNumber) {
        Write-Host "Script line: $($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor DarkGray
    }
    Write-Host "Nothing needs a reboot. Correct the problem and run setup again."
    exit 1
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
