$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot 'Rift.Desktop/Assets/Champions'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$version = (Invoke-RestMethod 'https://ddragon.leagueoflegends.com/api/versions.json')[0]
Invoke-WebRequest "https://ddragon.leagueoflegends.com/cdn/$version/data/fr_FR/summoner.json" -OutFile (Join-Path $folder 'summoner-fr.json')
$catalog = Get-Content (Join-Path $folder 'summoner-fr.json') -Raw | ConvertFrom-Json
$catalog.data.PSObject.Properties.Value | ForEach-Object -Parallel {
    $file = $_.image.full
    if ($file -notmatch '^[A-Za-z0-9_-]+\.png$') { throw 'Nom de sort invalide.' }
    $path = Join-Path $using:folder $file
    Invoke-WebRequest "https://ddragon.leagueoflegends.com/cdn/$using:version/img/spell/$file" -OutFile $path
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 8 -or [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Sort PNG invalide.' }
} -ThrottleLimit 6
"Sorts officiels Riot Data Dragon $version : https://ddragon.leagueoflegends.com/cdn/$version/data/fr_FR/summoner.json. Propriété de Riot Games. Rafraîchir avec native/UpdateSpells.ps1 (PowerShell 7)." | Set-Content (Join-Path $folder 'SPELLS-SOURCE.md') -Encoding utf8
Write-Output "Catalogue $version : sorts livrés."
