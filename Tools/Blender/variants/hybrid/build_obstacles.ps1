# Exports a stage's obstacles (Docs/obstacles_spec.md 13): builds hyb_<stage>.py's scene WITHOUT rendering, collects the
# objects tagged by obstacles/<stage>.py, writes _tmp/variants/hybrid/obstacles/obstacles_<stage>.json and, unless -Dry,
# Assets/Resources/Data/obstacles_<stage>.json; then (unless -NoCheck) projects the export back onto the painted layers
# with the stage camera of Data/stage_<stage>.json and writes obstacles_<stage>_overlay.png + the "HYB OBST CHECK" lines.
# Usage (PowerShell, from anywhere):
#   .\Tools\Blender\variants\hybrid\build_obstacles.ps1 -Stage stream
#   .\Tools\Blender\variants\hybrid\build_obstacles.ps1 -Stage lake,swamp -Dry
#   .\Tools\Blender\variants\hybrid\build_obstacles.ps1 -All -NoCheck
param(
    [ValidateSet("lake", "stream", "sea", "swamp", "ice", "ocean", "cave")][string[]]$Stage = @(),
    [switch]$All,
    [switch]$Dry,
    [switch]$NoCheck,
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $All -and $Stage.Count -eq 0) { throw "name a stage (-Stage stream) or -All" }
# one Blender process per stage: the stage modules keep module-level state (presets, palettes)
$list = if ($All) { @("--all") } else { $Stage }
foreach ($s in $list) {
    $a = @($s) + @(if ($Dry) { "--dry" }) + @(if ($NoCheck) { "--no-check" })
    Write-Host "== obstacles $s"
    & $Blender -b --python (Join-Path $here "hyb_obstacles.py") -- @a 2>&1 |
        Where-Object { $_ -cmatch "HYB OBST|Error|Traceback" } | ForEach-Object { Write-Host "   $_" }
}
