$ErrorActionPreference = "Stop"

$script:InstallRoot = Join-Path $env:ProgramData "ScheduleIBackpackServer"
$script:ConfigPath = Join-Path $script:InstallRoot "server.env"
$script:TaskName = "ScheduleI Backpack API"
$script:CaddyServiceName = "ScheduleIBackpackCaddy"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this tool as Administrator."
    }
}

function Read-ServerConfig {
    if (-not (Test-Path -LiteralPath $script:ConfigPath)) {
        throw "The server is not installed. Run SETUP-WINDOWS-VPS.bat first."
    }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $script:ConfigPath) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) { continue }
        $parts = $line.Split(@('='), 2)
        if ($parts.Count -eq 2) { $values[$parts[0].Trim()] = $parts[1].Trim() }
    }
    return $values
}

function Set-ApiEnvironment {
    param([hashtable]$Config)
    $env:DATABASE_URL = $Config["DATABASE_URL"]
    $env:TOKEN_PEPPER = $Config["TOKEN_PEPPER"]
}

function Set-PostgresClientPassword {
    param([hashtable]$Config)
    $url = $Config["DATABASE_URL"]
    if ($url -notmatch '^postgresql://backpack_api:([^@]+)@127\.0\.0\.1:5432/backpacks$') {
        throw "The database connection setting has an unexpected format."
    }
    $env:PGPASSWORD = $matches[1]
}

function Get-PythonExe {
    $path = Join-Path $script:InstallRoot "venv\Scripts\python.exe"
    if (-not (Test-Path -LiteralPath $path)) { throw "Python environment is missing: $path" }
    return $path
}

function Get-PostgresBin {
    $service = Get-CimInstance Win32_Service -Filter "Name LIKE 'postgresql-x64-%'" |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($null -ne $service -and $service.PathName -match '^"?([^" ]+\\pg_ctl\.exe)') {
        return Split-Path -Parent $matches[1]
    }
    $candidate = Get-ChildItem "C:\Program Files\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+$' } |
        Sort-Object { [int]$_.Name } -Descending | Select-Object -First 1
    if ($null -eq $candidate) { throw "PostgreSQL was not found." }
    return Join-Path $candidate.FullName "bin"
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Protect-ServerFile {
    param([string]$Path)
    & icacls.exe $Path /inheritance:r /grant:r "SYSTEM:(F)" "Administrators:(F)" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not secure $Path" }
}

function Invoke-ApiAdmin {
    param([Parameter(Mandatory=$true)][string[]]$Arguments)
    $config = Read-ServerConfig
    Set-ApiEnvironment $config
    $python = Get-PythonExe
    Push-Location (Join-Path $script:InstallRoot "api")
    try {
        & $python -m app.admin @Arguments
        if ($LASTEXITCODE -ne 0) { throw "The account command failed." }
    } finally { Pop-Location }
}

function Get-SteamId {
    param([string]$Prompt = "SteamID64")
    $value = (Read-Host $Prompt).Trim()
    if ($value -notmatch '^7656119[0-9]{10}$') { throw "Enter the 17-digit SteamID64 beginning 7656119." }
    return $value
}

function Read-PrivateText {
    param([string]$Prompt)
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
