$ErrorActionPreference='Stop'
try {
    & '..\src\Generate-WeatherImages.ps1' -Latitude 40.7 -Longitude -74 -Location 'NY' -OutputDir '..\tests\out' -Width 800 -Height 400
}
catch {
    Write-Host "CAUGHT: $($_.Exception.GetType().FullName)"
    Write-Host "Message: $($_.Exception.Message)"
    $_.Exception | Format-List * -Force
}
