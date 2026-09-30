# Rebuilds every hybrid deliverable into Tools/Blender/_tmp/variants/hybrid.
# Order matters: fish (the previews use the _t sprites) -> character -> each stage (writes stage_<id>.json)
# followed by its preview -> map -> aquarium + preview -> preset plates -> sanity check.
# -Install then copies the game files into Assets/Resources (sprites + Data JSON).
# Usage (PowerShell):  .\Tools\Blender\variants\hybrid\build_hybrid.ps1 [-Blender "...\blender.exe"] [-Install]
# (the time-of-day looks <stage>_<period>_*.png are built by build_periods.ps1, not here: Docs/time_currents_spec.md 12)
param(
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$scripts = @("hyb_fish.py", "hyb_character.py",
    "hyb_lake.py", "hyb_preview.py",
    "hyb_stream.py", "hyb_preview_stream.py",
    "hyb_sea.py", "hyb_preview_sea.py",
    "hyb_swamp.py", "hyb_preview_swamp.py",
    "hyb_ice.py", "hyb_preview_ice.py",
    "hyb_ocean.py", "hyb_preview_ocean.py",
    "hyb_cave.py", "hyb_preview_cave.py",
    "hyb_map.py",
    "hyb_aquarium.py", "hyb_preview_aquarium.py",
    "hyb_presets.py", "hyb_check.py",
    "hyb_encounter.py")
foreach ($s in $scripts) {
    Write-Host "== $s"
    # hyb_encounter.py builds every encounter_sets/<set>.py (the cave alone gives the legacy output)
    $extra = if ($s -eq "hyb_encounter.py") { @("--", "--all") } else { @() }
    & $Blender -b --python (Join-Path $here $s) @extra 2>&1 | Where-Object { $_ -cmatch "Error|Traceback|HYB |CHECK" } | ForEach-Object { Write-Host "   $_" }
}

if ($Install) {
    $root = Resolve-Path (Join-Path $here "..\..\..\..")
    $out = Join-Path $root "Tools\Blender\_tmp\variants\hybrid"
    $res = Join-Path $root "Assets\Resources"
    foreach ($id in @("lake", "stream", "sea", "swamp", "ice", "ocean", "cave")) {
        Copy-Item (Join-Path $out "$($id)_back.png"), (Join-Path $out "$($id)_front.png") (Join-Path $res "Sprites\Stages")
        Copy-Item (Join-Path $out "stage_$id.json") (Join-Path $res "Data")
    }
    Copy-Item (Join-Path $out "map_world.png"), (Join-Path $out "aquarium_back.png"), (Join-Path $out "aquarium_front.png") (Join-Path $res "Sprites\Stages")
    Copy-Item (Join-Path $out "map.json"), (Join-Path $out "aquarium.json"), (Join-Path $out "character\character.json") (Join-Path $res "Data")
    Copy-Item (Join-Path $out "character\angler_*.png") (Join-Path $res "Sprites\Character")
    Copy-Item (Join-Path $out "fish\*.png") (Join-Path $res "Sprites\Fish")
    # legend encounter set (hyb_encounter.py): every PNG except the review sheets (encounter_sheet*.png)
    New-Item -ItemType Directory -Force (Join-Path $res "Sprites\Encounter") | Out-Null
    Get-ChildItem (Join-Path $out "encounter\*.png") | Where-Object { -not $_.Name.StartsWith("encounter_sheet") -and -not $_.Name.StartsWith("_") } |
        Copy-Item -Destination (Join-Path $res "Sprites\Encounter")
    Write-Host "== installed into $res"
}
