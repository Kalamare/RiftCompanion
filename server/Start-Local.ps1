param(
    [ValidateRange(1024,65535)][int]$Port = 5081,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Get-Command docker -ErrorAction Stop | Out-Null
& docker info --format '{{.OSType}}' | Out-Null
if ($LASTEXITCODE) { throw 'Demarre Docker Desktop avec les conteneurs Linux.' }
if (-not $SkipBuild) { Get-Command dotnet -ErrorAction Stop | Out-Null }
& (Join-Path $PSScriptRoot 'Initialize-Dev.ps1')
$keyPath = Join-Path $PSScriptRoot '.secrets/riot-key'
if ([string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($keyPath))) {
    $secure = Read-Host 'Cle Riot du portail developer.riotgames.com (saisie masquee)' -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim()
        if ([string]::IsNullOrWhiteSpace($key)) { throw 'Cle Riot vide.' }
        [IO.File]::WriteAllText($keyPath, $key)
    } finally {
        $key = $null
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secure.Dispose()
    }
}
$saved = @{}
foreach ($name in @('RIFT_PORT','RIFT_FIXTURE_MODE','RIFT_HISTORY_BACKFILL','RIFT_SERVER_URL','RIFT_SERVER_ACCESS_KEY_FILE')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:RIFT_PORT = "$Port"
    $env:RIFT_FIXTURE_MODE = 'false'
    $env:RIFT_HISTORY_BACKFILL = 'false'
    $env:RIFT_SERVER_URL = "http://127.0.0.1:$Port/"
    $env:RIFT_SERVER_ACCESS_KEY_FILE = Join-Path $PSScriptRoot '.secrets/access-key'
    $compose = @('compose','-p','rift-real','-f',(Join-Path $PSScriptRoot 'compose.yaml'))
    if (-not $SkipBuild) {
        & (Join-Path $root 'native/Build.ps1') -Check
        & docker @compose build api worker
        if ($LASTEXITCODE) { throw 'Compilation Docker echouee.' }
    }
    & docker @compose up -d api worker
    if ($LASTEXITCODE) { throw 'Demarrage du backend echoue.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $health = Invoke-RestMethod ($env:RIFT_SERVER_URL + 'health') -TimeoutSec 2
            if ($health.data -eq 'riot') { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Le backend reel ne repond pas. Consulte docker compose -p rift-real -f server/compose.yaml logs --tail 50.' }
    Write-Host "Backend reel pret sur $($env:RIFT_SERVER_URL). Cela ne valide pas encore la cle Riot."
    & (Join-Path $root 'native/Start-Client.ps1')
    if ($LASTEXITCODE) { throw 'Lancement du client echoue.' }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
