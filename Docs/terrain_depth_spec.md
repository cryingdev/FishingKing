# Lake terrain depth, phase 1: implementation spec

The lake's water depth is data: a bed generated at runtime from the save's world seed over the old distance profile, with
named features (a shore shelf, flats under the pads and reeds, a weed shoal, a deep lane in front of the pier, an old
creek, holes and humps), a material and a kind per cell, and named zones. Nothing of it is drawn (no underwater visuals;
the painted water stays as rendered). The fish live on it, the float lies down on it, the golden carp lurks on its
weed edge. Phase 2 (obstacles placed by rules, with colliders) and phase 3 (a HUD radar with contour lines) are out of
scope; `-fkbathy show` is the radar's prototype.

This merges design A (data and geometry) and design B (gameplay); every code claim was checked against the repo at
`b9802e9`. Section 17 lists what was changed while building it and why. The Korean UI strings stay as they are.

> **Phase 2** ([lake_phase2_spec.md](lake_phase2_spec.md)) changes this design in places, marked "phase 2" below: the bed
> is generated freely from a per-seed character (no pull towards the old profile), the stage's distance profile is
> re-derived from the bed (the finished rows' medians, P\*) and is the water off the grid, the habitat's depth bands are
> relative to the lake (percentiles), the spawn weights are derived from the bed, the population is 15 with a
> per-encounter feeding roll, and the reach scale and its estimator (section 8) are replaced by the economy estimator and
> its gate. Recipe v2.

---

## 0. Decisions

| Topic | Decision |
|---|---|
| Where depth lives | `StageLayout.DepthAt(x, z)` plus `[NonSerialized] Bathymetry Bathy` on `StageLayout`. Every call site already holds `L`. `Bathymetry` carries the rich query set. |
| Where pins come from | The raw obstacle data (`Obstacles.ReadSet`), not the loaded runtime set: the lake is the same with `-fkobstacles off`. |
| Flats | Explicit capsule blobs in a C# recipe table, tuned against the dump PNG. |
| Central lane | Narrow part x −3.4..−0.9, depth ≥ 4.5 m: the bamboo rod reaches the legend's spot. |
| Pin windows | Weed bed [2.4, 3.0]; pads [1.0, 2.0]; pad stems [0.8, 2.2]; reeds [0.35, 1.2]; boat [1.2, 2.5]; sunken log 6.225 ± 0.10. Overlaps: the intersection; if empty, the higher priority wins. |
| Depth limits | 0.35 m .. 9.0 m. With the grid, the HUD float limit is `max(8, ceil(DepthMax·2)/2)` = 9, so every node can be plumbed. |
| Slope limit | 1.5 m/m per 4-neighbour edge (validated at 1.52, in whole cm). |
| Determinism | SplitMix64, FNV-1a, Murmur fmix32, integer-lattice value noise; no sin, cos, exp or pow in the generator (the camera's pitch constants are rounded to 1e-6); meanders by Catmull-Rom; depths quantized to whole cm. |
| Retries | Up to 8 attempts, then the fallback recipe, which the tests prove always passes. |
| How fish move | Global weighted targets from a node sampler, keeping today's motion style (12 s re-pick, far targets). |
| Bite budget | The sense range scaled per (species, period, rig class, rod), clamped to 0.70–1.30 (§8). A deterministic estimator, lazy per key and cached. **Phase 2: removed**; the per-encounter feeding chance F and the lake's economy gate replace it (lake_phase2_spec A5–A7). |
| Body rule | `MinWater(cm) = clamp(0.2 + 0.4·cm/100, 0.3, 1.2)`. Wander, approach and flee get a move guard; fights a look-ahead plus a yaw snap. |
| Deep runs | A separate `deepRnd` (seed + 2), drawn once per run on the lake; 30 % of a big runner's runs go to the deeper side. |
| Legend spot | On the grid, `ChooseSpot` tries every node in reach; the depth check covers a disc of 0.6·r. |
| Lying float | Up / Tilt (0.18 s) / Lie, hysteresis 0.30 / 0.20; HUD label "찌 누움" and a one-time hint. |
| Save seed | Minted in `Game.Boot` for old saves; `-fkbathyseed <n>`, and 1 whenever `-fkauto` is given, read by `Bathymetry` itself. |
| Out of scope | Catch-card and encyclopedia zone lines, a "바닥 N.Nm" lure readout, a plumbing bracket, a lying ball float, bite multipliers by zone, contours visible to players, colliders, other stages. |

---

## 1. Facts at b9802e9

- 20 `DepthAt(z)` calls on 19 lines plus the `Obstacles.Bed`/`BotAt` chain: 24 sites (§6).
- The HUD's float maximum was `max(max(0.5, DepthAt(max(zNear+2, 20)) − 0.3), 8)`: 8 m on the lake.
- Today's lake contradiction: the 12 pads sat over 3.9–6.7 m of water; the weed bed (3, 14) r 2.2, top −1.2, over 4.5 m
  (weed 3.3 m tall); the sunken log's top −5.825 is the profile at z 22.25 (6.225 m) + 0.4 (`lake.py`).
- `TitleScene` and `FishingScene` both build a lake `StageView`; `Art.Data` caches one `StageLayout` per id, shared by
  every view, so `L.Bathy` is assigned on every `StageView.Init`.
- `AutoPilot.Boot` and `Game.DebugBoot` run AfterSceneLoad; `Game.Boot` BeforeSceneLoad.
- `runRnd = new System.Random(seed + 1)` picks each run's side; the fight places the fish with `OnLine`.
- The float sprite `World/float_stick.png` is 7×22 px (fk_items.py group `world`); `PixelArtImporter` applies the
  import settings under `Sprites/`.

---

## 2. Files

**New**

| File | Role |
|---|---|
| `Assets/Scripts/Core/Bathymetry.cs` | Grid data, queries, cache, switches, enums |
| `Assets/Scripts/Core/BathyGen.cs` | Generator, RNG, hashing, noise, validation, the dump's pixels |
| `Assets/Scripts/Data/TerrainRecipes.cs` | The lake recipe; `For(stageId)` is null for every other stage |
| `Assets/Scripts/Fishing/HabitatModel.cs` | The habitat math (habitat factor, densities, swim ranges, `MinWater`, the estimator), free of the running game so it can be swept outside Unity |
| `Assets/Scripts/Fishing/FishHabitat.cs` | The runtime wrapper: samplers, targets, spawn points, swim depth, the reach scale's cache |
| `Assets/Scripts/Debug/BathyOverlay.cs` | `-fkbathy show` and `-fkbathy dump` |
| `Assets/Scripts/Debug/AutoPilot.Depth.cs` | `-fkauto depth` |
| `Assets/Scripts/Debug/AutoPilot.Habitat.cs` | `-fkauto habitat` |
| `Assets/Resources/Sprites/World/float_stick_lie.png`, `float_stick_tilt.png` (+ metas) | Lying-float art |
| `Docs/terrain_depth_spec.md` | This spec |

**Changed**: `Core/Art.cs`, `Core/SaveData.cs`, `Core/Game.cs`; `Fishing/StageView.cs`, `FishAgent.cs`,
`FishSpawner.cs`, `FishingController.cs`, `FishingController.Obstacles.cs`, `Obstacles.cs`, `Tackle.cs`,
`FishingHUD.cs`, `Legend/LegendWatch.cs`; `Data/Models.cs`, `Data/GameDatabase.cs`; `Debug/AutoPilot.cs` (dispatch,
the pointer from the start, `:868`), `AutoPilot.LegendSpot.cs`, `AutoPilot.TideBites.cs`; `Tools/Blender/fk_items.py`
(group `floatlie`); `Tools/Blender/variants/hybrid/obstacles/lake.py` (comments only); the docs in §15.

---

## 3. Data structures and API

### 3.1 StageLayout (`Core/Art.cs`)

`DepthAt(float z)` is gone (the compiler flagged every old caller). Now:

```csharp
[NonSerialized] public Bathymetry Bathy;                // set by StageView.Init every time: the lake's grid or null
public bool Terrain => Bathy != null;
public float DepthAt(float x, float z) =>
    (Bathy != null && Bathy.Covers(x, z) ? Bathy.Depth(x, z) : BaseDepthAt(z)) + TideOffset;
public float DepthAt(Vector3 p) => DepthAt(p.x, p.z);
public float ProfileDepth(float z) => BaseDepthAt(z) + TideOffset;   // the old DepthAt(z), same float ops
public float ProfileMeanDepth(float z) => BaseDepthAt(z);            // generator base (no tide)
```

Without a grid `DepthAt(x, z)` evaluates the old expression: bit-identical (tested, §14 D3).

### 3.2 `Core/Bathymetry.cs`

- Enums: `BedMat { Mud, Sand, Gravel, Weed }`, `BedKind { Open, Shelf, Flat, Shoal, Dropoff, Hump, Hole, Channel, Basin }`
  (the index is the habitat tables' column), `[Flags] BedFlag { Edge, WeedEdge, Pinned, Lane }`; `BedZone { id, name,
  kind, c, area, minD, maxD, box, nodes }`.
- Switches `Off, Show, Dump, Log`; `SeedOverride` (`-fkbathyseed <n>`, else 1 under `-fkauto`, else null).
- `For(L, worldSeed)`: null with `Off` or without a recipe; cached (the last key `(stageId, worldSeed, version)`); logs
  `[BATHY] lake: world seed W stage seed S attempt k[ fallback] hash 0xH built N ms depth min/mean/max a/b/c zones …`.
- Grid: `Cell = 0.5`, `X0 = −48, Z0 = 0, Nx = 193, Nz = 129` (x −48..48, z 0..64: the fishable extent and the art's
  overscan). Arrays `ushort cm[]`, `byte kind[], mat[], zone[], flags[]`, `ushort edgeCm[], weedEdgeCm[]` (~250 KB).
- Queries: `Covers`, `Depth` (bilinear over cm, no tide), `MatAt`, `KindAt`, `ZoneAt`, `FlagsAt` (nearest node; off the
  grid Mud / Open / null / None), `Gradient` (central differences at ±Cell, points deeper), `Slope`, `DeeperDir` (zero
  under 0.02), `EdgeDist`, `WeedEdgeDist` (99 off the grid), `MinDepthAlong` (every 0.25 m), `FirstShallower`,
  `MinDepthDisc` (centre + 8 ring points), `Zones`, `Zone(id)`, `NodeCount`, `NodeAt`, `NodePos`, `NodeDepth`,
  `NodeMat`, `NodeKind`, `NodeFlags`, `NodeZone`. The along / disc queries read the grid itself (the profile off it, the
  tide added), never `L.Bathy`, so they hold for a grid that is not the one in use.

### 3.3 `Data/TerrainRecipes.cs`

`TerrainRecipe { stage, version = 1, x0, z0, nx, nz, minDepth 0.35, maxDepth 9.0, maxSlope 1.5, feather 6, attempts 8,
blobs, shelfZ (5.5, 8), shelfWobble 1.5, noise 0.30 @ 14 m + 0.12 @ 5 m (×(1 − 0.6 wFlat)), channel, holes, humps, pins,
lane }`. Every number of §4 and §5 lives in `Lake()`. Bump `version` on any recipe change: it is mixed into the seed, so
every save's lake re-rolls on purpose and the golden hash changes.

### 3.4 Obstacles

- `ReadSet(stageId)`: the file read, cached per id, ignoring `Off` (`Load` still parses its own runtime copy).
- `Footprint(o)`: `BuildPoly(o.pts, o)`, the runtime footprint from raw data.
- `Bed(x, z) => -L.DepthAt(x, z)`, `BotAt(o, x, z) => o.bot <= -98f ? Bed(x, z) : o.bot`.

---

## 4. Generator (`BathyGen.Build(L, recipe, worldSeed, rawObstacles)`)

### 4.1 Determinism kit

- `Fnv1a(string)` over UTF-16 units; `Mix(a, b)` = Murmur fmix32 of `a ^ (b·0x9E3779B9)`.
- `stageSeed = Mix(worldSeed, Fnv1a(stageId) ^ (version·0x9E3779B9))`; attempt k: `as0 = stageSeed`,
  `asK = Mix(stageSeed, k)`.
- Streams per family: `SplitMix64` seeded from `Mix(asK, Fnv1a(name))` for `"flats"`, `"shelf"`, `"channel"`,
  `"holes"`, `"humps"`, `"noise"`. Tuning one family never reshuffles the others.
- Value noise `V(x, z, salt)` in [−1, 1]: lattice corners `((Mix(Mix(i, j), salt) >> 8)·2/2^24) − 1`, quintic fade,
  bilinear. Salts per attempt: a blob's rim `Mix(asK, Fnv1a(id))`, the flats' texture `Mix(asK, Fnv1a("flatnoise"))`,
  the shelf `Mix(asK, Fnv1a("shelf"))`, the materials `Mix(asK, Fnv1a("mat"))`; step 5's two salts are drawn from its
  stream.
- Allowed math: + − × ÷, `Sqrt`, `FloorToInt`, `Clamp`, polynomial smoothstep `S(e0, e1, x)` (also with e0 > e1).
- The hash: FNV-1a over the bytes of `cm`, `mat`, `zone`, `kind`.

### 4.2 Pipeline

D is depth (m, + down); node k at `x = X0 + i·0.5`, `z = Z0 + j·0.5`.

0. **Base**: `D = L.ProfileMeanDepth(z)`. *Phase 2*: the character's free base `B(x, z)` (lake_phase2_spec A1, steps 0a/0b; `ProfileMeanDepth` is now `AuthoredMeanDepth`).
1. **Flats** (set-to). Per blob, in table order, 4 draws: `scale U(1.0, 1.12)`, `wob U(0.4, 0.8)`, `e U(edge)`,
   `t U(depth)`. Per node: `sdf = segDist(p, a, b) − r·scale + wob·V(x/6, z/6, salt)`, for blobs with a gap
   `max(sdf, gap − sdfLane)`; `w = 1 − S(−e, 0, sdf)`; target `Σw·t/Σw + 0.12·V(x/4, z/4)`; `D = lerp(D, target, max w)`.
   `flatId` = the blob of the largest w (≥ 0.5), `shoalW` the largest shoal w.

   | id | flat | a → b | r | depth | e | gap |
   |---|---|---|---|---|---|---|
   | L1 | flatL | (−40, 3.0) → (−7.5, 3.2) | 3.2 | 0.6–1.0 | 2–3.5 | |
   | L2 | flatL | (−7.5, 5.0) → (−9.8, 13.5) | 3.2 | 1.3–1.8 | 2–3.5 | |
   | L3 | flatL | (−6.6, 14.0) → (−7.4, 18.0) | 2.4 | 1.0–1.6 | 1.4–2.0 | 1.2 |
   | L4 | flatL | (−8.4, 18.5) → (−11.8, 23.6) | 3.0 | 1.0–1.7 | 2–3.5 | |
   | L5 | flatL | (−13.2, 17.1) | 3.0 | 0.6–1.1 | 2–3.5 | |
   | L6 | flatL | (−40, 12) → (−15, 21) | 8.0 | 1.2–2.0 | 2–3.5 | |
   | R1 | flatR | (7.5, 3.2) → (40, 3.0) | 3.2 | 0.6–1.0 | 2–3.5 | |
   | R2 | flatR | (6.8, 4.5) → (7.2, 14.2) | 3.0 | 0.9–1.5 | 2–3.5 | |
   | R3 | shoal | (5.4, 13.0) → (2.8, 14.4) | 2.2 | 2.5–2.9 | 1.2 | 1.3 |
   | R4 | flatR | (10.0, 22.0) → (14.6, 29.0) | 3.4 | 1.0–1.7 | 2–3.5 | |
   | R5 | flatR | (15, 10) → (40, 19) | 8.0 | 1.2–2.0 | 2–3.5 | |

   Lane polygon (concave, CCW; even-odd test): (−3.4, 12.5) (−0.9, 12.5) (−0.9, 16.9) (1.5, 18.6) (4.6, 21.0)
   (5.2, 30.0) (−4.9, 30.0) (−5.2, 25.0) (−4.6, 19.4) (−3.4, 18.6). Shelf line `zShelf(x) = U(5.5, 8) + 1.5·V(x/9)`.
2. **Channel** (an old creek): start `(U(−2.5, 0.5), U(17, 20))`, length `U(28, 38)`, amplitude `U(2.5, 5)`; a control
   point every 8 m in z (x within the visible half width − 3); a uniform Catmull-Rom through them sampled every 0.5 m;
   half width `U(1.5, 2.5)`, extra depth `U(0.6, 1.0)·S(hw, 0, d)·(1 − wFlat)`.
3. **Holes**: 1 or 2; `R U(2, 3.5)`, aspect `U(1, 1.5)`, axis, extra `U(1.0, 1.8)`; hole 1 at z 18–32, |x| ≤ 6, hole 2 at
   z 30–50, |x| ≤ 0.8·half(z, 600); 40 tries (2 draws each) clear of the flats (R + 1.5), the pins (R + 2) and each other
   (Ra + Rb + 3); `D += extra·S(1, 0.3, rho)·(1 − wFlat)`.
4. **Humps**: 1–3; `R U(3, 6)`, aspect `U(1, 1.8)`, top `U(2.4, 4.0)`; hump 1 at z 30–38, the others 30–52, |x| ≤
   half(z, 640) − R; outside the lane by R + 1, clear of flats, pins, features; height `D(centre) − top ≥ 1.5`, capped at
   `1.3·0.55·R`; `D −= h·S(1, 0.45, rho)`.
5. **Noise**: `D += (0.30·V(x/14, z/14) + 0.12·V(x/5, z/5))·(1 − 0.6·wFlat)`.
6. **Feather**: `D = base + (D − base)·S(0, 6, min(x − X0, X1 − x, Z1 − z))` (none at the z = 0 shore). *Phase 2*: feathered to the provisional profile P0, the edges re-set to the final P\* after step 9 (lake_phase2_spec A1, steps 5b, 6, 9b).
7. **Pins, pass A** (§5): ring nodes blended towards each window in ascending priority, core nodes clamped into their
   effective window; then the global limits.
8. **Slope limit** (§17.1): (1) every node's window (a core's, the far edges' profile value, else [0.35, 9]) spread at
   the limit slope by two raster min/max-plus sweeps, and D clamped into it; (2) relaxed Jacobi passes (each node half
   way into the window its 4 neighbours allow, their mean when they disagree; cores clamped into their window; far-edge
   nodes fixed), double-buffered, only over the nodes round an edge still over the limit, up to 60 passes; (3) what is
   left is closed exactly: the mean of the largest limited function under D and the smallest over it, clamped into (1).
9. **Pins, pass B** (cores only), then clamp D to [0.35, 9.0].
10. **Quantize**: `cm = RoundToInt(D·100)`.
11. **Derived fields**: the node slope (central differences, one-sided at borders); the kind (first match: Hump —
    `rho < 1`, `humpC ≥ 0.3`; Hole — `holeC ≥ 0.3`; Dropoff — slope ≥ 0.35, 1 ≤ D ≤ 8.5; Channel; Shoal — `shoalW ≥ 0.5`;
    Flat — `wFlat ≥ 0.5`, z > zShelf; Shelf — z ≤ zShelf and (`wFlat ≥ 0.5` or D ≤ 2.6); Basin — D ≥ 5.5; Open); the
    material (Hump: gravel in its middle, else sand; Hole: mud; Channel: gravel / sand by noise; slope ≥ 0.6: gravel;
    Flat / Shoal: weed, flats partly sand; Shelf: weed within 1.5 m of a reed, else mud / sand; D ≥ 5.5: mud; else sand /
    mud); the flags (Edge = slope ≥ 0.35; WeedEdge = Dropoff, D ≥ 2.0 and within 2.5 m of a Flat / Shoal / weed node;
    Pinned; Lane); two-pass 8-neighbour chamfer distances (`edgeCm`, `weedEdgeCm`); the zones (first match hump_i >
    hole_i > channel > shoal > flatL / flatR > shelf > lane > basin > open), named 얕은 턱, 왼쪽 수초 평지, 오른쪽 수초 평지,
    수초 둔덕, 잔교 앞 깊은 골, 옛 물골, 수중 둔덕 N, 깊은 웅덩이 N, 깊은 바닥, 열린 바닥.
12. **Validate** (§5.4); on failure the next attempt; after 8, the **fallback** (steps 0, 1 at each blob's mid values,
    scale 1.06, no wobble or texture, then 6–11; no channel, holes, humps or noise) and `[BATHY] WARN fallback`.

**Cost**: measured in the Mono player ~70 ms per attempt (target 30 ms; the first build of a stage ~170 ms with the
pins' preparation); built once per stage and world seed and cached, at the title's or the stage's load. A lazy build
remains an option if a load ever hitches.

---

## 5. Constraints, pins, validation

### 5.1 Pins (from the raw obstacle set)

| Pin | Selector | Grow | Window [lo, hi] (m) | Blend | Prio |
|---|---|---|---|---|---|
| pier | rect x −3.5..3.5, z 0..3.5 | – | the profile (phase 2: the authored profile, `authD`) | 3.0 | 10 |
| sunklog | id `sunklog` | 0.3 | [6.125, 6.325] | 2.0 | 9 |
| weedbed | id `weedbed` | 0.2 | [2.4, 3.0] | 1.5 | 8 |
| reeds | kind weed with tag `reed` | 0.3 | [0.35, 1.2] | 2.0 | 7 |
| boat | id `boat`, `boat.skirt` | 0.4 | [1.2, 2.5] | 2.0 | 6 |
| stake | id `stake`, `stake.skirt` | 0.3 | [0.8, 2.5] | 1.5 | 6 |
| pads | kind `pad` | 0.3 | [1.0, 2.0] | 1.5 | 5 |
| pad stems | kind weed whose parent is a pad | 0 | [0.8, 2.2] | 1.5 | 4 |
| lane | lane polygon | – | [4.5, 9.0] | 1.0 | 3 |
| generic | any snag / weed zone with bot ≤ −98, top < −0.5, not matched above | 0 | [−top + 0.15, 9.0] | 1.0 | 2 |

Covers are not pinned. The pins mirror the baked obstacle tops of `Tools/Blender/variants/hybrid/obstacles/lake.py`
(sunklog bed 6.225, weedbed top −1.2): change both together.

### 5.2 Limits and continuity

Every node 0.35..9.0 m (bilinear never goes below the shallowest node, so every point in the water has ≥ 0.35 m); every
4-neighbour edge with a non-core node ≤ 1.5 m/m; the pier core is the profile to the cm (the walkable shore keeps
today's depths); the far edges (x = ±48, z = 64) are the profile, so stepping off the grid jumps ≤ 1 cm.

### 5.4 Validation (always; `[BATHY] CHECK` lines with `-fkbathy log`)

| # | Check |
|---|---|
| V1 | Every pin-core node inside its effective window (±0.0051: half a cm and the float's slop) |
| V2 | 0.35 ≤ D ≤ 9.0 everywhere |
| V3 | Pier core = the profile within 0.01 (phase 2: the authored profile) |
| V4 | Far-edge nodes = the profile within 0.01 (phase 2: the side and far edges = P\*) |
| V5 | Every 4-neighbour edge with a non-core node: \|Δcm\| ≤ 76 (1.52 m/m) |
| V6 | Under every zone down to the bed, D ≥ −top + 0.15; under every `bed` solid, D ≥ 0.3 |
| V7 | Lane core ≥ 4.5 |
| V8 | For cast distances 16, 20, 24, 27, 34, 36: ≥ 6 legend spot centres (0.9 m disc ≥ 4.3 m, z ≥ zNear + 3, within cast − 0.5 of (0.375, 0), clear of pads by 0.9 m and solids by 1.0 m, within 6 m of a lurk candidate: WeedEdge, D ≥ 4.5, z 14..min(30, cast + 5), in view) |
| V9 | (Information) the centre line's D / profile per 5 m of z (phase 2: P\* against the authored profile; the final rows' medians against P\*). Phase 2 adds V10 (R_ref's depth quantiles), V11 (R_ref's material and kind shares) and V12 (the sweep's spread): lake_phase2_spec A1 |

---

## 6. Call-site migration (24)

"Legacy": on grid-less stages and on the lake with `-fkbathy off`, the same floats and the same UnityEngine.Random
draw order as before; every new branch sits behind `L.Terrain`.

| # | Site | Now |
|---|---|---|
| 1 | `Tackle.Bottom` | `L.DepthAt(Surface.x, Surface.z) − 0.25` (Floor, the float's sink, the lure clamps follow) |
| 2–6 | `Obstacles.Bed`, `BotAt`, `InBand`, `Rub` (fish, line) | plan points |
| 7 | `FishAgent.PickTarget` | grid: `Habitat.Target`; else the old draw with `ProfileDepth` |
| 8 | pocket | `DepthAt(px, pz)` (px hoisted; no draw moves) |
| 9 | cover | `DepthAt(cx, cz)`; grid: covers whose hold has `MinWater + 0.2`, swim depth from the habitat |
| 10 | `UpdateBite` | `DepthAt(Pos.x, Pos.z)` |
| 11 | `Flee` | grid: the shallower of the way out and the end; else `ProfileDepth(Pos.z + 20)` (same draw order) |
| 12 | `FishSpawner.Spawn` | grid: size, then `Habitat.SpawnPoint`; else the old draw with `ProfileDepth` |
| 13 | first float depth | `ProfileDepth(15)` (2.0 m, seed-independent) |
| 14, 22 | ice aim, AutoPilot ice | `DepthAt(holeX, holeZ)` |
| 15 | run depth target | grid: the water at `RunDest(fightYawTarget)`; else at the fish |
| 16 | per-frame fight clamp | `DepthAt(pos.x, pos.z) − 0.2`, after the grid guard (§9.2) |
| 17 | cover hold | `DepthAt(hold.x, hold.y) − 0.3`; grid: `NearestCover` needs `MinWater + 0.2` at the hold |
| 18 | HUD float maximum | grid: `max(8, ceil(DepthMax·2)/2)`; else the old bound with `ProfileDepth` |
| 19 | spot depth | grid: `MinDepthDisc(c, 0.6·r) ≥ depthNeed + 0.3`; else `DepthAt(c)` |
| 20–21 | lurk depth | `bed = DepthAt(x, z)`, `min(lurkDepth, bed − 0.5)` or `bed − 0.5` |
| 23 | AutoPilot.LegendSpot | `DepthAt(s.x, s.z)`; grid: the disc check |
| 24 | AutoPilot.TideBites | `DepthAt(s.x, s.z)` (the sea: identical) |

Not migrated (exporter only): `lake.py`'s `_depth_at` (comment added) and the other stages' helpers.

---

## 7. Ecology (`HabitatModel.cs`, `FishHabitat.cs`)

### 7.1 Data

> **Phase 2**: the depth band is relative (`depth:pA-pB`, percentiles of the lake's depths over R_ref; shifts `@period:Np`), resolved per bed (`HabitatModel.Resolve`); the strings below are phase 1's. Lake_phase2_spec A3 has the conversion and the table for the four species.

`FishSpecies.habitat` (`HabitatDef { a, b, kindW[9], matW[4], edge, col Mid|Bottom, beta, shift[4], periodKind[4, 9],
runDeep }`), parsed from `"key:value,…"`, the species file's `"habitat"` (`Assets/Resources/Data/Fish/<id>.json`, read
by `SpeciesData`; formerly `GameDatabase.Habitats()`): `depth:a-b`, kind and material keys, `edge`, `col`, `beta`,
`@period:shift` (− shallower), `@period.kind:mult`, `runDeep`; an unknown key is a load error. Every non-legend species
on a stage with a generated bed must have one, with `depth` and `col` (the validator, Docs/data_reference.md 2.7).
`EncounterDef.lurkWeedEdge` (the golden carp). The strings as tuned (§17.3):

```
crucian_carp     depth:1.2-4.0,flat:1.5,shoal:1.5,shelf:1.2,dropoff:1.3,hump:0.9,channel:0.8,hole:0.6,basin:0.5,weed:1.4,mud:1.2,sand:1.0,gravel:0.8,edge:0.4,col:bottom,beta:0.65,@dawn:-0.8,@day:1.0,@evening:-0.8,@night:-0.5,@night.flat:1.2,@day.dropoff:1.6,@day.shoal:1.5
bluegill         depth:0.8-3.0,shelf:1.8,flat:1.6,shoal:1.5,dropoff:1.0,channel:0.7,hole:0.5,basin:0.4,sand:1.3,gravel:1.2,weed:1.3,mud:0.8,edge:0.3,col:mid,beta:0.75,@day:0.5,@night:1.0,@night.dropoff:1.5,@night.basin:0.6,@day.shelf:1.2,@day.shoal:1.2
carp             depth:3.0-7.0,hole:1.5,channel:1.3,basin:1.3,dropoff:1.2,shoal:0.9,flat:0.7,shelf:0.5,mud:1.4,weed:1.1,gravel:0.8,edge:0.2,col:bottom,beta:0.65,@dawn:-1.0,@day:0.5,@evening:-1.0,@night:-2.0,@night.flat:3.0,@night.shoal:2.6,@night.dropoff:0.8,@day.hole:1.5,@day.channel:1.5,@day.dropoff:1.2,@day.flat:0.5,@day.shelf:0.5,runDeep
largemouth_bass  depth:1.0-4.5,dropoff:1.8,hump:1.6,shoal:1.5,flat:1.2,shelf:1.1,channel:0.9,hole:0.8,basin:0.5,gravel:1.3,weed:1.2,mud:0.8,edge:0.5,col:mid,beta:0.8,@dawn:-0.4,@day:1.5,@evening:-0.4,@night:-0.3,@dawn.flat:1.8,@dawn.shelf:1.6,@dawn.dropoff:0.8,@day.hump:1.8,@day.dropoff:1.5,@day.shoal:1.3,@day.flat:0.8,runDeep
golden_carp      runDeep
```

`TimeActivity` and the population are unchanged: activity decides how many fish are about, the habitat only where.

### 7.2 Habitat factor

`w` = node depth, `a' = a + shift[p]`, `b' = b + shift[p]`; `Dfit` 1 inside, `max(0.2, 1 − 0.8(a' − w))` below,
`max(0.2, 1 − 0.4(w − b'))` above; `h = Dfit·kindW·periodKind·matW·(1 + edge·max(0, 1 − EdgeDist/2.5))`, 0 where the
node has less than `MinWater(maxCm)`.

### 7.3 Densities

> **Phase 2**: `RowKeep` is 0.5 (half of each row's share kept), and the stage's spawn weights are derived from the bed (lake_phase2_spec A3, A4).

Target region z in [zNear + 1.5, zMax] (spawn [zNear + 2, zMax], from distance [zMax − 4, zMax]), |x| ≤ half(z) =
min(xLim − 0.5, VisibleHalfWidth(z, 600)). Today's density `U = 1/(2·half(z))`; `hn = clamp(h / mean_U(h), 0.25, 3)`;
`W = U·((1 − β) + β·hn)` (0 under MinWater); then every row of the region (one z) is scaled back to today's share of it
(`RowKeep` = 1): the habitat moves a species along a distance band, onto the flats, drop-offs, humps and channels at that
range, not from beyond a rod's reach into it (§8.3).

### 7.4 Samplers

Per `(species, period, zMin, zMax)`, lazily, a cumulative W over the region's nodes. A draw: the period by the clock's
blend, a node by binary search, a ±0.25 m jitter (back to the node if that is shallower than the species' MinWater), the
swim depth there. The cover branch still runs after the main draw.

### 7.5 Swim depth

`bot = water − 0.3`, `lo = max(0.3, dMin + Δ/2)`, `hi = max(lo, dMax + Δ/2)`; Mid `U[min(lo, bot), min(hi, bot)]`,
Bottom `U[max(min(lo, bot), bot − 2), bot]`; ≥ 0.3. No def: today's rule.

---

## 8. Bite-rate budget

> **Phase 2: replaced.** The reach scale, its estimator and the D11 gates of this section are removed. A per-encounter feeding chance F, set per bed and rod by the economy estimator (`LakeEconomy`), bounds the lake's catches and income per minute to 0.8–1.25 of today's (lake_phase2_spec A5–A7). This section is kept as the record of phase 1.

**Rule** (the user's): for a reference player who casts evenly over the ±38° fan and keeps the rig unchanged, the stock's
bites per minute per (bait, rig class, period, rod) stay within 0.8–1.25× of today on every seed. If it meant per spot,
ecology would have to be almost invisible (β ≈ 0.25).

**Lever** (grid only, before `ApproachRolls++`): `sense = SenseRange() × ReachScale(species, clock blend, rig)`, the scale
in [0.82, 1.22] (`HabitatModel.ScaleMin/ScaleMax`); no approach to a hook in water shallower than the fish's MinWater.
`SenseFor(f)` exposes it. Rig classes: floats F1 ≤ 1.0, F2 ≤ 2.5, F4 ≤ 4.5, F6 beyond; lures Surface (floating), Mid
(suspending), Bottom (sinking). `ReachScale` blends the two periods' scales, each cached per (species, period, rig class,
cast distance) and logged `[HAB] budget …`.

### 8.1 Estimator: where the fish spend their time

The first estimator weighted the casts by the target density; the live A/B soak (§17.1) showed it tracks play only
loosely (r = 0.72): a wanderer re-targets after 12 s at 0.18–0.3 of its speed, so it spends most of its time between
targets, and its depth eases at half its speed. Now `HabitatModel.Simulate` runs the wander itself (deterministic,
`System.Random`; the game's `UnityEngine.Random` is never touched): 160 fish (today's lake 640, the same for every seed),
20 s settling then 240 s sampled every 0.5 s at a 0.2 s step; the stock's sizes (`RollSize`) and speeds; PickTarget's
rules (the habitat's node draw with its jitter, or today's uniform draw over the profile), the cover branch (0.6 × seek of
the targets in a cover of its types, on the bed one with `MinWater + 0.2` at its hold), `Steer` (2.5 rad/s, the
alignment factor), the depth eased at half its speed, and on the bed `KeepInWater` / `KeepOffBed`. The samples go into
1 m cells × 0.25 m depth bins (`Occupancy`).

Over the casts (from (mid of the walk range, 0) at yaw −38..38 every 4°, zNear + 2.5 out to the cast distance every
metre): today's samples within R (5 floats, 7 lures) whose depth is within 2.5 m of the hook (the class's hook depth over
the profile), and the bed's (over the grid, × the share of the species' sizes that come into the water at the hook,
`SizeShare`). `raw = bed(R) / today(R)`; the scale is the one that brings the bed's count to today's (solved over the
cumulative count by distance), clamped to [0.82, 1.22]; `scaled = bed(R × scale) / today(R)`. best10 / worst10: the
per-cast ratios after the scale of the best / worst 10 % of casts. The estimator's own noise (6 salts, seed 1): sd 0.02–0.07
of raw.

**Prewarm**: `FishingController.Init` starts the simulations for the rod in hand (today's lake and the bed's four periods
for the stage's stock, the period now first) on a worker thread (`Lazy`, `Task.Run`: ~2.4 s for 20 in the player); a
fish that senses the rig before its own is done waits for it or makes it.

### 8.2 Gates (`-fkauto depth` D11, seeds 1..50 × bamboo / carbon / dragon)

1. **Per species** (the spec's gate, fixed bounds `HabitatModel.RawMin/RawMax`, not derived from the clamp): raw in
   [0.67, 1.49], the scaled estimate in [0.9, 1.1], the scale in [0.82, 1.22], for every (species, rig class) pair some
   bait of the class draws.
2. **The user's rule**: per bait, rig class (floats F1..F6; a lure its own), period and rod, the stock's bites after the
   lever over today's in [0.8, 1.25]; a species' share = its stock weight × activity × √activity (the approach roll) ×
   its appeal for the bait.
3. Seed 1, bamboo: the best rig class's best10 ≥ 1.2 for every species and period (occupancy is smoother than the targets;
   it was 1.3 on the target density).

### 8.3 What the bed does to the budget

The bed is shallower than the old profile over most of the fan (the lake's sides and flats: at z 14 the profile says
4.5 m, the bed's mean over the view's width is 2.6 m with a third under 2 m), so fish that keep their old depths are
forced up over it: shallow rigs meet more of them, deep rigs fewer, before any habitat. With β = 0 (no ecology at all)
raw is already 0.75–1.86 per species (carp on F1 and bass on topwater with the carbon and dragon rods ~1.6–1.9). So gate 1
cannot pass with the 0.82–1.22 lever whatever the habitat strings (§17.1); gate 2, the user's rule, can. The habitat was
also pulling fish from beyond a long rod's reach into its fan (the long rods' pooled ratio after the lever reached ×1.6);
`RowKeep` (§7.3) stops that, and with the strings of §7.1 every rod passes gate 2 (seeds 1..50: after the lever
1.00–1.09 bamboo, 1.00–1.19 carbon, 1.00–1.22 dragon; the tightest is a topwater lure at dawn on the dragon rod).

---

## 9. Depth limits in motion and fights

`MinWater(cm) = clamp(0.2 + 0.4·cm/100, 0.3, 1.2)`: bluegill 0.3, crucian 0.34, bass (60 cm) 0.44, carp (90 cm) 0.56.

**9.1 Agents** (grid): after the steer in wander (after the current's carry), approach and flee, a step into water
shallower than its MinWater that is also shallower than where it was is undone (wander: a new target; approach: it
loses interest; flee: off down the slope, `DeeperDir`·15, or straight out). Always lifted to ≥ 0.3 m over the bed. A
nibbling fish's body and a biting fish carrying the bait off hold their plan position instead of such a step
(`ShallowStep`); their depth stays at the bait's (a bait on the bed: no lift).

**9.2 Fight** (grid, a run or a cover run, not a jump): `OnLine(y)` is `OnLineAt(y, fightYaw)`. Looking 1.5 m ahead
along its heading, water under `need + 0.2` turns the run 0.3 rad towards the deeper side; a fish still in water under
`need` is turned to the nearest yaw (8 steps of 0.05 rad either way) with enough, its target 0.25 rad further; none:
`[BATHY] WARN shallow fight` once. Reeling in is not limited (the bed clamp lifts it).

**9.3 Run side**: `RunDest(yaw) = Anchor + (sin, 0, cos)·(hPlan + 6)`; the drawn step's two sides' water `wC`, `wO`;
too shallow on the drawn side with room and water on the other: the other; `deep = deepRnd.NextDouble() < 0.3` drawn on
every run; a big runner (≥ 40 cm with `runDeep`: carp, bass, golden carp) takes the other side when it is 0.3 m deeper.
`deepRnd = new System.Random(seed + 2)`, so `runRnd`'s sequence is untouched. `[BATHY] run <sp> side <L|R> wC wO forced`.

---

## 10. Golden carp lurk and spot (grid)

Lurk candidates (rebuilt when the rod changes): WeedEdge nodes ≥ depthMin + 0.5 (4.5), z from lurkZMin to where a spot
within the cast can sit over them, in the view. `Place`: up to 12 candidates clear of standing props (1 m), not behind
the front layer, with a spot node that passes; else the old loop. `PlaceNatural`: the nearest candidate within 4 m of
(Angler.X, 16). `ChooseSpot`: every node within nearLurk − 1 of the lurk point and within cast − 0.5 of him that passes
(the 0.6·r disc ≥ 4.3), scored as before, one drawn among those within 1.0 of the best. `OfferSpot`: two failures in a
row move the lurk point. Test hooks `DebugPlace()`, `DebugChooseSpot(out s)`.

---

## 11. Lying float

**Behaviour** (`Tackle`): `FloatSit { Up, Tilt, Lie }`, `FloatLying`, `FloatLaid`; active for a stick float in the water
on the grid, not snagged. `slack = FloatDepth − bottom`, `settled = Depth ≥ bottom − 0.01`, it wants to lie when
settled with slack ≥ 0.30 (0.20 to stay lying); either way through Tilt for 0.18 s; lying down: a small ring (0.4 × the
nibble's) and `FloatLaid`. The bait rests at `min(FloatDepth, Bottom)` as before; a biting fish lifts it off the bottom,
so the float stands up through the tilt before the dip (the lying-float bite sign). `FollowFight` stands it up; the
upright sprite never changes (the fight's float reads its height). Plumbing: lengthen the float until it lies, shorten it
until it stands (0.5 m steps, to the bed's deepest node).

**Render**: `lieSr` beside `floatSr` (order `OrderRipple + 1`, front occlusion), the upright scale (`FloatScale`, the
8 px rule), flipped for floats left of the middle (the tip away from the centre), centred on the surface point with a
half-size bob and the dips; the 케미 light at the tip (`LieTipPx` (12.93, 0), `TiltTipPx` (9.14, 9.73), sprite px).

**HUD**: the label "찌 누움" (gold) while a float rig waits lying, else "찌 수심" (sky); the one-time hint
"찌가 누웠어요! 미끼가 바닥에 닿았다는 뜻이에요 — 찌 수심을 줄이면 다시 서요" (`SaveData.lieHint`).

**Art** (`fk_items.py` group `floatlie`): `float_stick` turned 90° about Y and 20° about Z (`float_stick_lie.png`, 32 × 7)
and 45° about Y + 20° about Z (`float_stick_tilt.png`, 24 × 24), the same geometry, palette, outline and 8 ppu; both
centred on the model origin; review sheets `_tmp/floatlie/sheet.png` (×12) and `strip.png` (1× on #456a8a at 1 and 0.36).

---

## 12. Save seed

`SaveData.worldSeed` (0 = a save from before the terrain) and `lieHint`; `NewGame` mints a seed
(`SaveSystem.NewSeed()`: a GUID hash, never 0, never UnityEngine.Random); `Game.Boot` mints one once for an old save and
saves it (`[BATHY] world seed minted for an old save`). `-fkfresh` and resetting progress go through `NewGame`: a new
game is a new lake. `StageView.Init` assigns `L.Bathy = Bathymetry.For(L, SeedOverride ?? Game.I.WorldSeed)` every time.

---

## 13. Debug switches

| Switch | Effect |
|---|---|
| `-fkbathy off` | No grid anywhere: the lake runs exactly the old code paths (the A/B baseline) |
| `-fkbathy log` | `[BATHY] CHECK` lines for every validation item and attempt |
| `-fkbathy show` | The contour overlay (debug only): 1 m contours as 1 px dots, cream (1 m) .. deep blue (8 m), WeedEdge nodes orange, lurk candidates gold crosses, sorting order 39 |
| `-fkbathy dump` | Top-down PNG `bathy_<stage>_<worldSeed>.png` (4 px a node: depth ramp, 1 m contours, kind tints, zone borders, hatched pin cores, the lane, the raw obstacles' footprints, lurk candidates) into `-fkshots` |
| `-fkbathyseed <n>` | The terrain's world seed (never saved) |
| `-fkhabsecs <s>` | Soak seconds per spot for `-fkauto habitat` (default 75) |
| `-fkauto depth`, `-fkauto habitat` | §14 |

---

## 14. Tests

**`-fkauto depth`** (e.g. `-fkfresh -fkrich -fkgear -fksave td_depth -fkscene Fishing -fkstage lake -fkauto depth
-fkshots <dir>`): `[DEPTH] CHECK` lines, "depth test done: N failed".

| # | Check |
|---|---|
| D1 | Seed 1 built twice: the same hash (and the grid in use); seed 12345 against `GoldenHash[version]` (0 = record) |
| D2 | Seeds 1..50: V1–V8 on every accepted grid, the attempts, no fallback, 50 distinct hashes, build times; the fallback alone passes |
| D3 | Stream, sea (tide 0, ±0.4), swamp, ice, cave, ocean and the lake with the grid off: `DepthAt(x, z)` = `ProfileDepth(z)` = a verbatim copy of the old `DepthAt(z)`, bit for bit; off the lake's grid the profile, ≤ 2 cm across its edge |
| D4 | The queries (node values, cell means, gradient signs, DeeperDir across the point, EdgeDist, MinDepthAlong, MinDepthDisc, zone names) |
| D5 | The world seed: minted once for an old save, kept through JSON, set for a new game; the test's seed leaves the save file byte for byte |
| D6 | The lying float (set at 4 m on a flat: tilt, lie within settle + 0.3 s, "찌 누움", the hint once; at night the 케미 at its tip; shortened to 1 m it stands through the tilt; laid again no second hint; a fight from it stands it on the first frame) and its shots |
| D7 | 2000 targets per species and period (bamboo's water): bass by day on drop-off + hump + shoal ≥ 1.4× today, at dawn on flat + shelf ≥ 1.3×; crucian at dawn on the flats ≥ 1.3×, by day ≤ 0.8× its dawn share; carp at night on flat + shoal ≥ 1.4×, by day in hole + channel + basin ≥ 1.2×. *Phase 2*: D7′ on three lakes (lake_phase2_spec A8) |
| D8 | 300 s soaks (carp, bass, the stock) and 10 fights by the lane's edges at 1/60 s: no free fish (wandering, coming, nibbling, biting, fleeing) in water shallower than its MinWater, none (wandering, coming, fleeing) under the bed − 0.3; no running fight frame in too little water, none under the bed − 0.2. (These soaks have no rig in the water; the habitat soak counts the nibbles and bites.) |
| D9 | 60 fights each (carp 70 cm, crucian 25 cm): of the runs whose sides differ ≥ 0.3 m the deeper taken 0.58–0.75 (carp) / 0.40–0.60 (crucian); none into too little water with the other side open; the step draws match a fresh `System.Random(seed + 1)` |
| D10 | 40 placements with the bamboo and the dragon rod: lurk ≥ 4.5 m, ≥ 75 % on WeedEdge, a spot every time with ≥ 4.3 m over 0.9 m |
| D11 | The estimator over seeds 1..50 × 4 species × 4 periods × their rig classes × bamboo / carbon / dragon: the three gates of §8.2. *Phase 2*: replaced by D11′ (the economy gate), D11b and D14 (lake_phase2_spec A8) |
| D12 | Every lake weed / snag zone, 20 points: the bed under its top |
| D13 | `bathy_lake_1.png`, `bathy_lake_7.png` and the overlay for seeds 1 and 7 (`depth_show_lake_seed*`) |

> **Phase 2**: `-fkauto habitat` is removed; `-fkauto economy` (lake_phase2_spec A8) soaks the new lake against today's (`-fkecomode legacy`).

**`-fkauto habitat`**: lake, seed 1, the bamboo rod with the starter reel and line, the stock; the clock frozen at each
period's centre; 떡밥 2 m, 옥수수 4 m, 지렁이 1 m floats; 70 spots over the reference player's fan (the estimator's: yaw
−36..36 every 8° × from zNear + 2.5 every 2 m out to the cast, from the walk's middle; moved off pads and weed / snag
zones), `-fkhabsecs` each (default 20); terrain on, then off (the stage reloaded, `Random.InitState(1515)`).
`-fkhabmode on|off`, `-fkhabperiods 0,2`, `-fkhabrigs paste,worm` run part of the cells (parallel runs; the
`[HAB] cell …` lines are pooled by hand with the same rule). CHECK (hard) every rig × period and every rig pooled over
the periods: bites on / off in [0.8, 1.25] — the point estimate once both sides have 100 bites, else the 95 % interval
(log ratio ± 1.96·√(1/a + 1/b), half a bite added each side) must reach the band; the fish in reach pooled per rig in
[0.8, 1.25]; the bed on: no fish (wandering, coming, nibbling, biting, fleeing) in water shallower than it swims in.

**Regressions**: lake `fish`, `obstacles`, `legendspot` (natural), `encounter` (natural), `breaks`, `pan`, `steer`,
`hold`, `lure`; the stream's `steer` `[SIDELOG]` lines against the pre-change build (equal: the stream has no bed); the
sea's `tidebites` against the pre-change build (the counts equal when both reproduce).

---

## 15. Docs updated

`data_reference.md`, `fishing_gameplay.md`, `architecture.md`, `testing.md`, `art_pipeline.md` (Korean);
`obstacles_spec.md`, `time_currents_spec.md`, `lures_legend_spec.md`, `legends_rollout.md` (English).

---

## 16. Risks

1. **The 0.8–1.25 rule** is read as "a reference player, averaged over the fan", not per spot (§8). The spec's
   per-species gate (§8.2, 1) fails structurally with the 0.82–1.22 lever (§8.3): the user decides between a wider lever
   (stronger ecology on the long rods) and leaving it as is (the user's own rule, gate 2, holds).
2. **Recipe edits re-roll every save's lake** (the version is in the seed). Bump the version on every terrain change.
3. **Lake numbers move on purpose**: the lake's random order changes in PickTarget, Spawn, Place and ChooseSpot; pads and
   reeds now sit in about 1 m of water (cover runs end shallow; big carp skip reed holds); the weed bed's bottom is 2.4–3 m
   (crankbaits touch bottom there); the boat floats over 1.2–2.5 m.
4. **Legacy bit-identity depends on discipline**: every new branch behind `L.Terrain`, the old draw orders kept.
5. **Bamboo spot area**: a 1 m × 2–3 m patch of the narrow lane; V8, D10 and the re-place cover it.
6. **Readability**: the lying sprite is ~10 × 2.5 px at the smallest; the tilt, the ring, the gold label and the hint.
7. **New players will often see a lying float** (the 2.0 m default lies on the flats): the hint.
8. **Cross-platform float determinism**: trig-free generation and cm quantization; D1 catches drift.

---

## 17. As built: changes from the plan

1. **The slope limit** (§4.2 step 8). The planned 12 relaxed Jacobi passes left ~1000 edges over the limit on every
   attempt (steepest 3.8–4.1 m/m: a flat's rim drops 5 m over its 2–3.5 m feather; a reed core held at ≤ 1.2 m sits
   beside 6.8 m of water), and the cores' fixed values could be mutually unreachable. Now: the windows' envelopes first,
   then ≤ 60 Jacobi passes over the nodes near a violation (cores clamped into their window rather than fixed), then an
   exact closure. Every seed passes V5 at its first attempt. Pass B clamps the cores only: blending the rings again
   after the limit steepened their edges past it.
2. **Validation tolerances**: V1 ±0.0051 (the profile pin's 1.425 m rounds to 1.42 / 1.43); V5 in whole cm (≤ 76).
3. **The reach scale's clamp is the planned 0.82–1.22** and D11's raw gate the planned [0.67, 1.49] as its own constants.
   (A first build widened the clamp to 0.70–1.30 and derived the raw gate from it, so the gate could not fail, and the
   live soak still failed the rule: §17.1.) The estimator now simulates where the fish spend their time (§8.1); the
   habitat keeps every distance band's share (`RowKeep`, §7.3); the per-species gate fails structurally (§8.3) and the
   user's rule is gated on its own (§8.2, 2).
4. **Habitat strings** (§7.1): the spec's numbers did not produce D7's shifts (the carp's night on the flats ×1.09, the
   bass's dawn flats ×1.15) nor the bamboo's best spots (crucian, bluegill); stronger period weights and higher β
   (crucian 0.65, bluegill 0.75) do. With the occupancy estimator and `RowKeep`: the crucian's day drop-offs / shoals
   1.6 / 1.5; the bass's β 0.8, day humps / drop-offs / shoals 1.8 / 1.5 / 1.3, and its shallower dawn and evening held to
   −0.4 m (dawn flats / shelves 1.8 / 1.6): a topwater lure at dawn on the dragon rod is the user's rule's tightest case.
5. **best10** is the scaled per-cast ratio of the best 10 % of casts, for the species' best rig class; D11 counts only the
   (species, rig class) pairs some bait of that class draws (a crucian never takes a topwater lure). Its floor is 1.2
   (§8.2, 3).
6. **The lying sprite is 32 × 7** (28 cut its tip off: it sits 12.9 px out), and the thin top is twice as thick in the
   lie / tilt frames (laid over, the 0.03 tube broke into dots).
7. **`HabitatModel.cs`** holds the pure math so the generator and the estimator were swept outside Unity (a .NET harness,
   not in the repo) before the player runs.
8. **Noise salts vary per attempt** (a blob's rim, the flats' texture, the materials), so every seed's noise differs.
9. **Agents already in too little water** (dragged there in a fight) may still move to deeper water.
10. **Build time**: ~70 ms per attempt in the Mono player (target 30 ms), once per stage and seed at a load.

### 17.1 The live A/B soak

**First build** (12 central spots, 75 s each, the target-density estimator, the clamp 0.70–1.30): 떡밥 2 m 142 / 120
bites (×1.18; dawn ×0.59, day ×1.52, night ×1.73), 옥수수 4 m 33 / 20 (×1.65), 지렁이 1 m 198 / 178 (×1.11); the fish
in reach ×1.49 / ×1.08 / ×0.78; the estimator against the live in-reach ratios r = 0.72. Failed the rule.

**This build** (the occupancy estimator, `RowKeep`, the clamp 0.82–1.22; 70 spots over the fan × 60 s = 4200 game s per
cell; eight processes, one per mode and period, `[HAB] cell` lines pooled with the test's rule):

| Rig | Bites on / off (pooled) | Ratio | Fish in reach | Single periods (dawn / day / evening / night) |
|---|---|---|---|---|
| 떡밥 2 m | 172 / 189 | ×0.91 (point) | ×0.98 | 29/38 ×0.77, 52/60 ×0.87, 27/16 ×1.67, 64/75 ×0.85 (all under 100 bites: the 95 % intervals reach the band) |
| 지렁이 1 m | 678 / 651 | ×1.04 (point) | ×0.91 | ×0.96, **×1.30** (179 / 138), ×0.99, ×0.97 |
| 옥수수 4 m | 0 / 6 | — | — | no fish within reach of the 4 m corn rig in 7 of 8 cells, either mode (6 bites at dawn off): uninformative |

The bed on: 0 frames of a fish (wandering, coming, nibbling, biting, fleeing) in water shallower than it swims in.
Open: the worm rig by day is ×1.30 on 179 / 138 bites (≈ 2.3 σ over 1.0; its fish in reach ×0.73 the other way), and the
corn rig gets too few fish over the whole fan to measure. D11's per-species gate fails structurally (§8.3). The rates
over the whole fan are a quarter of the first build's central spots (paste 0.2–1.1 / min), so a cell needs ~4 game hours
for 100 bites.

Regressions on this build: lake `fish`, `obstacles`, `legendspot`, `encounter` (natural), `breaks`, `pan`, `steer`, `hold`,
`lure`: 0 failed. The stream's `steer -fksidelog` against the pre-change build: the `[SIDELOG]` lines are not equal, but
two runs of this same build differ as much (463 of ~530 lines; the steer test is not on a fixed step), and the three
checks that fail on the stream (the hat graze, the edge sweep, the bent 회수) fail on the pre-change build too.
