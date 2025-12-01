# Simple verification script
# Runs the generator and checks that 3 images are produced

$scriptPath = Join-Path $PSScriptRoot '..\src\Generate-WeatherImages.ps1'
if (-not (Test-Path $scriptPath)) {
    Write-Error "Script not found at $scriptPath"
    exit 2
}

$out = Join-Path $PSScriptRoot 'out'
$lat = 40.7128
$lon = -74.0060
$loc = 'New York, NY'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path $out | Out-Null

Write-Host "Running generator (this will call Open-Meteo API)..."
& $scriptPath -Latitude $lat -Longitude $lon -Location $loc -OutputDir $out -Width 800 -Height 400
$exitCode = $LASTEXITCODE
Write-Host "Generator exited with code: $exitCode"

$expected = @("current.png","hourly.png","forecast.png")
$missing = @()
foreach ($f in $expected) {
    $p = Join-Path $out $f
    if (-not (Test-Path $p)) { $missing += $f }
}

if ($missing.Count -gt 0) {
    Write-Error "Missing images: $($missing -join ', ')"
    exit 1
}
else {
    Write-Host "All images are present in $out" -ForegroundColor Green
    Get-ChildItem $out -Filter "*.png" | ForEach-Object { Write-Host "  - $($_.Name) ($([math]::Round($_.Length/1KB))KB)" }
    exit 0
}
