param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sdkPath = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdkPath)) { $sdkPath = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.tools\nuget'
Push-Location $PSScriptRoot
try {
    & $sdkPath build Rift.Desktop/Rift.Desktop.csproj -c Release -o (Join-Path $repoRoot 'artifacts\native-profile-v20')
    if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée.' }
    if ($Check) {
        & $sdkPath run --project Rift.Checks/Rift.Checks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Vérifications échouées.' }
        & $sdkPath run --project Rift.UiChecks/Rift.UiChecks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Vérifications WPF échouées.' }
    }
} finally { Pop-Location }
