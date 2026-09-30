# FishingKing art direction: "hybrid" (classic 16-bit + cinematic mood)

Chosen direction: *"Classic 16-bit is good, but blend in some Cinematic mood."*
Base = retro16 craft (curated limited palettes, palette-ramp materials, Bayer dither only in gradients,
dash-dithered water bands, clean clusters, selective hue-shifted outlines, the ~6-head angler and its pose
table, reeds / pads / rowboat / pier). Mood = cinematic techniques driven by a **time-of-day preset**
(banded sky with a pixel sun and dithered glow rings, aerial-perspective haze per distance, a mist band on
the far waterline, sparse far-third glitter, exact mirrored reflections, warm rim on the outer sun-side
silhouette, teal-orange grading of the curated palettes).

Scripts: `Tools/Blender/variants/hybrid/` - renders: `Tools/Blender/_tmp/variants/hybrid/`
(`work/` = scratch EXR passes and zooms). Nothing here writes into `Assets/` and no `Tools/Blender/fk_*.py`
file is modified (fk_common / fk_persp / fk_fish / fk_character are imported read-only; fk_fish materials are
monkeypatched inside the hyb_fish process only).

| file | what |
|---|---|
| `hyb_core.py` | **the kit**: colour maths, palettes, presets, sky / water / mist / glitter / shaft helpers, materials, geometry, camera helpers, render passes + mirror pass, quantise / dither / despeckle / contour / outline / rim, stage JSON writer, sheet helpers |
| `hyb_lake.py` | lake stage (preset `lake`): `lake_back.png`, `lake_front.png`, `stage_lake.json` |
| `hyb_character.py` | angler, 7 poses: `character/angler_<pose>.png`, `character/character.json`, `character_sheet.png` |
| `hyb_fish.py` | all 36 fish: `fish/<id>_0/_1/_t0/_t1.png`, `fish_sheet.png` (+ `work/fish_all.png`) |
| `hyb_preview.py` | `preview_lake.png` (game crop 480x270 at 2x, 3 fish shadows, angler, rod) + shadow/water luminance print |
| `hyb_presets.py` | `presets.png`: one mood plate per preset, built only from the kit (lake, stream / sea, swamp / ice, ocean / cave) |
| `hyb_check.py` | sanity check vs the game data (fish sizes, character.json fields, stage JSON keys, colour counts) |
| `hyb_zoom.py`, `hyb_review.py` | inspection helpers (nearest zoom of a region, review sheet) |
| `build_hybrid.ps1` | rebuilds everything (fish -> character -> lake -> preview -> presets -> check), ~90 s |

---------------------------------------------------------------------------------------------------

## 1. Time-of-day presets (`hyb_core.PRESETS`)

All sky/sun/glow/streak/aurora/shaft sizes are **screen pixels of the 640x400 stage canvas** (the stage camera
is fixed, `HORIZON_ROW` = 89.5 for every standH): `up` = px above the horizon, `dx` = px right of the image
centre. The game shows `CROP` = (80, 65, 480, 270), i.e. only ~25 px of sky above the horizon - keep suns,
glows and aurora low (up <= ~20). Distances (haze) are metres.

| field | meaning |
|---|---|
| `sky` | `[(up_px, hex), ...]` horizon -> zenith stops; flat bands with narrow dithered transitions (`sky_soft` = transition width as a fraction of the band) |
| `sun` | `dict(dx, up, r, core, limb)` pixel sun disc, or `None` (no disc) |
| `glow` | `dict(col, rings=[(radius_px, level)], soft, flat[, dx, up])`: concentric flat glow rings (level = mix towards `col`), `flat` < 1 squashes them horizontally; `dx/up` only needed when `sun` is None |
| `streaks` | thin stratus lenses `dict(up, x0, x1, th, col, lit)`; the underside row is `lit` near the sun |
| `aurora` | `dict(cols=[dim, bright], up0, height, amp, amount, x0, x1)` or None |
| `shaft` | screen-space light shaft `dict(col, amount, top=(x, row, width), bottom=(x, row, width), fade)` or None (`apply_shaft`) |
| `key_dir` | world vector TOWARDS the key light, default light of every `m_tone` material (sun azimuth, lifted for readability) |
| `key_col`, `shadow_col` | their OKLab hues are the targets of ramp hue shifting (`shade`, `ramp_from`, `Pal.darker`): lighter tones lean to the key hue, darker tones to the shadow hue |
| `rim` | `dict(col, strength, dirs={'r': w, 'tr': w, 't': w, ...})` outer-silhouette rim towards the light |
| `haze` | `dict(col, near, dist, max)`: aerial perspective `f = max * (1 - exp(-(d - near) / dist))` |
| `mist` | `dict(col, amount, above, below, wisp)` band on the far waterline (px decay over land / water) |
| `glitter` | `dict(cols=[amber, bright], far, dens, width=(far_px, near_px), maxlen[, dx, row0, row1])` or None |
| `water` | `dict(bands=[6 hex far -> near], dark, refl=[2 hex], tint, deep)`; `tint` / `deep` go into `stage_<id>.json` |
| `grade` | `dict(shadow=(r,g,b), high=(r,g,b), sat, contrast, amount)` teal-orange split toning applied to curated palettes |
| extra | `crystal` (cave: 2 crystal glow colours for local lights) |

| preset | mood | sun / light | sky (horizon -> zenith) | haze / mist | water (far -> near), glitter | rim |
|---|---|---|---|---|---|---|
| **lake** | golden hour | disc dx +122, up 7.5 (in an open bay right of centre), 4 glow rings; key from the right, lifted `(0.70, 0.10, 0.70)` | gold -> amber -> coral -> rose -> lavender -> dusty blue -> deep navy | warm peach `#dfa58e`, 900 m; thin peach mist, 2.6 px up / 2.2 px down | peach -> mauve -> lavender -> teal-blue `#456a8a` play area; glitter far 30 %, sparse | `#ffcf8c`, 0.55, right/top |
| **stream** | clear early morning, valley mist | pale disc dx -70, up 16; key from the left `(-0.62, 0.15, 0.77)` | pale gold -> cream -> mint -> sky blue | cream-green, 500 m; THICK mist (0.95, 6 px up) | cream -> sage -> green-teal `#3e7a78`; short sparse glitter | `#fff2c8`, 0.4, left/top |
| **sea** | bright late afternoon | disc dx -230, up 72 (above the crop: only its glow shows); key from the left, high | hazy white -> pale cyan -> blue | pale blue-grey, 2500 m; light mist | silver-cyan -> deep blue `#2c7aa4`; wide glitter at dx -150 | `#fff4de`, 0.3, left/top |
| **swamp** | foggy dusk | no disc, dull amber glow behind the fog at dx -40; key high & dull | sickly amber -> olive grey -> dark green-grey | olive `#96946e`, **140 m** (fog eats the far layers); mist 0.95, 8 px | olive -> murky green `#3a4a30`; no glitter | weak `#d8c088`, top |
| **ice** | blue hour with aurora | no disc, rose afterglow at dx -140; aurora band up 9-26; key cool from above-left | rose -> lilac -> periwinkle -> deep blue | lavender, 1800 m; light lilac mist | ice: pale lilac -> periwinkle `#6a8ab8`; no glitter | pink `#ffc8d0`, 0.3, left |
| **ocean** | sunrise | disc dx -90, up 6 (pink-gold); key from the left, low | peach-gold -> pink -> mauve -> azure | warm peach, 3000 m; light mist | rose-silver -> deep blue `#1e5a8a`; long glitter path | `#ffd8b0`, 0.5, left/top |
| **cave** | crystal glow + one light shaft | no sky: dark violet rock gradient; one daylight shaft from a ceiling hole (dx ~-50); key from the shaft above | `#1e1830` -> `#0c0a14` | near-black violet fog, 60 m | dark blue-teal, glitter only under the shaft (rows 180-330) | cyan `#86dcff` (crystal light), top |

**Time-of-day periods** (Docs/time_currents_spec.md): every stage also renders dawn / day / evening / night looks
through `hyb_period.py` (`-- --period <p>`; `periods/<stage>.py` holds each stage's overrides, `periods/README.md` the
API; `build_periods.ps1 -Stage <stage>` renders them + the review sheet). Kit keys added for them (absent = unchanged):
`grade.gain` / `grade.tint` (exposure / light colour of the graded palettes, linear light), `stars=dict(cols, dens, up0,
seed, bright, glow_max)` in `sky_rgb`, and `"__whole__": True` in an override dict to replace a sub-dict.

**Obstacles** (Docs/obstacles_spec.md 13): `hyb_obstacles.py -- <stage>` (or `build_obstacles.ps1 -Stage <stage>`)
builds a stage's scene WITHOUT rendering (its own render function stopped at the first render pass), stamps the tags of
`obstacles/<stage>.py` (`obst` custom properties), writes `Assets/Resources/Data/obstacles_<stage>.json` in game space
and checks it against the painted `<stage>_front.png` (overlay: `_tmp/variants/hybrid/obstacles/`). No stage file changes.

`presets.png` shows every preset as a synthetic plate (sky, three hazed layers with a valley under the sun,
water bands, mirrored sky, glitter, mist, shaft) with its key / shadow / rim / haze / mist / water swatches.

---------------------------------------------------------------------------------------------------

## 2. Kit API (`import hyb_core as R`)

**Never edit `hyb_core.py` from a stage script.** Change a preset for one stage with
`R.use_preset("sea", sun=dict(dx=-180), glitter=dict(dens=0.3))` (sub-dicts merge), or build a new
`R.Preset(...)` / `R.PRESETS["sea"].copy(...)` inside the stage script.

Presets / grading
* `use_preset(name_or_preset, **overrides) -> pr` - call FIRST (module level, before any material or
  `shade`): sets `R.PR[0]`, the default key light `R.LIGHT` and the ramp hue targets `R.SHIFT`.
* `grade_hex(cols, pr=None, amount=None)` - teal-orange grade of a hex / list (use `amount` < 1 for assets
  shared between stages: angler 0.55, fish 0.35).
* `haze_f(dist)`, `hazed(cols, dist, extra=0.0)` - aerial perspective of a curated ramp (farther = lighter,
  warmer / hazier). `extra` may be negative (keep a far silhouette readable against the sky).
* `sun_rc()` (sun / glow centre col,row), `sun_side()` ('right' / 'left', from `key_dir`).
* colour helpers: `hexrgb, tohex, s2l, l2s, oklab, oklab_inv, lum (OKLab L), hue_of, shade, ramp_from, mixhex`.

Palettes (palette-index images, `-1` = transparent)
* `Pal(hexes)`: `.hex`, `.srgb`, `.lab`, `.index(hex)`, `.darker(i, steps)` (hue-shifted darker entry),
  `.extend(rgbs, snap)`.
* `recolour(idx, pal, mask, fn, snap)` - palette-space edit, one new colour per SOURCE colour (palettes stay
  small); `blend_idx(idx, pal, mask, target, k, lighter=False, space="linear"|"srgb")`.
* `quantize(rgb, a, pal, dither=True, thr=None)` - 2-colour ordered dither along palette segments
  (Bayer by default; `thr=dash_threshold(...)` for horizontal-run dithering on water / mist).

Sky / water / atmosphere (numpy, screen space)
* `sky_rgb(pr, mirror=False) -> (rgb, palette hexes)` - banded sky + glow rings + streaks + aurora + sun disc;
  `mirror=True` = the sky as mirrored by calm water (for the far water; no disc - glitter replaces it).
* `glow_level(pr)` - glow ring level per pixel (use it as "sun proximity" for rims / sheens).
* `water_bands(pal, bands, zones, rng, row_h, row_p)` - band index image, zones `[(row0, row1), ...]`
  joined by dash dithering; `ramp_step(pal, ramp, arr, d)` moves along a ramp (wave marks, ripples).
* `glitter_masks(pr, water, rng, avoid=None) -> (amber, bright)` - sparse short dashes in the far part of
  the water under the sun column (`far` fraction), no pillar, no rays.
* `mist_amount(pr, line, land, water)` - per-column far waterline -> amount; apply with
  `blend_idx(idx, pal, (amount > dash_threshold(...)) & (amount > 0.08), pr.mist["col"], 0.5)`.
* `light_shaft(pr)`, `apply_shaft(idx, pal, pr)` - translucent shaft (2 levels + dust), sRGB mixing.
* `dash_mask, run_noise, dash_threshold, min_runs, dash_len` - the retro16 horizontal-dash primitives.

Materials / geometry (Blender)
* `reset_materials()`, `m_tone(cols, bounds, soft, light=None -> preset key, wrap, lam, bias, grads, noise,
  pats, spec, ...)`, `m_flat(hex)`, `m_grad(...)`, `band_ramp`, `id_material()`; `USED` = colours used.
* `mesh_from, loft, ellipsoid, box, hpoly, vpoly, ellipse_pts, tagk(ob, kind, grp)`.
* camera: `rc(p, stand)`, `col_to_x(col, y, stand)`, `water_row(dist, stand)`, `z_for_row`, `world_z`.

Render / post
* `render_passes(tag) -> dict(rgb, a, id, depth, kinds)`, `kind_map(ps)`, `mirror_pass(objs, ids, tag)`
  (EXACT reflection: mirrored copies z -> -z through the same camera).
* `despeckle`, `inner_lines`, `remove_specks`, `outer_outline(idx, pal, lit_steps, dark_steps, light=sun_side())`
  (hue-shifted darker neighbour tones - never ink), `rim_light(idx, pal, pr, strength, mask, weight,
  levels)` (outer silhouette only via `outer_empty`, sun-side dirs of the preset, skips 1 px-thin parts),
  `to_rgba`, `save_png`, `load_png`, `count_colours`.
* `stage_json(stage_id, pr, clouds=False, birds=None)` - copies `Data/stage_<id>.json` and changes ONLY
  `waterTint` / `waterDeep` (from the preset) and `clouds` / `birds`. Same keys, same order.
* sheets: `upscale, over, affine_nn, line, sheet, flow_sheet`.

---------------------------------------------------------------------------------------------------

## 3. How to build another stage (recipe - follow `hyb_lake.py`)

Name the script `hyb_<stage>.py` in this folder, outputs `<stage>_back.png`, `<stage>_front.png`,
`stage_<stage>.json` (and `preview_<stage>.png` via a copy of `hyb_preview.py` with the stage id, standH
and fish points changed). Use `fk_persp.setup_camera(standH)` with the standH of `Data/stage_<id>.json`.

1. `PR = R.use_preset("<stage>")` at module level; `G = R.grade_hex`.
2. **Palettes** (curated, retro16-style 2-5 colour ramps per material): near props `G([...])`; every far
   layer `R.hazed(G([...]), distance)`; sky `SKY_RGB, SKY_PAL = R.sky_rgb(PR)` and
   `MSKY_RGB, MSKY_PAL = R.sky_rgb(PR, mirror=True)`; water `PR.water["bands"]`, `WSTEP = [dark] + bands[::-1] + [cap]`.
   Back palette = sky + mirrored sky + water + all layer ramps (+ glitter); front palette = prop ramps.
3. **Composition**: put the sun column over OPEN water (a bay / gap in the near shore, a valley in the far
   layers: see `GAP_C` / `VAL_C` and `ridge_mesh(valley=...)`), layer 3-4 silhouettes by distance, keep the
   middle ~60 % of the water empty (props hug the sides), no geometry below z = 0 except posts/stems.
   Tag every object with `R.tagk(ob, kind, grp)`: the post uses the kinds.
4. **Back pass** (order matters): `render_passes` -> sky pixels (`~ps["a"]`) = `SKY_RGB` -> `quantize`
   (Bayer) -> `despeckle` -> `inner_lines` on trees / buildings -> far rims near the sun (`glow_level`) ->
   `water_bands` -> mirrored sky in the far water (`bay_sky`) -> exact reflections of the far layers
   (`far_reflection`: `mirror_pass` -> quantize -> `min_runs` -> tint towards the water + darken, lighter
   break lines every 3rd row, dissolve over the bottom 25 %) -> wave marks -> front overlays (reflections /
   ripples / pad lines of front objects) -> sheen + `glitter_masks` -> mist -> (`apply_shaft`) -> save.
5. **Front pass**: `quantize` -> `remove_specks` -> `despeckle` -> `inner_lines` -> hand shading (pads) ->
   `rim_light(mask=solid kinds)` (before the outline!) -> `outer_outline(light=R.sun_side(PR))` on solid props
   only (no outline on reeds / pads) -> overlay codes for the back layer -> save. Render the front first
   (the back consumes its overlay).
6. `R.stage_json("<stage>", PR, clouds=False, birds=...)` (clouds stay false unless matching cloud sprites
   are rendered).
7. **Checks** (print them like `hyb_lake` / `hyb_preview`): pads / floating props OKLab L > the local water
   band L (+0.03); fish shadow L clearly below the play-area water L (lake: 0.28 vs 0.47-0.51); back <= ~90
   colours, front <= ~40; look at the preview crop and a 3x zoom of the far shore.

Rules that came out of the reviews (do not regress):
* glitter only in the far ~third, sparse short dashes, fading towards the viewer; no rays / pillars over
  the play area;
* rim ONLY on the outer silhouette (`outer_empty`, min gap 3 px) on the sun-facing sides; never on 1 px parts;
* no near-black ink outlines on characters / props (hue-shifted darker neighbour tones);
* lily pads / floating things always LIGHTER than the water; only fish shadows are darker;
* no gloss / specular stripes on fish; one limited palette per sprite;
* reflections: break lines use the reflected colour pushed towards the water, never the raw band colour
  (that reads as bright noise).

Island map / aquarium (different cameras): use the same kit with a preset override, e.g.
`use_preset("sea", key_dir=(0.5, 0.4, 0.77))` for the map (ortho camera: ignore `sky_rgb` / `water_row`;
ocean = `water_bands` by distance from the coast instead of rows, `glitter_masks` with `glitter=dict(dx=...,
row0=..., row1=...)`, `rim_light` + `outer_outline` on villages / trees, `hazed` not needed) and
`use_preset("lake", shaft=dict(...), key_dir=(-0.4, -0.3, 0.86))` for the aquarium (window content =
`sky_rgb(PR)` cropped, lamp light = `apply_shaft`, the fish sprites unchanged).

---------------------------------------------------------------------------------------------------

## 4. What the lake / angler / fish do

* **Lake** (`hyb_lake.py`): retro16 layout (pier, reeds, pads, rowboat, far-shore tree row with a cabin),
  but the far shore opens into a bay under the sun (dx +122); distant range 2400 m (hazy lavender), hills
  700 m (hazy mauve-green), back tree bands (hazy teal) and the dark backlit front row with olive-gold tops;
  a warm-lit cabin window; warm rims on silhouette tops near the sun; mirrored sky + glow in the bay water;
  exact reflections; thin peach mist; glitter in the far third. Play-area water `#456a8a` (L 0.51), fish
  shadows L 0.28. Pads L >= 0.59. Colours: back 84, front 34. JSON: waterTint `#456a8a`, waterDeep
  `#10283a`, clouds false, birds true (everything else identical to `Data/stage_lake.json`).
* **Angler** (`hyb_character.py`): retro16 model and pose table; palette graded at 0.55; key from the right
  (back in the mid tone, right flank / hat top lit, left side cool shadow), subtle rim (preset strength x 0.62,
  1 level, fading from hat to boots), hue-shifted outline (darkest tone `#2a2638`-ish violet navy, no ink).
  16 base colours + 9-10 rim tones per pose. The rod is held in the LEFT hand (character.json's hand = left
  hand; rod directions = fk_character's with rx negated, leaning up-left) and the RIGHT hand cranks the reel:
  its reel / reel2 / fight targets are solved on screen against where Angler.cs hangs the reel sprite
  (`game_reel`, knob px from `fk_items.py worldreels`), so the fist lands on the knob (report: `-- --dry`,
  debug px in `character_grip.json`).
* **Fish** (`hyb_fish.py`): retro16 craft; ramps hue-shifted with the lake key / shadow hues, species colours
  graded at 0.35, outline = the back colour 0.28 darker (hue-shifted), no spec. 10-18 colours per sprite.
  All 144 files match the pixel sizes of `Assets/Resources/Sprites/Fish` (checked by `hyb_check.py`).

Known limits
* The back layer uses 84 colours (sky bands x glow rings + hazed layers + reflection tints). That is within a
  16-bit BG budget (several 16-colour sub-palettes) but more than retro16's 32; raise `snap` in
  `recolour` calls or drop a glow ring if a stage needs fewer.
* The character's rim is on the right (lake / most sunsets). Stages lit from the left (stream, sea, ocean)
  still use the same sprites - the rim is subtle enough to read as ambient sky light.
