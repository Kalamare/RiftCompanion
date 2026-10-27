# Qualification only: compile upstream sources; do not run an injector or game.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pins = Get-Content (Join-Path $PSScriptRoot 'upstream.json') -Raw | ConvertFrom-Json
$work = Join-Path $PSScriptRoot 'work'
$reports = Join-Path $PSScriptRoot 'reports'
New-Item -ItemType Directory -Force $work, $reports | Out-Null
Get-Command git, msbuild -ErrorAction Stop | Out-Null

function Get-PinnedSource([string]$url, [string]$revision, [string]$folder) {
    if ($revision -notmatch '^[0-9a-f]{40}$') { throw 'A full upstream commit is required.' }
    # Fresh directory avoids stale output being reported as a successful build.
    if (Test-Path $folder) { throw "Use a fresh work directory; already exists: $folder" }
    & git init --quiet $folder
    if ($LASTEXITCODE) { throw 'git init failed.' }
    & git -C $folder fetch --depth 1 $url $revision
    if ($LASTEXITCODE) { throw "Cannot fetch pinned source: $url at $revision" }
    & git -C $folder checkout --quiet --detach FETCH_HEAD
    if ($LASTEXITCODE) { throw 'git checkout failed.' }
    $actual = & git -C $folder rev-parse HEAD
    if ($LASTEXITCODE -or $actual -ne $revision) { throw 'Unexpected upstream revision.' }
}

$stamp = [guid]::NewGuid().ToString('N')
$source = Join-Path $work $stamp
Get-PinnedSource $pins.repository $pins.commit $source
$submodule = & git -C $source ls-tree HEAD game-overlay/sln
if ($LASTEXITCODE -or $submodule -notmatch ('^160000 commit ' + $pins.nativeCommit + '\s')) {
    throw 'Native pin does not match the upstream submodule.'
}
# Git creates an empty submodule directory, which is safe to remove only if empty.
$native = Join-Path $source 'game-overlay/sln'
if (Test-Path $native) { [System.IO.Directory]::Delete($native) }
Get-PinnedSource $pins.nativeRepository $pins.nativeCommit $native
# A D3D11 device used by Direct2D also exposes ID3D10Device. Prefer the actual
# D3D11 renderer instead of silently selecting the compatibility interface.
$patch = (Resolve-Path (Join-Path $PSScriptRoot 'patches/prefer-d3d11.patch')).Path
& git -C $native apply --check --ignore-space-change $patch
if ($LASTEXITCODE) { throw 'Pinned renderer patch no longer applies.' }
& git -C $native apply --ignore-space-change $patch
if ($LASTEXITCODE) { throw 'Could not apply the D3D11 renderer selection patch.' }


$tracePatch = (Resolve-Path (Join-Path $PSScriptRoot 'patches/trace-pixels.patch')).Path
& git -C $native apply --ignore-space-change $tracePatch
if ($LASTEXITCODE) { throw 'Could not apply pixel diagnostics.' }

$result = [ordered]@{
    sourceDirectory = $source
    upstreamCommit = $pins.commit
    nativeCommit = $pins.nativeCommit
    configuration = 'Release|x64'
    patches = @(@{ name = 'prefer-d3d11.patch'; sha256 = (Get-FileHash $patch -Algorithm SHA256).Hash })
    built = $false
    executed = $false
    gameTested = $false
    outputs = @()
}
try {
    # Build only native renderer and its source dependencies, not the old Electron demo.
    & msbuild (Join-Path $native 'gameoverlay.sln') /t:n_overlay /p:Configuration=Release /p:Platform=x64 /m /nologo /verbosity:minimal 2>&1 |
        Tee-Object -FilePath (Join-Path $reports 'native-build.log')
    if ($LASTEXITCODE) { throw "Upstream native build failed with code $LASTEXITCODE. See reports/native-build.log." }
    $dll = Join-Path $native 'bin/Release/n_overlay.x64.dll'
    if (-not (Test-Path $dll)) { throw 'Build finished without the expected native DLL.' }
    $result.built = $true
    $result.outputs = @(@{ name = 'n_overlay.x64.dll'; sha256 = (Get-FileHash $dll -Algorithm SHA256).Hash })
} finally {
    $result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $reports 'result.json') -Encoding utf8
}
Write-Host 'Native compilation succeeded. Rendering, WPF transport and LoL/Vanguard compatibility remain unverified.'
