$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot 'Rift.Desktop/Assets/Champions'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$version = (Invoke-RestMethod 'https://ddragon.leagueoflegends.com/api/versions.json')[0]
$catalogUrl = "https://ddragon.leagueoflegends.com/cdn/$version/data/fr_FR/champion.json"
Invoke-WebRequest $catalogUrl -OutFile (Join-Path $folder 'champion-fr.json')
$catalog = Get-Content (Join-Path $folder 'champion-fr.json') -Raw | ConvertFrom-Json
$catalog.data.PSObject.Properties.Value | ForEach-Object -Parallel {
    $file = $_.image.full
    if ($file -notmatch '^[A-Za-z0-9_-]+\.png$') { throw 'Nom de portrait invalide.' }
    $path = Join-Path $using:folder $file
    Invoke-WebRequest "https://ddragon.leagueoflegends.com/cdn/$using:version/img/champion/$file" -OutFile $path
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 8 -or [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Portrait PNG invalide.' }
} -ThrottleLimit 8
"Portraits officiels Riot Data Dragon, catalogue $version, téléchargés le $(Get-Date -Format yyyy-MM-dd). Source : $catalogUrl. Propriété de Riot Games. Rafraîchir avec native/UpdatePortraits.ps1 (PowerShell 7)." | Set-Content (Join-Path $folder 'SOURCE.md') -Encoding utf8
Write-Output "Catalogue $version : $($catalog.data.PSObject.Properties.Value.Count) portraits livrés."
