# retro16: classic 16-bit style

This variant aims for the look of carefully hand-pixelled SNES art. It avoids the chibi, toy-like look. Each asset uses a small curated palette with hue-shifted ramps. Gradients use ordered Bayer 4x4 dithering. Pixel clusters are hard-edged with no anti-aliasing, and outlines are selective rather than black everywhere.

Everything is still generated from Blender geometry through the same camera, canvas and layer split, so it can replace the current pipeline stage by stage.

Scripts are in `Tools/Blender/variants/retro16/` and renders are in `Tools/Blender/_tmp/variants/retro16/`. To rebuild everything, run `build_retro16.ps1`. That takes about 1.5 minutes: roughly 55 s for the 36 fish, 20 s for the lake and 8 s for the 7 poses.

| file | what it does |
|---|---|
| `r16_core.py` | palettes and OKLab hue-shifted ramps, palette-ramp materials, colour/ID/depth passes, numpy post-process, sheet helpers |
| `r16_lake.py` | lake back and front layers, plus `lake.json` (same layout as `stage_lake`) |
| `r16_character.py` | the new angler: 7 poses and `character/angler.json` (same schema as `Data/character.json`) |
| `r16_fish.py` | all 36 species using `fk_fish` geometry with retro materials: `fish/<id>_0/_1/_t0/_t1.png` |
| `r16_preview.py` | the game-crop composite `preview_lake.png` (uses the in-game shadow tint formula) |
| `r16_zoom.py`, `r16_review.py` | inspection helpers |

The `fk_*.py` modules are imported read-only. `r16_fish.py` swaps `fk_fish.body_material`, `fin_material`, `fk_common.toon_material` and `glow_material` in memory for its own process only. `sys.dont_write_bytecode` is set, so nothing is written next to the shared scripts.

---

## 1. Techniques

### 1.1 Palette and hue-shifted ramps

- Every asset has a hand-curated palette defined as named ramps:
  - **Lake back layer:** 32 colours, 31 used.
  - **Lake front layer:** 32 colours, 29 used.
  - **Angler:** 16 colours.
  - **Each fish:** 13–17 colours, generated from its `col` dict.
- `shade(hex, dl)` and `ramp_from(base, dark, light)` build ramps in **OKLab**:
  - **Darker steps:** L goes down by about 0.1 per step, the hue rotates up to 18° towards 285° (blue-violet), and chroma rises slightly in the mid-darks.
  - **Lighter steps:** L goes up by about 0.075, the hue rotates towards 95° (warm yellow), and chroma drops.
  - So shadows are cool and saturated and lights are warm, instead of `base × grey`.
- `Pal.darker(i, n)` picks the palette colour that is `n` steps darker and hue-shifted. Outlines, contour lines and ripple marks use it. No colour outside the palette ever appears.

### 1.2 Materials: exact palette colours, not shading multipliers

`m_tone(cols, bounds, soft, light, grads, noise, pats, nvec)` computes one scalar and passes it through a colour ramp of palette hexes:

- The scalar `f` is: `lambert·lam + Σ axis gradients + noise + stripes + bias`.
- **Hard bands** (`soft = 0`, CONSTANT ramp) render exact palette colours. This is used for all solid objects.
- **Soft bands** (`soft > 0`) add a narrow LINEAR zone between two neighbouring ramp colours. The in-between colours are what the post-process turns into ordered dither. This is used for mountain haze and fish belly-to-back blends.
- **Noise on the tone scalar** makes the band borders clump into pixel clusters. This is how foliage, cloud puffs, hill canopy, fabric folds and wood grain work (`nvec=(9, 0.35, 1)` stretches noise along the planks).
- **`pats`:** stripe masks lower the tone for log walls, wicker, clinker strakes, the plaid shirt, bucket ribs and rope twists.
- **Key light:** `LB = (-0.72, -0.32, 0.62)` for scenery and `CL = (-0.72, -0.4, 0.56)` for the angler. Both are low and from the left, so surfaces facing the camera sit in the mid tone, the left edges catch the light and the right side falls into shadow. That gives form without a uniform 3-band toon look.

### 1.3 Render passes (`render_passes`)

1. **Colour pass:** EXR, 32-bit linear, converted to sRGB in numpy. Hex values round-trip exactly.
2. **ID/depth pass:** every mesh object gets an `ID` emission material that writes `(ObjectIndex % 1024, View Z depth, ObjectIndex // 1024)`.
   - `pass_index` is unique per object, or shared via the custom property `grp`. For example, a whole tree gets one `grp`, so there are no lines inside its canopy.
   - The custom property `kind` (for example `tree`, `water`, `sky`, `reed`) is returned as an id-to-kind map. Post rules are chosen by kind.
3. **Mirror pass** (`mirror_pass`): copies of the chosen objects, scaled by −1 in Z about the water plane, rendered alone. This gives perspective-correct reflections, which are then restyled in 2D.

### 1.4 Post-process (numpy, in this order)

- **`quantize`:** for each pixel it finds the nearest palette colour, then the palette segment that best contains the pixel. The tie-break prefers short segments, so the dither always mixes neighbouring ramp colours.
  - It flips to the second colour when `t > Bayer4x4[y%4, x%4]`.
  - Exact palette pixels have t = 0 and never flip, so dithering only happens where a gradient was authored.
  - Everything happens in linear RGB, so a 50% checker matches the physical mix.
- **`remove_specks`:** drops 8-connected islands smaller than 4 px (broken bits of sub-pixel geometry). Flowers are kept.
- **`despeckle`:** replaces orphan pixels inside a single object with the majority of their neighbours. Dither pixels (`~solid`) are protected.
- **`inner_lines`:** where two objects meet with a depth jump, the farther pixel becomes one ramp step darker. It reads as a hand-drawn contour or occlusion line, and it separates tree crowns, arms from the torso, fins from the body, and so on.
- **`outer_outline`** (selective outline, drawn 1 px outside the silhouette):
  - **Shadow side (right and below):** 2 ramp steps darker, or the asset's ink colour.
  - **Lit side (left and above):** 1 step darker (2 for the angler).
  - This replaces the old uniform heavy dark outline.
- **Sky and water gradients** are done in screen space. `band_rows` gives flat colour bands, with linear transitions of 3–20 rows at fixed canvas rows, and the result goes through the Bayer quantiser. The rows are chosen from the projection (see §2), so the depth bands follow the perspective.
  - Sky: `SKY_B = ([14, 30, 46, 61, 74, 85], [10, 10, 10, 8, 7, 5])`
  - Water: `WATER_B = ([111, 120, 136, 166, 218, 292], [3, 5, 7, 10, 14, 20])`
- **Reflections:**
  - The mirror-pass mask is cleaned with a 3-px horizontal majority, then recoloured by brightness into the dark water ramp (`WATER[0..2]`).
  - Rows shift by −1/0/+1 px in a 6-row cycle.
  - Every 5th row is broken by runs of `WATER[3]`, which gives the classic 16-bit ripple scanlines.
  - In the front layer, reeds, posts, the boat and the stake get the same treatment, plus light ripple dashes at their feet.
- **Wave marks and glitter:** random water points are projected, and dash length is `520/depth × 0.35–0.9 m`. Each mark is a light dash (one ramp step lighter) with a dark dash one row below and 1 px right. White glitter follows the sun direction on the right third.
  - Density is cut by 80% in the central play rectangle (x 190–450, y 140–330) and by 55% beyond 90 m.

### 1.5 Geometry changes (the "not childish" part)

- **Angler.**
  - **Proportions:** about 6 heads and 78 px tall, with 0.86 m legs.
  - **Build:** elliptical lofted torso, hips and legs (`loft`, with rx ≠ ry), a slim neck, a small head with hair and ears.
  - **Clothes:**
    - canvas bucket hat with a band and a sloped brim;
    - brick-red flannel shirt with a plaid stripe pattern and rolled sleeves (sleeve, cuff, bare forearm);
    - olive fishing vest with a yoke seam and a back cargo pocket with flap;
    - slate work trousers with noise folds and bunched hems;
    - leather boots with ink soles.
  - **Gear:** a wicker creel with lid and latch on the left hip, hung from a diagonal leather strap over the right shoulder.
  - **Posing:** the IK targets and rod directions come from `fk_character.POSES` (× 0.92), so the hand positions in the game stay as they are. Elbows flare slightly.
  - **Palette (16 colours):** ink `#1c1622`; skin `#a95c4a #d08c68 #f0bc94`; leather `#3c2826 #6e5a3c`; khaki `#6e5a3c #a48c60 #d4c08e`; olive `#343e2c #56623c #808c56`; shirt `#4a2432 #7c3a3c #a95c4a`; slate `#2a2c38 #434858 #606878`.
- **Trees** (replacing lollipops and plain cones):
  - Conifers are 5–7 star-shaped tiers: 12-point rims with alternate tips pushed out and drooping.
  - Broadleaf crowns are 7–10 icosphere puffs.
  - One `grp` per tree, a 5-tone forest ramp with cluster noise, and contour lines between trees.
- **Hills and mountains:**
  - `ridge_mesh` builds faceted 3D ridges (sum of four sines), with a Z haze gradient into the horizon colour.
  - The hills get 420 small crown bumps along the crest and the near slope, which gives a forested, clustered silhouette.
- **Far shore:** a grass bank with a dark earth cut, waterline rocks, a log cabin (log stripes, gable roof with shingle stripes, stone chimney, window, door), a small dock and a skiff.
- **Pier:**
  - 10 separate planks with random height, length and tone bias, stretched-noise grain, and a dark deck base so the 1-px gaps read as lines.
  - Side stringers, and tall end mooring posts with rope wraps.
  - A rope hanging into the water, a galvanised bucket (ribs, water, bail) and a teal tackle box with lid, handle and latch.
- **Reeds:** tapered leaf blades that bend over near the top, and cattails (stem, brown head, spike). Their width is clamped to at least 1.25 px at their distance, so they never break into dotted chains.
- **Lily pads:** slightly domed discs with a real V notch. The normals make a lit rim and a shadowed rim. Some have a 7-petal flower with a yellow stamen.
- **Rowboat:** a lofted clinker hull. It uses a `Backfacing` material: teal paint with strake lines outside, bare wood inside. It has gunwales, thwarts and oars, and a sagging mooring rope to a stake.
- **Fish:**
  - Back, side and belly each get a 3-tone ramp, chosen by banded lambert.
  - The regions blend in dithered zones (belly line ±0.1, back 0.6–0.8).
  - Patterns are shaded with their own ramps.
  - Scales are staggered 1-px rows (cells about 3.2 × 2.4 px).
  - There is a 1-px gill-cover arc, and fin rays every 2.6 px.
  - Eyes are painted: 2×2 with a catch-light, or a 3×3 ring for fish over 44 px.
  - The ink outline is the back colour at L−0.3.
  - Sprite sizes and file names are identical to the current `Sprites/Fish` set; `r16_check.py` verified all 144 files.

---

## 2. Useful numbers (camera is unchanged)

With standH 1.0:

| what | canvas row |
|---|---|
| horizon | 89.5 |
| far shore (y = 180 m) | 105.8 |
| y = 100 m | 117.5 |
| y = 60 m | 133 |
| y = 30 m | 164 |
| y = 16.5 m | 200 |
| y = 8 m | 248 |
| y = 3 m | 302 |
| feet | 313.8 |
| bottom of the game crop (row 335) | y = 1 m |

The game crop is x 80–560, y 65–335. `rc()`, `y_for_row()` and `z_for_row()` in `r16_lake.py` convert between world positions and rows.

---

## 3. Applying the style to the real game

### 3.1 Common recipe for any stage (7 stages)

1. Copy the stage function from `fk_stages.py` into `r16_<stage>.py`, using `r16_lake.py` as the template.
   - Keep `STAND`, the positions, the layout dict and `front()` membership.
   - Put front objects in `front_scene`, and tag each object with `kind` and `grp`.
2. Swap the materials:
   - `mflat` becomes `R.m_flat(pal_hex)`.
   - `mtoon(col)` becomes `R.m_tone(ramp, bounds, light=LB)`, where ramp is a curated 3–5 colour ramp; use `R.ramp_from(col)` as a first draft, then hand-tune.
   - `mnoise` becomes `m_tone(..., noise=0.1–0.25, nscale)`.
   - `mwood` becomes the plank builder or `pats` stripes.
   - `mgrad` becomes either a numpy `band_rows` (for sky or water), or `m_tone(grads=..., soft=...)` (for haze on objects).
3. Swap the helpers:
   - `S.pine` / `S.round_tree` become `conifer` / `broadleaf`.
   - `reeds` becomes `reed_clump`, `lily` becomes the notched `lily`.
   - Mountains become `ridge_mesh` plus haze.
   - Clouds become puff clusters with `CLOUD` ramp noise.
4. Choose the per-stage constants: `SKY`, `SKY_B`, `WATER` (deep→light, 6–7 colours) and `WATER_B`. Also set `waterTint` and `waterDeep` in the JSON so the fish shadows use the new water colours.
5. Post the back layer as in `render_back`:
   - quantize with Bayer, despeckle, contour lines for `tree`/`cabin`/`dock`;
   - mirror pass for the shore objects;
   - reflections, wave marks and glitter.
6. Post the front layer as in `render_front`: specks, despeckle, contour lines, selective outline, then mirror-pass reflections and ripple dashes for anything standing in water.
7. Keep each layer's palette at 32 colours or fewer, and share the water ramp between the back and front layers.

To ship a stage, point the output path at `Sprites/Stages/<id>_back|front.png` and `Data/stage_<id>.json`.

### 3.2 Per-stage notes

- **Stream.**
  - Snowy peaks: add a snow tone as the lightest colour of the MTN ramp above a Z gradient.
  - Waterfall: a vertical white/cyan `m_tone` with `pats` stripes along Z and soft bands, so it dithers into the pool.
  - Foam: `CLOUD`-ramp puffs.
  - Boulders: `ROCK` 3-tone plus noise, with contour lines.
  - Banks: bank mesh with noise, and conifers.
  - Rapids: wave marks around each rock (light and dark dash pairs, denser downstream).
  - Water ramp is teal: `#1e4a52 #2a6068 #3a7a7e #52968e #74b2a6 #a0d0c4`.
- **Sea.**
  - Concrete jetty: a grey ramp with noise.
  - Tetrapods: a 3-tone grey with contour lines.
  - Lighthouse: `pats` red and white stripes.
  - Islands: `ridge_mesh` with strong haze.
  - Water: deep blue bands, and a glitter path under the sun. Make the band row at y > 600 m 1–2 px wide so it reads as horizon glare.
  - Buoy: a 3-tone red ramp.
- **Swamp.**
  - Olive-teal palette.
  - Mist: after quantising, overlay a numpy band that blends the mist colour at 25% and 50% with the Bayer mask (horizontal bands at the far shore).
  - Cypress: broadleaf puffs on a flared trunk, with hanging-moss strands (thin lofts with a light-green ramp, min width 1.25 px).
  - Murky water: 5 bands with low contrast. Keep the lantern flat bright colours.
- **Ice.**
  - Ice sheet: numpy row bands along Y, like the water (pale blue to white). Snow patches are soft noise clusters.
  - Cracks: 1-px dark dashes in `Pal.darker`, with a light dash next to them for a bevel.
  - Aurora: `m_tone(grads, soft=0.15)` with a green→violet ramp. The soft bands dither into the night sky.
  - Pines: add a snow colour as the top ramp tone, driven by the normal's Z.
  - Hole: dark water ramp with a 1-px light rim.
- **Ocean.** Deck planks use the pier plank builder. The hull uses a white/blue ramp. Wave marks go up ×2 for whitecaps. The ship silhouette gets haze bands.
- **Cave.**
  - Palette: a dark violet ramp.
  - Crystals: flat 3-tone cyan or violet ramps with an extra white highlight tone.
  - Glow halos: in numpy, a radial gradient around each crystal's projected position, quantised with Bayer between the wall colour and the crystal's darkest tone. It is only a radius of about 6–10 px, which is the classic 16-bit glow.
  - Water reflections of the crystals come from the mirror pass.

### 3.3 Seven poses

`r16_character.py` already renders all seven (idle, aim, cast, reel, reel2, fight, cheer). They use the same 96×112 crop with the feet 10 px above the bottom, and write `angler.json` with the same fields.

To ship, point `OUT` at `Sprites/Character` and copy `angler.json` to `Data/character.json`. The rod stays drawn by Unity from `handX/handY` and the rod direction.

Because the IK targets are identical, the hand positions stay within 4 px of the current sprites. The one exception is cheer: its hands sit 9 px higher because the arms are longer. Run `r16_check.py` to compare.

### 3.4 36 fish

`r16_fish.py` renders all 36 already (the `fish/` folder, 144 PNGs). To ship, point `OUT` at `Sprites/Fish`.

Tunables:
- `B3` sets the tone bounds.
- `_region_col` sets the dither zones.
- `scale_mask` sets the scale-row density.
- `paint_eye` sets the eye size thresholds.

Top views keep the fk silhouettes. The palette and outline make them read as crisp shadows after the in-game `UnderwaterTint` multiply.

### 3.5 Island map (`fk_misc.build_map`)

- Keep the terrain mesh, but colour it with `m_tone` using height and slope bands:
  - deep sea (`WATER` ramp), with a shallow band set by a soft Z gradient;
  - sand `#d8c08a #b89a64`;
  - grass (FOREST ramp) with noise clusters;
  - rock (ROCK ramp);
  - snow: `CLOUD` ramp.
- Use `soft = 0.04` on the sea/sand and sand/grass bounds, so the shoreline and surf dither.
- Replace trees with miniature conifers and broadleaf trees (one `grp` each, with contour lines) and houses with `cabin()`.
- Post-process: quantize + despeckle + inner lines. Give the outer outline only to the landmarks and stage icons.
- Add wave marks as numpy dashes around the coast (light dash one row off the shore).

### 3.6 Aquarium room (`fk_misc.build_aquarium`)

- Walls and floor: WOOD plank builder and ramps.
- Tank water: numpy vertical `band_rows` (light at the top, deep at the bottom) with Bayer transitions.
- Light shafts: diagonal bands mixed at 25% with the Bayer mask.
- Gravel: a 3-tone noise-cluster ramp.
- Plants: `reed_clump` blades in the LILY/REED ramps.
- Glass: 1-px diagonal highlight dashes and a sel-out frame.
- Fish on display: reuse the r16 side sprites.
- Keep the room to 32 colours or fewer, sharing the water ramp.

### 3.7 Items and UI (`fk_items`)

The same `m_tone` + `quantize` + `outer_outline` chain works for these. Use a 12–16 colour palette per item family, taking wood and metal from the front palette and the lure colours from the fish ramps.
