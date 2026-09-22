param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
$docker = if ($dockerCommand) { $dockerCommand.Source } else { 'C:\Program Files\Docker\Docker\resources\bin\docker.exe' }
$sdk = Join-Path $root '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
& (Join-Path $PSScriptRoot 'Initialize-Dev.ps1')
$project = 'riftchecks-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$saved = @{}
foreach ($name in @('RIFT_PORT','RIFT_FIXTURE_MODE','RIFT_SERVER_URL','RIFT_SERVER_ACCESS_KEY_FILE')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$env:RIFT_PORT = "$port"; $env:RIFT_FIXTURE_MODE = 'true'
$env:RIFT_SERVER_URL = "http://127.0.0.1:$port/"
$env:RIFT_SERVER_ACCESS_KEY_FILE = Join-Path $PSScriptRoot '.secrets/access-key'
$compose = @('compose', '-p', $project, '-f', (Join-Path $PSScriptRoot 'compose.yaml'), '--profile', 'checks')
try {
    if (-not $SkipBuild) { & $docker @compose build; if ($LASTEXITCODE) { throw 'Docker build failed.' } }
    & $docker @compose run --rm checks
    if ($LASTEXITCODE) { throw 'PostgreSQL integration checks failed.' }
    & $docker @compose up -d api worker
    if ($LASTEXITCODE) { throw 'Backend startup failed.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { $null = Invoke-RestMethod -Uri ($env:RIFT_SERVER_URL + 'health') -TimeoutSec 2; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'API did not become ready.' }
    & $sdk run --project (Join-Path $PSScriptRoot 'Rift.ClientChecks/Rift.ClientChecks.csproj') -c Release
    if ($LASTEXITCODE) { throw 'REST/SignalR checks failed.' }
} finally {
    # The random project created above owns this disposable test volume; never targets the dev deployment.
    & $docker @compose down --volumes --remove-orphans
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
