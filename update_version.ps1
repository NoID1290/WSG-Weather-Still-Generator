# Auto-increment version on push
# Version format: a.b.c.MMDD where:
#   a = frontend update (GUI)
#   b = backend update
#   c = little fix
#   MMDD = month and day of push

$projectFilePath = "s:\VScodeProjects\weather-still-api\WeatherImageGenerator\WeatherImageGenerator.csproj"

# Read the project file
[xml]$projectFile = Get-Content $projectFilePath

# Get current version
$currentVersion = $projectFile.Project.PropertyGroup.Version
Write-Host "Current version: $currentVersion"

# Parse version parts
$versionParts = $currentVersion -split '\.'
$a = [int]$versionParts[0]
$b = [int]$versionParts[1]
$c = [int]$versionParts[2]

# Get today's date in MMDD format
$today = Get-Date
$dateString = $today.ToString("MMdd")

# Create new version: a.b.c.MMDD
$newVersion = "$a.$b.$c.$dateString"

# Update Version, AssemblyVersion, and FileVersion
$projectFile.Project.PropertyGroup.Version = $newVersion
$projectFile.Project.PropertyGroup.AssemblyVersion = $newVersion
$projectFile.Project.PropertyGroup.FileVersion = $newVersion

# Save the project file
$projectFile.Save($projectFilePath)

Write-Host "Version updated to: $newVersion"
