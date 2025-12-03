# ============================================
#   1. SETTINGS & PATHING
# ============================================

# Force "Dot" decimals (Fixes 5,0 vs 5.0 issues on French/EU systems)
$Cult = [System.Globalization.CultureInfo]::InvariantCulture

# AUTO-DETECT SCRIPT FOLDER
if ($PSScriptRoot) { 
    $WorkingDir = $PSScriptRoot 
} else { 
    $WorkingDir = Get-Location 
}

# File settings
$ImageFolder = $WorkingDir
$Extensions = "*.png","*.jpg","*.jpeg","*.bmp","*.webp"
$OUTPUT = "$WorkingDir\slideshow_fixed.mp4"
$MUSIC = "$WorkingDir\music.mp3"

# Video Timing Settings
$DUR = 8      # Time image is STATIC (fully visible)
$FADE = 0.5     # Time spent TRANSITIONING (fading)
$FPS = 30       # Output framerate

# Resolution: "1080p", "4k", or "vertical"
$RES_MODE = "1080p"

# ============================================
#   2. LOAD IMAGES
# ============================================

Write-Host "------------------------------------------------"
Write-Host "WORKING DIRECTORY: $WorkingDir" -ForegroundColor Cyan
Write-Host "------------------------------------------------"

$Images = Get-ChildItem -Path "$ImageFolder\*" -Include $Extensions -File | Sort-Object Name

if ($Images.Count -lt 2) {
    Write-Host "[ERROR] Found $($Images.Count) images." -ForegroundColor Red
    Write-Host "Please ensure at least 2 images are in this folder."
    exit
}

Write-Host "[INFO] Found $($Images.Count) images. Processing..." -ForegroundColor Green

# ============================================
#   3. CALCULATE RESOLUTION & FILTERS
# ============================================

switch ($RES_MODE.ToLower()) {
    "1080p"    { $W=1920; $H=1080 }
    "4k"       { $W=3840; $H=2160 }
    "vertical" { $W=1080; $H=1920 }
    default    { $W=1920; $H=1080 }
}

# Calculate exact duration (Static + Fade)
$ClipDuration = $DUR + $FADE
# Convert to string with dot decimal for FFmpeg
$ClipDurStr = $ClipDuration.ToString($Cult)

# Pre-Scale Filter
$PreScale = "scale=$($W):$($H):force_original_aspect_ratio=decrease,pad=$($W):$($H):(ow-iw)/2:(oh-ih)/2,setsar=1"

# ============================================
#   4. BUILD FILTER COMPLEX
# ============================================

$FilterParts = @()
$Inputs = ""
$Index = 0

# A. INPUT CHAIN
foreach ($Img in $Images) {
    # -loop 1 inputs the image, -t sets the duration
    $Inputs += "-loop 1 -t $ClipDurStr -i `"$($Img.FullName)`" "
    
    # Scale and reset timestamps
    $FilterParts += "[${Index}:v]$PreScale,framerate=$FPS,setpts=PTS-STARTPTS[v$Index];"
    
    $Index++
}

# B. TRANSITION CHAIN (XFADE)
$LastLabel = "[v0]" 
$CurrentOffset = 0

# Convert FADE to safe string
$FadeStr = $FADE.ToString($Cult)

for ($i=0; $i -lt $Images.Count-1; $i++) {
    $NextInput = "[v$($i+1)]"
    $OutputLabel = "[f$i]"
    
    # Calculate offset (Accumulate Static time only)
    $CurrentOffset += $DUR
    $OffStr = $CurrentOffset.ToString($Cult)
    
    # FIX: Use Format Operator (-f) to build the string safely
    # This guarantees the syntax "duration=5" instead of accidental "duration==5"
    $XFadeString = "{0}{1}xfade=transition=fade:duration={2}:offset={3}{4};" -f $LastLabel, $NextInput, $FadeStr, $OffStr, $OutputLabel
    
    $FilterParts += $XFadeString
    
    $LastLabel = $OutputLabel
}

# Final mapping
if ($Images.Count -eq 1) { $FinalMap = "[v0]" } else { $FinalMap = $LastLabel }

$FilterParts += "$FinalMap" + "format=yuv420p[outv]"

# Join all parts into one long string
$FilterComplex = [string]::Join("", $FilterParts)

# ============================================
#   5. EXECUTE FFMPEG
# ============================================

# We assemble the command carefully to avoid quote issues
$FFMPEG_CMD = "ffmpeg -y $Inputs -filter_complex `"$FilterComplex`" -map `"[outv]`""

# Add Audio if found
if (Test-Path $MUSIC) {
    Write-Host "[AUDIO] Adding Audio: $MUSIC" -ForegroundColor Magenta
    $FFMPEG_CMD += " -i `"$MUSIC`" -map `"$($Images.Count):a`" -shortest "
}

$FFMPEG_CMD += " -c:v libx264 -preset medium -crf 23 `"$OUTPUT`""

Write-Host "[RUNNING] Starting FFmpeg..." -ForegroundColor Green

# Use cmd /c to execute the string
cmd /c $FFMPEG_CMD

Write-Host ""
if (Test-Path $OUTPUT) {
    Write-Host "[DONE] Video saved to: $OUTPUT" -ForegroundColor Cyan
} else {
    Write-Host "[FAIL] FFmpeg failed to create the file." -ForegroundColor Red
}