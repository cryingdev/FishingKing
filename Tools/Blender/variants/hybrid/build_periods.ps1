# Renders a stage's time-of-day looks (Docs/time_currents_spec.md 3 / 12) and its review sheet.
# Every period run writes _tmp/variants/hybrid/periods/<stage>_<p>_back.png / _front.png / <stage>_<p>.json and, unless
# -Dry, installs Assets/Resources/Sprites/Stages/<stage>_<p>_back.png / _front.png + Assets/Resources/Data/Periods/<stage>_<p>.json.
# The legacy files (<stage>_back.png, stage_<stage>.json) are never touched; the native period prints its byte check.
# Usage (PowerShell, from anywhere):
#   .\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake                  # all 4 periods, then the sheet
#   .\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake -Period night    # one period, then the sheet
#   .\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake -Period night -Dry -Parallel
param(
    [Parameter(Mandatory = $true)][ValidateSet("lake", "stream", "sea", "swamp", "ice", "ocean", "cave")][string]$Stage,
    [ValidateSet("dawn", "day", "evening", "night")][string[]]$Period = @("dawn", "day", "evening", "night"),
    [switch]$Dry,
    [switch]$Parallel,
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
# a stage rendered with OVERSCAN (Data/stage_<id>.json widthPx > 640, the sea): its periods go through
# build_overscan.ps1 (the 640 layout and the wide canvas, the back layer spliced so the home view stays as it is)
$layoutPath = Join-Path (Resolve-Path (Join-Path $here "..\..\..\..")) "Assets\Resources\Data\stage_$Stage.json"
if ([int](Get-Content $layoutPath -Raw | ConvertFrom-Json).widthPx -gt 640) {
    if ($Dry) { throw "$Stage is an overscan stage: build_overscan.ps1 renders and installs (no -Dry)" }
    & (Join-Path $here "build_overscan.ps1") -Stage $Stage -Target $Period -NoDepth -NoObstacles -Blender $Blender
    return
}
$filter = { $_ -cmatch "Error|Traceback|HYB PERIOD|HYB PREVIEW|CHECK" }
$extra = @(if ($Dry) { "--dry" })      # always an array: splatting a bare string would pass it letter by letter
if ($Parallel) {
    $logs = @{}
    $procs = foreach ($p in $Period) {
        $log = Join-Path $env:TEMP "fk_period_$($Stage)_$p.log"
        $logs[$p] = $log
        Start-Process -FilePath $Blender -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
            -ArgumentList (@("-b", "--python", (Join-Path $here "hyb_$Stage.py"), "--", "--period", $p) + $extra)
    }
    $procs | ForEach-Object { $_.WaitForExit() }
    foreach ($p in $Period) { Write-Host "== $Stage $p"; Get-Content $logs[$p], "$($logs[$p]).err" | Where-Object $filter | ForEach-Object { Write-Host "   $_" } }
} else {
    foreach ($p in $Period) {
        Write-Host "== $Stage $p"
        & $Blender -b --python (Join-Path $here "hyb_$Stage.py") -- --period $p @extra 2>&1 | Where-Object $filter | ForEach-Object { Write-Host "   $_" }
    }
}
Write-Host "== review sheet"
& $Blender -b --python (Join-Path $here "hyb_period_preview.py") -- $Stage 2>&1 | Where-Object $filter | ForEach-Object { Write-Host "   $_" }
