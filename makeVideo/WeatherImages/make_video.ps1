# ============================================
#   1. SETTINGS & PATHING
# ============================================

$Cult = [System.Globalization.CultureInfo]::InvariantCulture

if ($PSScriptRoot) { 
    $WorkingDir = $PSScriptRoot 
} else { 
    $WorkingDir = Get-Location 
}

# File settings
$ImageFolder = $WorkingDir
$Extensions = "*.png","*.jpg","*.jpeg","*.bmp","*.webp"
$OUTPUT = "$WorkingDir\slideshow_v3.mp4"
$MUSIC = "$WorkingDir\music.mp3"

# Video Timing Settings
$DUR = 8        # Time image is STATIC
$FADE = 0.5     # Time spent TRANSITIONING
$FPS = 30       # Output framerate

# Resolution: "1080p", "4k", or "vertical"
$RES_MODE = "1080p"

# Enable/disable fade transitions
$ENABLE_FADE = $false

# ============================================
#   2. LOAD IMAGES
# ============================================

Write-Host "------------------------------------------------"
Write-Host "WORKING DIRECTORY: $WorkingDir" -ForegroundColor Cyan
Write-Host "------------------------------------------------"

$Images = Get-ChildItem -Path "$ImageFolder\*" -Include $Extensions -File | Sort-Object Name

if ($Images.Count -lt 2) {
    Write-Host "[ERROR] Found $($Images.Count) images." -ForegroundColor Red
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

$ClipDuration = $DUR + $FADE
$ClipDurStr = $ClipDuration.ToString($Cult)

# --- FIX 1: FORCE PIXEL FORMAT EARLY ---
# We added "format=yuv420p" to ensure every image has the same color space immediately
$PreScale = "scale=$($W):$($H):force_original_aspect_ratio=decrease,pad=$($W):$($H):(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p"

# ============================================
#   4. BUILD FILTER COMPLEX
# ============================================

$FilterParts = @()
$Inputs = ""
$Index = 0

foreach ($Img in $Images) {
    # Keep the framerate input option, it helps the demuxer
    $Inputs += "-framerate $FPS -loop 1 -t $ClipDurStr -i `"$($Img.FullName)`" "
    
    # --- FIX 2: USE 'fps' INSTEAD OF 'framerate' ---
    # We replaced 'framerate=$FPS' with 'fps=$FPS' which is more robust for static loops
    $FilterParts += "[${Index}:v]$PreScale,fps=$FPS,setpts=PTS-STARTPTS[v$Index];"
    
    $Index++
}

$LastLabel = "[v0]" 
$CurrentOffset = 0
$FadeStr = $FADE.ToString($Cult)



for ($i=0; $i -lt $Images.Count-1; $i++) {
    $NextInput = "[v$($i+1)]"
    $OutputLabel = "[f$i]"
    
    $CurrentOffset += $DUR
    $OffStr = $CurrentOffset.ToString($Cult)
    

    
    $XFadeString = "{0}{1}xfade=transition=fade:duration={2}:offset={3}{4};" -f $LastLabel, $NextInput, $FadeStr, $OffStr, $OutputLabel
    
    if ($ENABLE_FADE) {
        $FilterParts += $XFadeString
    } else { 
        # If fades are disabled, use a simple concat instead
        $ConcatString = "{0}{1}concat=n=2:v=1:a=0{2};" -f $LastLabel, $NextInput, $OutputLabel
        $FilterParts += $ConcatString
    }
    $LastLabel = $OutputLabel
}

if ($Images.Count -eq 1) { $FinalMap = "[v0]" } else { $FinalMap = $LastLabel }

# We don't need format=yuv420p at the end anymore because we did it at the start, 
# but keeping it ensures safety for the encoder.
$FilterParts += "$FinalMap" + "format=yuv420p[outv]"

$FilterComplex = [string]::Join("", $FilterParts)

# ============================================
#   5. EXECUTE FFMPEG
# ============================================

$FFMPEG_CMD = "ffmpeg -y $Inputs -filter_complex `"$FilterComplex`" -map `"[outv]`""

if (Test-Path $MUSIC) {
    Write-Host "[AUDIO] Adding Audio: $MUSIC" -ForegroundColor Magenta
    $FFMPEG_CMD += " -i `"$MUSIC`" -map `"$($Images.Count):a`" -shortest "
}

$FFMPEG_CMD += " -c:v libx264 -preset medium -crf 23 `"$OUTPUT`""

Write-Host "[RUNNING] Starting FFmpeg..." -ForegroundColor Green

cmd /c $FFMPEG_CMD

Write-Host ""
if (Test-Path $OUTPUT) {
    Write-Host "[DONE] Video saved to: $OUTPUT" -ForegroundColor Cyan
} else {
    Write-Host "[FAIL] FFmpeg failed to create the file." -ForegroundColor Red
}