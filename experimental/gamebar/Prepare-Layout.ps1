$ErrorActionPreference = 'Stop'
$publish = Join-Path $PSScriptRoot 'bin/x64/Release/net9.0-windows10.0.26100.0/win-x64/publish'
$build = Split-Path $publish -Parent
$output = Join-Path $PSScriptRoot 'package'
$layout = Join-Path $output 'layout'
if (-not (Test-Path (Join-Path $publish 'Rift.GameBarProbe.exe'))) { throw 'Publier le prototype avant de preparer le dossier.' }
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item (Join-Path $publish '*') $layout -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Assets') $layout -Recurse -Force
foreach ($name in @('Microsoft.Gaming.XboxGameBar.winmd','Microsoft.Gaming.XboxGameBar.Private.winmd')) {
    Copy-Item (Join-Path $build $name) $layout -Force
}
[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture','x64')
$manifest.Package.Resources.Resource.SetAttribute('Language','fr-FR')
$manifest.Package.Dependencies.TargetDeviceFamily.SetAttribute('Name','Windows.Desktop')
$manifest.Package.Dependencies.TargetDeviceFamily.SetAttribute('MinVersion','10.0.19041.0')
$manifest.Package.Dependencies.TargetDeviceFamily.SetAttribute('MaxVersionTested','10.0.26100.0')
$manifest.Package.Applications.Application.SetAttribute('Executable','Rift.GameBarProbe.exe')
$manifest.Save((Join-Path $layout 'AppxManifest.xml'))
foreach ($name in @('Rift.GameBarProbe.exe','resources.pri','Microsoft.Gaming.XboxGameBar.dll','Microsoft.Gaming.XboxGameBar.pri','Microsoft.Gaming.XboxGameBar.winmd','Assets/StoreLogo.png')) {
    if (-not (Test-Path (Join-Path $layout $name))) { throw "Fichier requis absent : $name" }
}
Copy-Item (Join-Path $PSScriptRoot 'Install-Probe.ps1') $output -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') $output -Force
Copy-Item (Join-Path $PSScriptRoot 'NOTICE.txt') $output -Force
Write-Host "Dossier experimental prepare : $output"
