# ============================================================
#  Konvertiert AAIA_Module_Manager.png → AAIA_Module_Manager.ico
#  Benötigt: .NET System.Drawing (Windows, immer verfügbar)
#  Aufruf: powershell -ExecutionPolicy Bypass -File create-icon.ps1
# ============================================================

Add-Type -AssemblyName System.Drawing

$pngPath = "..\src\AAIA.ModuleManager\Assets\AAIA_Module_Manager.png"
$icoPath = "..\src\AAIA.ModuleManager\Assets\AAIA_Module_Manager.ico"

if (-not (Test-Path $pngPath)) {
    Write-Error "PNG nicht gefunden: $pngPath"
    exit 1
}

$sizes = @(256, 128, 64, 48, 32, 16)
$bitmaps = @()

foreach ($size in $sizes) {
    $src = [System.Drawing.Image]::FromFile((Resolve-Path $pngPath))
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $src.Dispose()
    $bitmaps += $bmp
}

# ICO schreiben: Header + Directory + Daten
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)

# ICO Header
$writer.Write([uint16]0)        # Reserved
$writer.Write([uint16]1)        # Type: ICO
$writer.Write([uint16]$sizes.Count)

# Offset-Berechnung: Header(6) + Directory($sizes.Count * 16) + Daten
$dataOffset = 6 + $sizes.Count * 16
$pngStreams = @()

foreach ($bmp in $bitmaps) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams += $ms
}

# Directory Entries
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s    = $sizes[$i]
    $data = $pngStreams[$i].ToArray()
    $w    = if ($s -eq 256) { 0 } else { $s }
    $h    = if ($s -eq 256) { 0 } else { $s }
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)      # Color count
    $writer.Write([byte]0)      # Reserved
    $writer.Write([uint16]1)    # Planes
    $writer.Write([uint16]32)   # Bit count
    $writer.Write([uint32]$data.Length)
    $writer.Write([uint32]$dataOffset)
    $dataOffset += $data.Length
}

# Bild-Daten
foreach ($ms in $pngStreams) {
    $writer.Write($ms.ToArray())
    $ms.Dispose()
}

$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $icoPath), $stream.ToArray())
$writer.Dispose()

foreach ($bmp in $bitmaps) { $bmp.Dispose() }

Write-Host "Icon erstellt: $icoPath" -ForegroundColor Green
