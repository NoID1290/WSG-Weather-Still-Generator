# Test script for weather-still-api
Write-Host "Running tests..." -ForegroundColor Green
dotnet test WSG.sln --verbosity normal
if ($LASTEXITCODE -eq 0) {
    Write-Host "Tests completed successfully!" -ForegroundColor Green
} else {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit 1
}
