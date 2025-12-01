<#
Generate-WeatherImages.ps1
PowerShell script to fetch weather data from Open-Meteo (no API key) and produce 3 PNG images:
 - current.png (current conditions)
 - hourly.png (hourly temperature summary)
 - forecast.png (3-day forecast summary)

Designed for Windows PowerShell 5.1 (uses System.Drawing)
#>

param(
    [Parameter(Mandatory=$false)] [double]$Latitude = 40.7128,
    [Parameter(Mandatory=$false)] [double]$Longitude = -74.0060,
    [Parameter(Mandatory=$false)] [string]$Location = "New York, NY",
    [Parameter(Mandatory=$false)] [string]$OutputDir = "./images",
    [Parameter(Mandatory=$false)] [int]$Width = 1200,
    [Parameter(Mandatory=$false)] [int]$Height = 600
)

Set-StrictMode -Version Latest

# Load System.Drawing assemblies
try { [void][System.Reflection.Assembly]::LoadWithPartialName('System.Drawing') } catch {}
try { [void][System.Reflection.Assembly]::LoadWithPartialName('System.Drawing.Drawing2D') } catch {}

function Validate-Parameters {
    param($Latitude, $Longitude, $OutputDir, $Width, $Height)
    if ($Latitude -lt -90 -or $Latitude -gt 90) { throw "Latitude must be between -90 and 90" }
    if ($Longitude -lt -180 -or $Longitude -gt 180) { throw "Longitude must be between -180 and 180" }
    if ($Width -le 0 -or $Height -le 0) { throw "Width and Height must be positive" }
    return [System.IO.Path]::GetFullPath($OutputDir)
}

function Ensure-OutputDir {
    param([string]$dir)
    if (-not (Test-Path -Path $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }
}

function Get-WeatherData {
    param([double]$lat, [double]$lon)

    $base = 'https://api.open-meteo.com/v1/forecast'
    $query = @{latitude = $lat; longitude = $lon; current_weather = 'true'; hourly = 'temperature_2m,precipitation,weathercode'; daily = 'temperature_2m_max,temperature_2m_min,weathercode'; timezone='auto'}

    $params = ($query.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key, [uri]::EscapeDataString($_.Value) }) -join '&'
    $url = "{0}?{1}" -f $base, $params

    Write-Host "Fetching weather data from Open-Meteo for $lat,$lon"
    try {
        $json = Invoke-RestMethod -Uri $url -Method Get -UseBasicParsing -ErrorAction Stop
        return $json
    }
    catch {
        Write-Error "Failed to fetch weather data from Open-Meteo: $($_.Exception.Message)"
        throw $_
    }
}

function Convert-WeatherCodeToText {
    param([int]$code)
    # Open-Meteo / WMO weather codes (simplified mapping)
    switch ($code) {
        {$_ -eq 0} { 'Clear' ; break }
        {$_ -in 1..3} { 'Mainly clear / partly cloudy' ; break }
        {$_ -in 45..48} { 'Fog / depositing rime' ; break }
        {$_ -in 51..57} { 'Drizzle' ; break }
        {$_ -in 61..67} { 'Rain' ; break }
        {$_ -in 71..77} { 'Snow' ; break }
        {$_ -in 80..82} { 'Rain showers' ; break }
        {$_ -in 95..99} { 'Thunderstorm' ; break }
        default { 'Unknown' }
    }
}

function Draw-Image {
    param(
        [System.Drawing.Bitmap]$bmp,
        [scriptblock]$drawAction
    )
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    # Gradient background
    try {
        $rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)
        $c1 = [System.Drawing.Color]::FromArgb(24, 32, 40)
        $c2 = [System.Drawing.Color]::FromArgb(12, 90, 160)
        $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $g.FillRectangle($lg, $rect)
        $lg.Dispose()
    }
    catch {
        $g.Clear([System.Drawing.Color]::FromArgb(33, 33, 33))
    }
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    & $drawAction $g
    $g.Dispose()
}

function Save-BitmapAsPng {
    param([System.Drawing.Bitmap]$bmp, [string]$path)
    try {
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Saved image -> $path"
    }
    catch {
        Write-Warning "Failed to save image $path : $_"
    }
    finally {
        $bmp.Dispose()
    }
}

function Create-CurrentImage {
    param($weather, $location, $width, $height, $outPath)

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    try { $fontTitle = New-Object System.Drawing.Font('Segoe UI', 36, [System.Drawing.FontStyle]::Bold) }
    catch { $fontTitle = New-Object System.Drawing.Font('Arial', 36, [System.Drawing.FontStyle]::Bold) }
    try { $font = New-Object System.Drawing.Font('Segoe UI', 18) }
    catch { $font = New-Object System.Drawing.Font('Arial', 18) }

    $drawAction = {
        param($g)
        $brushWhite = [System.Drawing.Brushes]::White
        $g.DrawString("Current Conditions", $fontTitle, $brushWhite, 16, 10)
        $g.DrawString("$location", $font, $brushWhite, 16, 70)

        $t = $weather.current_weather.temperature
        try { $tempFont = New-Object System.Drawing.Font('Segoe UI', 96, [System.Drawing.FontStyle]::Bold) }
        catch { $tempFont = New-Object System.Drawing.Font('Arial', 96, [System.Drawing.FontStyle]::Bold) }
        
        $tempStr = "{0:N1}°C" -f ([double]$t)
        $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 0, 0, 0))
        $g.DrawString($tempStr, $tempFont, $shadowBrush, 44, 134)
        $g.DrawString($tempStr, $tempFont, [System.Drawing.Brushes]::Orange, 40, 130)
        $shadowBrush.Dispose()

        $infoX = 520; $infoY = 160; $boxW = 640; $boxH = 320
        $g.FillRectangle([System.Drawing.Brushes]::DarkSlateGray, $infoX, $infoY, $boxW, $boxH)

        $cond = Convert-WeatherCodeToText $weather.current_weather.weathercode
        try {
            Draw-SmallIcon -g $g -code $weather.current_weather.weathercode -x ($infoX + $boxW - 140) -y ($infoY + 24)
        }
        catch {}
        
        $g.DrawString("Condition: $cond", $font, $brushWhite, $infoX + 16, $infoY + 20)
        $g.DrawString("Wind: $($weather.current_weather.windspeed) km/h", $font, $brushWhite, $infoX + 16, $infoY + 60)
        $g.DrawString("Wind Dir: $($weather.current_weather.winddirection)°", $font, $brushWhite, $infoX + 16, $infoY + 100)

        $tstamp = $weather.current_weather.time
        $g.DrawString("Updated: $tstamp", $font, $brushWhite, 16, $height - 60)
    }

    Draw-Image -bmp $bmp -drawAction $drawAction
    Save-BitmapAsPng -bmp $bmp -path $outPath
}

function Draw-SmallIcon {
    param($g, $code, $x, $y)
    $code = [int]$code
    if ($code -eq 0 -or ($code -in 1..3)) {
        $sunBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 220, 80))
        $g.FillEllipse($sunBrush, $x, $y, 96, 96)
        $sunBrush.Dispose()
    }
    elseif ($code -in 61..67 -or $code -in 80..82) {
        $cloudBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 220, 220))
        $g.FillEllipse($cloudBrush, $x, $y + 16, 100, 60)
        $g.FillEllipse($cloudBrush, $x + 28, $y, 100, 60)
        for ($i = 0; $i -lt 3; $i++) { $g.FillEllipse([System.Drawing.Brushes]::LightBlue, $x + 24 + ($i * 20), $y + 72, 10, 16) }
        $cloudBrush.Dispose()
    }
    else {
        $cb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 200, 200))
        $g.FillEllipse($cb, $x + 8, $y + 16, 100, 60)
        $cb.Dispose()
    }
}

function Create-HourlyImage {
    param($weather, $location, $width, $height, $outPath)

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    try { $fontTitle = New-Object System.Drawing.Font('Segoe UI', 28, [System.Drawing.FontStyle]::Bold) }
    catch { $fontTitle = New-Object System.Drawing.Font('Arial', 28, [System.Drawing.FontStyle]::Bold) }
    try { $font = New-Object System.Drawing.Font('Segoe UI', 14) }
    catch { $font = New-Object System.Drawing.Font('Arial', 14) }

    $drawAction = {
        param($g)
        $g.DrawString("Hourly Temperature (next 24h) - $location", $fontTitle, [System.Drawing.Brushes]::White, 16, 10)

        $now = [DateTime]::Parse($weather.current_weather.time)
        $hourly_times = $weather.hourly.time | ForEach-Object { [DateTime]::Parse($_) }
        $hourly_temps = $weather.hourly.temperature_2m

        $i0 = 0
        for ($hIdx = 0; $hIdx -lt $hourly_times.Count; $hIdx++) { if ($hourly_times[$hIdx] -ge $now) { $i0 = $hIdx; break } }
        $slice_ts = $hourly_times[$i0..([math]::Min($i0 + 23, $hourly_times.Count - 1))]
        $slice_vals = $hourly_temps[$i0..([math]::Min($i0 + 23, $hourly_temps.Count - 1))]

        $gX = 40; $gY = 70; $gW = $width - 80; $gH = $height - 180
        $g.DrawRectangle([System.Drawing.Pens]::DimGray, $gX, $gY, $gW, $gH)

        if ($slice_vals.Count -eq 0) { $min = 0; $max = 1 }
        else {
            $min = [math]::Floor(($slice_vals | Measure-Object -Minimum).Minimum) - 2
            $max = [math]::Ceiling(($slice_vals | Measure-Object -Maximum).Maximum) + 2
        }
        $range = $max - $min
        if ($range -le 0) { $range = 1 }

        for ($gridIdx = 0; $gridIdx -le 4; $gridIdx++) {
            $y = $gY + ($gH - ($gridIdx * $gH / 4))
            $g.DrawLine([System.Drawing.Pens]::Gray, $gX, $y, $gX + $gW, $y)
            $val = $min + ($gridIdx * $range / 4)
            $g.DrawString("{0:N1}°C" -f $val, $font, [System.Drawing.Brushes]::LightGray, $gX + $gW + 10, $y - 10)
        }

        $pointCount = $slice_vals.Count
        if ($pointCount -gt 1) {
            $stepX = $gW / ($pointCount - 1)
            $prevPt = $null
            $points = New-Object "System.Drawing.PointF[]" $pointCount
            
            for ($j = 0; $j -lt $pointCount; $j++) {
                $val = [double]$slice_vals[$j]
                $fx = $gX + ($j * $stepX)
                $fy = $gY + ($gH - (($val - $min) / $range * $gH))

                $pt = New-Object System.Drawing.PointF($fx, $fy)
                $points[$j] = $pt
                
                if ($prevPt -ne $null) {
                    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 120, 200), 3)
                    $pen.SetLineCap([System.Drawing.Drawing2D.LineCap]::Round, [System.Drawing.Drawing2D.LineCap]::Round, [System.Drawing.Drawing2D.DashCap]::Round)
                    $g.DrawLine($pen, $prevPt, $pt)
                    $pen.Dispose()
                }
                $g.FillEllipse([System.Drawing.Brushes]::Orange, $fx - 4, $fy - 4, 8, 8)

                if ((($j % 3) -eq 0) -or ($j -eq $pointCount - 1)) {
                    $lbl = $slice_ts[$j].ToString('HH:mm')
                    $g.DrawString($lbl, $font, [System.Drawing.Brushes]::LightGray, $fx - 16, $gY + $gH + 6)
                }
                $prevPt = $pt
            }

            # Fill under curve
            try {
                $polyLen = $pointCount + 2
                $poly = New-Object "System.Drawing.PointF[]" $polyLen
                for ($pi = 0; $pi -lt $pointCount; $pi++) { $poly[$pi] = $points[$pi] }
                $poly[$pointCount] = New-Object System.Drawing.PointF($gX + $gW, $gY + $gH)
                $poly[$pointCount + 1] = New-Object System.Drawing.PointF($gX, $gY + $gH)
                $fillBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(64, 255, 120, 200))
                $g.FillPolygon($fillBrush, $poly)
                $fillBrush.Dispose()
            }
            catch {}
        }

        $startX = $gX + 8
        $startY = $gY + $gH + 40
        $g.DrawString("Sample of next hours:", $font, [System.Drawing.Brushes]::White, $startX, $startY)
        $yNow = $startY + 28
        for ($k = 0; $k -lt [math]::Min(6, $slice_vals.Count); $k++) {
            $ts = $slice_ts[$k].ToString('MMM dd HH:mm')
            $val = $slice_vals[$k]
            $g.DrawString("$ts - {0}°C" -f $val, $font, [System.Drawing.Brushes]::LightGray, $startX, $yNow)
            $yNow += 24
        }
    }

    Draw-Image -bmp $bmp -drawAction $drawAction
    Save-BitmapAsPng -bmp $bmp -path $outPath
}

function Create-ForecastImage {
    param($weather, $location, $width, $height, $outPath)

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    try { $fontTitle = New-Object System.Drawing.Font('Segoe UI', 28, [System.Drawing.FontStyle]::Bold) }
    catch { $fontTitle = New-Object System.Drawing.Font('Arial', 28, [System.Drawing.FontStyle]::Bold) }
    try { $font = New-Object System.Drawing.Font('Segoe UI', 16) }
    catch { $font = New-Object System.Drawing.Font('Arial', 16) }

    $drawAction = {
        param($g)
        $g.DrawString("3-day Forecast - $location", $fontTitle, [System.Drawing.Brushes]::White, 16, 10)

        $daily_dates = @()
        if ($weather.daily -and $weather.daily.time) { $daily_dates = $weather.daily.time | ForEach-Object { [DateTime]::Parse($_) } }
        
        if ($daily_dates.Count -eq 0) {
            $g.DrawString("No daily forecast data available", $font, [System.Drawing.Brushes]::LightGray, 40, 90)
            return
        }

        $maxVals = $weather.daily.temperature_2m_max
        $minVals = $weather.daily.temperature_2m_min
        $codes = $weather.daily.weathercode

        $boxX = 40; $boxY = 70; $boxW = ($width - 80) / [math]::Max(1, $daily_dates.Count); $boxH = $height - 140
        $x = $boxX
        for ($i = 0; $i -lt [math]::Min(3, $daily_dates.Count); $i++) {
            $g.FillRectangle([System.Drawing.Brushes]::DarkSlateGray, $x, $boxY, $boxW - 12, $boxH)
            $g.DrawRectangle([System.Drawing.Pens]::DimGray, $x, $boxY, $boxW - 12, $boxH)

            $d = $daily_dates[$i].ToString('ddd, MMM dd')
            $g.DrawString($d, $font, [System.Drawing.Brushes]::White, $x + 12, $boxY + 12)

            $highStr = if ($maxVals[$i] -ne $null) { "{0:N1}" -f ([double]$maxVals[$i]) } else { '-' }
            $lowStr = if ($minVals[$i] -ne $null) { "{0:N1}" -f ([double]$minVals[$i]) } else { '-' }
            
            $g.DrawString("High: " + $highStr + "°C", $font, [System.Drawing.Brushes]::Orange, $x + 12, $boxY + 60)
            $g.DrawString("Low: " + $lowStr + "°C", $font, [System.Drawing.Brushes]::LightBlue, $x + 12, $boxY + 96)

            $codeVal = if (($codes -and $i -lt $codes.Count) -and $codes[$i] -ne $null) { $codes[$i] } else { $null }
            $desc = Convert-WeatherCodeToText $codeVal
            $g.DrawString($desc, $font, [System.Drawing.Brushes]::LightGray, $x + 12, $boxY + 140)

            try { Draw-SmallIcon -g $g -code $codeVal -x ($x + ($boxW - 140)) -y ($boxY + 32) } catch {}

            $x += $boxW
        }
    }

    Draw-Image -bmp $bmp -drawAction $drawAction
    Save-BitmapAsPng -bmp $bmp -path $outPath
}

# Main flow
try {
    $OutputDir = Validate-Parameters -Latitude $Latitude -Longitude $Longitude -OutputDir $OutputDir -Width $Width -Height $Height
}
catch {
    Write-Error "Parameter validation failed: $($_.Exception.Message)"
    exit 2
}

Ensure-OutputDir -dir $OutputDir

try {
    $weather = Get-WeatherData -lat $Latitude -lon $Longitude
}
catch {
    Write-Error "Unable to fetch weather data."
    exit 3
}

$curPath = Join-Path $OutputDir 'current.png'
$hourlyPath = Join-Path $OutputDir 'hourly.png'
$forecastPath = Join-Path $OutputDir 'forecast.png'

Create-CurrentImage -weather $weather -location $Location -width $Width -height $Height -outPath $curPath
Create-HourlyImage -weather $weather -location $Location -width $Width -height $Height -outPath $hourlyPath
Create-ForecastImage -weather $weather -location $Location -width $Width -height $Height -outPath $forecastPath

Write-Host "All images created in $OutputDir" -ForegroundColor Green
