# 낚시왕 — Obstacles: casts, snags and cover fights (spec v1)

Status: implementable spec for the user request "장애물 추가" with the decisions below. The painted props of every stage
(rocks, pier posts, lily pads, reeds, logs, stumps, roots, tetrapods, the boat, crystals / pillars) become game objects:
a flying cast hits the ones above the water and bounces off them, a frog lure sits on a lily pad, lures and rigs snag on
what lies under the water ("밑걸림!"), and in a fight cover-seeking fish run for their cover, where the line rubs and
wears. Positions come from the Blender stage scenes, exported into game space, so they match the painted props to the
pixel (the stream is exported and proven: section 13.5).

Units: game space is metres (x right, y up, water surface y = 0, z forward from the angler's feet); plan vectors are
`(x, z)`. Canvas pixels are the 640x400 stage canvas (x right, y DOWN from its top; a stage rendered with overscan is
wider, the sea's 800x400: its `widthPx`, the same camera with 80 more columns each side, the 640 layout at its centre);
"RT px" is the 480x270 render target. Times are real seconds unless marked. Probabilities are per event unless marked
"/m" (per metre of travel) or "/s".

Constraints kept: the reel UI stays fixed at the bottom right; casting stays drag down + flick up; the lure 톡 stays a
short downward pull; freeing snags and pulling fish out of cover use the existing rod sweep / side pressure and the 톡
(no new gesture); normally the screen shows just the painted art (no new stage renders, no outlines outside aiming);
no unrequested polish or QA rounds — one capture set (section 14).

---

## 0. Decisions

1. **Obstacles are data**: `Assets/Resources/Data/obstacles_<stage>.json`, written by the Blender exporter
   `Tools/Blender/variants/hybrid/hyb_obstacles.py` from the very scenes that paint the stage (same builder functions,
   same seeds, nothing rendered). A stage without the file (or with `-fkobstacles off`) plays exactly as today. The four
   time-of-day looks share one file (same geometry in every period: `Docs/time_currents_spec.md` 3.1 rule 1).
2. **Six kinds** (section 1): `solid` (an above-water collider: rocks, posts, trunks, tetrapods, the boat, crystals,
   walls, overhanging branches), `pad` (a floating landing surface), `snag` (underwater hard snags: the bodies of rocks
   under the surface, sunken logs, roots, tetrapod fields, rubble), `weed` (weed beds, reed beds), `cover` (a fish
   hideout with a hold point), `rim` (the ice hole's edge).
3. **Casts collide** (section 4): the flying rig hits solids and bounces with the material's restitution; a bounce that
   drops the rig within 2 m of the struck prop (or under an overhanging branch) is a **bank shot** — a natural entry that
   draws fish (x1.3 for 8 s). A rig that comes to rest on a prop's top or on the bank **perches** until a 톡 or a wind
   drops it into the water.
4. **Lily pads** (section 5): the frog lure lands on a pad and sits (weedless: drag it across, drop it off the edge for
   the strike); a popper sits too; other lures slide off the edge (20 % catch the pad); natural baits catch the pad and
   stay until pulled free (톡 / sweep; winding tears the bait off). The line never breaks on a pad.
5. **Snags "밑걸림!"** (section 6): the hook catches in snag / weed zones by chance (rig type x zone x depth x speed).
   It frees with the rod sweep to the free side (the side arrow shows it after a miss) or a 톡 (a slack line first
   helps). Winding against it builds tension until the line breaks and a lure is lost; the `회수` button becomes `끊기`
   (cut the line). The frog is weedless over weeds, reeds and pads; the soft worm nearly so.
6. **Cover fights** (section 7): 23 cover-seeking species (table 7.1, incl. 큰입배스, 가물치, 강꼬치고기, 피라루쿠) may
   turn a run towards their cover. While the line rubs on abrasive structure (or the fish grinds it on a rock's base) an
   **abrasion meter** fills by the structure, the tension, the fish's motion and the line; a scuffed line is weaker
   (strength x (1 - 0.45 A)) and breaks at A = 1. Side pressure away from the cover (the existing side arrow) turns the
   run and pulls the fish out ("커버에서 끌어냈다!").
7. **Bites near structure** (section 8): a bait in a cover zone gets x1.35 (cover species) / x1.15 (others), inside a
   snag zone x1.1; the stream pocket's x1.3 is not stacked (the larger wins). Cover species also wander near their covers.
   The risk is the snags and the rubbing.
8. **Display** (sections 9, 10): the painted art only. While aiming a cast (the wind-up), faint dotted outlines of the
   **underwater** obstacles (snag and weed zones) in cast range; nothing for solids / pads (they are painted). In a fight:
   warning texts, an abrasion meter in the fight strip, tiny sparks where the line rubs.
9. **Save: none.** No new save fields (every hint is contextual, shown each time it applies).
10. **Test switches** (section 12): `-fkobstacles show|off`, `-fkobstlog`, `-fksnag <x>`, `-fkauto obstacles`.
11. **Blender** (section 13): the shared exporter + one module per stage `obstacles/<stage>.py` (one owner each, like
    `periods/`). The stream is exported, installed and verified: the export lands on the painted rocks within the
    stage's 1 px outline (0 unexplained pixels); the camera maths agree to 0.0001 px.

---

## 1. Kinds and materials

### 1.1 Kinds

| kind | what | y range (`bot`..`top`) | used by |
|---|---|---|---|
| `solid` | a painted prop above the water, as 1-4 stacked convex prisms (`tiers`); `bed` = it goes on down to the bed (posts, trunks) | 0 (or above: an overhang) .. its top | casts (4), perches (5.4), the rig's drift / sweep / wind (4.9), fights if `abrasive` and standing in the water (7.4) |
| `pad` | a floating lily pad (convex, usually a circle) | ~0.01 .. 0.03 | cast landings (5) |
| `snag` | an underwater hard snag zone: a painted prop's body under the surface (`<id>.skirt`), sunken logs, roots, tetrapod fields, rubble, the ocean hull | the bed (`-99`) .. its top (< 0) | snags (6), fights if `abrasive` (7.4), aim outlines (9), bites (8) |
| `weed` | a weed bed or a reed bed (`top` >= -0.1: reaching the surface) | the bed .. its top | snags (weed column, 6.2), aim outlines, bites |
| `cover` | a fish hideout: the zone round a structure + the hold point (`hx`, `hz`) where a fish digs in | the bed .. -0.2 | cover runs (7), wandering holders and bites (8) |
| `rim` | the ice hole's edge (a circle) | -0.4 .. iceY | ice fights (7.4) |

### 1.2 Materials (`mat`)

`e` = restitution of the normal speed, `keep` = the part of the tangential speed kept at a contact, `grab` = snag
factor (6.3), `rough` = abrasion factor (7.5), `freeK` = how readily a snag there comes free (6.5). Per-obstacle
multipliers `grabK` / `roughK` come from the JSON (default 1).

| mat | used for | e | keep | contact sound / word | grab | rough | freeK | name in texts |
|---|---|---|---|---|---|---|---|---|
| `rock` | boulders, cave rock, walls, islets | 0.45 | 0.55 | Tock / `탁!` | 1.0 | 1.0 | 1.0 | 바위 |
| `concrete` | tetrapods | 0.40 | 0.50 | Tock / `딱!` | 1.4 | 1.3 | 0.7 | 테트라포드 |
| `wood` | posts, stakes, logs, stumps, the boardwalk, trunks | 0.30 | 0.50 | Knock / `톡!` | 1.2 | 0.7 | 0.8 | 말뚝 (tag `post`), 통나무 (`log`), 그루터기 (`stump`), 나무 (else) |
| `root` | cypress knees, root tangles | 0.25 | 0.45 | Knock / `톡!` | 1.3 | 0.8 | 0.7 | 뿌리 |
| `hull` | the lake's rowboat, the ocean hull, the buoy | 0.35 | 0.60 | Thunk / `퉁!` | 0.3 | 0.6 | 1.2 | 배 (the ocean: 배 밑) |
| `crystal` | crystal clusters | 0.55 | 0.70 | Ting / `쨍!` | 0.8 | 1.6 | 1.0 | 수정 |
| `leaf` | crowns, limbs, moss drapes (overhangs) | 0.10 | 0.20 | Rustle / `사삭` | - | 0.2 | - | 나뭇가지 |
| `reed` | reed clumps (weed kind) | - | - | Rustle | 0.9 | 0.3 | 1.3 | 갈대 |
| `weed` | weed beds | - | - | - | 1.0 | 0 | 1.4 | 수초 |
| `pad` | lily pads | - | - | Tear on a pull | - | 0 | 1.6 | 연잎 |
| `ice` | the hole rim | 0.30 | 0.60 | - | 0 | 0.9 | - | 얼음 구멍 가장자리 |
| `gravel` | rubble / boulder beds on the bottom | - | - | - | 0.8 | 0.6 | 1.1 | 돌바닥 |

The table lives in C# (`ObstacleMat.Get(mat)`, section 2.5); the JSON carries only `mat` and the two multipliers.

### 1.3 Not obstacles

Grass tufts, duckweed, flowers, the angler's own ground (his boulder / pier / deck / ledge / the ice he stands on),
props on land or on the ice that no cast or fight can reach, the far layers beyond the fishable water, the cave's vault
and stalactites (the stylised cast arc may pass above the view), the ocean's bow rails (the cast leaves the rod tip
beyond them). The exporter drops anything whose footprint and zones never come within |x| <= xLim + 0.3,
zNear - 1 <= z <= zFar (13.3 step 4).

---

## 2. Data

### 2.1 File

`Assets/Resources/Data/obstacles_<stage>.json`, loaded with `Art.Data<ObstacleSet>("obstacles_" + id)`. Written only by
the exporter (never edited by hand: re-export instead). Coordinates are game space, rounded to 1 mm.

### 2.2 Top level

| field | type | meaning |
|---|---|---|
| `version` | int | 1 |
| `stage` | string | the stage id |
| `generator`, `source` | string | `hyb_obstacles.py`; the builders it ran, e.g. `hyb_stream.py render_front(Random(31))` |
| `camera` | object | `standH, camBack, camUp, pitch, focalPx, widthPx, heightPx, xLim, zNear, zFar` of `stage_<id>.json` at export time |
| `obstacles` | array | the entries, sorted solid, rim, pad, snag, weed, cover, then by z |

The loader compares `camera` with the stage's `StageLayout`; any difference > 0.001 logs
`[OBST] obstacles_<id>.json was exported for another camera: re-export` (the data still loads).

### 2.3 An obstacle

| field | type | meaning |
|---|---|---|
| `id` | string | unique in the stage, e.g. `md25.0`, `md25.0.skirt`, `md25.0.cover` |
| `kind` | string | 1.1 |
| `mat` | string | 1.2 |
| `shape` | string | `circle` (`x, z, rad`), `capsule` (`x0, z0` - `x1, z1`, `rad`), `poly` (`pts`); the footprint's best fit |
| `x`, `z`, `r` | float | the footprint's centroid and bounding radius (broad phase) |
| `rad`, `x0`, `z0`, `x1`, `z1` | float | circle / capsule parameters (0 for a poly) |
| `pts` | float[] | the footprint at the waterline (solids) or the zone's plan outline, convex, counter-clockwise, flattened `x0, z0, x1, z1, ...`, <= 16 points; present for every shape (a circle's code may ignore it) |
| `bot`, `top` | float | the y range; `bot = -99` means "down to the bed" (y = -`StageLayout.DepthAt(z)`, tide included) |
| `bed` | bool | solids: the prop goes on down to the bed (a post, a trunk) |
| `tiers` | array | solids only: `{y0, y1, pts}` stacked convex prisms (pts as above, <= 12 points), bottom to top; a point is inside the solid if it lies in a tier's y range and inside its polygon |
| `grabK`, `roughK` | float | multipliers of the material's `grab` / `rough` (default 1) |
| `tags` | string[] | `abrasive` (rubs a line: 7.4), `rock`, `post`, `log`, `stump`, `trunk`, `tet`, `hull`, `crystal`, `reed`, `overhang`, `near` (at his feet: casts never reach it), `pocket` (a stream rock with a slack pocket: CurrentField), `bank`, `midstream`, `wall` |
| `coverFor` | string[] | cover entries: the cover types this zone counts as (`rock`, `post`, `pad`, `reed`, `weed`, `boat`, `log`, `root`, `tet`, `rim`, `hull`, `crystal`) |
| `hx`, `hz`, `holdCross` | float, bool | cover entries: the hold point; `holdCross` = the line from the angler to it crosses the parent's footprint (else the hold point lies in the parent's skirt) |
| `layer` | string | `front` / `back`: the painted layer it belongs to (the ocean's front layer bobs: 9.3) |
| `parent` | string | derived zones: the painted obstacle they belong to |
| `src` | string[] | the Blender object names (debugging) |

Example (the stream's second mid-stream boulder, abridged):

```json
{"id":"md25.0","kind":"solid","mat":"rock","shape":"poly","x":6.31,"z":24.971,"r":1.051,"pts":[5.317,24.998,5.411,24.481,...],
 "bot":0.0,"top":0.522,"bed":false,"tiers":[{"y0":0.0,"y1":0.174,"pts":[...]},{"y0":0.174,"y1":0.348,"pts":[...]},{"y0":0.348,"y1":0.522,"pts":[...]}],
 "grabK":1.0,"roughK":1.0,"tags":["rock","abrasive","midstream","pocket"],"layer":"front","parent":"","src":["Mid","MidMoss"]}
{"id":"md25.0.skirt","kind":"snag","mat":"rock","bot":-99.0,"top":-0.35,"pts":[...],"tags":["rock","abrasive"],"parent":"md25.0"}
{"id":"md25.0.cover","kind":"cover","mat":"rock","bot":-99.0,"top":-0.2,"pts":[...],"coverFor":["rock"],"hx":6.582,"hz":26.045,"holdCross":true,"parent":"md25.0"}
```

### 2.4 Conventions

- Plan tests use the `(x, z)` of a point; the y range with `bot = -99` resolves to the bed at that point's z.
- Where the game draws something under the water it uses `Persp.Apparent` (the aim outlines, the rub sparks).
- `pts` / tier polygons are convex; point-in-polygon = all edge cross products >= 0 (CCW).
- An obstacle is ignored by the cast test if tagged `near` (the rod tip is above and beyond it).

### 2.5 C# (U1)

New file `Assets/Scripts/Fishing/Obstacles.cs`:

```csharp
[Serializable] public class ObstacleTier { public float y0, y1; public float[] pts; }
[Serializable] public class Obstacle
{
    public string id, kind, mat, shape; public float x, z, r, rad, x0, z0, x1, z1; public float[] pts;
    public float bot, top; public bool bed; public ObstacleTier[] tiers; public float grabK = 1f, roughK = 1f;
    public string[] tags, coverFor; public float hx, hz; public bool holdCross; public string layer, parent; public string[] src;
    // runtime (built on load): Vector2[] Poly; Vector2[][] TierPoly; ObstacleMat Mat; bool Abrasive, Near;
}
[Serializable] public class ObstacleCamera { public float standH, camBack, camUp, pitch, focalPx, xLim, zNear, zFar; public int widthPx, heightPx; }
[Serializable] public class ObstacleSet { public int version; public string stage, generator, source; public ObstacleCamera camera; public Obstacle[] obstacles; }

public struct ObstacleMat { public float e, keep, grab, rough, freeK; public string word, name; public Color fx; public static ObstacleMat Get(string mat); }

public class Obstacles
{
    public static Obstacles Load(StageLayout L);          // null file / -fkobstacles off -> Empty (every query false / 1)
    public static bool Off, Show, Log; public static float SnagMult = 1f;   // switches (12)
    public IReadOnlyList<Obstacle> Solids, Pads, Snags, Weeds, Covers, Rims;
    public float Bed(float z);                            // -L.DepthAt(z)
    public bool Inside(Obstacle o, Vector2 xz, float grow = 0f);   // footprint (+ grow m)
    public bool InSolid(Vector3 p, out Obstacle o, out int tier);
    public bool SweepSolid(Vector3 a, Vector3 b, out ObstacleHit hit); // first contact on the segment (4.3)
    public Obstacle PadAt(Vector2 xz);
    public Obstacle ZoneAt(Vector3 p, bool snag, bool weed); // snag / weed zone whose band holds p.y (the hook)
    public bool BlockedAtSurface(Vector2 xz, float grow = 0.05f); // inside a standing solid's waterline footprint
    public bool Rub(Vector2 entry, Vector3 fish, out Obstacle o, out Vector3 at); // 7.4
    public Obstacle NearestCover(Vector3 fish, FishSpecies sp, float reach); // 7.2
    public float StructureMult(Vector3 hook, FishSpecies sp);  // 8.1
}
public struct ObstacleHit { public Obstacle o; public int tier; public Vector3 at, normal; public bool top; public float t; }
```

`StageView` loads it (`StageView.Obstacles`, next to `Water` and `Current`). All queries first reject by the bounding
circle (`r` + margin). Stages have <= ~200 entries; no spatial index is needed.

---

## 3. What each stage exports (Blender modules)

### 3.1 Common rules

- Everything painted **in or over the fishable water** that a cast or a line can meet is an obstacle; its underwater
  body (the painted props are clipped at the waterline) is a `snag` skirt round the waterline footprint; structures
  fish hide in get a `cover` zone. Helpers (never painted: sunken logs, weed beds, rock piles, the ocean hull) are made
  by the module's `extra()` and must sit **relative to painted props or in open water under the surface** — they are
  only ever seen as the aim outlines.
- Default sizes: boulders `skirt` 0.5 m (mid-water) / 0.35 m (bank), `cover` 1.6 / 1.1 m; posts `skirt` 0.3 m,
  `cover` 0.8 m; trunks `skirt` 0.8 m (roots), `cover` 1.5 m; tetrapods `skirt` 0.8 m, `cover` 1.2 m; pads `weed`
  0.8 m (`weed_top` -0.4). Skirt tops -0.35 (surface lures pass over); cover tops -0.2.
- Solids: `tiers` 3 for boulders and domes (4 for big ones close to the camera), 1 for posts / trunks / walls, 2 for
  crowns. The check (13.5) wants, for solids in the play area, painted-vs-export edge <= 1 px (the outline) and collider
  IoU >= 0.7.
- A cover whose hold point a hooked fish can never reach (the fight keeps fish within |x| <= xLim - 0.3,
  zNear + 0.2 .. zFar - 1) is not written (the exporter logs it).

### 3.2 stream — done (`obstacles/stream.py`)

| Blender (front layer) | kind / mat | tags | tiers | skirt / cover | count in the JSON |
|---|---|---|---|---|---|
| `Mid*` (+ `MidMoss`), grp `md<z>` | solid rock | rock, abrasive, midstream, **pocket** | 3 | 0.5 / 1.6 | 5 (+5 skirts, 5 covers, all `holdCross`) |
| `Bank*` (+ moss), grp `bk<side><z>` | solid rock | rock, abrasive, bank | 3 | 0.35 / 1.1 | 11 (+11 skirts, 3 covers in the skirt; 4 more lie outside the channel: dropped) |
| `Flank*`, grp `fl<x>` | solid rock | rock, abrasive, near | 4 | 0.25 / - | 3 (+3 skirts) |
| `AnglerRock` | not an obstacle | | | | |

46 entries (19 solid, 19 snag, 8 cover). The five `pocket` rocks agree with `CurrentField.Rocks` within 0.01-0.05 m
(the hard-coded pockets may stay; U3 may read them from the tag later).

### 3.3 lake (owner B1)

| Blender (front) | kind / mat | tags | notes |
|---|---|---|---|
| `Post` + `RopeWrap` grp `po<x><z>` at (+-1.38, 0.95) | solid wood, bed | post, abrasive, near | the two pier-end posts in the water (the four under the pier are behind him: `where` z > 0); `cover` 0.8 `post` |
| `Stake` (-10.4, 13.0) | solid wood, bed | post, abrasive | `cover` 0.8 `post` |
| rowboat: kind `boat` (Hull, Gunwale, Thwart, Oar, Blade) grp `boat` | solid hull | hull, abrasive | tiers 2 (the hull top 0.5 m); a rig landing inside it perches (5.4); `skirt` 0.4 (keel), `cover` 1.2 `boat` |
| `MoorRope` | not an obstacle | | |
| pads: kind `pad`, 12 (4 clusters, x +-6..13, z 11..28) | pad pad | | `weed` 0.8 (`weed_top` -0.4) per pad; a cover per cluster: helper or `cover` 1.2 `pad` on the middle pad |
| reeds: kinds `reed` / `cattail` (4 clumps: (-5.6, 3), (6.6, 5.2), (-12.5, 17), (14.5, 28)) | weed reed | reed | `top` -0.05, `bot` -99; `cover` 1.0 `reed` |
| helpers | | | a sunken log on the bed in open water, e.g. (-4.5, 21) - (-1.8, 23.5), `top` = bed + 0.4 (snag wood, tag log, abrasive, cover `log`); a submerged weed bed centred (3.0, 14.0) r 2.2, `top` -1.2 (weed; the crankbait dives into it) |

### 3.4 sea (owner B2)

| Blender (front) | kind / mat | tags | notes |
|---|---|---|---|
| tetrapods: `Leg` x4 + `Hub` per unit, kind `tet`, grp per unit (34 units, two mounds x +-3.3..12, z -0.5..14.4) | solid concrete | tet, abrasive | tiers 3; `skirt` 0.8, `cover` 1.2 `tet` (the classic 우럭 / 감성돔 hole) |
| buoy (17, 44): Float, Body, Band, Mark, Lamp | solid hull | hull | tiers 3; no cover |
| helpers | | | the underwater tetrapod field along both mounds' toes: a snag concrete band per side, e.g. x from +-3.0 to +-12.5, z 0.5..16, `top` -1.0, abrasive, `cover_for` tet (x1.4 grab from the mat) |
| Deck, Kerb, bollard, cooler, bucket, the far mole / lighthouse | not obstacles | | |

### 3.5 swamp (owner B1)

| Blender (front) | kind / mat | tags | notes |
|---|---|---|---|
| cypress `Trunk` + `Rib` (kind `trunk`, grp `T0..T4`) | solid wood, bed | trunk, abrasive | tiers 1, `top` 3.8 (the trunk up to the crown; the crown is its own obstacle); `skirt` 0.8 (the roots), `cover` 1.5 `root` |
| cypress crown: `Limb`, `Pad` + `Sprig` with kind `trunk` (not the lily pads: kind `pad`), grp `T*` | solid leaf | overhang | `id="{grp}.crown"`, tiers 2; `bot` ~3.8 m: casts under it pass, casts into it drop (4.8) |
| moss: `Drape`, `Moss` (kind `moss`, grp `T*m`) | solid leaf | overhang | `id="{grp}.crown"` (-> `T0m.crown`: its own overhang, hanging down to ~2.1 m) |
| knees: `Knee` (kind `knee`, 23) | solid root | root, abrasive | tiers 1; `skirt` 0.3 |
| dead snag S0: `Snag`, `SRib`, `Branch` | solid wood (branches: leaf) | trunk, abrasive | as the trunks |
| `Log` + `LogMoss` + `Stub` (8.7, 16.6) - (11.7, 18.9) | solid wood | log, abrasive | shape capsule; `skirt` 0.4, `cover` 1.2 `log` |
| `Post` (boardwalk) at (+-1.3, 0.8) | solid wood, bed | post, abrasive, near | `cover` 0.8 `post` |
| pads (kind `pad`, 12) | pad | | `weed` 0.8 (`weed_top` -0.4); snakehead / arapaima cover `pad` |
| reeds (4 clumps) | weed reed | reed | `top` -0.05; `cover` 1.0 `reed` |
| helpers | | | two sunken logs in the open channel (snag wood, `top` -1.5, cover `log`), a root tangle under each far trunk (the skirts do it) |

### 3.6 ice (owner B3)

| Blender | kind / mat | notes |
|---|---|---|
| back: `Funnel` (kind `rim`) | rim ice, `bot` -0.4 (ice thickness under the water line), `top` iceY | exports as the circle (holeX, holeZ, holeR) = (1.9, 3.0, 0.62): the exporter fits the funnel's bottom ring |
| helper | snag gravel under the hole: circle r 1.5 at the hole, `bot` -99, `top` = bed + 0.3 | the jig / egi / soft worm touching the bottom under the hole |
| helper | snag rock pile (5.0, 8.0) r 2.0, `top` = bed + 0.8 | cover `rock` for 모캐 |
| drifts, shanties, auger, bucket | not obstacles | on the ice |

### 3.7 ocean (owner B2)

| Blender | kind / mat | notes |
|---|---|---|
| helper: the hull under the bow | snag hull, abrasive, `bot` -1.4, `top` 0 | plan = `hyb_ocean.outline(inset=0)` (the gunwale outline, Blender x, y); `skirt` 1.5 (1.0 leaves the hold point short of the fight's limits: no cover), `cover` 1.2 `hull` (the fish diving under the bow) |
| helper `reef`: a submerged rock pinnacle (수중여) left of the bow | snag rock, abrasive (tags rock, abrasive, reef), `bot` -99, `top` -2.2 | an irregular 12-gon round (-6.5, 16.0), 2.4 x 2.1 m (x -7.63..-5.25, z 14.96..17.04); `cover` 1.5 `rock` (hold (-7.04, 17.34), behind it: the line crosses the rock) |
| helper `kelp`: a drifting mat of sargassum weed (모자반) right of the bow | weed weed (tags weed, kelp), `bot` -1.4, `top` -0.05 | an irregular 16-gon round (6.2, 11.5), 4.9 x 3.0 m (x 3.75..8.67, z 9.99..13.04); `cover` 1.0 `weed` (hold (7.00, 13.02), behind it) |
| deck, bulwark, rails, cooler, rod, coil | not obstacles | the cast leaves the rod tip beyond the bow |

The hull and its zones lie under the painted bow (z <= 2.7): the aim outlines (sorted under the front layer, 9.3) can
never show them, so the ocean gets its open-water structure from `reef` and `kelp`, 11-17 m out (within every rod's
cast + 2 m), off the straight-ahead lane (|x| >= 3.75) and short of the ocean legends' lurk band (z 20-45, 6 m deep:
청새치 / 백상아리 lurk points and encounters are untouched). What they do:

- **reef**: sinking lures (spoon, jig, egi) and float rigs set deeper than 2.2 m snag on it (`밑걸림!`); a crank (runs
  to 2.5 m) grazes its top (depthF 0.5, the 딱! deflection 6.8); surface lures, the minnow and a kona run fast pass
  over (a kona left to sink does not). A 방어 (cover `rock`, added to its `hull`) holds on it: bites there x1.35, cover
  runs to it, and the line rubs on the rock (`줄이 바위에 쓸린다!`).
- **kelp**: reaches the film, so surface lures and the kona catch it too (`수초에 걸렸다!`; soft: a sweep either way
  frees it), the frog is weedless. 만새기 (new: seek 0.45, reach 12, dig 1.0, `weed`) and 참다랑어 (`hull, weed`) hang
  under it: bites x1.35, cover runs into it (not abrasive: the fish digs in, the line is not worn).
- The ocean current (drift) moves rigs as before; a snagged rig stays fixed at its snag point (6.4).

A structure on the bed (18-20 m down here) does not read: the outline is drawn at the zone's top refracted
(`Persp.Apparent`), and 16 m down that puts it ~10 m nearer on screen than it lies. The exporter's ocean check prints,
per new zone, its outline projected with the JSON camera against the painted bow: reef 128 / 128 samples on screen and 0
on the bow, kelp 176 / 176 and 0 (the covers, never outlined while aiming, touch the painted rod / pulpit rail at a few
samples in `-fkobstacles show` only).

### 3.8 cave (owner B3)

| Blender | kind / mat | tags | notes |
|---|---|---|---|
| front `Stalag` (6, kind `stalagmite`) + `Base` boulders (6) at (-7.6, 9.7) / (8.3, 12.7) | solid rock | rock, abrasive | tiers 3 (spires 1); `skirt` 0.5, `cover` 1.4 `rock` |
| front crystals `Cry` (grp `fc<i>`; the ones at z > 5) | solid crystal | crystal, abrasive | tiers 2; `cover` 1.0 `crystal` |
| front `Chunk5..10` (boulders by the ledge) | solid rock | rock, abrasive, near (z < 2.5) | `skirt` 0.4 |
| back `Wall*` (kind `wall`: the side walls close in at x -9.3 / +10.7 up to z 32) | solid rock | rock, wall, abrasive | tiers 1; no cover |
| back `Column` (15.5, 77) (the other is outside xLim), `Islet` x5, back `Cry` within reach | solid rock / crystal | abrasive | `skirt` 0.6, `cover` 1.5 `rock` / `crystal` |
| `Stal`, `Ceil*`, the ledge `Chunk0..4`, mushrooms, lantern | not obstacles | | |
| helper | snag rock ridge on the bed across (-3, 30) - (4, 36), `top` = bed + 1.0 | | cover `rock` for 실러캔스 / 초롱아귀 |

---

## 4. Casts: collision and bounce (U2)

### 4.1 When

Every frame of `Tackle.Mode.Flying` (not on the ice: the jig drops straight into the hole). The flight is unchanged
until the first contact: `p(t) = lerp(from, to, t)` with `y = lerp(from.y, 0, t) + flyArc 4t(1 - t)`,
`flyDur = 0.45 + 0.022 d`, `flyArc = 1.2 + 0.28 d`.

### 4.2 Velocity

Before a contact (analytic): `v = ((to - from).x / flyDur, ((to.y - from.y) + flyArc 4(1 - 2t)) / flyDur,
(to - from).z / flyDur)`. After one (ballistic): integrated with `g = clamp(-8 flyArc / flyDur^2, -90, -25)` m/s^2 (the
arc's own gravity, so the bounce keeps the cast's feel), no drag.

### 4.3 Contact test

`SweepSolid(p_prev, p_now)`: sub-steps of <= 0.15 m; for each sub-point `q`, every solid not tagged `near` whose
bounding circle (`r` + 0.05) holds `q.xz` and whose `bot..top` holds `q.y`: find the tier with `y0 <= q.y <= y1` and
`q.xz` inside its polygon. The first hit is refined by 4 bisection steps between the previous sub-point and `q`.
Normal: if the previous sub-point was above the tier's `y1` and inside its polygon, a **top** contact, `n = (0, 1, 0)`;
else a **side** contact, `n` = the outward normal (horizontal) of the tier edge nearest `q.xz` (circle: radial; capsule:
from the nearest point of its segment).

### 4.4 Response

`vn = (v . n) n`, `vt = v - vn`; `v' = -e_c vn + keep_c vt` with `e_c = e`, `keep_c = keep` for a side contact and
`e_c = 0.5 e`, `keep_c = 0.7 keep` for a top contact; `|v'|` capped at **8 m/s** (a light rig loses most of its energy:
a bounce carries it at most ~2 m); the rig restarts 0.02 m outside the contact point along `n`. At most 3 contacts per
cast. The rig **stops** when `|v'| < 1.5 m/s` or on the third contact:

- stopped on a top contact -> it **perches** on the prop (5.4);
- stopped on a side contact -> it slides down the face: falls straight down 0.02 m outside the face to the water over
  0.25 s (a natural entry: 4.8 applies).

### 4.5 Where it lands

When the rig reaches `y <= 0` at `at`:

1. `at.xz` inside a `pad` footprint -> 5.1-5.3 (after a bounce too);
2. not a valid water point -> it perches **on the bank** at `at` (5.4). Valid water: `|x| <= xLim - 0.6`,
   `zNear + 0.3 <= z <= zFar - 1`, `WaterFx.DriftOpen(x, z)` (in view, not behind the front layer) and not inside a
   standing solid's waterline footprint;
3. else the rig enters the water at `at` (not at the aimed target): the existing landing (`OnLanded`) with
   `Tackle.EnterWater(at)`; the splash / ripple / plop at `at`, at **x0.5 size and volume** after a bounce (it dropped,
   it was not thrown).

`Tackle.Launch(from, to, Action<Landing> landed)` replaces the `Action` callback; `Landing { Vector3 at; Obstacle
struck, pad, perch; bool bank, natural; int contacts; }`. `FishingController.OnLanded(Landing)` uses `at` everywhere it
used `aimTarget`.

### 4.6 Contact FX and sound

At each contact: the material's sound (10.3) at volume `clamp(|vn| / 12, 0.3, 1)`; its word popped at the contact point
(`hud.LureFeedback(word, UIKit.Cream, 0.6 s)`, e.g. `탁!`); a puff of 3-5 px in the material colour (`Fx.Puff`: rock
`#a0a098`, concrete `#c8c8c0`, wood / root `#8a6a48`, hull `#e8e8e8`, crystal: `Fx.Sparkle #aef0ff` x3, leaf: 3 leaf
pixels `#6a8a4a` falling 0.6 s). The line keeps drawing to the rig (no line-wrap visuals).

### 4.7 Bank shot

A rig that entered the water after at least one contact with a solid not tagged `overhang`, within **2.0 m** (plan
distance to that solid's footprint) of it, is a bank shot: flash `뱅크샷!` (`UIKit.Gold`, 1.0 s) and the natural-entry
bonus (8.3). A cast that merely lands near a prop without touching it is not one.

### 4.8 Overhangs

Solids tagged `overhang` (the swamp's crowns and moss) have `bot` well above the water: an arc passing under them flies
on; one that meets them hits leaves (`e` 0.10, `keep` 0.20) and drops almost straight down. Entering the water inside
the overhang's footprint: flash `가지 아래로 쏙!` (`UIKit.Gold`, 1.0 s) and the natural-entry bonus (the shade under the
branches is a classic spot).

### 4.9 A rig in the water never enters a prop

`Tackle.StepCurrent`'s `Ok()`, `Tackle.Sideways` (sweep drag, the 톡's pull and dart) and `Tackle.Wind` treat a
standing solid's waterline footprint (grown 0.05 m) as out of the water: the rig stops against it and slides along it
(the existing axis-split fallback); winding a rig that is against a prop bends it round the prop (project out along the
footprint's normal, keep the wound distance). A float rig drifting into a rock stops at it and swings round.

---

## 5. Landing surfaces: pads and perches (U2)

### 5.1 The frog on a pad

The frog (`bait_frog`) landing in a pad footprint **sits on the pad**: `Tackle.OnPad = pad`, depth 0, drawn on the pad
(film sprite at `y = pad.top`), no drift (the current / wind do not move it), no snag ever (weedless).

- Winding: it crawls across the pad towards the angler at 0.7 x the wound distance (pad friction); its rhythm (the
  frog's `runMax` 2 s, then a pause) counts as usual.
- A 톡: it hops 0.35 m towards the angler.
- Off the edge (its point leaves the footprint): `퐁` — a small splash, `Sfx.Plop` 0.5, and a **pad-drop window** of
  1.2 s: strike rolls of following fish x1.5.
- Sitting still on the pad for >= 1.5 s with a topwater-liking fish (`actionPrefs[Topwater] >= 0.5`) within 4 m: that
  fish may strike **through the pad** at 0.35 x its normal strike chance; the bite shows as a big splash and the pad's
  wobble (a 2-frame 1 px shift of a ripple ring), flash `연잎을 뚫고 덮쳤다!` (`UIKit.Gold`, 1.0 s) with the usual
  `쑥! 지금 탭해서 챔질!`.
- Flash on landing: `연잎 위에 앉았다!` (`UIKit.Gold`, 1.0 s).

### 5.2 Other lures on a pad

| lure | on the pad |
|---|---|
| popper | sits like the frog (no pad strike); leaving the edge (wind / 톡) it catches the pad with 30 % (trebles): a pad snag (5.3) |
| softworm | slides off the edge nearest the angler over 0.25 s (`툭`, a 2 px ripple), 0 % catch |
| spinner, spoon, crank, kona, minnow, jig, egi | slide off the same way; 20 % catch the pad: a pad snag |

### 5.3 Natural baits on a pad, and pad snags

A float rig landing in a pad footprint always catches the pad: `S.Snagged` with a **pad snag** (soft). Flash
`연잎에 걸렸어요 — 톡 당기거나 좌우로 밀어요` (`UIKit.Bad`, 1.6 s). Freeing:

- a 톡: `p = 0.6 + 0.3 x strength`;
- a sweep either side held >= 50 % for 0.5 s: free;
- winding: after 0.8 m of wind it comes free — a lure with nothing lost, a natural bait **with its bait torn off**
  (`Game.I.ConsumeBait()`, flash `미끼가 연잎에 뜯겼어요`, the rig is retrieved: `S.Retrieving`);
- the tension never breaks the line on a pad (6.6 does not apply).

A freed rig drops into the water at the pad's edge nearest the angler.

### 5.4 Perches

A rig stopped on a solid's top (4.4) or out of the water (4.5) perches: `Tackle.Perch = (obstacle or null, point)`. It
is drawn at the point (sorting over the front layer, 45) and the fish ignore it. Flash (`UIKit.Sky`, 1.8 s):
`{바위|말뚝|보트|통나무|테트라포드|수정} 위에 걸쳐졌어요 — 톡 당겨 떨어뜨려요`, on the bank
`물가에 떨어졌어요 — 톡 당기거나 감아요`. Freeing:

- a 톡: it hops off towards the angler and drops into the water 0.3 m outside the footprint (the nearest valid water
  point on the line to him): a natural entry (8.3);
- winding: after 0.4 m of wind it slides off the same way; off a prop, treble / hook lures roll a **lip snag** (25 %;
  natural baits 10 %; frog, softworm 0 %): a hard snag at the footprint's edge at y = -0.2 (6.4).

---

## 6. Snags — "밑걸림!" (U3)

### 6.1 Zones and contact

A zone is every `snag` (hard) and `weed` (weed column) entry. The hook `H = Tackle.HookPos` (float rigs: the bait at
`FloatDepth`; lures: `Depth`) is **in** a zone when `H.xz` is inside its footprint and `H.y` is in `bot..top` (bed
resolved). Topwater lures on the film (`Depth <= 0.12`) are only in zones whose `top >= -0.1` (reeds, surface weeds).

### 6.2 Rates

Each frame the hook moved `d` metres (ground distance of `HookPos`, drift included) inside a zone, it snags with
`p = 1 - exp(-rate x d)`, `rate = base x grab x grabK x speedF x depthF x bottomF x SnagMult`:

| rig | base, hard (/m) | base, weed (/m) | per bottom touch in a hard zone |
|---|---|---|---|
| float rig (natural bait) | 0.08 | 0.12 | - |
| spinner | 0.20 | 0.25 | 0.10 |
| spoon | 0.22 | 0.25 | 0.15 |
| crank | 0.06 (the lip deflects it: 6.8) | 0.30 | 0.10 |
| kona | 0.10 | 0.20 | 0.10 |
| minnow | 0.18 | 0.25 | 0.10 |
| popper | 0 | 0.20 (surface zones only) | - |
| frog | 0 | **0 (weedless)** | - |
| softworm | 0.05 | 0.05 | 0.04 |
| jig | 0.30 | 0.20 | 0.20 |
| egi | 0.30 | 0.25 | 0.20 |

(`grab` of a weed zone = its mat's grab: weed 1.0, reed 0.9.)

### 6.3 Multipliers

- `speedF`: the hook's ground speed `< 0.3 m/s` -> 1.3 (crawling); `> 1.2 m/s` -> 0.6 (skimming); else 1.
- `depthF`: the hook within 0.3 m under the zone's `top` -> 0.5 (grazing the top); else 1.
- `bottomF`: `Tackle.OnBottom` in a hard zone -> 1.5; else 1.
- Bottom touches: at `Tackle.BottomTouch` with the hook in a hard zone, roll the per-touch chance x grab x grabK.
- No rolls: 1.0 s after a snag was freed; with the rig within 0.5 m of home; while `Tackle.Hanging` (no travel); in the
  legend encounter.

### 6.4 The snagged state

`FishingController.S.Snagged` (entered from Waiting / Retrieving; the lip snag and pad snags too):

- The rig is fixed at the snag point (no drift, no sinking), `Tackle.Snag = { Obstacle zone; Vector3 at; int freeSide;
  bool soft; float r; }`.
- Visual: the line straight and taut to the entry (`Angler.Tension01 = 0.3 + 0.6 r`), rings at the line's entry every
  0.8 s (`LineRing` 0.4), a float lies pulled down (dip -3 px, alpha 0.7); a lure shows a silt puff once.
- HUD: the fight strip in **snag mode** (10.2); flash `밑걸림!` (`UIKit.Bad`, 1.2 s; in a weed zone `수초에 걸렸다!`,
  reeds `갈대에 걸렸다!`, a pad 5.3's text).
- Fish: no approaches; a following fish turns away.

### 6.5 Freeing

- **A 톡** (`LureIn.FlickNow`, for float rigs too while snagged): `p = (0.25 + 0.35 x strength) x freeK x slackB`,
  `slackB = 1.5` if nothing was wound for >= 1.5 s before it (or line was given: counter-circles / B), else 1. The rod
  jerks as usual.
- **The sweep to the free side**: `freeSide = -sign(H.x - Anchor.x)` when `|H.x - Anchor.x| >= 1 m` (pull from the
  other angle: it backs the hook out the way it went in), else the opposite of the hook's last sideways motion (random
  if none). The rod held swept to `freeSide` at `|lean| >= 0.5` for 0.6 s frees it. Held the wrong way for 1.0 s (or a
  failed 톡): the side arrow appears over the line's entry pointing `freeSide` (`SideArrow` snag mode: Prompt
  animation; Right state while held correctly); each committed wrong sweep still frees with 15 %. **Soft snags (weed,
  reed, pad) free with a sweep either way** held 0.5 s.
- **The current** (float rigs on a moving-water stage): every 3 s, 8 % (the stream nudges it loose).
- Freed: flash `빠졌다!` (`UIKit.Gold`, 0.9 s), a small splash + silt puff; the rig pops 0.3 m towards the angler and
  0.3 m up; back to `S.Waiting`; 1.0 s without snag rolls.

### 6.6 Forcing it

Winding while snagged builds the snag tension ratio `r`: `dr/dt = 0.9 x revs/s` while winding; `-1.2/s` while not (to a
floor of 0.12 on moving water / 0.05 still); `-1.5/s` while giving line. `r >= 0.6`: the strip's word
`팽팽해요! 감지 마세요!` (blinking red) and `Sfx.Warn` as in the fight (faster over 0.85). `r >= 1` for the rod's
`BreakGrace` (0.3 + 0.6 flex s) -> **the line breaks**: `Sfx.Snap`, shake, `Game.I.LoseTackle()` (a lure is lost, a
natural bait used up), flash `밑걸림으로 줄이 끊어졌다!` + `  (루어를 잃었다)` (`UIKit.Bad`, 2 s), `S.Ready`. The reel's
drag does not save it (the hand is on the spool against a snag). Soft snags follow the same rule except pads (5.3).

The **`회수` button reads `끊기`** while snagged: a tap cuts the line at once (`줄을 끊었어요` + the lure-lost note).

### 6.7 Weedless

The frog: zone rates 0, pad rules 5.1, reeds and weeds pass. The soft worm: 0.05 (Texas-rigged). Everything else
snags.

### 6.8 Crank deflection (a reward)

A crank wound through a hard zone whose snag roll failed bumps off it at most every 0.8 s: the word `딱!`
(`LureFeedback`, `UIKit.Sky`), a 2 px puff, and a 1.0 s strike window x1.3 for its followers.

### 6.9 The ice

No sweep there: 톡 and slack free, the gravel / rock-pile helpers catch the bottom lures under the hole (per-touch
rolls only).

---

## 7. Cover-seeking fights (U4)

### 7.1 Species

New `FishSpecies` fields `coverSeek` (0..1), `coverReach` (m), `coverDig` (how hard it holds), `coverFor` (string[]),
set in `GameDatabase.BuildFish` by one table (like the jump styles). Everyone else 0.

| id | name | seek | reach | dig | cover types |
|---|---|---|---|---|---|
| largemouth_bass | 큰입배스 | 0.55 | 10 | 1.2 | post, pad, reed, weed, boat, log |
| carp | 잉어 | 0.30 | 8 | 1.0 | weed, reed, pad |
| golden_carp (legend) | 황금잉어 | 0.45 | 10 | 1.3 | weed, pad, reed |
| mandarin_fish | 쏘가리 | 0.70 | 8 | 1.4 | rock |
| rainbow_trout | 무지개송어 | 0.20 | 8 | 0.8 | rock |
| lenok | 열목어 | 0.25 | 8 | 1.0 | rock |
| rockfish | 우럭 | 0.75 | 6 | 1.5 | tet, rock |
| black_porgy | 감성돔 | 0.55 | 8 | 1.2 | tet, rock |
| red_seabream | 참돔 | 0.20 | 10 | 1.0 | tet |
| snakehead | 가물치 | 0.65 | 10 | 1.4 | pad, reed, root, log, weed |
| catfish | 메기 | 0.45 | 8 | 1.1 | root, log |
| arowana | 아로와나 | 0.20 | 8 | 0.8 | root |
| arapaima (legend) | 피라루쿠 | 0.60 | 14 | 1.6 | root, log, weed |
| northern_pike | 강꼬치고기 | 0.55 | - | 1.2 | rim |
| arctic_char | 북극곤들매기 | 0.20 | - | 1.0 | rim |
| burbot | 모캐 | 0.30 | 6 | 1.0 | rock |
| sturgeon (legend) | 철갑상어 | 0.40 | - | 1.5 | rim |
| yellowtail | 방어 | 0.40 | 12 | 1.1 | hull, rock (the ocean's reef) |
| mahi_mahi | 만새기 | 0.45 | 12 | 1.0 | weed (the ocean's drifting weed mat) |
| bluefin_tuna | 참다랑어 | 0.35 | 14 | 1.3 | hull, weed |
| great_white (legend) | 백상아리 | 0.45 | 16 | 1.6 | hull |
| coelacanth (legend) | 실러캔스 | 0.60 | 14 | 1.5 | crystal, rock |
| anglerfish | 초롱아귀 | 0.40 | 8 | 1.1 | rock |
| crystal_koi | 수정비단잉어 | 0.35 | 10 | 1.0 | crystal |

Legends hooked out of their encounter fight by the same rules.

### 7.2 When a run heads for cover

At each Run / Burst start (`OnPhase`; not a carried-on burst, not a downstream run), unless a pull-out happened in the
last 5 s: `NearestCover(fish, sp, reach)` = the cover entry whose `coverFor` meets the species' types, whose hold point
is within `reach` m (plan) of the fish and reachable (its yaw from the anchor within `±MaxFightYaw` at its distance, its
distance <= line + 8 m). With one found, roll `p = seek x (1.5 on the first run after the hook set) x (0.5 if
Stamina < 0.3)`, capped 0.9. Yes -> a **cover run** to it.

The ice (seek of `rim` fish): a **rim run**: roll `p = seek`; the fish runs wide under the ice: its target horizontal
distance from the hole `holeR + 0.6 x depth + 2 m` in a random direction (fightYaw ±π as today).

### 7.3 The cover run and the hold

- Target: `fightYawTarget = atan2(hx - Anchor.x, hz - Anchor.z)` clamped to `±MaxFightYaw`; `FishRun` = the sign of the
  yaw change (under 0.05 rad: the side of `hx` from the fish, else +1) — so side pressure and the arrow work as for any
  run; `fishDepthTarget` = clamp(bed - 0.5, the species' depth range).
- `FightModel.CoverRun = true`: the run's force x1.2 and its line-taking speed x1.15; its clock does not run out before
  it arrives (at most 4 s).
- Arrival: plan distance to the hold point <= 0.8 m (or the line long enough and the yaw within 0.08 rad) ->
  `FightModel.CoverHold`: the fish **digs in**: force 0.6, it takes no line, it tires at 0.5 x, for up to `5 s x dig`,
  then it runs out with a normal run. While it holds, the line rubs (7.4: the hold point is behind the structure or in
  its skirt).
- The phase label reads `커버로 도망친다!` during the cover run, `커버에 박혔다!` during the hold (7.7 priorities).

### 7.4 Rubbing

Each fight frame (not during a jump: the line is in the air), with `E` = `Angler.WaterEntry.xz` and `F` = the fish
(`xz`, depth `dF`):

1. the plan segment `E-F` crosses (Cyrus-Beck clip) the footprint, grown 0.10 m, of an `abrasive` solid standing in the
   water (`bot <= 0.05`), or of an `abrasive` snag zone whose band holds the line's depth there (`dF x s` at the clip
   parameter `s`); or
2. the fish's point (at its depth) is inside an `abrasive` snag zone (grinding the line on a rock's base); or
3. the ice: the fish's horizontal distance from the hole centre `h > holeR + 0.6 x dF` (the line leans on the rim's
   lower edge); the rim's `rough` applies.

The contact point: the clip's entry point at depth `dF x s` (case 1), the fish's mouth (2), the rim point towards the
fish at y = -0.1 (3).

Two exceptions keep a slack line off structure it does not press on:
- a `hull` snag zone (under a boat: the ocean hull and its skirt, the rowboat's keel skirt) counts in case 1 only with the
  fish inside it or within `Obstacles.HullRubReach` = 1.0 m of it (a line merely entering the water over the skirt by
  the bow does not rub);
- any rub counts (wear, warning, rasp) only on a taut line: tension >= `FishingController.RubTaut` = 0.3 x
  min(line limit, fish power) (the fish's pull too: PE 3호 holding a dug-in 감성돔 sits at ~0.15 of its limit).

### 7.5 The abrasion meter

`FightModel.Abrasion` A in 0..1, from 0 at each hook set, **never decreasing** in a fight:

`dA/dt = 0.22 x rough x roughK x (0.4 + 0.6 x TensionRatio) x (0.5 + 0.5 x clamp01(fishPlanSpeed / 1.5)) / tough(line)`,
x0.5 while giving line.

| line | tough |
|---|---|
| line_nylon2 나일론 2호 | 1.0 |
| line_nylon4 나일론 4호 | 1.15 |
| line_fluoro6 플로로카본 6호 | 1.7 (abrasion resistant) |
| line_pe3 PE 합사 3호 | 0.6 (braid cuts on rock) |
| line_pe8 PE 합사 8호 | 0.8 |
| line_titan 티타늄 와이어 | 5.0 |

A new `LineDef.tough` field (GameDatabase). Worked example, rock, tension 0.7, the fish moving: nylon 2호 breaks in
~6.5 s of rubbing, PE 3호 ~3.9 s, fluoro ~11 s, titanium ~33 s.

Effects: `FightModel.LineLimit = line.strength x (1 - 0.45 A)` (the break test, the HUD's red zone and the drag mark
follow it); at `A >= 1` the result is `Snapped` at once with the cause `Abrasion` (`FightModel.SnapCause`).

### 7.6 Side pressure and the arrow pull it out

The cover run and the hold keep `SideActive` (a run to one side): the arrow over the line's entry points away from the
cover (`-FishRun`), unchanged states. Against it:

- the run turns after `TurnTime x dig` (0.7 s x dig) of full lean (as today, scaled by the lean) -> **pulled out**:
  flash `커버에서 끌어냈다!` (`UIKit.Gold`, 0.9 s), `fightYawTarget` moves 0.35 rad away from the cover, `CoverRun` /
  `CoverHold` end, the run is cut (`FightModel.Turn`), 5 s cover cooldown;
- during a hold, steady winding (revs >= 0.5) at tension >= 0.5 for `2.5 s x dig` also drags it out ("horsing it": it
  costs abrasion);
- a with-current run turned this way still counts as pulled out of the flow (time spec 9.7).

### 7.7 Warnings (priority in the phase label: break > abrasion > rubbing > cover run > side pressure > the rest)

| when | text | where / colour / time |
|---|---|---|
| a cover run starts | `커버로 파고든다! 반대로 밀어요!` | flash, `UIKit.Bad`, 1.1 s |
| during the cover run / the hold | `커버로 도망친다!` / `커버에 박혔다!` | phase label, `UIKit.Bad` |
| the first rub of the fight | `줄이 {바위\|테트라포드\|말뚝\|통나무\|뿌리\|배 밑\|수정\|얼음 구멍 가장자리\|갈대}에 쓸린다!` | flash, `UIKit.Bad`, 1.2 s |
| rubbing | `줄이 쓸리는 중!` | phase label, `UIKit.Bad`, blinking |
| A >= 0.6 (once) | `줄이 버티지 못해요! 빨리 빼내요!` | flash, `UIKit.Bad`, 1.2 s |
| pulled out | `커버에서 끌어냈다!` | flash, `UIKit.Gold`, 0.9 s |
| the break | `줄이 {name}에 쓸려 끊어졌다!` + `  (루어를 잃었다)` | flash, `UIKit.Bad`, 2 s (replaces `줄이 끊어졌다!`) |

### 7.8 What stays as it is

Runs, rests, bursts, jumps and their hook throws; side pressure constants; the current in the fight (downstream runs,
loads); landing; the legend encounter window (no obstacles inside it).

---

## 8. Bites near structure (U4)

### 8.1 The multiplier

`FishingController.BiteMult(f)` gets `x StructureMult(hook, sp)`:

- the hook in a `cover` zone whose `coverFor` meets the species' cover types and `seek >= 0.3`: **x1.35**;
- the hook in any `cover` zone otherwise: x1.15;
- else the hook in a `snag` / `weed` zone: x1.1;
- else 1.

On the stream the pocket's x1.3 and this multiplier do not stack: `max(pocket, structure)`. The existing clamp
0.25..2 stays.

### 8.2 Holders

`FishAgent.PickTarget`: a cover species picks a wander target inside one of its cover zones (uniformly, at its depth
range, clamped to the water) with chance `0.6 x seek` (bass 0.33, mandarin 0.42, rockfish 0.45, snakehead 0.39 ...); on
the stream the existing pocket holders keep their chance and pick a pocket or a rock cover 50 / 50.

### 8.3 The natural-entry bonus

After a bank shot (4.7), a drop under an overhang (4.8), a slide down a face (4.4) or a perch knocked off with a 톡
(5.4): for 8 s fish within 5 m of the entry get x1.3 in `BiteMult`, and a lure's `Q` +0.15 for its first 2 s.

---

## 9. Aiming outlines (U1)

### 9.1 What and when

While the wind-up guide shows (`S.Aiming`, pulled, the fan visible; not on the ice): faint outlines of every `snag` and
`weed` zone whose footprint comes within `castDist + 2 m` of the anchor. Never solids, pads, covers or rims. They fade
in over 0.15 s with the fan and out over 0.12 s at the release; alpha 0.22 while pulled, 0.32 once armed (the fan's own
levels, with its shimmer). Nothing in any other state (except `-fkobstacles show`).

### 9.2 Style

Each zone's polygon at `y = top`, lifted by `Persp.Apparent`, drawn 1 px wide, dashed 2 on / 2 off along the perimeter,
snag `#d8f0ff`, weed `#b8e8a0`; zones smaller than 4 px across on screen are skipped; pixels under the front layer's
opaque pixels are not drawn (the outline stays "in the water").

### 9.3 How

A static overlay built once at stage load (and on a rod change): a canvas-sized (640x400; the sea 800x400) point-filtered `Texture2D` placed exactly
like the back sprite (centred on the scene origin, 16 px per unit), sorting `Fx.OrderRipple + 1` (under the fan dots);
only its alpha animates. On the ocean it follows the front layer's swell bob (1 px; the hull zones stay under the bow,
the reef and the weed mat show in the open water in front of it).

---

## 10. HUD, texts, sounds, FX

### 10.1 Texts (Korean; besides 7.7)

| when | text | where / colour / time |
|---|---|---|
| a contact | the material word: `탁!` `딱!` `톡!` `퉁!` `쨍!` `사삭` | `LureFeedback` at the contact, `UIKit.Cream`, 0.6 s |
| a bank shot | `뱅크샷!` | flash, `UIKit.Gold`, 1.0 s |
| under an overhang | `가지 아래로 쏙!` | flash, `UIKit.Gold`, 1.0 s |
| a perch | `{바위\|말뚝\|보트\|통나무\|테트라포드\|수정} 위에 걸쳐졌어요 — 톡 당겨 떨어뜨려요` / `물가에 떨어졌어요 — 톡 당기거나 감아요` | flash, `UIKit.Sky`, 1.8 s |
| the frog on a pad | `연잎 위에 앉았다!` | flash, `UIKit.Gold`, 1.0 s |
| a popper on a pad | `연잎 위에 올라갔다` | flash, `UIKit.Sky`, 1.0 s |
| a pad strike | `연잎을 뚫고 덮쳤다!` | flash, `UIKit.Gold`, 1.0 s |
| a pad snag | `연잎에 걸렸어요 — 톡 당기거나 좌우로 밀어요` | flash, `UIKit.Bad`, 1.6 s |
| the bait torn off a pad | `미끼가 연잎에 뜯겼어요` | flash, `UIKit.Bad`, 1.6 s |
| a snag | `밑걸림!` / `수초에 걸렸다!` / `갈대에 걸렸다!` | flash, `UIKit.Bad`, 1.2 s |
| snag mode, idle | `감지 말고 톡! 또는 좌우로 밀어요` | strip phase label, `UIKit.Sky` |
| snag, the arrow shown | `◀ 반대쪽으로 밀어 봐요` / `반대쪽으로 밀어 봐요 ▶` (the free side) | strip phase label, `UIKit.Gold` |
| snag tension >= 0.6 | `팽팽해요! 감지 마세요!` | strip phase label, `UIKit.Bad`, blinking |
| freed | `빠졌다!` | flash, `UIKit.Gold`, 0.9 s |
| a crank deflection | `딱!` | `LureFeedback`, `UIKit.Sky`, 0.6 s |
| broken by forcing | `밑걸림으로 줄이 끊어졌다!` (+ `  (루어를 잃었다)`) | flash, `UIKit.Bad`, 2 s |
| cut with 끊기 | `줄을 끊었어요` (+ `  (루어를 잃었다)`) | flash, `UIKit.Bad`, 1.6 s |

New strings are checked with `Tools/font_coverage.py` (README).

### 10.2 The fight strip

- **Snag mode** (`S.Snagged`): the fight panel shows with the fish name slot reading `밑걸림` (`UIKit.Bad`; `수초` /
  `갈대` / `연잎`), the distance slot the rig's distance, the tension bar = `r` (same colours, the danger zone), the
  stamina bar and the side strip hidden, the phase label per 10.1. The `회수` button's label reads `끊기`.
- **The abrasion meter** (fights): label `쓸림` (15 -> snapped size, `UIKit.Cream`) at (470, 28) 40 x 10 and a bar at
  (510, 30) 156 x 6 (canvas units, bottom-left anchored, under the distance label, above the stamina bar; U4 may nudge
  them a few units so no label overlaps); hidden until the first rub, then kept for the fight; fill `#e8d8a0` ->
  `#ff8a3a` at 0.5 -> `UIKit.Bad` from 0.8 (blinking).
- The drag mark and the red zone use the weakened `LineLimit` (7.5).

### 10.3 Sounds (synthesised in `Sfx`, like every sound)

| name | what |
|---|---|
| `Tock` | rock / concrete contact: a 1.2-1.5 kHz click, 30 ms, + a short noise tick |
| `Knock` | wood / root: a 600-700 Hz knock, 60 ms |
| `Thunk` | hull: 250 Hz, 150 ms, with a light ring |
| `Ting` | crystal: 2.8 kHz sine, 300 ms decay + shimmer |
| `Rustle` | leaf / reed: band noise 1-3 kHz, 120 ms |
| `Rasp` | the line rubbing (loop while rubbing): band noise 2-4 kHz, amplitude-modulated at 18 Hz, volume `0.15 + 0.3 x min(1, dA/dt / 0.25)` |
| `Tear` | a pad / weed pulled loose: an 80 ms low-passed noise burst |

`Sfx.Warn`, `Sfx.Snap`, `Sfx.Plop` are reused.

### 10.4 FX

Contact puffs (4.6); rub sparks: every 0.12 s while rubbing, 1-2 px `#ffd080` sparkles (`Fx.Sparkle`, 0.15 s) at the
contact point's apparent position; snag rings (6.4); freed: a small splash + silt puff; the pad wobble (5.1).

---

## 11. Save

None. No new `SaveData` fields; every hint above shows whenever it applies (they are short).

---

## 12. Test switches

| switch | effect |
|---|---|
| `-fkobstacles show` | draws every obstacle in every state, colour-coded as the Blender overlay: solid tiers `#ffe040` (outline + 20 % fill), snag `#60e0ff` dashed, weed `#a8f070` dashed, pad `#70f0a0`, cover `#ff70d8` dashed + a 7 px cross at the hold point, rim `#ffffff` (underwater ones at their apparent `top`); logs `[OBST] <stage>: n solid, n pad, n snag, n weed, n cover, n rim` |
| `-fkobstacles off` | loads no obstacle data (today's game) |
| `-fkobstlog` | logs every event: `[OBST] contact md25.0 side v 14.2 -> 6.1`, `perch`, `pad frog md..`, `snag md13.5.skirt spoon d 0.6 rate 0.33`, `free tok p 0.52`, `free sweep`, `break`, `cover run mandarin_fish -> md44.0.cover`, `rub md44.0 A 0.42`, `pullout`, `bank shot` |
| `-fksnag <x>` | `Obstacles.SnagMult` (0 = never, 99 = at once) |
| `-fkauto obstacles` | the capture scenario (14) |

---

## 13. Blender scaffolding (done)

### 13.1 Files

| file | what | who edits |
|---|---|---|
| `Tools/Blender/variants/hybrid/hyb_obstacles.py` | the exporter + checker: builds a stage's scene without rendering (runs `hyb_<stage>.render_front/back(Random(seed))` and stops it at its first render pass), stamps the module's rules, adds its helpers, collects the tagged objects, converts them to game space, writes the JSON, verifies it against the painted PNGs, writes the overlay | nobody (shared) |
| `Tools/Blender/variants/hybrid/obstacles/<stage>.py` | a stage's `LAYERS`, `RULES`, `extra()`, `check()` | the stage's owner only |
| `Tools/Blender/variants/hybrid/obstacles/_template.py` | the module API (docstring) + a commented example | nobody |
| `Tools/Blender/variants/hybrid/obstacles/stream.py` | the stream (done) | the stream owner |
| `Tools/Blender/variants/hybrid/build_obstacles.ps1` | `-Stage <s>[,<s>]` / `-All`, `-Dry`, `-NoCheck`: one Blender process per stage | nobody |

Nothing in `hyb_core.py`, `hyb_period.py` or any `hyb_<stage>.py` changed: stage scripts stay byte-identical in output.

### 13.2 Tagging convention (Blender custom properties)

An object is an obstacle when it carries `obst` (`solid | pad | snag | weed | cover | rim`). The other properties:
`obst_id` (default: the object's `grp` tag, else its name without `.001`; objects sharing id and kind are one obstacle),
`obst_mat`, `obst_tags` (comma list), `obst_tiers` (1-4), `obst_shape` (`auto | poly | circle | capsule`),
`obst_skirt` / `obst_skirt_top` (derived `<id>.skirt` snag ring), `obst_cover` / `obst_cover_for` (derived `<id>.cover`
with its hold point), `obst_weed` / `obst_weed_top` (derived `<id>.weed`), `obst_top` / `obst_bot` (y range override;
`-99` = the bed), `obst_grabK` / `obst_roughK`. Export-only helpers are named `OBST_<kind>_<id>` and hidden from render.
A stage script may stamp these itself when it creates a prop; normally the module's `RULES` stamp them after the build
so the stage scripts stay untouched.

### 13.3 Module API (`obstacles/<stage>.py`) and the export steps

```python
STAGE = "stream"
LAYERS = [("front", "render_front", 31)]          # (layer, hyb_<stage> render function, the seed its main() passes)
RULES = [                                         # first matching rule wins; obst=None = never an obstacle
    dict(name="AnglerRock", obst=None),
    dict(name="Mid", obst="solid", mat="rock", tiers=3, skirt=0.5, skirt_top=-0.35, cover=1.6, cover_for="rock",
         tags="rock,abrasive,midstream,pocket"),
    # match keys: name (base-name prefix / list), kind (the "kind" tag), grp (regex), where (callable(ob)), layer;
    # every other key k -> obst_<k>; id may use {grp} / {name} / {kind}, e.g. id="{grp}.crown"
]
def extra(ctx):                                   # optional: export-only helpers, Blender axes (x right, y forward, z up)
    return [ctx["helper_box"]("snag", "sunklog1", (3.0, 22.0, -1.8), (4.0, 0.35, 0.35), rot_z=0.4, mat="wood",
                              tags="log,abrasive", top=-1.2, bot=-99.0, cover=1.0, cover_for="log")]
def check(ctx):                                   # optional: stage checks printed after the export
    ...
```

Steps (`hyb_obstacles.run`): 1. for each layer: `C.reset_scene()`, run the builder until its first
`R.render_passes` (intercepted: nothing renders; the stage camera is set), 2. stamp `RULES`, 3. `extra(ctx)`
(`ctx`: stage, layer, L = the stage JSON, smod = the stage module, helper, helper_box, R, scene), 4. collect every
`obst` object: world vertices of the evaluated meshes -> game space (Blender (X, Y, Z) -> game (X, Z, Y)); solids:
`bot = 0` (or the lowest point if it floats above 0.05 m: an overhang), `top` = the highest point, `bed` = it reaches
below -0.1; tiers = the plan convex hull of the vertices in each of `tiers` equal height bands (<= 12 points); pads /
snags / weeds: the plan hull (<= 16 points); rims: the circle fitted to the lowest ring; shape = circle / capsule / poly
by fit; derived skirt / cover / weed zones = the footprint grown by the given metres; the cover's hold point = the first
point just behind the footprint as seen from (0, 0) that is inside the fight's limits and makes the line cross the
footprint (`holdCross`), else the channel-side point of the skirt, else no cover; 5. drop obstacles that never touch the
fishable water; 6. write `_tmp/.../obstacles_<stage>.json` (+ install unless `--dry`); 7. check (13.5).

### 13.4 Commands (PowerShell; Blender 5.2)

```powershell
# export + install + check (Assets/Resources/Data/obstacles_<stage>.json, the overlay in _tmp)
.\Tools\Blender\variants\hybrid\build_obstacles.ps1 -Stage stream
# several / all stages with a module, not installed, without the check
.\Tools\Blender\variants\hybrid\build_obstacles.ps1 -Stage lake,swamp -Dry
.\Tools\Blender\variants\hybrid\build_obstacles.ps1 -All -NoCheck
# raw
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python Tools/Blender/variants/hybrid/hyb_obstacles.py -- stream [--dry] [--no-check]
```

Outputs: `Tools/Blender/_tmp/variants/hybrid/obstacles/obstacles_<stage>.json`, `obstacles_<stage>_overlay.png`;
installed `Assets/Resources/Data/obstacles_<stage>.json`. The stream run takes ~6 s. A smoke run built the front and
back scenes of all seven stages through the interception without an error (17 s in all).

### 13.5 Verification — the stream (done)

The check projects the export with the stage camera read from `Data/stage_stream.json` (a re-implementation of
`Persp.ToPixel`, not Blender's camera) and compares it with the installed `Sprites/Stages/stream_front.png` (=
`stream_dawn_front.png`; the other periods share its silhouette):

| check | result |
|---|---|
| camera: JSON-camera pixel vs Blender-camera pixel, 1243 obstacle vertices | max **0.0001 px** |
| the rebuilt front layer rasterised through the JSON camera vs the PNG's opaque pixels | 27335 opaque px = 25831 raster px + 1504 px of the stage's 1 px outline ring; **0** unexplained painted px, **0** raster px unpainted |
| per solid (18 with visible paint): the exported prop's pixels vs its painted pixels, edge distance | mean **0.84 px**, max **1.00 px** (the outline) |
| per solid: the collider (tier prisms) vs the painted prop | IoU 0.81 mean; edge mean 0.77 px, max 5.83 px (the big near flank / bank boulders, domes approximated by 3-4 prisms); centre offset 0.71 px mean. The five mid-stream rocks: IoU 0.72-0.85, edge <= 0.80 px mean, <= 2 px max |
| `pocket` rocks vs `CurrentField.Rocks` | centres within 0.01-0.05 m |

Overlay: `Tools/Blender/_tmp/variants/hybrid/obstacles/obstacles_stream_overlay.png` (top: the export on the painted
stage at 2x: solid colliders yellow, skirts cyan dashed, covers magenta dashed with hold crosses, the game crop dotted;
middle: the pixel-agreement map: green = raster and paint agree, yellow = the 1 px outline, red = unexplained paint,
blue = raster not painted; bottom: a close-up of the far play area).

### 13.6 Split for the Blender agents

| agent | owns | delivers |
|---|---|---|
| B1 | `obstacles/lake.py`, `obstacles/swamp.py` | 3.3, 3.5; `build_obstacles.ps1 -Stage lake,swamp` installed, both overlays, the CHECK lines |
| B2 | `obstacles/sea.py`, `obstacles/ocean.py` | 3.4, 3.7 |
| B3 | `obstacles/ice.py`, `obstacles/cave.py` | 3.6, 3.8 (the ice rim and the cave walls come from the BACK layer: `LAYERS` gets `("back", "render_back", 77)`; back-layer solids are checked by the camera line + the overlay, the painted check covers the front layer) |

Pass: camera <= 0.01 px; painted: 0 unexplained px; solids in the play area: painted-vs-export edge <= 1 px, collider
IoU >= 0.7; every helper in the water and within reach. Nobody edits `hyb_obstacles.py`; a needed feature goes to the
spec owner.

---

## 14. Capture plan (one set)

`-fkfresh -fkrich -fkgear -fksave obst -fkscene Fishing -fkstage stream -fkauto obstacles -fkobstlog -fkshots <dir>`
(new `Debug/AutoPilot.Obstacles.cs`). Every step logs a `[OBST] CHECK <name> PASS|FAIL <numbers>` line. The autopilot
forces what it needs through internal test hooks (`Obstacles.SnagMult`, `FishingController.DebugCover = "<cover id>"`
forcing the next run's cover roll, `DebugHook` as today), seeded so the rolls repeat.

| # | step | CHECK | shot |
|---|---|---|---|
| 1 | stream, spinner, wind-up held armed 1 s | outlines drawn: >= 5 snag zones in range | `obst_aim.png` (full screen) |
| 2 | a flick whose target is 0.3 m beyond `md25.0`'s top | `bounce`: a contact on md25.0 logged; entry <= 2 m from it; `뱅크샷!` shown | `obst_bounce.png` (the frame after the contact) |
| 3 | spoon cast past `md13.5`, `SnagMult` 99 for the wind through its skirt, then a strong 톡 | `snag_tok`: snagged, freed by 톡 (seeded) | `obst_snag.png` (snag mode strip) |
| 4 | the same, then the sweep to `freeSide` | `snag_sweep`: freed within 0.8 s of the correct lean | - |
| 5 | the same, then wind at 2 rev/s | `snag_break`: the line broke, the spoon lost | - |
| 6 | `DebugHook` a 45 cm 쏘가리 at (-5.2, 40), nylon 2호, forced cover run to `md44.0.cover`, no side pressure, winding 0.8 rev/s | `rub_break`: rubbing logged, A reaches 1, break cause Abrasion | `obst_rub.png` (A ~0.5) |
| 7 | the same fish again, side pressure against the cover run | `pullout`: turned with A < 0.6, `커버에서 끌어냈다!` | `obst_pullout.png` |
| 8 | swamp (only if `obstacles_swamp.json` exists, else `SKIP`): the frog onto a pad cluster, wait 2 s, wind off the edge; then a worm float onto a pad, a 톡 | `frog_pad` (sat, dropped off, window opened), `pad_snag` (snagged, freed) | `obst_pad.png` |
| 9 | `-fkobstacles show` on for 1 s on every stage that has data | - | `obst_show_<stage>.png` |

The ocean (3.7) has its own set: the same command with `-fkstage ocean` (`Debug/AutoPilot.ObstaclesOcean.cs`):
`ocean_aim` (the reef and the weed mat outlined, every dash in open water; the hull's none; the aim layer under the front
layer), `ocean_snag_reef` (a spoon 3 m down wound through the reef snags, a 톡 frees it), `ocean_snag_kelp` (a minnow
0.8 m down in the mat: a weed snag), `ocean_snag_open` (the same spoon in open water: none), `ocean_bites` (BiteMult
with the hook in `reef.cover` / `kelp.cover` against open water: x1.35 / x1.15), `ocean_cover` (방어 -> `reef.cover`,
만새기 -> `kelp.cover`; a hooked 방어 sent to the reef rubs the line on it), `ocean_drift`, `ocean_lurk`; shots
`obst_ocean_aim`, `obst_ocean_snag`, `obst_ocean_rub`, `obst_ocean_show`.

No other capture rounds.

---

## 15. Unity implementation split

| agent | owns | sections |
|---|---|---|
| U1 data + outlines | `Fishing/Obstacles.cs` (new), `Fishing/StageView.cs` (load), the aim overlay (a new small `Fishing/ObstacleOverlay.cs`), `FishingController.DrawFan` hook (alpha only), `Core/Game.cs` switches | 2, 9, 12 |
| U2 casts + surfaces | `Fishing/Tackle.cs` (flight contacts, bounce, perch, pads, 4.9), `FishingController.OnLanded(Landing)`, `Audio/Sfx.cs` (Tock, Knock, Thunk, Ting, Rustle, Tear), `Fishing/Fx.cs` if needed | 4, 5, 10.3-10.4 |
| U3 snags | `FishingController` (`S.Snagged`, freeing, forcing, 끊기), `Tackle.cs` (snag rolls, `Snag`), `FishingHUD.cs` (snag mode), `SideArrow.cs` (snag mode) | 6, 10.1-10.2 |
| U4 fights + bites | `FightModel.cs` (Abrasion, CoverRun / CoverHold, LineLimit, SnapCause), `FishingController` (cover runs, rub test, pull-out, BiteMult), `FishingHUD.cs` (abrasion meter, texts), `Data/Models.cs` + `Data/GameDatabase.cs` (cover fields, `LineDef.tough`), `FishAgent.cs` (holders), `Audio/Sfx.cs` (Rasp) | 7, 8 |
| U5 test + docs | `Debug/AutoPilot.Obstacles.cs` (new), README (controls: 밑걸림 풀기, 끊기; switches) | 12, 14 |

`Tackle.cs` and `FishingController.cs` are shared: U2 goes first, then U3, then U4 (U1 in parallel with U2; U5 last).
Constants of the rod sweep, side pressure and the current stay unchanged.
