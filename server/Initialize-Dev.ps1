param([switch]$Demo)
$ErrorActionPreference = 'Stop'
$secretDirectory = Join-Path $PSScriptRoot '.secrets'
New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
foreach ($name in @('postgres-password', 'access-key')) {
    $path = Join-Path $secretDirectory $name
    if (-not (Test-Path -LiteralPath $path)) {
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        [IO.File]::WriteAllText($path, [Convert]::ToBase64String($bytes))
    }
}
$riotPath = Join-Path $secretDirectory 'riot-key'
if (-not (Test-Path -LiteralPath $riotPath)) { [IO.File]::WriteAllText($riotPath, '') }
$envPath = Join-Path $PSScriptRoot '.env'
if (Test-Path -LiteralPath $envPath) {
    Write-Host 'Configuration existante conservée. Ajustez server/.env si nécessaire.'
} else {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '.env.example'))
    if ($Demo) { $text = $text.Replace('RIFT_FIXTURE_MODE=false', 'RIFT_FIXTURE_MODE=true') }
    [IO.File]::WriteAllText($envPath, $text)
}
Write-Host 'Secrets de développement initialisés (non affichés, exclus de Git et des images Docker).'
