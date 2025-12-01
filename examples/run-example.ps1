# Example: run the image generator
# Make sure you run this from the workspace root.

$scriptPath = Join-Path $PSScriptRoot '..\src\Generate-WeatherImages.ps1'
if (-not (Test-Path $scriptPath)) {
    Write-Error "Script not found at $scriptPath"
    exit 1
}

$latitude = 40.7128
$longitude = -74.0060
$location = 'New York, NY'
$out = Join-Path $PSScriptRoot '..\images'

# Create the images (1200x600)
Write-Host "Running generator for $location ($latitude,$longitude) -> $out"
& $scriptPath -Latitude $latitude -Longitude $longitude -Location $location -OutputDir $out -Width 1200 -Height 600

Write-Host "Done. Check the images folder." -ForegroundColor Green
