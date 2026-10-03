<#
.SYNOPSIS
  The checks for a new species (Docs/data_reference.md 2.7 "확인 절차"): the validator, one build, then
  -fkauto newspecies for that species, plus (only when asked, with -Yes) short regression suites, in parallel.

.DESCRIPTION
  1. The species validator. With a build it is the build's own gate (FishingKingSetup.BuildWindows runs
     SpeciesValidator first and builds nothing on an error); with -NoBuild the editor batch method
     SpeciesValidator.Batch runs alone. Either way a validator error stops here.
  2. One Windows build (skipped with -NoBuild: the existing Builds/Windows/FishingKing.exe is used).
  3. In parallel: -fkauto newspecies -fkspecies <Id> (no pointer gestures) and the pointer-free -Regress suites, each
     in its own process with its own -fksave; the suites that drive the pointer with gestures (flicks, drags, taps:
     timing-sensitive) run one after another in a single lane beside them. At most -MaxParallel processes at a time
     (default: half the logical CPUs), each with its own timeout (killed when it runs over).
  4. A summary table (suite, result, failed, duration, log); exit code 1 on any failure.

  Regression suites are NEVER started unasked: -Regress without -Yes prints the plan and stops (exit 2), and -DryRun
  prints the plan (suites, lanes, timeouts, estimated time) and launches nothing. Ask the user (what, why, how long)
  before passing -Yes.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\Test\new_species.ps1 -Id freshwater_eel
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\Test\new_species.ps1 -Id freshwater_eel -Regress tour -DryRun
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\Test\new_species.ps1 -Id my_legend -Regress "encounter,legendspot" -NoBuild -Yes
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Id,
    # short regression suites, comma separated, each "name" or "name=arg" (see $Suites below / the doc)
    [string[]]$Regress = @(),
    [switch]$NoBuild,
    [string]$Out,
    [switch]$DryRun,
    [switch]$Yes,
    [int]$MaxParallel = 0,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.3.22f1\Editor\Unity.exe",
    [string]$Exe
)

$ErrorActionPreference = "Stop"
$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Stamp = Get-Date -Format "yyyyMMdd_HHmmss"
if (-not $Out) { $Out = Join-Path $Repo "Logs\new_species\${Id}_$Stamp" }
$BuildDir = Join-Path $Repo "Builds\Windows"
if (-not $Exe) { $Exe = Join-Path $BuildDir "FishingKing.exe" }
if ($MaxParallel -le 0) { $MaxParallel = [Math]::Max(1, [int][Math]::Floor([Environment]::ProcessorCount / 2)) }
$T0 = Get-Date

# ---------------------------------------------------------------------------------------------- the species' data
$fishFile = Join-Path $Repo "Assets\Resources\Data\Fish\$Id.json"
$stagesFile = Join-Path $Repo "Assets\Resources\Data\stages.json"
$Stage = $null
$IsLegend = $false
try {
    $stages = (Get-Content $stagesFile -Raw -Encoding UTF8 | ConvertFrom-Json).stages
    foreach ($st in $stages) { if ($st.fish | Where-Object { $_.id -eq $Id }) { $Stage = $st.id; break } }
    if (Test-Path $fishFile) { $IsLegend = $null -ne (Get-Content $fishFile -Raw -Encoding UTF8 | ConvertFrom-Json).encounter }
} catch { Write-Warning "could not read the species data: $_ (the validator will say more)" }
if (-not $Stage) { $Stage = "lake" }
$StageCount = 0
try { $StageCount = @(($stages | Where-Object { $_.id -eq $Stage }).fish).Count } catch {}
if ($StageCount -gt 12) {
    Write-Warning "$Stage now has $StageCount species: more than the map dialog's 12 cells. Ask the user, then run -Regress tour (and fix the dialog layout if it overflows)."
}

# a timed-out run is ended with its whole process tree (Unity's shader compiler, licensing client, bee_backend ...)
function Stop-Tree([int]$procId) { & taskkill.exe /T /F /PID $procId 2>&1 | Out-Null }

# ---------------------------------------------------------------------------------------------- the suites
# Each: args (after the common -fkfresh -fksave/-fkshots/-logFile), pointer = drives the pointer with gestures (runs in the
# serial lane), timeout and estimate in minutes, Done = the regex of its last line with the failed count in group "f".
# {arg} is the "name=arg" value (or the default), {id} the species, {stage} its stage, {repo} the repository.
$Suites = [ordered]@{
    newspecies = @{ args = "-fkrich -fkauto newspecies -fkspecies {id}"; pointer = $false; timeout = 20; est = 3
                    done = 'newspecies test done: (?<f>\d+) failed'; why = "the new species' own checks (economy, niche, card, collection, aquarium)" }
    species    = @{ args = "-fkrich -fkauto species -fkrepo {repo}"; pointer = $false; timeout = 5; est = 1
                    done = 'species test done: (?<f>\d+) failed'; why = "the validator's fixtures and the data dumps (in-game)" }
    cards      = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage {arg} -fkauto cards"; def = "{stage}"; pointer = $false; timeout = 10; est = 2
                    done = 'cards done: (?<f>\d+) failed'; why = "every ordinary species' catch card on a stage (arg: the stage)" }
    depth      = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage lake -fkauto depth"; pointer = $false; timeout = 30; est = 10
                    done = 'depth test done: (?<f>\d+) failed'; why = "the lake's whole terrain / fish / economy test (a terrain or recipe change)" }
    tour       = @{ args = "-fkrich -fkrecords all -fkauto tour -fkdetail {arg}"; def = "{id}"; pointer = $true; timeout = 8; est = 2
                    done = $null; why = "the screens: map dialog, shop, collection (arg: the detail page's species), aquarium, every stage" }
    fish       = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage {stage} -fkfish {arg} -fkauto fish -fkcount 2"; def = "{id}"; pointer = $true; timeout = 10; est = 3
                    done = 'done, caught (?<c>\d+)'; why = "real casts, bites and fights of one species (a jump / fight behaviour)" }
    lure       = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage lake -fklure {arg} -fkauto lure"; def = "bait_minnow"; pointer = $true; timeout = 12; est = 2
                    done = 'lure test done: (?<f>\d+) failed'; why = "one lure's action (arg: the lure id; all = the five, ~4 min)" }
    encounter  = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage {stage} -fkencounter now -fklegend {arg} -fkauto encounter -fkencplay perfect"; def = "{id}"; pointer = $true; timeout = 12; est = 3
                    done = 'encounter test done \([^)]*\): (?<f>\d+) failed'; why = "a legend's encounter, one perfect play (arg: the legend id)" }
    legendspot = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage {stage} -fkencounter natural -fklegend {arg} -fkauto legendspot"; def = "{id}"; pointer = $true; timeout = 10; est = 2
                    done = 'legend spot test done: (?<f>\d+) failed'; why = "a legend's splash spot (arg: the legend id)" }
    obstacles  = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage {arg} -fkauto obstacles -fkobstlog"; def = "{stage}"; pointer = $true; timeout = 15; est = 5
                    done = 'obstacles test (?:\(ocean\) )?done: (?<f>\d+) failed'; why = "casts, snags and cover on a stage (arg: the stage)" }
    breaks     = @{ args = "-fkrich -fkgear -fkscene Fishing -fkstage lake -fkauto breaks -fkobstlog"; pointer = $true; timeout = 8; est = 2
                    done = 'breaks test done: (?<f>\d+) failed'; why = "line breaks and lost fish (a fight behaviour)" }
    aqua       = @{ args = "-fkrich -fkaqua {arg}"; def = "live"; pointer = $true; timeout = 10; est = 2
                    done = 'done: \d+ ok, (?<f>\d+) failed'; why = "the aquarium scenario (arg: feed / live / clean / decor / tanks; a new feed style: live)" }
}

function Expand([string]$s, [string]$arg) {
    return $s.Replace("{arg}", $arg).Replace("{id}", $Id).Replace("{stage}", $Stage).Replace("{repo}", "`"$Repo`"")
}

# the runs: newspecies first, then the -Regress list
$runs = New-Object System.Collections.ArrayList
function Add-Run([string]$name, [string]$arg) {
    if (-not $Suites.Contains($name)) { throw "unknown suite '$name' (known: $(($Suites.Keys | Where-Object { $_ -ne 'newspecies' }) -join ', '))" }
    $s = $Suites[$name]
    if (-not $arg -and $s.def) { $arg = Expand $s.def "" }
    $label = if ($arg -and $name -ne "newspecies") { "$name=$arg" } else { $name }
    $key = ($label -replace '[^A-Za-z0-9_]', '_')
    [void]$runs.Add([pscustomobject]@{
        Name = $label; Key = $key; Suite = $name; Args = (Expand $s.args $arg); Pointer = $s.pointer; Timeout = $s.timeout; Est = $s.est
        Done = $s.done; Why = $s.why; Log = (Join-Path $Out "$key.log"); Shots = (Join-Path $Out "shots_$key"); Save = "ns_${key}_$Stamp"
        Proc = $null; Start = $null; End = $null; Result = "not run"; Failed = ""; Note = "" })
}
Add-Run "newspecies" ""
$regList = @($Regress | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($r in $regList) {
    $p = $r -split '=', 2
    Add-Run $p[0].Trim() $(if ($p.Count -gt 1) { $p[1].Trim() } else { "" })
}

# ---------------------------------------------------------------------------------------------- the plan
$free = @($runs | Where-Object { -not $_.Pointer })
$serial = @($runs | Where-Object { $_.Pointer })
$poolSlots = [Math]::Max(1, $MaxParallel - $(if ($serial.Count -gt 0) { 1 } else { 0 }))
# the estimate: the pointer-free runs longest first on the pool's slots, the serial lane beside them
$slots = @(0) * $poolSlots
foreach ($r in ($free | Sort-Object Est -Descending)) {
    $i = 0
    for ($k = 1; $k -lt $slots.Count; $k++) { if ($slots[$k] -lt $slots[$i]) { $i = $k } }
    $slots[$i] += $r.Est
}
$poolMin = ($slots | Measure-Object -Maximum).Maximum
$serialMin = ($serial | Measure-Object Est -Sum).Sum
if (-not $serialMin) { $serialMin = 0 }
$prepMin = if ($NoBuild) { 1 } else { 3 }   # (a build with the Library warm: ~1-3 min)
$estWall = $prepMin + [Math]::Max($poolMin, $serialMin) + 1

function Q([string]$s) { if ($s -match '\s') { return "`"$s`"" } return $s }

# a run's player command line (after the exe)
function Cmd($r) { return "-fkfresh -fksave $($r.Save) $($r.Args) -fkshots $(Q $r.Shots) -logFile $(Q $r.Log) -screen-fullscreen 0 -screen-width 1280 -screen-height 720" }

function Show-Plan {
    Write-Host ""
    Write-Host "new_species.ps1 -Id $Id  (stage $Stage$(if ($IsLegend) { ', a legend' }))"
    Write-Host "  out:       $Out"
    if ($NoBuild) { Write-Host "  1. validator: Unity -batchmode -executeMethod FishingKing.EditorTools.SpeciesValidator.Batch (~1 min; stops here on an error)" }
    else { Write-Host "  1+2. build:  Unity -batchmode -executeMethod FishingKing.EditorTools.FishingKingSetup.BuildWindows -> $BuildDir (its species validator gate first; ~1-3 min with the Library warm)" }
    Write-Host "  3. at most $MaxParallel processes at a time ($([Environment]::ProcessorCount) logical CPUs):"
    Write-Host "     parallel (pointer-free), $poolSlots slot(s):"
    foreach ($r in $free) { Write-Host ("       {0,-26} timeout {1,3} min, ~{2} min  {3}" -f $r.Name, $r.Timeout, $r.Est, $r.Why) }
    if ($serial.Count -gt 0) {
        Write-Host "     serial lane (pointer gestures, one after another, beside the parallel ones):"
        foreach ($r in $serial) { Write-Host ("       {0,-26} timeout {1,3} min, ~{2} min  {3}" -f $r.Name, $r.Timeout, $r.Est, $r.Why) }
    }
    Write-Host "     command lines:"
    foreach ($r in $runs) { Write-Host "       $($r.Name): FishingKing.exe $(Cmd $r)" }
    Write-Host ("  estimated wall time: ~{0} min (prep ~{1}, parallel ~{2}, serial lane ~{3})" -f $estWall, $prepMin, $poolMin, $serialMin)
    Write-Host ""
}

Show-Plan
if ($DryRun) { Write-Host "-DryRun: nothing launched."; exit 0 }
$regRuns = @($runs | Where-Object { $_.Suite -ne "newspecies" })
if ($regRuns.Count -gt 0 -and -not $Yes) {
    Write-Host "Regression suites asked for ($(($regRuns | ForEach-Object { $_.Name }) -join ', ')): nothing launched."
    Write-Host "Ask the user first (what, why, how long: the plan above), then run again with -Yes."
    exit 2
}

New-Item -ItemType Directory -Force $Out | Out-Null
$rows = New-Object System.Collections.ArrayList

# the editor in batch mode (killed when it runs over its timeout); its exit code and time
function Run-Unity([string[]]$uargs, [string]$log, [int]$timeoutMin) {
    $t = Get-Date
    $p = Start-Process -FilePath $Unity -ArgumentList (($uargs + @("-logFile", (Q $log))) -join ' ') -PassThru -WindowStyle Hidden
    $null = $p.Handle   # (keeps the exit code readable)
    if (-not $p.WaitForExit($timeoutMin * 60000)) { try { Stop-Tree $p.Id } catch {} ; return @{ code = -1; secs = ((Get-Date) - $t).TotalSeconds } }
    $p.WaitForExit()
    return @{ code = $p.ExitCode; secs = ((Get-Date) - $t).TotalSeconds }
}

function Fmt([double]$secs) { return "{0}:{1:00}" -f [int][Math]::Floor($secs / 60), [int]($secs % 60) }

# ---------------------------------------------------------------------------------------------- 1, 2: validator / build
if ($NoBuild) {
    $vlog = Join-Path $Out "validator.log"
    Write-Host "validator ..."
    $r = Run-Unity @("-batchmode", "-nographics", "-projectPath", (Q $Repo), "-executeMethod", "FishingKing.EditorTools.SpeciesValidator.Batch") $vlog 10
    $sum = (Select-String -Path $vlog -Pattern '\[SPECIES\] validate: (.*)' -ErrorAction SilentlyContinue | Select-Object -Last 1)
    $ok = $r.code -eq 0
    [void]$rows.Add([pscustomobject]@{ Suite = "validator"; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Failed = $(if ($ok) { 0 } else { 1 }); Duration = (Fmt $r.secs); Log = $vlog })
    if ($sum) { Write-Host "  $($sum.Matches[0].Groups[1].Value)" }
    if (-not $ok) {
        Select-String -Path $vlog -Pattern '\[SPECIES\] .*(ERROR|error)' -ErrorAction SilentlyContinue | Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" }
        $rows | Format-Table -AutoSize | Out-String -Width 400 | Write-Host
        Write-Host "The validator failed: nothing else was run."
        exit 1
    }
    if (-not (Test-Path $Exe)) { Write-Host "-NoBuild but no build at $Exe"; exit 1 }
    $newest = Get-ChildItem (Join-Path $Repo "Assets") -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest.LastWriteTime -gt (Get-Item $Exe).LastWriteTime) { Write-Warning "the build is older than $($newest.FullName): it may not have the latest data" }
} else {
    $blog = Join-Path $Out "build.log"
    Write-Host "build (with the validator gate) ..."
    $r = Run-Unity @("-batchmode", "-quit", "-projectPath", (Q $Repo), "-executeMethod", "FishingKing.EditorTools.FishingKingSetup.BuildWindows", "-fkBuildOut", (Q $BuildDir)) $blog 40
    $vsum = Select-String -Path $blog -Pattern '\[SPECIES\] validate: (.*)' -ErrorAction SilentlyContinue | Select-Object -Last 1
    $vok = $vsum -and -not (Select-String -Path $blog -Pattern '\[SPECIES\] \d+ errors in the species data' -Quiet)
    [void]$rows.Add([pscustomobject]@{ Suite = "validator (build gate)"; Result = $(if ($vok) { "PASS" } else { "FAIL" }); Failed = $(if ($vok) { 0 } else { 1 }); Duration = "-"; Log = $blog })
    $bline = Select-String -Path $blog -Pattern '\[FishingKing\] Build (\w+) ->' -ErrorAction SilentlyContinue | Select-Object -Last 1
    $bok = $vok -and $bline -and $bline.Matches[0].Groups[1].Value -eq "Succeeded" -and $r.code -eq 0
    [void]$rows.Add([pscustomobject]@{ Suite = "build"; Result = $(if ($bok) { "PASS" } else { "FAIL" }); Failed = $(if ($bok) { 0 } else { 1 }); Duration = (Fmt $r.secs); Log = $blog })
    if ($vsum) { Write-Host "  validator: $($vsum.Matches[0].Groups[1].Value)" }
    if ($bline) { Write-Host "  $($bline.Line.Trim())" }
    if (-not $bok) {
        Select-String -Path $blog -Pattern '\[SPECIES\] .*error|error CS\d+' -ErrorAction SilentlyContinue | Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" }
        $rows | Format-Table -AutoSize | Out-String -Width 400 | Write-Host
        Write-Host "The validator or the build failed: nothing else was run."
        exit 1
    }
}

# ---------------------------------------------------------------------------------------------- 3: the runs
function Start-Run($r) {
    New-Item -ItemType Directory -Force $r.Shots | Out-Null
    $r.Proc = Start-Process -FilePath $Exe -ArgumentList (Cmd $r) -PassThru
    $r.Start = Get-Date
    $r.Result = "running"
    Write-Host ("  start {0,-26} pid {1}" -f $r.Name, $r.Proc.Id)
}

$queue = New-Object System.Collections.Queue
foreach ($r in $free) { $queue.Enqueue($r) }
$lane = New-Object System.Collections.Queue
foreach ($r in $serial) { $lane.Enqueue($r) }
$laneCur = $null
$active = New-Object System.Collections.ArrayList
try {
    while ($queue.Count -gt 0 -or $lane.Count -gt 0 -or $active.Count -gt 0) {
        # the serial lane: the next pointer suite once the last one is done
        if (-not $laneCur -and $lane.Count -gt 0 -and $active.Count -lt $MaxParallel) {
            $laneCur = $lane.Dequeue(); Start-Run $laneCur; [void]$active.Add($laneCur)
        }
        while ($queue.Count -gt 0 -and ($active.Count + $(if (-not $laneCur -and $lane.Count -gt 0) { 1 } else { 0 })) -lt $MaxParallel) {
            $r = $queue.Dequeue(); Start-Run $r; [void]$active.Add($r)
        }
        Start-Sleep -Seconds 2
        foreach ($r in @($active)) {
            $over = ((Get-Date) - $r.Start).TotalMinutes -gt $r.Timeout
            if ($r.Proc.HasExited -or $over) {
                if (-not $r.Proc.HasExited) { try { Stop-Tree $r.Proc.Id; $r.Proc.WaitForExit(10000) | Out-Null } catch {} ; $r.Result = "TIMEOUT" } else { $r.Result = "exited" }
                $r.End = Get-Date
                $active.Remove($r)
                if ($r -eq $laneCur) { $laneCur = $null }
                Write-Host ("  end   {0,-26} {1} after {2}" -f $r.Name, $r.Result, (Fmt ($r.End - $r.Start).TotalSeconds))
            }
        }
    }
} finally {
    # never leave a process of ours behind (Ctrl+C, an error)
    foreach ($r in $runs) { if ($r.Proc -and -not $r.Proc.HasExited) { try { Stop-Tree $r.Proc.Id } catch {} } }
}

# ---------------------------------------------------------------------------------------------- 4: the summary
$notes = New-Object System.Collections.ArrayList
foreach ($r in $runs) {
    $failed = $null
    $text = if (Test-Path $r.Log) { Get-Content $r.Log -Raw -Encoding UTF8 } else { "" }
    $checkFails = ([regex]::Matches($text, 'CHECK FAIL')).Count
    $exceptions = ([regex]::Matches($text, '(?m)^\w*Exception: ')).Count
    if ($r.Result -eq "TIMEOUT") { $failed = "-" }
    elseif ($r.Done) {
        $m = [regex]::Matches($text, $r.Done)
        if ($m.Count -eq 0) { $r.Result = "NO END"; $failed = "-" }
        elseif ($m[$m.Count - 1].Groups["c"].Success) { $c = [int]$m[$m.Count - 1].Groups["c"].Value; $failed = $(if ($c -gt 0) { 0 } else { 1 }); $r.Note = "caught $c" }
        else { $failed = [int]$m[$m.Count - 1].Groups["f"].Value }
    } else {
        $failed = $checkFails + $exceptions   # (no end line: CHECK FAILs and exceptions; review the shots)
        $r.Note = "review the shots"
    }
    if ($failed -is [int]) { $r.Result = $(if ($failed -eq 0) { "PASS" } else { "FAIL" }) }
    $r.Failed = $failed
    if ($exceptions -gt 0) { $r.Note = ("$($r.Note) $exceptions exception(s)").Trim() }
    [void]$rows.Add([pscustomobject]@{ Suite = $r.Name; Result = $r.Result; Failed = $failed
        Duration = $(if ($r.Start -and $r.End) { Fmt ($r.End - $r.Start).TotalSeconds } else { "-" }); Log = $r.Log; Note = $r.Note })
    # the new species' report lines: the economy's band, its weight, a soak note, skips
    if ($r.Suite -eq "newspecies") {
        foreach ($l in ($text -split "`r?`n")) {
            if ($l -match '\[NEWSP\] (CHECK FAIL|CHECK SKIP|NOTE|D11b \S+''s derived|D11'' the band''s closest)') { [void]$notes.Add(($l -replace '^.*?\[NEWSP\] ', '')) }
        }
    }
}
Write-Host ""
$rows | Format-Table Suite, Result, Failed, Duration, Note, Log -AutoSize | Out-String -Width 400 | Write-Host
foreach ($n in $notes) { Write-Host "  $n" }
$wall = ((Get-Date) - $T0).TotalSeconds
Write-Host ("total wall time {0} (shots and logs in {1})" -f (Fmt $wall), $Out)
$bad = @($rows | Where-Object { $_.Result -ne "PASS" })
if ($bad.Count -gt 0) { exit 1 }
exit 0
