$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 requis.' }
if (-not (Get-AppxPackage -Name Microsoft.XboxGamingOverlay)) { throw 'Installer ou activer Windows Game Bar avant ce test.' }
$settings = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if ($settings.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'Prototype non signe : activer volontairement le Mode developpeur dans les parametres Windows Pour les developpeurs, puis relancer. Aucun certificat a importer.'
}
$manifestPath = Join-Path $PSScriptRoot 'layout/AppxManifest.xml'
if (-not (Test-Path $manifestPath)) { throw 'Extraire toute l archive, puis lancer ce script depuis son dossier.' }
[xml]$manifest = Get-Content $manifestPath -Raw
if ($manifest.Package.Identity.Name -ne 'Rift.GameBarProbe') { throw 'Identite de paquet inattendue.' }
Add-AppxPackage -Register $manifestPath
Write-Host 'Prototype enregistre pour cet utilisateur. Dans LoL, ouvrir Win+G et choisir Rift Companion - test plein ecran dans les widgets.'
Write-Host 'Conserver ce dossier jusqu a la desinstallation. Les donnees Rift Companion ne sont pas modifiees.'
