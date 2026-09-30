# Rebuilds every retro16 deliverable into Tools/Blender/_tmp/variants/retro16 (never touches Assets/).
# Usage (PowerShell):  .\Tools\Blender\variants\retro16\build_retro16.ps1 [-Blender "...\blender.exe"]
param(
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($s in @("r16_fish.py", "r16_character.py", "r16_lake.py", "r16_preview.py")) {
    Write-Host "== $s"
    & $Blender -b --python (Join-Path $here $s) 2>&1 | Where-Object { $_ -match "Error|Traceback|R16" } | ForEach-Object { Write-Host "   $_" }
}
