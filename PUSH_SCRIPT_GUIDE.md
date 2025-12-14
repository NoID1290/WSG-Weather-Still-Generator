# Auto-push script (`push.ps1`) — short guide

Use `push.ps1` to bump a semantic version segment and push a commit.

Version format: `a.b.c.MMDD` (a=frontend, b=backend, c=patch)

Basic usage
```
.\push.ps1
```

Common options
```
.\push.ps1 -Type frontend
.\push.ps1 -Type backend
.\push.ps1 -Type fix -CommitMessage "Message" -Branch develop
```

Behavior
- Reads version from `WeatherImageGenerator.csproj` and updates the selected segment
- Updates the date (MMDD), commits, and pushes

Requirements
- Git, PowerShell 5.1+, and a `csproj` with version fields
