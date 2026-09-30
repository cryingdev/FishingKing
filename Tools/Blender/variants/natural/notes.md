# FishingKing art variant "natural" (자연주의 픽셀아트)

The goal is grown-up, believable and calm pixel art. The camera, canvases, crops, JSON schemas and
sprite naming stay exactly as they are today, so this style can replace the current art without any
gameplay change.

Scripts are in `Tools/Blender/variants/natural/`. They only import `fk_common`, `fk_persp` and
`fk_fish` and never modify them. Output goes to `Tools/Blender/_tmp/variants/natural/`.

| script | output | run time |
|---|---|---|
| `nat_core.py` | shared library (colour science, light rig, materials, mesh accumulator, SS + mode filter, outlines, sheets) | - |
| `nat_lake.py` | `lake_back.png`, `lake_front.png`, `lake.json` | ~35 s |
| `nat_character.py` | `character/angler_<pose>.png` (all 7 poses), `character.json`, `character_sheet.png` | ~15 s |
| `nat_fish.py [-- ids]` | `fish/<id>_0/_1/_t0/_t1.png` (all 36 species), `fish_sheet.png`, `fish_all.png` | ~6 min for 36 |
| `nat_preview.py` | `preview_lake.png`: in-game composite at 2x | ~5 s |

Run each one with `& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python <script>`.
Run order: fish, then character, then lake, then preview. The preview needs the other outputs.
`_zoom.py`, `_px.py` and `_insp.py` are small debug helpers: zoom a region, print pixel colours, make an
inspection sheet.

---------------------------------------------------------------------------------------------------

## 1. What changed and why it no longer looks childish

| was | now |
|---|---|
| light-independent normal-toon (N·fixed vector), the same 3 bands on everything | a real **sun + sky** evaluated by EEVEE: cast shadows, contact AO, form shading |
| flat saturated primaries | **OKLab-generated hue-shifted ramps**: 5 values, cool/blue shadows, warm/yellow lights, desaturated earthy base colours |
| heavy near-black 1px outline on everything | scenery: **no outline**. Sprites: 1px **darker same-hue sel-out** outline (never black). Character: **depth-aware inner lines** as well |
| 1-spp aliased render (speckles, broken thin lines) | **3-4x supersample + MODE filter**: each output pixel takes the majority colour of its block. Clean clusters, no AA blends, thin features kept or dropped deterministically |
| lollipop sphere trees, plain cone pines | deciduous crowns from 20-45 **lumpy leaf clumps** in 6 crown types (round, oval, wide, irregular multi-crown, willow), plus **conifers** from 7-10 jagged, drooping tier skirts, bushes, birches, emergent tall spruces |
| stick reeds, flat disc lily pads | reed clumps of 30-90 **tapered, curved, drooping blades** in 5 colours (sage, olive, dark, straw, dry), cattail heads, notched pads with lifted edges, red curled rims and white water lilies, lumpy half-sunk boulders with a wet band |
| flat water plus a sine "reflection" polygon | **depth/Fresnel water**: screen-row bands with 2x2 Bayer transitions, wind streaks, shadows of the pier and reeds cast on the water, a **true mirrored reflection pass** that is rippled and broken, a sunlit waterline, sparse glints |
| chibi angler (huge head and hat, stubby legs, red/green/blue) | **1.76 m adult, 7.5 heads tall**: long legs, shoulders, slate chambray shirt, khaki fishing vest (back pocket, mesh yoke), olive-drab trousers, leather boots, flat-top bucket hat, belt, natural stance per pose, and a soft cast + contact **ground shadow** |
| fish: white cartoon eyes, flat 3-band toon, black outline | posterised **countershading**, a cool/warm light-multiply ramp, a wet specular glint, a **gill-cover arc**, softened patterns and scales, fins with rays, **small dark eyes with a muted iris**, and a same-hue outline |

---------------------------------------------------------------------------------------------------

## 2. Techniques (all in `nat_core.py`)

### 2.1 Light rig (one sun for the whole game)
* `SUN_DIR = (-0.45, -0.55, 0.70)` (towards the sun). The sun is behind-left of the camera and about
  45° high (late morning). It lights the angler's back and the camera-facing sides, so silhouettes
  read well. Shadows fall forward and right.
* Sun energy = π, so N·L = 1 gives a light value of 1.0. Angle 0.3° gives crisp pixel shadows.
* World = hemispherical grey ambient: up 0.30, horizon 0.20, down 0.07. Only the light *intensity*
  is used, because colour comes from the ramps.
* Material core (`_light_value`): `Diffuse BSDF(white) -> Shader to RGB -> RGB to BW`. This is
  multiplied by `mix(1, AO, ao)` (the AO node, distance tuned per object size). Optionally
  `+ (noise - 0.5) * 2 * tex` is added, with world-space noise used as a band-breaker for leaves,
  bark, rock, cloth and grain.
* The light value feeds a CONSTANT ColorRamp with ramp positions `[0, .17, .36, .62, .93]`:
  deep shadow / shadow / half-tone / lit / highlight. `bias` shifts a material darker or lighter
  (lily pads -0.24, bank grass -0.42).

### 2.2 Palette / ramps (`gen_ramp(base, n=5, spread, cool, warm, sat)`)
* The base hex is the *lit* colour. Lightness multipliers are `0.60 0.75 0.88 1.00 1.10` in OKLab L.
* Shadows are shifted towards blue (a -0.004, b -0.020 times strength 1, .7, .35, .05). The
  highlight is shifted towards yellow (a +0.004, b +0.022). Highlight chroma is slightly reduced.
* Result: every material is a hand-style pixel-art ramp. Scenery never has more than 5 colours
  per material, plus the quantised haze steps.
* Lake key colours:
  * sky, horizon to top: `#d6dcd6 #cbd6d6 #bccdd5 #adc3d2 #9fb9cf #93b0ca #88a7c4`
  * clouds: `#b3bec8 #cbd3d8 #e0e3e1 #efede7`
  * mountains `#6f817f`, hills `#4c5e45`, grass `#6a7444`, stone lip `#a0967f`
  * deciduous greens: `#5f7340 #6c7c45 #55683b #7a8550 #687a4a`
  * willow `#7f8b52`
  * conifers: `#3b5143 #415846 #384a3d`
  * bark `#584a3d`, birch `#c3bdab`
  * water keys (u = 0 horizon to 1 bottom): `#a9bbbd .06 #93a9b0 .2 #7892a0 .42 #617f8b .7 #526f78 1 #46626a`
  * planks: `#8e7c65 #857159 #9a8a73 #7b6651 #8a7862`, post `#6b5a48` (wet band `#4d5040`)
  * reeds: `#6c7a3e #7d8956 #55643a #a39a63 #8a8a52`, cattail `#5a4332`
  * pads: `#5f7a40 #6b8446 #56703b #7b884c`, pad rim `#7a4c3d`, lily `#ebe7da`/`#d6b24a`
  * hull `#5e6f69`, boat interior `#8c775b`
* Angler: skin `#c39174`, hair `#3b2d25`, shirt `#5d6b7f`, vest `#978b63`, yoke `#716c52`,
  pocket `#85794f`, trousers `#4b4a3c`, boots `#45331f`, sole `#2f2620`, hat `#ae9f78`,
  band `#5b4a38`, belt `#3c3028`.
* Value plan: a light hat at the top, mid vest and sleeves, dark trousers and boots. This keeps the
  figure readable against water (mid teal-grey) and wood (warm mid).

### 2.3 Atmospheric perspective
`_haze()` uses camera View Distance: `f = 0.8 * (1 - exp(-d/800 m))`, floored to 12 steps, then
mixed towards the haze colour `#b7c5cb`. The far shore (180-330 m) gets 1-2 steps, the hills about
5 steps, the mountains about 8 steps. Contrast and saturation fall off with distance. Near props
(< 40 m) are untouched.

### 2.4 Water (`nat_lake.water_mat` + `compose_water`)
* **Band coordinate u = (8.85 / horizontal camera distance)^0.8.** This is proportional to screen
  rows below the horizon, so the 10 bands have roughly equal pixel height. Bands are interpolated in
  OKLab between the water keys (hazy sky reflection at the horizon, deep teal near the pier).
* The 2x2 Bayer ordered dither (`bayer2`, computed in *output*-pixel window coordinates, so it
  survives supersampling) makes 25/50/75 % transitions between bands.
* Wind streaks: world noise stretched `(0.09, 0.8, 1)` is thresholded to ±0.55 band. It fades out
  towards the horizon to avoid aliasing.
* Cast shadows: the water material reads the same light value. Shadowed pixels switch to a second
  ramp (`shade_hex(dl=-0.07, cool)`). In the back render the front objects are
  `visible_camera = False` but still cast shadows, so the pier, reeds and boat shadow the water.
* **Reflection pass**: every mesh except sky, water, lily pads, hills and mountains gets
  `matrix_world = Scale(z = -1) @ M`. The sun z is negated and the world gradient is flipped, which
  gives the exact mirror image of the lit scene. Render it transparent at SS, then apply the mode
  filter.
* `compose_water` (numpy, top-down) runs only on pixels whose colour is one of the water ramp
  colours. Keys are exact, which the mode filter guarantees.
  * Per-row ripple offset: `round(amp(u) * sin(row * 1.9 + ...))`, 0 px at the horizon, up to
    ±1.6 px near the camera.
  * Horizontal breaks from fbm, with more breaks near the camera. Break pixels become a +0.04 L
    "sky-catching" ripple.
  * Reflection colour: OKLab L × 0.78, chroma × 0.75. It is mixed with the water by Fresnel alpha
    `0.72 → 0.37`, quantised to quarters, so it stays a few flat colours.
  * A sunlit 1px waterline under the far bank.
  * About 70 glints of 1-5 px, avoiding the gameplay middle.

### 2.5 Supersample + mode filter (`render_ss`, `mode_down`, `take`)
* Render at W·SS × H·SS with 1 sample and filter_size 0. Use SS=3 for stages and SS=4 for sprites.
  The camera uses a fixed-FOV sensor fit, so it does not change.
* For every SS×SS block, pick the most frequent opaque colour, breaking ties towards the block
  centre. Alpha = opaque coverage ≥ `cover`: 0.45 for sprites and the front layer, 0.4 for the
  reflection, 0 for the opaque back layer.
* The chosen sample index `pick` is returned, so aligned passes (ID/depth) can be reduced with the
  same choice (`take`).
* Minimum widths are applied in geometry: reed blades and stalks are at least 1.2 px wide at their
  own distance (`px_m(y)`), so they survive the filter as clean 1px lines.

### 2.6 Outlines
* Scenery (back and front layers): none. Separation comes from value and hue.
* Sprites: `outline_sel(mulL, cool)`. It adds a 1px outside outline whose colour is the average
  adjacent sprite colour with OKLab L × 0.5-0.58 and a cool shift, so it is a darker same-hue line
  and never black. The angler uses 0.58 and fish use 0.5.
* Character inner lines: a second render uses a view-layer material override
  (`Object Index / 16`, `(View Distance - 6) / 12`) with the Raw view transform and a 16-bit PNG.
  The same `pick` reduces it. At every part-ID boundary, the pixel that is *behind* (depth + 3.5 cm)
  is darkened by 0.1 OKLab L. Parts: 1 boots, 2 trousers, 3 torso/vest, 4 far arm, 5 near arm,
  6 head, 7 hat.
* Character ground shadow: a 0.6 m ground disc renders only the sun light value (the character is
  `visible_camera = False`, so it still casts). A 14 cm contact zone around each boot is added.
  Empty sprite pixels in shadow become `#141b26` at alpha 0.42. These are the only semi-transparent
  pixels, and Unity blends them normally.

### 2.7 Geometry helpers
* `Acc`/`Bank`: one bmesh per material that accumulates icospheres, jagged cones, tubes, ribbons,
  boxes and heightfields. A 3000-clump forest becomes about 20 objects. The whole lake builds in
  about 20 s.
* `deciduous()`, `conifer()`, `bush()`, `reed_clump()`, `lily_pad()`, `water_lily()`,
  `rock_lump()`, `rowboat()` (lofted hull with an inner shell, gunwales, thwarts and oars), and the
  heightfield bank, hills and mountains (all in `nat_lake.py`). They are all parametric and ready to
  reuse.
* Mirror-safe materials use `|Z|` (wet bands, bank lip), so the reflection pass shades them
  correctly.

### 2.8 Fish look (`nat_fish.py`, monkeypatched on top of `fk_fish`)
* `natural_params`: all species colours get OKLab chroma × 0.86. Scale-pattern alpha × 0.45. Bright
  spots are pulled 45 % towards the flank colour. Eye radius × 0.8. The iris is muted gold
  (`#a8904e`), or silver (`#b9bcb4`) for shiny pelagic fish.
* Body: 6-stop constant countershading ramp (belly, belly/side, side, side/back, back, dark spine),
  then the `fk_fish` patterns, then a 1px gill-cover arc at `t = 1 - (1 - peak) * 0.6`.
* Light: side sun `(-0.35, -0.8, 0.5)`, top sun `(-0.35, -0.3, 0.89)`. The light value drives a
  4-step **multiply** ramp `#878a9c #b6b8c4 #e7e5e3 #fffdf6` (cool shadow to warm light). A glossy
  Shader-to-RGB threshold adds a wet glint for `shine > 0.2`.
* Fins: 20 % towards the flank colour, lighter and less saturated (reads as translucent). Rays
  radiate from the object origin: `sin(atan2(z, -x) * 0.55 * px) > 0.35` darkens by one step. The
  origin is the tail base for the tail and pectorals, and the body centre for the other fins.
* Geometry, sizes (`px`), frames, top views and `TOP_USES_SIDE` are unchanged, so output is a
  drop-in for `Sprites/Fish`.

---------------------------------------------------------------------------------------------------

## 3. How to roll it out

### 3.0 Pipeline integration (once)
1. Copy `nat_core.py` to `Tools/Blender/fk_natural.py`. Keep `OUT` for previews, and point the
   writers at `C.SPRITES`/`C.DATA` as the current scripts do.
2. In `fk_stages.render_stage`, replace the flow with the `nat_lake.render_all` flow:
   * back render (front `visible_camera = False`) at SS3 with `mode_down(cover=0)`
   * mirrored reflection pass
   * front render at SS3 with `mode_down(0.45)` and no outline
   * `compose_water`
3. Stage builders then use `N.lit(...)` instead of `mtoon`/`mflat`/`mnoise`, `N.Bank` for foliage,
   `water_mat()` instead of `water()`, and `sky_mat()` instead of `sky()`.
4. In each stage JSON, set `waterTint` to the middle water band and `waterDeep` to the darkest
   underwater colour. `StageView.UnderwaterTint` then tints fish shadows correctly. The lake uses
   `#5a7882` / `#16262b`.
5. Unity: no code change is needed. Sprites keep point filtering and the same PPU and pivots.
   Semi-transparent shadow pixels under the angler blend with the default sprite shader.

### 3.1 The 7 stages
Common to all stages: the same light rig, ramps, haze, water and reflection pipeline. What changes
is the palette, haze, sun, props and water parameters.

* **lake**: done (reference implementation).
* **stream** (standH 1.4, boulder, converging banks, waterfall):
  * Water keys are cooler and clearer: `#b4c6c2 → #86a9a8 → #4f7775 → #3a5c5a`. Stronger streaks:
    stretch `(0.2, 0.9)`, threshold 0.6/0.4. Reflection alpha × 0.6 with more breaks (`thr - 0.15`)
    because the water is running.
  * White-water foam where the water is near rocks: in `compose_water`, lighten water pixels within
    2-3 px above rock pixels of the front layer.
  * Banks: `rock_lump` boulders with `wet_mat`, conifers, mossy ground heightfield.
  * Waterfall: a vertical quad with noise stretched `(1, 1, 0.08)` and a 4-colour white-blue ramp.
    Mist: 2-3 translucent flat bands at alpha 0.3, quantised.
  * Snowy mountains: mountain material mixed with a snow ramp `#e8edf1` where `|Z| > snowline +
    noise` and normal Z > 0.55.
* **sea** (standH 3, concrete jetty, tetrapods, lighthouse):
  * Water: blue-green `#aebfc6 → #6f95a6 → #3b6a7d → #2b5566`. Wind streaks plus a second, longer
    swell noise at 0.35 band. Glints about 120, biased towards the far half.
  * Concrete: `lit("#a9a497", tex .12, tex_scale 3)`. Tetrapods get strong AO (ao 0.7,
    ao_dist 0.6), which makes the pile read.
  * Lighthouse: desaturated stripes `#e6e2d8` / `#a8473d`. Horizon islands get haze.
  * The reflection pass covers the jetty, lighthouse and buoy.
* **swamp** (standH 0.9, overcast mist):
  * Light: sun energy 0.55π and ambient up 0.42 / hor 0.34 / down 0.15. The ramps get softer and
    shadows lighter. `HAZE = {col "#b9c1ad", dist 140, max 0.85}` gives thick mist, so trees fade
    within 60 m.
  * Water: murky olive `#a4ab94 → #6f7a5e → #4a5a3e → #3a4830`. Reflection alpha × 0.8 and less
    ripple (still water).
  * Trees: cypress trunks (`tube` with 4-6 buttress roots), `deciduous(kind = "willow")` crowns, and
    hanging moss as `Acc.ribbon` strands (droop, 1.2 px wide, `#8a9170`).
  * Lantern: flat `#f2d38a` with a 1px `#c9a45c` ring. There is no point light, so the look stays
    calm.
* **ice** (standH 0.25, winter dusk):
  * Low sun: `SUN_DIR = (-0.55, -0.4, 0.35)`. Ramps use `warm = 1.5`, `cool = 1.4` (golden light,
    long blue shadows).
  * Snow: `lit("#e9eef4", spread 0.6)` with snow caps on conifers. The cap uses the same material
    selected by a normal-Z mask (`ShaderNodeNewGeometry.Normal.z > 0.5`) mixed with the foliage
    ramp.
  * Ice sheet: `water_mat` with ice keys `#dfe8ee → #b9ccd8 → #9ab4c6`, the streak noise replaced by
    thin crack lines (noise distance < 0.02, dark `#7f9bb2`), and no reflection pass (ice is matte).
    The fishing hole uses the lake water keys, darkened.
  * Aurora: keep the translucent bands, but desaturate (`#7fc8a8`, `#8f86c8`) and quantise alpha to
    0.15/0.3.
* **ocean** (standH 1.7, boat deck):
  * Open-sea water `#b3c5d0 → #6f93ab → #34607f → #1f4561`. Swell bands. The reflection pass is
    limited to the boat hull.
  * Deck planks reuse the pier planks (`#a08a6c` teak).
  * Hull paint `lit("#e3e0d8", spread 0.7)` with a navy stripe `#34465e`. Rails are
    `lit("#aeb4b8", n = 4)` with a glossy glint.
  * Cooler `lit("#d9dcdf")`, life ring `#c65b45`.
* **cave** (standH 1.2, no sun):
  * Delete the sun. Add 3-5 point lights: a warm lantern near the angler, and cool lights next to
    crystal clusters. Shader-to-RGB picks them up, so all ramps and AO work unchanged.
  * Ambient: up/hor/down = 0.05/0.04/0.02.
  * Haze: `{col "#141b2a", dist 60, max 0.9}` (depth darkness instead of sky haze).
  * Crystals stay `flat()` emissive with a 2-step halo ramp.
  * Water keys: `#2b4150 → #1b2c38 → #121e27`. In the reflection pass, mirror the point lights too
    (negate their z).
  * Rock walls: `rock_lump` scaled up, with `wet_mat` bands.

### 3.2 The 7 angler poses
All 7 poses are already produced by `nat_character.py` with the same JSON schema.
* Per pose: `POSES[pose]` = hand targets `(forward, side, up)`, `lean` (+ is forward), `stance`
  (spread, near-foot forward, far-foot forward, knee bend), `pole_up` for raised arms, and `rod`
  (Unity direction, unchanged).
* Arms and legs use a 3D two-bone IK with pole vectors. The torso leans around the hips.
* To add a pose: add a dict entry. The crop, hand anchor, inner lines, outline and ground shadow
  follow automatically.
* Replace `Assets/Resources/Sprites/Character/angler_*.png` and `Data/character.json` together. The
  hand anchors moved because the body is now realistic. Measured sprite heights (outline included)
  are 76 px (fight) to 81 px standing, and 93 px for cheer (arms up).

### 3.3 The 36 fish
`nat_fish.py` with no arguments renders all 36 species (side frames 0/1 and top t0/t1) with the
current sizes and names, so it is a direct drop-in for `Sprites/Fish`.
* Per-species overrides can go in `natural_params`:
  * fantasy fish (`golden_carp`, `crystal_koi`, `cave_tetra`) can skip `soften` to keep their glow
  * `great_white` / `ocean_sunfish` top views read better at `w * 1.15` than `* 1.45`
* Top views stay dark-backed, so `UnderwaterTint` makes readable shadows on every water palette
  above. Checked on the lake at 12-18 m depth.

### 3.4 Island map (`fk_misc.build_map`)
* Keep the heightfield, markers and tilted ortho camera.
* Replace `C.toon_shade` with the light-value multiply ramp: sun `SUN_DIR`, ramp
  `#7f8596 #b3b6c0 #e6e4e0 #fffaf0` multiplied by the vertex colour.
* Swap the palette for natural colours:
  * water: deep `#2d5470`, mid `#3d6d86`, shallow `#5f8f9c`, lake `#557f8c`, river `#5f8f9c`
  * land: sand `#c9bb8f`, grass `#7a8a4e`, grass2 `#6c7c45`, forest `#4f6440`, rock `#8a857a`,
    rock2 `#77736a`, snow `#e8ecef`
  * swamp: `#5d6647` / `#4c5540`
  * ice: `#d8e2ea`
* Water gets 3 depth bands with Bayer transitions and a 1px foam line (`#b9c9c8`) where land meets
  water.
* Trees: tiny `deciduous()` (3-5 clumps, h 0.5-0.8) and `conifer()` (4 tiers) from `nat_lake`, in
  place of cone and lollipop.
* Village roofs: `#8a4a3a`, `#5a6a7a`, `#8a7a4a`. Walls: `#d8cfbd`.
* Lighthouse: desaturated stripes. Harbour/boat: hull `#e3e0d8`, stripe `#34465e`.
* Render at SS3 with `mode_down(cover=0)`. No outline, and no haze (orthographic map).

### 3.5 Aquarium room (`fk_misc.build_aquarium`)
* Interior light: a sun through the window from the left, `SUN_DIR = (-0.8, -0.35, 0.45)`, energy
  0.8π, plus a warm area or point light in the tank hood. Ambient up/hor/down = 0.25/0.2/0.12.
* Walls: plaster `lit("#d9ccb3", tex .06)`. The plank floor reuses pier planks in `#8a6a4a` tones.
  The cabinet is `lit("#6e5038")` with grain.
* Tank water: a vertical band gradient (`water_mat` with u = world Z), keys `#6f9fa8` (top) to
  `#274c5a` (bottom), with Bayer transitions. Light rays are translucent quads quantised to alpha
  0.12, with 2-3 caustic streaks at the surface.
* Gravel: 60-80 small `rock_lump` pebbles in 4 muted stones with AO 0.7 (contacts read).
* Plants: `reed_clump`-style ribbons in sage and olive ramps, and 1-2 driftwood `tube`s.
* Replace the toy castle with a slate rock arch (two `rock_lump`s and a flat lintel) for a
  grown-up look.
* Front layer: tank frame `lit("#2f353c", n = 4)` with a glossy glint, and glass streaks at alpha
  0.18.
* The fish inside the tank use the natural side sprites.

---------------------------------------------------------------------------------------------------

## 4. Gotchas
* Shader-to-RGB is EEVEE only, so keep `BLENDER_EEVEE`, 1 TAA sample, `filter_size 0`, the
  Standard view transform and `dither_intensity 0`. Otherwise the mode filter and the exact water
  colour keys break.
* AO at 1 spp is noisy. The SS mode filter removes it. Do not render sprites at 1x without SS.
* Keep `sys.dont_write_bytecode = True` before importing the fk modules (no `__pycache__` writes).
  Keep scratch renders in a private folder, because `fk_common.render_sprite` shares `_tmp/raw.png`.
* Reflection pass: exclude objects that lie *on* the water (pads) and very distant objects
  (hills/mountains mirrored below the treeline looked like fog).
* Front-layer objects cast shadows onto the back layer's water only through `visible_camera =
  False`. Do not use `hide_render` for that pass.
