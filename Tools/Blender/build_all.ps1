# Regenerates every sprite + layout JSON from the Blender scripts into Assets/Resources.
# Usage (PowerShell):  .\Tools\Blender\build_all.ps1  [-Blender "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"]
param(
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$steps = @(
    @("fk_fish.py", @()),                                   # 42 species: side view (2 frames) + top view shadows
    @("fk_items.py", @()),                                  # rods, reels, lines, baits, tanks, UI icons, 9-slice frames, floats
    @("fk_character.py", @()),                              # angler seen from behind, 7 poses + rod anchors
    @("fk_stages.py", @("--", "lake", "stream", "sea", "swamp", "ice", "ocean", "cave", "clouds")),
    @("fk_misc.py", @())                                    # world map, aquarium, logo
)
foreach ($s in $steps) {
    $script = Join-Path $here $s[0]
    Write-Host "== $($s[0])"
    & $Blender -b --python $script @($s[1]) 2>&1 | Where-Object { $_ -match "Error|Traceback|done|ok" } | ForEach-Object { Write-Host "   $_" }
}
Write-Host "All assets rebuilt. Unity re-imports them automatically (or use FishingKing > Reimport Sprites)."
