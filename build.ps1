# Build script for weather-still-api
Write-Host "Building the solution..." -ForegroundColor Green
dotnet build OpenMeteo.sln -c Release
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build completed successfully!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
