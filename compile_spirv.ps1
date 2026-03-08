# compile_spirv.ps1 — Offline SPIR-V compilation for Vulkan shaders
# Requires glslc from the Vulkan SDK (https://vulkan.lunarg.com/)
# Usage: .\compile_spirv.ps1
#
# Compiles all GLSL 4.50 shaders from Rendering/Vulkan/shaders/ to .spv
# Output files are placed alongside the sources and included in the build.

$ErrorActionPreference = "Stop"

$shaderDir = Join-Path (Join-Path (Join-Path (Join-Path $PSScriptRoot "WeatherImageGenerator") "Rendering") "Vulkan") "shaders"

# Detect glslc from Vulkan SDK or PATH
$glslc = Get-Command "glslc" -ErrorAction SilentlyContinue
if (-not $glslc) {
    $vulkanSdk = $env:VULKAN_SDK
    if ($vulkanSdk) {
        $glslcPath = Join-Path $vulkanSdk "Bin" "glslc.exe"
        if (Test-Path $glslcPath) { $glslc = $glslcPath }
    }
}
if (-not $glslc) {
    # Try common install locations
    $commonPaths = @(
        "C:\VulkanSDK\*\Bin\glslc.exe",
        "$env:USERPROFILE\VulkanSDK\*\Bin\glslc.exe"
    )
    foreach ($p in $commonPaths) {
        $found = Get-Item $p -ErrorAction SilentlyContinue | Sort-Object -Descending | Select-Object -First 1
        if ($found) { $glslc = $found.FullName; break }
    }
}

if (-not $glslc) {
    Write-Host "[ERROR] glslc not found. Install the Vulkan SDK from https://vulkan.lunarg.com/" -ForegroundColor Red
    Write-Host "        Or set VULKAN_SDK environment variable to your SDK path." -ForegroundColor Red
    exit 1
}

Write-Host "[SPIR-V] Using compiler: $glslc" -ForegroundColor Cyan

# Shader compilation map: source → stage type
$shaders = @(
    @{ Name = "tile.vert.glsl";              Stage = "vert" }
    @{ Name = "tile.frag.glsl";              Stage = "frag" }
    @{ Name = "weather_overlay.vert.glsl";   Stage = "vert" }
    @{ Name = "weather_overlay.frag.glsl";   Stage = "frag" }
    @{ Name = "overlay.vert.glsl";           Stage = "vert" }
    @{ Name = "overlay.frag.glsl";           Stage = "frag" }
    @{ Name = "vertex.glsl";                 Stage = "vert" }
    @{ Name = "fragment.glsl";               Stage = "frag" }
    @{ Name = "ui.vert.glsl";               Stage = "vert" }
    @{ Name = "ui.frag.glsl";               Stage = "frag" }
    # GRIB2 weather data visualization shaders
    @{ Name = "grib2_data.vert.glsl";       Stage = "vert" }
    @{ Name = "grib2_data.frag.glsl";       Stage = "frag" }
    @{ Name = "grib2_particles.vert.glsl";  Stage = "vert" }
    @{ Name = "grib2_particles.frag.glsl";  Stage = "frag" }
    @{ Name = "grib2_wind.vert.glsl";       Stage = "vert" }
    @{ Name = "grib2_wind.frag.glsl";       Stage = "frag" }
    @{ Name = "grib2_clouds.vert.glsl";     Stage = "vert" }
    @{ Name = "grib2_clouds.frag.glsl";     Stage = "frag" }
    @{ Name = "grib2_contour.vert.glsl";    Stage = "vert" }
    @{ Name = "grib2_contour.frag.glsl";    Stage = "frag" }
    @{ Name = "grib2_atmosphere.vert.glsl"; Stage = "vert" }
    @{ Name = "grib2_atmosphere.frag.glsl"; Stage = "frag" }
)

$errors = 0
$compiled = 0

foreach ($shader in $shaders) {
    $src = Join-Path $shaderDir $shader.Name
    $spvName = $shader.Name -replace "\.glsl$", ".spv"
    $dst = Join-Path $shaderDir $spvName
    $stage = $shader.Stage

    if (-not (Test-Path $src)) {
        Write-Host "  [SKIP] $($shader.Name) - source not found" -ForegroundColor Yellow
        continue
    }

    Write-Host "  [COMPILE] $($shader.Name) -> $spvName" -ForegroundColor Gray -NoNewline

    $stageArg = "-fshader-stage=$stage"
    $result = & $glslc $stageArg --target-env=vulkan1.0 -o $dst $src 2>&1
    if ($LASTEXITCODE -eq 0) {
        $size = (Get-Item $dst).Length
        Write-Host " ($size bytes)" -ForegroundColor Green
        $compiled++
    }
    else {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
        $errors++
    }
}

Write-Host ""
if ($errors -eq 0) {
    Write-Host "[SPIR-V] All $compiled shaders compiled successfully." -ForegroundColor Green
}
else {
    Write-Host "[SPIR-V] $errors shader(s) failed, $compiled succeeded." -ForegroundColor Red
    exit 1
}
