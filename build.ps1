# Build script for weather-still-api
# Defaults to building the WeatherImageGenerator project. Use -All to build the full solution instead.
param(
    [switch]$All,
    [string]$Configuration = "Release"
)

# Prepare build arguments
$buildArgs = @("-c", $Configuration, "-v", "normal")

# If Release build, supress PDB debug files and XML documentation for a clean output
if ($Configuration -eq "Release") {
    $buildArgs += "-p:DebugType=None"
    # Optional: also suppress XML docs if desired, but user focused on PDBs.
    # $buildArgs += "-p:GenerateDocumentationFile=false" 
}

if ($All) {
    Write-Host "Building the full solution (WSG.sln) in $Configuration..." -ForegroundColor Green
    dotnet build WSG.sln $buildArgs
} else {
    Write-Host "Building WeatherImageGenerator in $Configuration..." -ForegroundColor Green
    dotnet build WeatherImageGenerator\WeatherImageGenerator.csproj $buildArgs
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build completed successfully!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
