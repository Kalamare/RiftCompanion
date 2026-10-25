$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../goverlay/Test-Build.ps1')
$result = Get-Content (Join-Path $PSScriptRoot '../goverlay/reports/result.json') -Raw | ConvertFrom-Json
if (-not $result.built) { throw 'Native GOverlay build did not succeed.' }
$source = $result.sourceDirectory
& cmake -S $PSScriptRoot -B (Join-Path $PSScriptRoot 'build-goverlay') -A x64 "-DGOVERLAY_SOURCE=$source"
if ($LASTEXITCODE) { throw 'Bridge configuration failed.' }
& cmake --build (Join-Path $PSScriptRoot 'build-goverlay') --config Release
if ($LASTEXITCODE) { throw 'Bridge build failed.' }
$output = Join-Path $PSScriptRoot 'build-goverlay/Release'
Copy-Item (Join-Path $source 'game-overlay/sln/bin/Release/n_overlay.x64.dll') $output -Force
# Keep the upstream attribution beside this local development build.
Copy-Item (Join-Path $source 'LICENSE') (Join-Path $output 'GOverlay-LICENSE.txt') -Force
Write-Host "Compiled locally. Run $output/RiftDx11Probe.exe --goverlay for manual rendering checks."
