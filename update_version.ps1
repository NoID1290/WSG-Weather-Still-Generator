# Auto-increment version date on project files
# Version format: a.b.c.MMDD where:
#   a = frontend update (GUI)
#   b = backend update
#   c = little fix
#   MMDD = month and day of push

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFilePaths = @(
    (Join-Path $repoRoot "WeatherImageGenerator\WeatherImageGenerator.csproj"),
    (Join-Path $repoRoot "Grib2\Grib2.csproj")
)

function Update-ProjectVersionDate {
    param(
        [string]$ProjectPath,
        [string]$NewVersion
    )

    if (-not (Test-Path $ProjectPath)) {
        Write-Host "Project not found: $ProjectPath" -ForegroundColor Yellow
        return
    }

    [xml]$projectFile = Get-Content $ProjectPath
    $projectFile.Project.PropertyGroup.Version = $NewVersion
    $projectFile.Project.PropertyGroup.AssemblyVersion = $NewVersion
    $projectFile.Project.PropertyGroup.FileVersion = $NewVersion
    $projectFile.Save($ProjectPath)

    Write-Host "Version updated: $ProjectPath -> $NewVersion"
}

# Use the main app version as the source for the shared date-stamped version.
[xml]$mainProjectFile = Get-Content $projectFilePaths[0]
$currentVersion = $mainProjectFile.Project.PropertyGroup.Version
Write-Host "Current version: $currentVersion"

$versionParts = $currentVersion -split '\.'
$a = [int]$versionParts[0]
$b = [int]$versionParts[1]
$c = [int]$versionParts[2]

$today = Get-Date
$dateString = $today.ToString("MMdd")
$newVersion = "$a.$b.$c.$dateString"

foreach ($projectFilePath in $projectFilePaths) {
    Update-ProjectVersionDate -ProjectPath $projectFilePath -NewVersion $newVersion
}

$assemblyInfoPath = Join-Path $repoRoot "WeatherImageGenerator\AssemblyInfo.cs"
if (Test-Path $assemblyInfoPath) {
    $assemblyInfoContent = Get-Content $assemblyInfoPath -Raw
    $assemblyInfoContent = $assemblyInfoContent -replace '(\[assembly: AssemblyVersion\(")[^"]*("\)\])', "`$1$newVersion`$2"
    Set-Content $assemblyInfoPath $assemblyInfoContent
    Write-Host "AssemblyInfo.cs updated with version: $newVersion"
}
