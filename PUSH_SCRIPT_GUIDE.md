# Auto-Push Script Usage Guide

## Overview
The `push.ps1` script automatically increments your version number and pushes to GitHub in one command.

## Version Format
`a.b.c.MMDD`
- **a** = Frontend updates (GUI changes)
- **b** = Backend updates  
- **c** = Little fixes/patches
- **MMDD** = Month and day of push (auto-generated)

## How to Use

### Basic Usage (fix - patch version)
```powershell
.\push.ps1
```
- Updates: `0.0.0.1213` → `0.0.1.1213`
- Message: "Auto-commit: Version update (v0.0.1.1213)"

### Frontend Update
```powershell
.\push.ps1 -Type frontend
```
- Updates: `0.0.0.1213` → `1.0.0.1213`
- Resets b and c to 0

### Backend Update
```powershell
.\push.ps1 -Type backend
```
- Updates: `0.0.0.1213` → `0.1.0.1213`
- Resets c to 0

### Custom Commit Message
```powershell
.\push.ps1 -Type fix -CommitMessage "Fixed login bug"
```
- Message: "Fixed login bug (v0.0.1.1213)"

### Specify Branch
```powershell
.\push.ps1 -Type fix -Branch "develop"
```
- Pushes to the develop branch instead of main

## Parameters
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Type` | string | "fix" | Update type: frontend, backend, or fix |
| `CommitMessage` | string | "Auto-commit: Version update" | Custom commit message |
| `Branch` | string | "main" | Target branch for push |

## Examples
```powershell
# Simple fix
.\push.ps1

# Backend with custom message
.\push.ps1 -Type backend -CommitMessage "Added API endpoint"

# Frontend update to develop branch
.\push.ps1 -Type frontend -CommitMessage "New UI redesign" -Branch develop

# All parameters
.\push.ps1 -Type backend -CommitMessage "Database optimization" -Branch main
```

## What Happens
1. Reads current version from `WeatherImageGenerator.csproj`
2. Increments the appropriate version segment
3. Updates the date portion (MMDD)
4. Saves changes to `.csproj`
5. Stages the file with git
6. Creates a commit with your message + version
7. Pushes to GitHub

## Requirements
- Git installed and configured
- GitHub credentials configured (SSH or HTTP)
- PowerShell 5.1 or higher
- `.csproj` file must have Version, AssemblyVersion, and FileVersion properties
