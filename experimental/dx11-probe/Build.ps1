$ErrorActionPreference = 'Stop'
& cmake -S $PSScriptRoot -B (Join-Path $PSScriptRoot 'build') -A x64
if ($LASTEXITCODE) { throw 'CMake configuration failed.' }
& cmake --build (Join-Path $PSScriptRoot 'build') --config Release
if ($LASTEXITCODE) { throw 'DX11 probe build failed.' }
& ctest --test-dir (Join-Path $PSScriptRoot 'build') -C Release --output-on-failure
if ($LASTEXITCODE) { throw 'DX11 WARP lifecycle test failed.' }
