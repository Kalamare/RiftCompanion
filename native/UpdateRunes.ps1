$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot 'Rift.Desktop/Assets/Runes'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$version = (Invoke-RestMethod 'https://ddragon.leagueoflegends.com/api/versions.json')[0]
$url = "https://ddragon.leagueoflegends.com/cdn/$version/data/fr_FR/runesReforged.json"
$trees = Invoke-RestMethod $url
$runes = @($trees | ForEach-Object { $_; $_.slots | ForEach-Object { $_.runes } })
$runes | ForEach-Object -Parallel {
    $icon = $_.icon
    if ($icon -notmatch '^perk-images/[A-Za-z0-9_/-]+\.png$' -or $icon.Contains('..')) { throw 'Chemin de rune invalide.' }
    $path = Join-Path $using:folder "$($_.id).png"
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest "https://ddragon.leagueoflegends.com/cdn/img/$icon" -OutFile $path }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 8 -or [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Rune PNG invalide.' }
} -ThrottleLimit 6
$data = @{}
foreach ($rune in $runes) { $data[[string]$rune.id] = @{ name = $rune.name; description = $rune.longDesc; image = @{ full = "$($rune.id).png" } } }
@{ data = $data } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $folder 'runes-fr.json') -Encoding utf8
"Runes et arbres officiels Riot Data Dragon $version. Source : $url ; images https://ddragon.leagueoflegends.com/cdn/img/. Propriété de Riot Games. Rafraîchir avec native/UpdateRunes.ps1 (PowerShell 7)." | Set-Content (Join-Path $folder 'SOURCE.md') -Encoding utf8
Write-Output "$($runes.Count) runes et arbres livrés ($version)."
