param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$frameCount = 8
$sourceSize = 50
$iconSizes = @(16, 20, 24, 32)

Add-Type -AssemblyName System.Drawing

function Get-AlphaBounds {
    param([Parameter(Mandatory = $true)][System.Drawing.Bitmap] $Bitmap)

    $left = $Bitmap.Width
    $top = $Bitmap.Height
    $right = -1
    $bottom = -1
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -eq 0) {
                continue
            }

            $left = [Math]::Min($left, $x)
            $top = [Math]::Min($top, $y)
            $right = [Math]::Max($right, $x)
            $bottom = [Math]::Max($bottom, $y)
        }
    }

    if ($right -lt $left -or $bottom -lt $top) {
        throw 'A source frame contains no visible pixels.'
    }

    return [System.Drawing.Rectangle]::FromLTRB(
        $left,
        $top,
        $right + 1,
        $bottom + 1)
}

function New-SizedPngBytes {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap] $Source,
        [Parameter(Mandatory = $true)][System.Drawing.Rectangle] $SourceBounds,
        [Parameter(Mandatory = $true)][int] $Size
    )

    $margin = 1
    $available = $Size - (2 * $margin)
    $scale = [Math]::Min(
        $available / $SourceBounds.Width,
        $available / $SourceBounds.Height)
    $destinationWidth = [Math]::Max(
        1,
        [int][Math]::Round($SourceBounds.Width * $scale))
    $destinationHeight = [Math]::Max(
        1,
        [int][Math]::Round($SourceBounds.Height * $scale))
    $destinationX = [int][Math]::Floor(($Size - $destinationWidth) / 2)
    $destinationY = [int][Math]::Floor(($Size - $destinationHeight) / 2)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality =
                [System.Drawing.Drawing2D.CompositingQuality]::HighSpeed
            $graphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.SmoothingMode =
                [System.Drawing.Drawing2D.SmoothingMode]::None
            $destination = [System.Drawing.Rectangle]::new(
                $destinationX,
                $destinationY,
                $destinationWidth,
                $destinationHeight)
            $graphics.DrawImage(
                $Source,
                $destination,
                $SourceBounds,
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Write-MultiSizeIcon {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap] $Source,
        [Parameter(Mandatory = $true)][System.Drawing.Rectangle] $SourceBounds,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $images = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $iconSizes) {
        $images.Add(
            (New-SizedPngBytes `
                -Source $Source `
                -SourceBounds $SourceBounds `
                -Size $size))
    }
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write)
    try {
        $writer = [System.IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] $images.Count)
            $offset = 6 + (16 * $images.Count)
            for ($index = 0; $index -lt $images.Count; $index++) {
                $size = $iconSizes[$index]
                $writer.Write([byte] $size)
                $writer.Write([byte] $size)
                $writer.Write([byte] 0)
                $writer.Write([byte] 0)
                $writer.Write([uint16] 1)
                $writer.Write([uint16] 32)
                $writer.Write([uint32] $images[$index].Length)
                $writer.Write([uint32] $offset)
                $offset += $images[$index].Length
            }

            foreach ($image in $images) {
                $writer.Write([byte[]] $image)
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$frames = [System.Collections.Generic.List[System.Drawing.Bitmap]]::new()
$temporaryPaths = [System.Collections.Generic.List[string]]::new()
try {
    $unionBounds = [System.Drawing.Rectangle]::Empty
    for ($index = 0; $index -lt $frameCount; $index++) {
        $frameName = 'cat-frame-{0:D2}.png' -f ($index + 1)
        $framePath = [System.IO.Path]::Combine($resolvedSource, $frameName)
        if (-not [System.IO.File]::Exists($framePath)) {
            throw "Missing source frame: $framePath"
        }

        $frame = [System.Drawing.Bitmap]::new($framePath)
        if ($frame.Width -ne $sourceSize -or $frame.Height -ne $sourceSize) {
            $frame.Dispose()
            throw "Source frame must be ${sourceSize}x${sourceSize}: $framePath"
        }

        $frames.Add($frame)
        $bounds = Get-AlphaBounds -Bitmap $frame
        $unionBounds = if ($unionBounds.IsEmpty) {
            $bounds
        }
        else {
            [System.Drawing.Rectangle]::Union($unionBounds, $bounds)
        }
    }

    for ($index = 0; $index -lt $frameCount; $index++) {
        $iconName = 'tray-cat-frame-{0:D2}.ico' -f ($index + 1)
        $outputFile = [System.IO.Path]::Combine($resolvedOutput, $iconName)
        $temporaryFile = [System.IO.Path]::Combine(
            $resolvedOutput,
            ".generate-$iconName")
        if ([System.IO.File]::Exists($temporaryFile)) {
            throw "Temporary generation file already exists: $temporaryFile"
        }

        $temporaryPaths.Add($temporaryFile)
        Write-MultiSizeIcon `
            -Source $frames[$index] `
            -SourceBounds $unionBounds `
            -Path $temporaryFile

        Move-Item -LiteralPath $temporaryFile -Destination $outputFile -Force
    }

    Write-Output (
        "Generated $frameCount tray animation icons with sizes " +
        "$($iconSizes -join ', ') from $resolvedSource to $resolvedOutput.")
}
finally {
    foreach ($frame in $frames) {
        $frame.Dispose()
    }

    foreach ($temporaryPath in $temporaryPaths) {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}
