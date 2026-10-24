$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
foreach ($spec in @(@('StoreLogo',50,50),@('Square150x150Logo',150,150),@('Square44x44Logo',44,44),@('SplashScreen',620,300))) {
    $bitmap = New-Object System.Drawing.Bitmap([int]$spec[1], [int]$spec[2])
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(23,29,39))
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100,230,196))
        try { $graphics.FillRectangle($brush, [int]($spec[1]/4), [int]($spec[2]/4), [int]($spec[1]/2), [int]($spec[2]/2)) }
        finally { $brush.Dispose() }
        $bitmap.Save((Join-Path $assets ($spec[0]+'.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
