# encounter_sets/ — one module per underwater backdrop set

`hyb_encounter.py` (runner) and `hyb_encounter_kit.py` (helpers + shared sprites) are generic and shared. A set is
**one file, `encounter_sets/<set>.py`**; the set id is `EncounterDef.backdrop` (`cave`, `lake`, `swamp`, `ice`,
`ocean`). Work only in your own module. The look of each set (colours, layers, camera, light): `Docs/legends_rollout.md` 4.

## Add a set

1. Copy `_template.py` to `<set>.py` (it builds: flat placeholder layers incl. a ceiling, a frame, one new lure).
   `cave.py` is the full reference (3D-rendered mid layer, pseudo-perspective floor, quantised palettes).
2. Build and review:

```
cd Tools/Blender
blender -b --python variants/hybrid/hyb_encounter.py -- <set>             # build into _tmp/variants/hybrid/encounter/
blender -b --python variants/hybrid/hyb_encounter.py -- <set> --install   # + copy into Assets/Resources/Sprites/Encounter
blender -b --python variants/hybrid/hyb_encounter.py -- --all             # every module not starting with "_"
```

No argument = `cave` (the legacy output, byte-identical to before the refactor). `build_hybrid.ps1` runs `--all`,
and its `-Install` copies every PNG in `encounter/` except the review sheets (`encounter_sheet*.png`).
Errors print as `ENC ERROR ...` and stop the set before anything is saved. Scratch passes go to
`_tmp/variants/hybrid/work/encounter/<set>/` (per set).

## Module API

| Name | Required | Meaning |
|---|---|---|
| `SET` | yes | the set id (= the file name) |
| `PRESET` | no | hyb_core preset while building (default = the set id) |
| `layers()` | yes | → `{name: top-down RGBA float image}` with the names below |
| `frame()` | yes | → the 24×24 frame image (`E.make_frame(F, corners)` makes the standard one) |
| `LURES` | yes | lure billboards this set's legends need: fk_items `bait_<key>` keys, e.g. `["golden", "corn", "softworm"]` |
| `LURE_TWEAKS` | for new lures | `{key: fn(mesh objects)}` posing frame 1 (the kit already has `egi`, `softworm`, `jig`) |
| `HORIZON` | yes | bg row of the eye-level horizon (the haze band; the cave uses 150) |
| `PROFILE` | yes | runtime look for the preview mock (mirrors the code's set row, rollout 4) |
| `REVIEW`, `SHEET` | no | review-sheet offsets (`ray`, `mid`, `floor`, `fore`, `ceiling`) and file name (default `encounter_sheet_<set>.png`) |

Kit (`import hyb_encounter_kit as E`): `E.rgb / blank / put / vnoise2 / fbm / poly_mask / quant_rows /
floor_rows / ceiling_rows / make_frame / lure_frames`, plus hyb_core (`R.quantize`, `R.Pal`, `R.bayer`,
`R.dash_threshold`, `R.render_passes`, `R.m_tone`, ...).

## Files, names, sizes

| File | Size | Req. | Content / placement (spec 2.5) |
|---|---|---|---|
| `uw_<set>_bg.png` | 640×400, **opaque** | yes | water gradient + far silhouettes; fixed 1:1, parallax 0.1; its row `HORIZON` sits on the projected floor horizon |
| `uw_<set>_mid.png` | 768×200 | yes | terrain / weeds / roots / trunks; bottom row on the floor horizon; parallax 0.35 |
| `uw_<set>_floor.png` | 768×120 | no | floor in pseudo-perspective, top row = the horizon (`E.floor_rows`); parallax 0.8 |
| `uw_<set>_ceiling.png` | 768×120 | no | underside of the surface / ice, BOTTOM row = its far edge on the projected surface horizon (`E.ceiling_rows`); parallax 0.6 |
| `uw_<set>_ray.png` | 64×256 | no | the set's own light shaft (else the shared `uw_ray`) |
| `uw_<set>_fore.png` | 256×128 | yes | out-of-focus foreground, bottom-left; parallax 1.3 |
| `enc_frame_<set>.png` | 24×24 | yes | 9-slice, 6 px border: 1 px ink, 2 px band, 1 px inner line at px 3 (`frameLine`), corner studs |
| `lure_<key>_0/1.png` | 16×8 | per `LURES` | side view, line tie to the RIGHT, 2 frames (from `fk_items.build_bait`) |

Shared sprites (written by every run, only when missing or changed, identical from every set; tinted in code):
`uw_ray` 64×256, `lure_halo` 24×24, `eyeshine` 7×7, `silt_0..3` 8×8, `bubble_s` 3×3, `bubble_m` 5×5, `waterline` 32×4.

Rules:
- Pixel art: hard pixels, palette-quantised, Bayer / dash dither (hybrid style); layers dissolve into the water
  towards the horizon instead of ending on a hard edge.
- A lure billboard is built from the same `fk_items` model as the item icon. Each new lure has ONE owner set that
  lists it and supplies its frame-1 tweak (lake: `golden`, `corn`; swamp: `frog`, `popper`; ocean: `kona`); sets
  sharing a lure (`softworm`, `jig`) produce identical files.
- A new frame needs its 6 px border in `Assets/Editor/PixelArtImporter.cs` (`Borders`) — a code-side change.
