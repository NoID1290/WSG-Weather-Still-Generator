# Auto-push to GitHub with automatic version increment
# Version format: a.b.c.MMDD where:
#   a = frontend update (GUI)
#   b = backend update
#   c = little fix
#   MMDD = month and day of push

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("frontend", "backend", "fix")]
    [string]$Type = "fix",
    
    [Parameter(Mandatory=$false)]
    [string]$CommitMessage = "Auto-commit: Version update",
    
    [Parameter(Mandatory=$false)]
    [string]$Branch = "main"
)

$projectFilePath = "WeatherImageGenerator\WeatherImageGenerator.csproj"
#$repoRoot = git rev-parse --show-toplevel

if (-not $?) {
    Write-Host "❌ Error: Not in a Git repository!" -ForegroundColor Red
    exit 1
}

Write-Host "[START] Auto-push process..." -ForegroundColor Cyan
Write-Host "[TYPE] Update type: $Type" -ForegroundColor Yellow

# Read the project file
[xml]$projectFile = Get-Content $projectFilePath

# Get current version
$currentVersion = $projectFile.Project.PropertyGroup.Version
Write-Host "[VERSION] Current version: $currentVersion" -ForegroundColor White

# Parse version parts
$versionParts = $currentVersion -split '\.'
[int]$a = $versionParts[0]
[int]$b = $versionParts[1]
[int]$c = $versionParts[2]

# Increment based on update type
switch ($Type) {
    "frontend" {
        $a++
        $b = 0
        $c = 0
        Write-Host "[UPDATE] Frontend version incremented" -ForegroundColor Green
    }
    "backend" {
        $b++
        $c = 0
        Write-Host "[UPDATE] Backend version incremented" -ForegroundColor Green
    }
    "fix" {
        $c++
        Write-Host "[UPDATE] Fix version incremented" -ForegroundColor Green
    }
}

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
Write-Host "[SUCCESS] Version updated to: $newVersion" -ForegroundColor Green


# Also update AssemblyInfo.cs to keep it in sync
$assemblyInfoPath = "WeatherImageGenerator\AssemblyInfo.cs"
if (Test-Path $assemblyInfoPath) {
    $assemblyInfoContent = Get-Content $assemblyInfoPath -Raw

    $patternVersion = '(\[assembly:\s*AssemblyVersion\("')[^"']*("'\)\])'
    $patternFileVersion = '(\[assembly:\s*AssemblyFileVersion\("')[^"']*("'\)\])'
    $patternInformational = '(\[assembly:\s*AssemblyInformationalVersion\("')[^"']*("'\)\])'

    # Use .NET regex replace with $1/$2 replacement tokens constructed safely
    $assemblyInfoContent = [regex]::Replace($assemblyInfoContent, $patternVersion, ('$1' + $newVersion + '$2'))
    $assemblyInfoContent = [regex]::Replace($assemblyInfoContent, $patternFileVersion, ('$1' + $newVersion + '$2'))
    $assemblyInfoContent = [regex]::Replace($assemblyInfoContent, $patternInformational, ('$1' + $newVersion + '$2'))

    # If any of the attributes are missing, append them so the file stays explicit
    if ($assemblyInfoContent -notmatch 'AssemblyVersion') {
        $assemblyInfoContent += "`r`n[assembly: AssemblyVersion(\"$newVersion\")]"
    }
    if ($assemblyInfoContent -notmatch 'AssemblyFileVersion') {
        $assemblyInfoContent += "`r`n[assembly: AssemblyFileVersion(\"$newVersion\")]"
    }
    if ($assemblyInfoContent -notmatch 'AssemblyInformationalVersion') {
        $assemblyInfoContent += "`r`n[assembly: AssemblyInformationalVersion(\"$newVersion\")]"
    }

    Set-Content -Path $assemblyInfoPath -Value $assemblyInfoContent -Encoding UTF8
    Write-Host "[SUCCESS] AssemblyInfo.cs updated with version: $newVersion" -ForegroundColor Green
}

# Stage the updated file
Write-Host "[STAGING] Changes..." -ForegroundColor Cyan
git add $projectFilePath

# Create commit message with version info
$finalCommitMessage = "$CommitMessage (v$newVersion)"

# Commit
Write-Host "[COMMITTING] Changes..." -ForegroundColor Cyan
git commit -m $finalCommitMessage

if ($?) {
    Write-Host "[SUCCESS] Commit successful" -ForegroundColor Green
} else {
    Write-Host "[WARNING] No changes to commit (this might be fine)" -ForegroundColor Yellow
}

# Check if branch exists and create if needed
$branchExists = git rev-parse --verify $Branch 2>$null
if (-not $branchExists) {
    Write-Host "[BRANCH] Creating branch: $Branch" -ForegroundColor Cyan
    git checkout -b $Branch
}

# Push to GitHub
Write-Host "[PUSHING] To GitHub ($Branch)..." -ForegroundColor Cyan
git push origin $Branch

if ($?) {
    Write-Host "[SUCCESS] Pushed to GitHub!" -ForegroundColor Green
    Write-Host "[VERSION] New version: $newVersion" -ForegroundColor Yellow
} else {
    Write-Host "[ERROR] Failed to push to GitHub!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[COMPLETE] Auto-push finished!" -ForegroundColor Cyan
