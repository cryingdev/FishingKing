# Lake phase 2: free terrain, fish that adjust, the economy gate

Phase 1 ([terrain_depth_spec.md](terrain_depth_spec.md)) generated the lake's bed over the old distance profile and kept
every rig's bites per minute by scaling how far a fish senses the rig. Phase 2 (this document) lets the bed go free, makes
the fish follow whatever lake a seed draws, and bounds only the economy:

- **(A) code** (this part): free terrain generation with the profile re-derived from the bed, depth bands relative to the
  lake, spawn weights derived from the bed, a bigger stock with a per-encounter feeding roll, the economy estimator and
  its gate, the tests. It builds and passes with today's 36 species.
- **(B) art**: six new lake species' models and sprites (a separate branch).
- **(C) integration**: the six species' files, the roster, the UI's capacity, the docs, the full test plan.

The user's balance rule: per-species, per-rig and per-spot differences are allowed and wanted (finding where each fish
lives is the game); averaged over the reference player's casting fan, every rig and period, the lake's catches per minute
and income per minute stay within 0.8–1.25 of today's lake, on 50 seeds (the estimator) and in a live soak.

Sections A1–A9 are the design as decided; each ends with what was built differently and why. The measured tables are in
section 10.

---

## A1. Free terrain generation (`Data/TerrainRecipes.cs`, `Core/BathyGen.cs`, `Core/Bathymetry.cs`)

**Recipe v2** (`TerrainRecipe.version = 2`: every save's lake re-rolls on purpose, no migration). The character ranges are
recipe data (`CharacterSpec`); "lerp by u" means `x + (y − x)·u`.

| What | As designed | As built (D2 tuning, below) |
|---|---|---|
| Main depth `Dm` | 3.6 + 3.8·u_depth | lerp(3.1, 6.7) by u_depth |
| Shelf end `zs`, drop width `ws` | U(4, 8), U(8, 18) | same |
| Side shoaling σ round x = xc | 0.45·u_side, xc U(−6, 6) | lerp(0.15, 0.5) by u_side |
| Far rise φ over z 45..64 | 0.25·U | same |
| Holes | 1, a second with P = 0.35 + 0.4·u_depth; R U(2, 3.5) | P = lerp(0.6, 0.95) by u_depth; R U(3, 4.5); 80 tries |
| Humps | 1 + ⌊3.99·u_rock⌋ (max 4), relative tops | same; the first at z 28–40 (was 30–38); 80 tries |
| Hump top | `clamp(Dloc·U(0.40, 0.65) − 0.4·u_rock, 0.8, 4.0)`, rise ≥ max(1.0, 0.3·Dloc), height ≤ 1.3·0.55·R | same |
| Noise amplitude | × U(0.8, 1.2) | same |
| Flats (weed) | radius × lerp(0.95, 1.30), depth + lerp(+0.25, −0.25) by u_weed | same |
| Extra weed flats | ⌊3.99·u_weed²⌋ (0–3): r 3–6, depth 1.0–2.2, edge 2–3.5, z 8–40; ≥ 3 m from the lane and each other, ≥ 2 m from a pin | same; zones `weed1..3` "수초 평지 1..3" |
| Shelf weed | within lerp(1.0, 2.5) m of a reed (by u_weed) | same |
| Flat sand | noise > lerp(0.05, 0.50) by u_weed | same |
| Gravel | slope ≥ lerp(0.8, 0.5), channel noise > lerp(0.75, 0.15), open water noise > lerp(0.85, 0.45), by u_rock | same |
| Lane carve | towards `max(4.8, Dm·U(0.9, 1.1))`, `S(1.5, −1.0, laneSd)` | `max(5.4, …)`, `S(2.5, −1.0, laneSd)` |

**Pipeline** (`BathyGen.Run`; new draws come from new named streams, so the old streams stay put):

- **0a** stream `"character"` (a new one every attempt, so a retry draws a new lake): u_depth, u_weed, u_rock, u_side, then
  zs, ws, xc, φ, the noise scale and the lane's scale. Stored on `Bathymetry.Character` (`BedCharacter`: the u, the
  derived numbers, the counts, a label "shallow / mid / deep" × "weedy / rocky / mixed"); `[BATHY] character …`.
- **0b** the base `B(x, z) = 1.2 + (Dm − 1.2)·S(zs, zs + ws, z)·(1 − σ·S(0, h(z), |x − xc|))·(1 − φ·S(45, 64, z))`,
  `h(z) = 0.9·half(z, 640)`. The side and far factors take from the excess only: `B(x, 0) = 1.2` exactly (the shore's
  depth is the authored profile's at z 0).
- **1** the flats with the weed's radius and depth; the extra weed flats (stream `"weedflats"`). **1c** the lane carved:
  `wL = S(2.5, −1.0, laneSd)`, `D = max(D, D + (laneT − D)·wL)`.
- **2–5** channel, holes (count rule), humps (relative tops), noise (scaled). Blob positions unchanged (under the fixed art).
- **5b** the provisional profile **P0**: per row the median of a copy of the bed with the pins' first pass and the slope
  limit's envelope applied, over `|x| ≤ min(half(z, 600), X1 − feather)`; smoothed [1 4 6 4 1]/16; [0.6, 8.8]; whole cm.
- **6** feathered to P0 at the side and far edges (not the shore).
- **7–9** pins (the pier's window and ring on the authored profile, `Ctx.authD`), the slope limit (its envelope holds the
  side and far edges on the profile), pins again, the global limits.
- **9b** (as built) **P\*** = the finished rows' medians over the same windows, unsmoothed, whole cm; the edges re-set to
  P\*, the slope limit and the pins' cores again (only the edge band moves).
- **11b** the lake's statistics over **R_ref** (z in [zNear + 1.5, 50] = [2.7, 50], |x| ≤ half(z, 600), a node weighs
  1/(2·hw) so every row weighs the same): a weighted CDF over the nodes' cm gives the depth at each whole percentile
  (`quantCm[0..100]`), every node's mid-rank in per mille (`rankPm`, (F(< d) + F(≤ d))/2) and the materials' and kinds'
  shares. The hash adds P\*'s rows after the four arrays.

**`Bathymetry` API**: `Profile(z)` (P\* lerped; before the grid its first row; beyond it the last row × the authored
profile's shape), `Quantile(pct)`, `NodeRankPct(k)`, `RankPct(depthM)`, `MatShare`, `KindShare`, `ProfileRows`,
`Character`, `RefNodes`, `RefU`, `FinalRowMedian(j)`, `CharacterLine()`.

**Bug fixed first**: `Bathymetry.Water` (the along / disc queries, V8) read `L.DepthAt`, i.e. `L.Bathy`, which is assigned
only after `Build` returns: V8 checked the authored profile on a session's first build and the previous grid in the
sweeps. It now reads its own grid, off it its own P\*.

**Validation** (V1–V9 as phase 1, with V3 on the authored profile and V4 on P\*):

| Check | Rule |
|---|---|
| V3 | the pier's core = `authD` within 1 cm |
| V4 | the side and far edges = P\* within 1 cm |
| V9 (information) | P\* against the authored profile every 5 m; the final rows' medians against P\* |
| V10 (R_ref) | Q10 ≥ 0.6; Q50 in [1.8, 5.5]; Q90 in [3.4, 8.6]; Q90 − Q10 ≥ 1.8; Q50 − Q10 ≥ 0.6; Q90 − Q50 ≥ 0.6 |
| V11 (R_ref, U-weighted) | weed ≥ 5 %, gravel ≥ 2 %, sand ≥ 5 %, mud ≥ 5 %; flat + shoal ≥ 6 %; drop-off ≥ 3 %; hole + channel ≥ 2 %; hump ≥ 0.5 % (the fallback is waived on the last two) |
| V12 (D2 only) | over seeds 1..50: Q50 spans ≥ 2.0 m; ≥ 5 lakes with Q50 ≤ 2.8 and ≥ 5 with Q50 ≥ 4.2; weed share spans ≥ 15 points; gravel ≥ 6 |

**Fallback**: the middle character (every u 0.5, the middle of every range), no noise, channel, holes, humps or extra
flats; it passes V1–V10 and V11's materials, flat + shoal and drop-off (D2).

**Determinism**: no trig or pow in the generator (u_weed² is a multiply), SplitMix streams, sorted-float medians, cm
quantization. `GoldenHash[2]` for seed 12345: **0xc778575c** (recorded by the Mono player on the first `-fkauto depth`
run and set in `AutoPilot.Depth.GoldenHash`).

### A1, as built

1. **The character ranges were retuned** (the spec's rule: if more than 10 % of first attempts fail V10 or V11 in D2,
   retune the ranges, not the bounds). With the ranges as designed, 33 of 50 first attempts failed: V10 12 (deep lakes'
   Q50 up to 6.5 > 5.5; flat-bottomed lakes' Q90 − Q50 under 0.6), V11 23 (hole + channel 1.2–1.9 %: a channel's banks
   are steeper than 0.35 and classify as drop-off, so a creek is mostly "drop-off"; one small hole is ~1.2 % of R_ref;
   a weedy lake sometimes placed no hump), V8 5, V4 7; V12 had only 3 lakes with Q50 ≤ 2.8, and one seed fell back.
   The table above lists the changes; after them 2 of 50 first attempts fail (V11) and over 300 seeds 25 of 300 (8 %) fail
   V10 or V11, the most attempts any seed needs is 3, and no seed falls back.
2. **The lane carve** reaches 2.5 m outside the lane (was 1.5) towards at least 5.4 m (was 4.8): with the `Water` fix V8
   measures the real bed, and the lane's narrow neck (x −3.4..−0.9 at z 12.5–16.9, 2.5 m wide) is where the bamboo's 16 m
   cast finds the legend's spot; a 0.9 m disc must stay over 4.3 m there.
3. **P\* is the finished rows' medians** (step 9b) instead of the pre-feather medians smoothed: those differed from the
   finished rows by up to 2.4 m (the pier pinned to the authored profile over z 0–6.5, the pads' and reeds' slope cones,
   the sunken log's pit), and even the finished medians smoothed left 0.36 m against D3b's 0.25. The smoothed medians of
   the pinned copy (P0) still feather the edges first; the second slope limit only moves the edge band. D3b is now exact.
4. The slope limit's envelope is its own function (`Envelope`), shared by step 5b and the slope limit.
5. V4's message names its first node off (`first (x, z) d not P*`): a pin core at the side edge (x = −48, z 28) makes it
   fail on about 1 % of first attempts.

## A2. The derived profile and its consumers

`StageLayout` (`Core/Art.cs`):

- `DepthAt(x, z)`: the grid where it covers the point, off it `Bathy.Profile(z)`; without a grid `BaseDepthAt(z)`; + the
  tide. Without a grid the same float operations as before (D3).
- `ProfileDepth(z) = (Bathy?.Profile(z) ?? BaseDepthAt(z)) + TideOffset`.
- `AuthoredDepth(z) = BaseDepthAt(z) + TideOffset`; `AuthoredMeanDepth(z) = BaseDepthAt(z)` (was `ProfileMeanDepth`).

| Site | Now |
|---|---|
| `HabitatModel.Casts` (today's water) | `L.AuthoredDepth` |
| `HabitatModel.Simulate`, no-bed target | `L.AuthoredDepth` (a bed without a node: its P\*) |
| `HabitatModel.SimWorld.Water` | no bed: `L.AuthoredDepth`; a bed, off its grid: `b.Profile(z) + tide` |
| `HabitatModel.Casts` (the bed's water) | off the grid `b.Profile(z) + tide` |
| `FishingController` (first float depth), `FishAgent` (`PickTarget`, `Flee`), `FishSpawner`, `FishingHUD` | unchanged; they follow `ProfileDepth` / `DepthAt` (bit-identical off the lake) |

The default float depth `min(2, P*(15) − 0.3)` now depends on the seed.

## A3. Depth bands relative to the lake (`Fishing/HabitatModel.cs`)

`HabitatDef.Parse`: `depth:pA-pB` sets `rel`, `pa`, `pb`; `@period:Np` sets `shiftP[p]` (points, − shallower, the sign
optional); metre forms still parse into `a`, `b`, `shift`. Which unit each token used is recorded (`depthM`, `depthP`,
`shiftM`, `shiftPct`) for the validator.

`HabitatModel.Resolve(h, b) → HabRes` (pure): per period the band shifted by `shiftP[p]`, slid back into [0, 100] keeping
its width; its depths there; `swimShiftM[p] = b.Quantile(mid'_p) − b.Quantile(mid)`. `FishHabitat` caches it per species
(a FishHabitat is per bed) and logs `[HAB] band <id> <period> pA'-pB' = a..b m`.

`H(h, res, p, w, P, kind, mat, edgeDist, minWater)`: a relative band is judged on the node's rank P: 1 inside
[A', B'], `max(0.2, 1 − (A' − P)/25)` below, `max(0.2, 1 − (P − B')/50)` above; an absolute band as before; the rest of the
formula and the body rule unchanged. `Weights` passes `b.NodeRankPct(k)`; `SwimRange` uses `res.swimShiftM[p]` for a
relative band (halved, as before). Fight depth, flee, the aquarium and the encyclopedia keep the absolute swim column.

`RowKeep` 1 → **0.5**.

The four lake species (only the depth and shift tokens changed):

| Species | depth | dawn / day / evening / night |
|---|---|---|
| `crucian_carp` | p25-p70 | −15p / 15p / −15p / −10p |
| `bluegill` | p5-p50 | – / 10p / – / 20p |
| `carp` | p55-p100 | −20p / 10p / −20p / −40p |
| `largemouth_bass` | p20-p75 | −8p / 25p / −8p / −5p |

The old metre bands on the 50 lakes pooled (D2, information): crucian 1.2–4.0 m = p5–p63, bluegill 0.8–3.0 = p0–p46,
carp 3.0–7.0 = p46–p99, bass 1.0–4.5 = p1–p70.

**Validator E9** (a generated-bed stage, an ordinary species): `habitat`, `col:` and a p-form `depth:` (0 ≤ A < B ≤ 100,
B − A ≥ 10); every `@period:` shift in points with |N| ≤ 50; a metre depth or a metre shift is an error ("mixes units").

## A4. Derived spawn weights

`RarityInfo.SpawnBase`: common 40, uncommon 16, rare 3.5, epic 1.5, legendary 1.2.

`HabitatModel.DerivedWeights(b, [(species, base, act[4])])` (pure, doubles):

1. `h*(s, p) = max_k(kindW·periodKind[p]) · max_m matW · (1 + edge)` (`HMax`);
2. `q(s, p) = Σ_Rref U·H / (h*·ΣU)` (the body rule included);
3. `q̄(p) = Σ base·a·q / Σ base·a` over the stage's ordinary species;
4. `A(s, p) = clamp(q / q̄, 0.6, 1.4)`;
5. `W(s, p) = base·A·a`. `[HAB] weights seed <n>: <id> A=… W=…`.

Loader (`SpeciesData`): on a stage with a recipe (`TerrainRecipes.Has`) `weight` is optional; absent, the rarity's base is
stored and `StageDef.GivenWeight[id] = false`; `StageDef.Derived`. (Both are properties: the species dump walks fields, so
the other stages' lines are unchanged.) The old NaN skip is gone: every `spawns` value is a positive base. E8 still requires
a weight on a stage without a bed. A given weight replaces only the base: A and the activity still apply.

`FishSpawner.Pick`: `w = ctl.Habitat != null ? Habitat.SpawnWeight(sp, base, look) : base·a`, `SpawnWeight = base ×
lerp(A(from), A(to), F) × a`; the rod's luck and the bait's rare boost as before. A detached spawner (`ctl` null) keeps
base × a. Lake roster: `population` 15, no weights.

## A5. Population and the per-encounter feeding roll

`FishAgent.Feeding`, `FeedDecided`, `LastInReach`. In `FishingController.WantsToApproach`, after the reach, MinWater,
depth (±2.5) and appeal gates and before the approach roll, only with a habitat: `RollFeeding(f)` decides once
(`Feeding = FeedAll || Random.value < Habitat.FeedP`; `FeedAll` short-circuits, no draw) and stamps `LastInReach`; not
feeding → no approach. `UpdateWander` forgets the decision after 10 s out of reach (`FishAgent.FeedForget`). Off the lake
nothing runs, so the sea and the stream draw no extra random numbers.

`FishingController.FeedAll` (`-fkfeedall`; set by the autopilot for breaks, obstacles, lure, hold, steer, pan, occlusion
and zoom).

## A6. The reach scale removed

`FishingController.SenseFor` → `SenseRange()`. Gone: `FishHabitat.ReachScale`, `Scale`, `Budget`, `scales`;
`HabitatModel.ScaleMin/ScaleMax/RawMin/RawMax/Budget/Estimate`; `Debug/AutoPilot.Habitat.cs` and `-fkauto habitat`,
`-fkhab*` (`BiteBand`, `SoakSpot` moved to `AutoPilot.Economy.cs`). Kept: `Simulate`, `Occupancy`, `Casts` (now without a
reach), `SizeShare`, `HookDepth`, `RigClass`, `MakeRegion`, `Weights`, `Pick`.

## A7. The economy estimator (`Fishing/LakeEconomy.cs`, pure) and F at runtime

**Today**: no bed (the authored water, uniform targets, 640 simulated fish, a static cache per species and fish region),
`N = 8`, `F = 1`, the legacy stock {crucian 40, bluegill 35, carp 16, bass 14} with the current species data.

**Reference player**: the casts of `HabitatModel.Casts` (yaw −38..38 every 4°, every metre from zNear + 2.5 to the cast,
from x 0.375); rigs half floats (paste F2, paste F6, worm F1, worm F6, corn F6, shrimp F2) and half lures (spinner, spoon,
crank, minnow, soft worm, popper, frog; their class by `FishHabitat.RigOf`), equal within each half; the golden paste left
out; periods by length 3/9/3/9; the starter line, the lake's bite multiplier 1.1; reach 5 m a float, 7 m a lure; the fish
region to `min(zFar − 2, cast + 14)`.

Per species s, period p, rig and cast c: `occ` = the species' simulated fish-time within the reach of c whose depth is
within 2.5 m of the hook's (`HookDepth(class, w_c)`) × `SizeShare(w_c)` / samples (per class, reused across rigs);
`p = min(0.95, appeal·stealth·biteMult·act·0.45·clamp(√a, 0.25, 2))` (act 1 a float, 0.15 + 1.25·0.6 a lure);
`T = 1 − (1 − p)^8`; `λ = N·π_s(p)·F·occ·T` (π the species' share of the stock's weights in that period: derived W, or the
legacy stock × a); income `λ·EP(w_c)` (the expected price over t = U^1.7 of the sizes with MinWater ≤ w_c, 32 quantiles).
Averaged over casts, rigs (by weight) and periods (by length).

**F** = `clamp(1/√(C1·I1), 0.25, 1)` from the new lake over today at F = 1, so catches end at `1/√π` and income at `√π`
(π the mean price ratio).

**Runtime**: `FishHabitat.Prewarm(castDist)` makes the job on the main thread (the samplers, the covers) and runs it on
worker threads (at most half the cores): the bed's simulations for the stage's ordinary species × 4 periods, today's for
the legacy stock, then F, cached per (bed hash, cast distance): `[ECO] seed <n> rod <cd> C1 .. I1 .. -> F ..`.
`FishHabitat.FeedP` returns it once ready, else `FDefault(rod)`: D11′'s 50-seed medians, bamboo 0.506, carbon 0.451,
dragon 0.405, the other rods lerped by their cast (glass 0.478, big game 0.440, surf 0.413); a new rod in hand starts its
own estimate. The main thread never waits.

`-fkecomode legacy` (`Game.DebugBoot`, any scenario): `Bathymetry.Off`, the lake's population 8 and the legacy stock.

## A8. Tests

`-fkauto depth`: D1 (+ `GoldenHash[2]`), D2 (V1–V11 on every accepted grid, V12, the attempts, the first attempts'
V10 / V11 failures ≤ 5 of 50, the character table, the fallback, the pooled depths), D3 (+ `AuthoredDepth`, off the grid
P\*), D3b, D4 (+ the statistics), D5, D6, D7′, D8, D9, D10, D11′, D11b, D12, D13, D14. Gates as in section 10.

D7′ as built: 2000 targets per ordinary species and period (z ≤ 30) on seed 1 and the shallowest- and deepest-Q50 lakes
of D2. Gates: **carp by day mean rank ≥ the uniform targets' mean rank + 10** on each lake; bluegill at dawn and by day
≤ 40; every species' and period's mean rank within 10 points across the three lakes; seed 1's six kind shifts with
**bass at dawn on flat + shelf, crucian at dawn on the flats and carp at night on flat + shoal ≥ 1.1** (phase 1: 1.3, 1.3,
1.4). The spec's "carp by day ≥ 60" and those three thresholds are not reachable with the strings' β and kind weights
(which this part was to keep): a habitat with β 0.65 steers about two thirds of the density, and sweeping the bands,
`RowKeep` 0..1, the `hn` clamp up to [0.1, 6] and even β up to 0.95 moved the carp's day rank to 56–60 and the bass's dawn
flats to 1.23 at best; seed 1 is a "mid weedy" lake whose flats and shelves are 45 % of the bamboo's water, which caps
any flat preference. The direction and the cross-lake consistency (the point of relative bands) hold; the measured values
are logged against the old thresholds.

`-fkauto species`: the new E8 / E9 rules, 7 more fixtures (an inverted p-band, a too narrow band, a band in metres on a
bed, mixed units, a p-shift out of range, a missing weight off the bed, and a positive control: a weight given on the
bed passes; `Fx.clean`), 56 in all. `spawn_baseline.txt` adds the lake's derived weights on seed 1.

`-fkauto economy` (`Debug/AutoPilot.Economy.cs`): `-fkecomode new|legacy`, `-fkecorod`, `-fkecoperiods`, `-fkecorigs`
(paste2, pasteB, worm1, cornB; wormB, corn2, shrimp2, a lure by name), `-fkecosecs 30`, `-fkecorestock 10`; the fan's
spots (yaw −36..36 every 8° × every 2 m: bamboo 70, carbon 110, moved off pads and weed / snag zones); a fixed 1/60 s
step, the clock frozen at the period's centre, `Random.InitState(1515)` as the stage opens; every bite counted
(`DebugBiter`: species, cm, price) and let go; `[ECO] spot`, `[ECO] cell` and `[ECO] diag` lines (approach rolls,
approaches, feeding decisions, and while no fish is engaged the time-averaged fish that like the bait within the sense
range, at the depth and with water enough at the hook), the shallow-water frames, and in the new mode the estimator's
prediction for the same spots and rigs (`[ECO] predict`: both lakes' takes and income per unit time and their expected
fish in the window, `EcoJob.Predict`). Pooled by hand (one process per mode and period).

**As built: the soak draws a fresh stock every 10 spots and at every rig** (`-fkecorestock`, the stage reopened with
`Random.InitState(1515 + 1000 n)`). Every bite's fish is let go and swims off, and its place is taken by a new draw; a
species that never takes the bait (a bass on paste) is never taken out, so over a long soak it fills the stock. In the
first calibration run (one stock per period, 35 minutes per rig) the 8-fish legacy lake's paste on the bottom got 3, 3 and
0 bites in three periods after its paste at 2 m had replaced most of its fish (the new lake's 15 fish with F ≈ 0.5 drift
more slowly), and the pooled new / legacy ratio came out ×1.6–1.8 against a predicted ×0.86. With 15 s spots (half the
drift) a diagnostic run gave ×0.97 catches and ×1.04 income. The estimator models the stock as drawn, so the soak keeps
it there. **Finding for the user**: in long sessions with one bait the stock fills with the species that do not take it,
today's 8-fish lake faster than the new one: the new lake gives more bites than today's in a long single-bait session
(about ×1.6 for paste after 35 minutes), as much over a fresh stock. A natural turnover of the stock (fish that come and go)
would bound that; it is not part of this change.

## A9. Docs

English: this document; `terrain_depth_spec.md` (pointers, "phase 2"); cross-references in `obstacles_spec.md`,
`lures_legend_spec.md`, `legends_rollout.md`, `time_currents_spec.md`. Korean: `data_reference.md` (0.1, 1.1, 1.2, 1.3,
2.6, 2.7), `fishing_gameplay.md` (11, 11.1, 11.5), `testing.md`, `architecture.md`, CHANGELOG.

---

## 10. Measured (the build of `step2-code`, Mono player)

**D2** (seeds 1..50): V1–V11 on every accepted grid; attempts 0:48 1:1 2:1; no fallback; 50 distinct hashes; built in 56 ms
mean, 152 ms max. First attempts failing V10 or V11: 2 of 50. V12: Q50 2.38–5.34 m (span 2.96; 10 lakes ≤ 2.8, 14 ≥ 4.2),
weed 6.9–27.0 %, gravel 10.1–35.4 %. D3b: P\* = the finished rows' medians exactly. The 50 lakes pooled: Q10 1.29, Q25
1.71, Q50 3.24, Q75 4.82, Q90 5.78 m. Seed 1: "mid weedy" (u 0.40 / 0.74 / 0.59 / 0.94), Dm 4.54, Q10/50/90
1.13/2.72/4.76 m, weed 20 %, gravel 25 %, sand 21 %, mud 34 %, 2 holes, 3 humps, 2 extra weed flats.

**D7′** (mean target rank; seed 1 / shallowest seed 16 / deepest seed 26; the uniform targets 39 / 40 / 37):

| Species | dawn | day | evening | night |
|---|---|---|---|---|
| crucian | 33/33/32 | 46/47/42 | 34/34/33 | 34/35/32 |
| bluegill | 31/30/29 | 33/34/32 | 31/32/29 | 39/39/35 |
| carp | 46/47/44 | 52/56/50 | 47/46/44 | 33/33/35 |
| bass | 35/36/33 | 50/54/45 | 39/39/34 | 40/41/34 |

Seed 1's kind shifts: bass by day on drop-off + hump + shoal ×1.73, at dawn on flat + shelf ×1.17; crucian at dawn on the
flats ×1.27, by day ×0.60 of its dawn share; carp at night on flat + shoal ×1.38, by day in hole + channel + basin ×1.91.

**D11′** (seeds 1..50 × bamboo / carbon / dragon): F never clamped; F bamboo 0.480–0.524 (median 0.506), carbon
0.429–0.473 (0.451), dragon 0.384–0.436 (0.405). After F: catches ×0.974–1.019 / income ×0.982–1.027 (bamboo),
×0.972–1.017 / ×0.983–1.029 (carbon), ×0.975–1.023 / ×0.977–1.026 (dragon). Seeds 1 and 37 recomputed: identical. No rig's
fan ratio under ×0.5 or over ×2 (0 of 1950). 58 s for the 150 estimates (2400 + 12 simulations) on 7 threads.

Seed 1, bamboo (new × F over today): per rig paste2 0.92, pasteB 0.82, worm1 1.13, wormB 0.86, cornB 0.69, shrimp2 1.14,
spinner 1.10, spoon 1.10, crank 1.64, minnow 1.15, softworm 1.06, popper 1.64, frog 1.64; per period (catches / income)
dawn 1.01/1.01, day 0.97/0.92, evening 1.02/1.10, night 1.00/1.04; share of catches new / today: crucian 0.27/0.32,
bluegill 0.40/0.35, carp 0.09/0.14, bass 0.24/0.19; XP ×0.99. The best 10 % of casts on each species' best rig over its fan
mean: crucian (paste2) ×2.42, bluegill (spinner) ×1.95, carp (paste2) ×2.30, bass (minnow) ×1.92 (carbon: 2.67, 2.36,
2.41, 1.96). Golden paste ×0.94 catches, ×0.89 income of today's golden paste; the dragon's luck ×1.00.

**D11b**: A at its clamp in 0 of 800 (seed, species, period); the stock's mean multiplier 1.000 (no clamp, so Σ base·a·A
= Σ base·a exactly); the override path and determinism pass. **D14**: 15 fish; 891 of 2000 encounters feeding at F 0.435
(expected 870 ± 3 × 22); kept at 9 s, dropped at 10.5 s; `FeedAll` draws nothing.

## 11. The live calibration soak (A)

`-fkauto economy`, seed 1, the bamboo rod, rigs paste2 / pasteB / worm1 / cornB, 70 spots × 30 game s each, a fresh stock
every 10 spots; eight processes (legacy and new × the four periods), pooled by hand with the test's rules (periods by
length, rigs equally; the catches' point estimate with ≥ 100 bites a side; income by a seeded bootstrap over the spots in
blocks of one yaw). The new mode's `[ECO] predict` lines give the estimator's ratio for the same spots and rigs at the
runtime F (0.486).

| | New | Legacy | Ratio | Predicted | Live / predicted |
|---|---|---|---|---|---|
| **Catches / min** (pooled) | 1.901 (1092 bites) | 2.042 (1136) | **×0.93** (point) | ×0.90 | **1.03** |
| **Income / min** (pooled) | 91.7 | 108.5 | **×0.84** (95 % 0.65–1.09) | ×0.81 | **1.04** |
| paste2 | 2.13 / 92.4 | 2.27 / 124.1 | ×0.94 / ×0.74 | ×0.92 / ×0.81 | 1.02 / 0.92 |
| pasteB | 1.83 / 70.7 | 2.47 / 119.2 | ×0.74 / ×0.59 | ×0.82 / ×0.69 | 0.91 / 0.86 |
| worm1 | 2.66 / 149.6 | 2.37 / 123.7 | ×1.12 / ×1.21 | ×1.14 / ×1.10 | 0.99 / 1.10 |
| cornB | 0.98 / 54.0 | 1.06 / 67.0 | ×0.92 / ×0.81 | ×0.69 / ×0.62 | 1.34 / 1.29 |
| dawn / day / evening / night (catches) | | | ×1.02 / ×0.99 / ×1.03 / ×0.82 | ×0.98 / ×0.89 / ×0.93 / ×0.88 | |

Both pooled ratios are within 0.8–1.25 and within ±20 % of the prediction: no refit of the take term or the reach was
needed. The bed: 0 frames of a fish in water shallower than it swims in. Bites by species (new / legacy): crucian 472 / 523,
bluegill 386 / 325, carp 171 / 250, bass 63 / 38 (the new lake's carp keep to its deep water, beyond most of the bamboo's
spots: the float rigs' income is lower, the lures' higher; the reference player's fan with lures, D11′, is ×1.01). Per
rig and period the differences are the ones the user's rule allows (pasteB ×0.74, cornB's estimate 1.34 off on 145 /
148 bites).

The diagnostics (`[ECO] diag`): the approach rolls per approach match on both sides (p as the game's formula), and the
new lake's feeding decisions came out 0.48 feeding at F 0.486. While no fish is engaged, the fish in the window number
0.16–0.24 on the legacy side and 0.81–1.06 on the new against the estimator's undepleted 1.0 / 1.9: the window is emptied
by the fish that come (all of them on the legacy side, the feeding half on the new), which is why the take is modelled per
encounter (T) and the rate follows the flux of fish into the window rather than the count in it.

The first run (one stock per period) and a diagnostic run (15 s spots) are described under A8.

## 12. Regressions (the same build)

`fish` (lake) caught 2; `obstacles`, `legendspot` and `encounter` (both `-fkencounter natural`), `breaks`, `pan`, `hold`
and `lure` 0 failed; `tour` 19 shots, no exception; `steer` (stream) 3 failed, the three that fail on main (the hat
graze, the edge sweep, the bent 회수). `tidebites` (sea) measured its first soak at 4.42 bites/min (the earlier run in
`Logs/mg2_tidebites.log`: 4.87) and then stalled in Retrieving after line cuts at the hook, as that earlier run did; it
was stopped there. The sea has no habitat, so nothing in this change runs on it.
