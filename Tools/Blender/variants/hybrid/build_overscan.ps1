# Renders a stage with OVERSCAN: its layers wider than the 640 px layout (Assets/Resources/Data/stage_<id>.json widthPx,
# e.g. the sea's 800), for the game's camera to pan over when a fish runs past its 480 px home frame
# (Assets/Scripts/Core/ViewZoom.cs). The same camera and focal length: the extra columns are just more of the same view.
# Per target (the legacy <stage>_back / _front + stage json, and each period):
#   1. the 640 layout (FK_CANVAS_W=640, dry): today's images, kept in _tmp/variants/hybrid/overscan/
#   2. the wide canvas (installs the period's front / back / look json)
#   3. hyb_overscan.py: the 640 layout's back layer into the centre of the wide one (the back's random dash dithering
#      would otherwise re-roll across the whole width: the game's home view stays pixel-identical); the front's home
#      view is checked identical (its geometry is the 640 layout's, built first; the overscan props come after)
# then the front depth map (hyb_frontdepth.py, installed) and the obstacles (build_obstacles.ps1, installed + checks),
# and the period review sheet. The stage script must build the 640 layout first and exactly as before (its anchor
# columns + hyb_core.OX, extensions on their own random): see hyb_sea.py.
# Usage (PowerShell): .\Tools\Blender\variants\hybrid\build_overscan.ps1 -Stage sea [-Target legacy,day] [-NoDepth] [-NoObstacles]
param(
    [Parameter(Mandatory = $true)][ValidateSet("lake", "stream", "sea", "swamp", "ice", "ocean", "cave")][string]$Stage,
    [ValidateSet("legacy", "dawn", "day", "evening", "night")][string[]]$Target = @("legacy", "dawn", "day", "evening", "night"),
    [switch]$NoDepth,
    [switch]$NoObstacles,
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here "..\..\..\..")
$out = Join-Path $root "Tools\Blender\_tmp\variants\hybrid"
$scr = Join-Path $out "overscan"
$spr = Join-Path $root "Assets\Resources\Sprites\Stages"
$data = Join-Path $root "Assets\Resources\Data"
$layout = Get-Content (Join-Path $data "stage_$Stage.json") -Raw | ConvertFrom-Json
if ([int]$layout.widthPx -le 640) { throw "stage_$Stage.json widthPx is $($layout.widthPx): no overscan to render (set it wider first)" }
New-Item -ItemType Directory -Force $scr | Out-Null
$filter = { $_ -cmatch "Error|Traceback|HYB PERIOD CHECK|HYB OVERSCAN|HYB OBST|HYB FDEPTH|HYB PREVIEW sheet|CHECK" }
function Blend([string[]]$a) {
    & $Blender -b --python @a 2>&1 | Where-Object $filter | ForEach-Object { Write-Host "   $_" }
}
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($t in $Target) {
    $per = if ($t -eq "legacy") { @() } else { @("--period", $t) }
    $name = if ($t -eq "legacy") { $Stage } else { "$($Stage)_$t" }
    $tmp = if ($t -eq "legacy") { $out } else { Join-Path $out "periods" }
    Write-Host "== $name : the 640 layout"
    $env:FK_CANVAS_W = "640"
    try { Blend (@((Join-Path $here "hyb_$Stage.py"), "--") + $per + @("--dry")) } finally { Remove-Item Env:FK_CANVAS_W }
    Copy-Item (Join-Path $tmp "$($name)_back.png") (Join-Path $scr "$($name)_back_640.png") -Force
    Copy-Item (Join-Path $tmp "$($name)_front.png") (Join-Path $scr "$($name)_front_640.png") -Force
    Write-Host "== $name : the $($layout.widthPx) px canvas"
    Blend (@((Join-Path $here "hyb_$Stage.py"), "--") + $per)
    $wideBack = Join-Path $tmp "$($name)_back.png"
    Blend @((Join-Path $here "hyb_overscan.py"), "--", $wideBack, (Join-Path $scr "$($name)_back_640.png"), $wideBack,
        (Join-Path $spr "$($name)_back.png"), "--front", (Join-Path $tmp "$($name)_front.png"), (Join-Path $scr "$($name)_front_640.png"))
    if ($t -eq "legacy") {
        # (a legacy run never installs: its front and the stage json go in like build_hybrid.ps1 -Install)
        Copy-Item (Join-Path $out "$($Stage)_front.png") $spr -Force
        Copy-Item (Join-Path $out "stage_$Stage.json") $data -Force
    }
}
if (-not $NoDepth) {
    Write-Host "== front depth map"
    Blend @((Join-Path $here "hyb_frontdepth.py"), "--", $Stage)
}
if (-not $NoObstacles) {
    Write-Host "== obstacles"
    & (Join-Path $here "build_obstacles.ps1") -Stage $Stage -Blender $Blender
}
Write-Host "== review sheet"
Blend @((Join-Path $here "hyb_period_preview.py"), "--", $Stage)
Write-Host ("== done in {0:0} s" -f $sw.Elapsed.TotalSeconds)
