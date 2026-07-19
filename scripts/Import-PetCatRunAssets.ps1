param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$frameCount = 8
$frameWidth = 50
$frameHeight = 50
$sourceWidth = $frameCount * $frameWidth
$sourceHeight = $frameHeight
$pngSignature = [byte[]] (137, 80, 78, 71, 13, 10, 26, 10)

Add-Type -AssemblyName PresentationCore

function Get-PngHeader {
    param([Parameter(Mandatory = $true)][string] $Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 26) {
        throw "PNG file is too short: $Path"
    }

    for ($index = 0; $index -lt $pngSignature.Length; $index++) {
        if ($bytes[$index] -ne $pngSignature[$index]) {
            throw "File is not a PNG: $Path"
        }
    }

    [pscustomobject] @{
        Width =
            ([int] $bytes[16] * 16777216) +
            ([int] $bytes[17] * 65536) +
            ([int] $bytes[18] * 256) +
            [int] $bytes[19]
        Height =
            ([int] $bytes[20] * 16777216) +
            ([int] $bytes[21] * 65536) +
            ([int] $bytes[22] * 256) +
            [int] $bytes[23]
        BitDepth = [int] $bytes[24]
        ColorType = [int] $bytes[25]
    }
}

function Read-Bgra32Bitmap {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $source = $decoder.Frames[0]
        if ($source.Format -ne [System.Windows.Media.PixelFormats]::Bgra32) {
            throw "PNG must decode directly as Bgra32 without conversion: $Path (actual: $($source.Format))"
        }

        return $source
    }
    finally {
        $stream.Dispose()
    }
}

function Get-Bgra32Pixels {
    param([Parameter(Mandatory = $true)][System.Windows.Media.Imaging.BitmapSource] $Bitmap)

    $stride = $Bitmap.PixelWidth * 4
    $pixels = [byte[]]::new($stride * $Bitmap.PixelHeight)
    $Bitmap.CopyPixels($pixels, $stride, 0)
    return $pixels
}

function Assert-EqualPixels {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Expected,
        [Parameter(Mandatory = $true)][byte[]] $Actual,
        [Parameter(Mandatory = $true)][string] $FrameName
    )

    if ($Expected.Length -ne $Actual.Length) {
        throw "Decoded pixel length mismatch for $FrameName."
    }

    for ($index = 0; $index -lt $Expected.Length; $index++) {
        if ($Expected[$index] -ne $Actual[$index]) {
            throw "Decoded pixel mismatch for $FrameName at byte offset $index."
        }
    }
}

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$sourceHeader = Get-PngHeader -Path $resolvedSource
if ($sourceHeader.Width -ne $sourceWidth -or
    $sourceHeader.Height -ne $sourceHeight -or
    $sourceHeader.BitDepth -ne 8 -or
    $sourceHeader.ColorType -ne 6) {
    throw "Source must be a ${sourceWidth}x${sourceHeight}, 8-bit RGBA PNG (color type 6). Actual: $($sourceHeader.Width)x$($sourceHeader.Height), bit depth $($sourceHeader.BitDepth), color type $($sourceHeader.ColorType)."
}

$sourceBitmap = Read-Bgra32Bitmap -Path $resolvedSource
$sourcePixels = Get-Bgra32Pixels -Bitmap $sourceBitmap
$sourceStride = $sourceWidth * 4
$frameStride = $frameWidth * 4
$temporaryPaths = [System.Collections.Generic.List[string]]::new()

try {
    for ($frameIndex = 0; $frameIndex -lt $frameCount; $frameIndex++) {
        $frameName = 'cat-frame-{0:D2}.png' -f ($frameIndex + 1)
        $outputFile = [System.IO.Path]::Combine($resolvedOutput, $frameName)
        if ([System.IO.File]::Exists($outputFile)) {
            $existingHeader = Get-PngHeader -Path $outputFile
            if ($existingHeader.Width -ne $frameWidth -or
                $existingHeader.Height -ne $frameHeight -or
                $existingHeader.BitDepth -ne 8 -or
                $existingHeader.ColorType -ne 6) {
                throw "Refusing to overwrite an existing output with an unexpected specification: $outputFile"
            }
        }

        $cropRectangle = [System.Windows.Int32Rect]::new(
            $frameIndex * $frameWidth,
            0,
            $frameWidth,
            $frameHeight)
        $cropped = [System.Windows.Media.Imaging.CroppedBitmap]::new(
            $sourceBitmap,
            $cropRectangle)
        $temporaryFile = [System.IO.Path]::Combine(
            $resolvedOutput,
            ".import-$frameName")
        if ([System.IO.File]::Exists($temporaryFile)) {
            throw "Temporary import file already exists: $temporaryFile"
        }

        $temporaryPaths.Add($temporaryFile)
        $stream = [System.IO.File]::Open(
            $temporaryFile,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write)
        try {
            $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
            $encoder.Frames.Add(
                [System.Windows.Media.Imaging.BitmapFrame]::Create($cropped))
            $encoder.Save($stream)
        }
        finally {
            $stream.Dispose()
        }

        $outputHeader = Get-PngHeader -Path $temporaryFile
        if ($outputHeader.Width -ne $frameWidth -or
            $outputHeader.Height -ne $frameHeight -or
            $outputHeader.BitDepth -ne 8 -or
            $outputHeader.ColorType -ne 6) {
            throw "Imported frame has an unexpected PNG specification: $temporaryFile"
        }

        $expectedPixels = [byte[]]::new($frameStride * $frameHeight)
        for ($row = 0; $row -lt $frameHeight; $row++) {
            [System.Array]::Copy(
                $sourcePixels,
                $row * $sourceStride + $frameIndex * $frameStride,
                $expectedPixels,
                $row * $frameStride,
                $frameStride)
        }

        $importedBitmap = Read-Bgra32Bitmap -Path $temporaryFile
        $actualPixels = Get-Bgra32Pixels -Bitmap $importedBitmap
        Assert-EqualPixels -Expected $expectedPixels -Actual $actualPixels -FrameName $frameName
    }

    for ($frameIndex = 0; $frameIndex -lt $frameCount; $frameIndex++) {
        $frameName = 'cat-frame-{0:D2}.png' -f ($frameIndex + 1)
        $outputFile = [System.IO.Path]::Combine($resolvedOutput, $frameName)
        Move-Item -LiteralPath $temporaryPaths[$frameIndex] -Destination $outputFile -Force
    }

    Write-Output "Imported $frameCount frames from $resolvedSource to $resolvedOutput with decoded pixel equality verified."
}
finally {
    foreach ($temporaryPath in $temporaryPaths) {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}
