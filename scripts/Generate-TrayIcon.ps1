param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-TrayIconPngBytes {
    param([int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $scale = $Size / 16.0

            $background = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(255, 0, 105, 115))
            $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $dark = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(255, 0, 52, 58))
            $outline = [System.Drawing.Pen]::new(
                [System.Drawing.Color]::White,
                [Math]::Max(1.0, $scale))
            try {
                $inset = [Math]::Max(1.0, $scale)
                $diameter = $Size - (2.0 * $inset)
                $graphics.FillEllipse($background, $inset, $inset, $diameter, $diameter)
                $graphics.DrawEllipse($outline, $inset, $inset, $diameter, $diameter)

                $points = @(
                    [System.Drawing.PointF]::new(4 * $scale, 11 * $scale),
                    [System.Drawing.PointF]::new(4 * $scale, 4 * $scale),
                    [System.Drawing.PointF]::new(6.5 * $scale, 6.3 * $scale),
                    [System.Drawing.PointF]::new(8 * $scale, 5.8 * $scale),
                    [System.Drawing.PointF]::new(9.5 * $scale, 6.3 * $scale),
                    [System.Drawing.PointF]::new(12 * $scale, 4 * $scale),
                    [System.Drawing.PointF]::new(12 * $scale, 11 * $scale),
                    [System.Drawing.PointF]::new(10.2 * $scale, 13 * $scale),
                    [System.Drawing.PointF]::new(5.8 * $scale, 13 * $scale)
                )
                $graphics.FillPolygon($white, $points)

                $eyeSize = [Math]::Max(1.0, 1.15 * $scale)
                $graphics.FillEllipse($dark, 5.8 * $scale, 8.2 * $scale, $eyeSize, $eyeSize)
                $graphics.FillEllipse($dark, 9.1 * $scale, 8.2 * $scale, $eyeSize, $eyeSize)
            }
            finally {
                $outline.Dispose()
                $dark.Dispose()
                $white.Dispose()
                $background.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$images = @(16, 32) | ForEach-Object {
    [pscustomobject]@{
        Size = $_
        Bytes = [byte[]] (New-TrayIconPngBytes -Size $_)
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if (-not [System.IO.Directory]::Exists($outputDirectory)) {
    throw "The output directory does not exist: $outputDirectory"
}

$file = [System.IO.FileStream]::new(
    $resolvedOutput,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
try {
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] $images.Count)

        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $writer.Write([byte] $image.Size)
            $writer.Write([byte] $image.Size)
            $writer.Write([byte] 0)
            $writer.Write([byte] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] 32)
            $writer.Write([uint32] $image.Bytes.Length)
            $writer.Write([uint32] $offset)
            $offset += $image.Bytes.Length
        }

        foreach ($image in $images) {
            $writer.Write([byte[]] $image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $file.Dispose()
}
