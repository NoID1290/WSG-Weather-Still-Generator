# Build script for weather-still-api
# Defaults to building the WeatherImageGenerator project. Use -All to build the full solution instead.
param(
    [switch]$All,
    [string]$Configuration = "Release"
)

if ($All) {
    Write-Host "Building the full solution (OpenMeteo.sln) in $Configuration..." -ForegroundColor Green
    dotnet build OpenMeteo.sln -c $Configuration -v normal
} else {
    Write-Host "Building WeatherImageGenerator in $Configuration..." -ForegroundColor Green
    dotnet build WeatherImageGenerator\WeatherImageGenerator.csproj -c $Configuration -v normal
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build completed successfully!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
