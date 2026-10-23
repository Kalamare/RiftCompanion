param([Parameter(ValueFromRemainingArguments=$true)][string[]]$ClientArguments)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
try {
    if (-not $env:RIFT_SERVER_URL) {
        $port = if ($env:RIFT_PORT) { $env:RIFT_PORT } else { '5080' }
        $configuration = Join-Path $repo 'server/.env'
        if (-not $env:RIFT_PORT -and (Test-Path -LiteralPath $configuration)) {
            foreach ($line in [IO.File]::ReadLines($configuration)) {
                if ($line -match '^\s*RIFT_PORT=(\d+)\s*$') { $port = $Matches[1] }
            }
        }
        $number = 0
        if (-not [int]::TryParse($port, [ref]$number) -or $number -lt 1 -or $number -gt 65535) { throw 'Port serveur invalide.' }
        $env:RIFT_SERVER_URL = "http://127.0.0.1:$number/"
    }
    if (-not $env:RIFT_SERVER_ACCESS_KEY_FILE) { $env:RIFT_SERVER_ACCESS_KEY_FILE = Join-Path $repo 'server/.secrets/access-key' }
    if (-not (Test-Path -LiteralPath $env:RIFT_SERVER_ACCESS_KEY_FILE)) {
        throw 'Initialise le backend avec server/Initialize-Dev.ps1, puis démarre Docker Compose. La clé Riot se configure uniquement sur le serveur.'
    }
    $runtime = Join-Path $repo '.tools/dotnet'
    if (Test-Path -LiteralPath (Join-Path $runtime 'dotnet.exe')) { $env:DOTNET_ROOT = $runtime }
    $executable = Join-Path $repo 'artifacts/native-profile-v41/RiftCompanion.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'Compile la v41 avec native/Build.ps1.' }
    if ($ClientArguments.Count) { Start-Process -FilePath $executable -ArgumentList $ClientArguments }
    else { Start-Process -FilePath $executable }
} catch {
    Write-Host $_.Exception.Message
    exit 1
}
