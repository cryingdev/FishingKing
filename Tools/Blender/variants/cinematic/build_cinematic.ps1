# Rebuilds the whole "cinematic" style variant into Tools/Blender/_tmp/variants/cinematic (never touches Assets/).
param([string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($s in @("cine_character.py", "cine_fish.py", "cine_lake.py", "cine_preview.py")) {
    Write-Host "== $s"
    & $Blender -b --python (Join-Path $here $s) 2>&1 | Where-Object { $_ -match "Error|Traceback|done" } | ForEach-Object { Write-Host "   $_" }
}
