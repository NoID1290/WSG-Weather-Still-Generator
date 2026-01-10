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
    ,
    [Parameter(Mandatory=$false)]
    [switch]$AttachAssets,
    [Parameter(Mandatory=$false)]
    [switch]$SkipVersion,
    [Parameter(Mandatory=$false)]
    [switch]$NoRelease
)

$projectFilePath = "WeatherImageGenerator\WeatherImageGenerator.csproj"
$ecccProjectFilePath = "ECCC\ECCC.csproj"
$solutionPath = "WSG.sln"

#$repoRoot = git rev-parse --show-toplevel

# Will populate with changelog content to use as GitHub release notes
$releaseNotes = $null

if (-not $?) {
    Write-Host "❌ Error: Not in a Git repository!" -ForegroundColor Red
    exit 1
}

Write-Host "[START] Auto-push process..." -ForegroundColor Cyan
Write-Host "[TYPE] Update type: $Type" -ForegroundColor Yellow
Write-Host "[FLAGS] NoRelease: $NoRelease   AttachAssets: $AttachAssets   SkipVersion: $SkipVersion" -ForegroundColor Yellow

function Update-ProjectVersion {
    param (
        [string]$Path,
        [string]$Name,
        [string]$UpdateType
    )

    if (-not (Test-Path $Path)) {
        Write-Host "[WARNING] Project $Name not found at $Path" -ForegroundColor Yellow
        return $null
    }

    # Read the project file
    [xml]$proj = Get-Content $Path

    # Get current version
    $ver = $proj.Project.PropertyGroup.Version
    if (-not $ver) { $ver = "1.0.0.0101" } # Default if missing
    
    # Check if this update type applies to this project
    # ECCC (Lib) shouldn't update on Frontend changes
    if ($Name -eq "ECCC" -and $UpdateType -eq "frontend") {
        Write-Host "[INFO] Skipping $Name version update (Frontend change only)" -ForegroundColor Gray
        return $ver
    }

    Write-Host "[$Name] Current version: $ver" -ForegroundColor White

    # Parse version parts
    $parts = $ver -split '\.'
    # Ensure we have at least 3 parts
    while ($parts.Count -lt 3) { $parts += "0" }
    
    [int]$vA = $parts[0]
    [int]$vB = $parts[1]
    [int]$vC = $parts[2]

    # Increment based on update type
    switch ($UpdateType) {
        "frontend" {
            $vA++
            $vB = 0
            $vC = 0
            Write-Host "[$Name] Frontend version incremented" -ForegroundColor Green
        }
        "backend" {
            $vB++
            $vC = 0
            Write-Host "[$Name] Backend version incremented" -ForegroundColor Green
        }
        "fix" {
            $vC++
            Write-Host "[$Name] Fix version incremented" -ForegroundColor Green
        }
    }

    # Get today's date in MMDD format
    $today = Get-Date
    $dateStr = $today.ToString("MMdd")

    # Create new version: a.b.c.MMDD
    $newVer = "$vA.$vB.$vC.$dateStr"

    # Update properties
    $proj.Project.PropertyGroup.Version = $newVer
    $proj.Project.PropertyGroup.AssemblyVersion = $newVer
    $proj.Project.PropertyGroup.FileVersion = $newVer

    # Save
    $proj.Save($Path)
    Write-Host "[$Name] Updated to: $newVer" -ForegroundColor Green
    
    return $newVer
}

# Update Versions
if (-not $SkipVersion) {
    $newWsgVersion = Update-ProjectVersion -Path $projectFilePath -Name "WSG" -UpdateType $Type
    $newEcccVersion = Update-ProjectVersion -Path $ecccProjectFilePath -Name "ECCC" -UpdateType $Type
    
    # Use WSG version for global tagging/changelog as it's the main app
    $newVersion = $newWsgVersion
} else {
    Write-Host "[INFO] SkipVersion is set; not incrementing versions" -ForegroundColor Yellow
    [xml]$p = Get-Content $projectFilePath
    $newVersion = $p.Project.PropertyGroup.Version
}


# Also update AssemblyInfo.cs to keep it in sync
$assemblyInfoPath = "WeatherImageGenerator\AssemblyInfo.cs"
if (Test-Path $assemblyInfoPath) {
    $assemblyInfoContent = Get-Content $assemblyInfoPath -Raw

    # Use -replace operator with proper backreferences
    $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyVersion\("[^"]*"\)\]', "[assembly: AssemblyVersion(""$newVersion"")]"
    $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyFileVersion\("[^"]*"\)\]', "[assembly: AssemblyFileVersion(""$newVersion"")]"
    $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyInformationalVersion\("[^"]*"\)\]', "[assembly: AssemblyInformationalVersion(""$newVersion"")]"

    # If any of the attributes are missing, append them so the file stays explicit
    if ($assemblyInfoContent -notmatch 'AssemblyVersion') {
        $assemblyInfoContent += "`r`n[assembly: AssemblyVersion(""$newVersion"")]"
    }
    if ($assemblyInfoContent -notmatch 'AssemblyFileVersion') {
        $assemblyInfoContent += "`r`n[assembly: AssemblyFileVersion(""$newVersion"")]"
    }
    if ($assemblyInfoContent -notmatch 'AssemblyInformationalVersion') {
        $assemblyInfoContent += "`r`n[assembly: AssemblyInformationalVersion(""$newVersion"")]"
    }

    if (-not $SkipVersion) {
        Set-Content -Path $assemblyInfoPath -Value $assemblyInfoContent -Encoding UTF8
        Write-Host "[SUCCESS] AssemblyInfo.cs updated with version: $newVersion" -ForegroundColor Green
    } else {
        Write-Host "[INFO] Skipping AssemblyInfo update due to SkipVersion" -ForegroundColor Yellow
    }
}

# Update CHANGELOG.md automatically
if (-not $SkipVersion) {
    Write-Host "[CHANGELOG] Updating CHANGELOG.md..." -ForegroundColor Cyan
    
    $changelogPath = "docs\CHANGELOG.md"
    $date = Get-Date -Format "yyyy-MM-dd"
    
    # Build the list of changes
    $commitList = @()
    
    # Add the main commit message if it's not the default
    if ($CommitMessage -and $CommitMessage -ne "Auto-commit: Version update") {
        # Remove "Auto-commit: " prefix if present and add to list
        $cleanMessage = $CommitMessage -replace "^Auto-commit:\s*", ""
        $commitList += "- $cleanMessage"
    }
    
    # Get additional commits since last tag (excluding version update commits)
    $lastTag = git describe --tags --abbrev=0 2>$null
    if ($lastTag) {
        $commits = git log "$lastTag..HEAD" --pretty=format:"%s" --no-merges 2>$null
        if ($commits) {
            foreach ($commit in $commits) {
                # Skip auto-commit and duplicate messages
                if ($commit -notmatch "^Auto-commit:|^Release |^Version update") {
                    $entry = "- $commit"
                    if ($commitList -notcontains $entry) {
                        $commitList += $entry
                    }
                }
            }
        }
    }
    
    # If still no commits, use a default message
    if ($commitList.Count -eq 0) {
        $commitList = @("- Version bump")
    }
    
    # Use a single generic category for all changes
    $categorySection = "### Changelog`n" + ($commitList -join "`n")
    
    # Read current changelog
    if (Test-Path $changelogPath) {
        $content = Get-Content $changelogPath -Raw
        
        # Create new entry
        $newEntry = @"

## [$newVersion] - $date

$categorySection

"@

        # Save for GitHub release notes
        $releaseNotes = $newEntry.Trim()
        
        # Find where to insert (after the header, before the first version entry)
        # Look for the first ## [ pattern which indicates a version entry
        $pattern = "(?s)(# Changelog.*?)(## \[)"
        if ($content -match $pattern) {
            $newContent = $content -replace $pattern, "`$1$newEntry`$2"
            Set-Content $changelogPath $newContent -Encoding UTF8
            Write-Host "[SUCCESS] CHANGELOG.md updated with version $newVersion" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] Could not find insertion point in CHANGELOG.md" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[WARNING] CHANGELOG.md not found" -ForegroundColor Yellow
    }
}

# Stage the updated files
Write-Host "[STAGING] Changes..." -ForegroundColor Cyan
git add $projectFilePath
git add $solutionPath
if (-not $SkipVersion) {
    git add $changelogPath
    git add $assemblyInfoPath
}

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

# Create and push a git tag for the new version (v{version}) if it doesn't exist
$tagName = "v$newVersion"
$existingTag = git tag --list $tagName
if (-not $existingTag) {
    Write-Host "[TAG] Creating annotated tag: $tagName" -ForegroundColor Cyan
    git tag -a $tagName -m "Release $tagName"
    if ($?) {
        Write-Host "[TAG] Pushing tag $tagName to origin" -ForegroundColor Cyan
        git push origin $tagName
        if ($?) {
            Write-Host "[SUCCESS] Tag pushed: $tagName" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] Failed to push tag $tagName" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[WARNING] Failed to create tag $tagName" -ForegroundColor Yellow
    }
} else {
    Write-Host "[INFO] Tag $tagName already exists; skipping tag creation" -ForegroundColor Yellow
}

# If NoRelease is set, skip creating a GitHub release
if ($NoRelease) {
    Write-Host "[INFO] NoRelease flag is set; skipping GitHub release creation and upload" -ForegroundColor Yellow
} else {
    # If GitHub CLI is available, create a GitHub release so shields using /v/release work
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        # Use changelog-derived notes when available
        if (-not $releaseNotes) { $releaseNotes = "Automated release for $tagName" }

        # Check if release already exists
        $releaseCheck = gh release view $tagName 2>$null
        if (-not $releaseCheck) {
            Write-Host "[RELEASE] Creating GitHub release for $tagName" -ForegroundColor Cyan
            gh release create $tagName --title "$tagName" --notes $releaseNotes --target $Branch
            if ($?) {
                Write-Host "[SUCCESS] GitHub release created: $tagName" -ForegroundColor Green
            } else {
                Write-Host "[WARNING] Failed to create GitHub release via gh CLI" -ForegroundColor Yellow
            }
        } else {
            Write-Host "[INFO] GitHub release $tagName already exists; updating notes" -ForegroundColor Yellow
            gh release edit $tagName --notes $releaseNotes
        }
    } else {
        Write-Host "[INFO] 'gh' CLI not found; skipping GitHub release creation" -ForegroundColor Yellow
    }
} 

# If requested, build the project, zip artifacts, and upload to the GitHub release
if ($AttachAssets) {
    if ($NoRelease) {
        Write-Host "[INFO] AttachAssets was requested but NoRelease is set; skipping asset upload" -ForegroundColor Yellow
    } else {
        Write-Host "[ASSETS] AttachAssets requested; building and uploading artifacts" -ForegroundColor Cyan

    # location for published artifacts
    $artifactRoot = Join-Path -Path (Split-Path -Parent $projectFilePath) -ChildPath "artifacts"
    $publishDir = Join-Path $artifactRoot "WeatherImageGenerator-$newVersion"

    # Clean previous artifacts
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    # Build/publish
    # We use -p:DebugType=None to suppress PDB generation for the main project
    Write-Host "[BUILD] dotnet publish -c Release -o $publishDir -p:DebugType=None" -ForegroundColor Cyan
    dotnet publish $projectFilePath -c Release -o $publishDir -p:DebugType=None

    if (-not $?) {
        Write-Host "[ERROR] dotnet publish failed; skipping artifact upload" -ForegroundColor Red
    } else {
        # Cleanup any dev files that might have been copied (PDBs from dependencies, XML docs)
        Write-Host "[CLEANUP] Ensuring no dev files (*.pdb, *.xml) in release" -ForegroundColor Cyan
        Get-ChildItem -Path $publishDir -Include *.pdb,*.xml -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

        # Create zip
        $zipName = "WSG-Weather-Still-Generator-$newVersion.zip"
        $zipPath = Join-Path $artifactRoot $zipName
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Write-Host "[ZIP] Creating $zipPath" -ForegroundColor Cyan
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

        # Upload to existing release via gh
        $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
        if ($ghCmd) {
            Write-Host "[UPLOAD] Uploading $zipName to release $tagName" -ForegroundColor Cyan
            gh release upload $tagName $zipPath --clobber
            if ($?) {
                Write-Host "[SUCCESS] Uploaded asset: $zipName" -ForegroundColor Green
            } else {
                Write-Host "[WARNING] Failed to upload asset $zipName" -ForegroundColor Yellow
            }
        } else {
            Write-Host "[INFO] 'gh' CLI not found; cannot upload assets" -ForegroundColor Yellow
        }
    }
    }
}

Write-Host "`n[COMPLETE] Auto-push finished!" -ForegroundColor Cyan
