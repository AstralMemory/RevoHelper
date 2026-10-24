param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\RevoHelper.App\Assets')
)

Add-Type -AssemblyName System.Drawing

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

function New-RevoHelperBitmap([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([Drawing.Color]::Transparent)

    $cornerRadius = [int]($size * 0.22)
    $cornerDiameter = $cornerRadius * 2
    $edge = $size - 1
    $backgroundPath = [Drawing.Drawing2D.GraphicsPath]::new()
    $backgroundPath.AddArc(0, 0, $cornerDiameter, $cornerDiameter, 180, 90)
    $backgroundPath.AddArc($edge - $cornerDiameter, 0, $cornerDiameter, $cornerDiameter, 270, 90)
    $backgroundPath.AddArc($edge - $cornerDiameter, $edge - $cornerDiameter, $cornerDiameter, $cornerDiameter, 0, 90)
    $backgroundPath.AddArc(0, $edge - $cornerDiameter, $cornerDiameter, $cornerDiameter, 90, 90)
    $backgroundPath.CloseFigure()
    $backgroundBrush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    $letterColor = [Drawing.ColorTranslator]::FromHtml('#00A9D6')
    $fontSize = $size * 0.68
    $font = [Drawing.Font]::new('Segoe UI', $fontSize, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    $format.Alignment = [Drawing.StringAlignment]::Center
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $brush = [Drawing.SolidBrush]::new($letterColor)
    $textArea = [Drawing.RectangleF]::new(0, $size * 0.015, $size, $size)
    $graphics.DrawString('R', $font, $brush, $textArea, $format)

    $brush.Dispose()
    $backgroundBrush.Dispose()
    $backgroundPath.Dispose()
    $format.Dispose()
    $font.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$png = New-RevoHelperBitmap 256
$png.Save((Join-Path $resolvedOutput 'RevoHelperLogo.png'), [Drawing.Imaging.ImageFormat]::Png)

$iconBitmap = New-RevoHelperBitmap 256
$iconHandle = $iconBitmap.GetHicon()
$icon = [Drawing.Icon]::FromHandle($iconHandle)
$stream = [IO.File]::Create((Join-Path $resolvedOutput 'RevoHelper.ico'))
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$iconBitmap.Dispose()
$png.Dispose()
