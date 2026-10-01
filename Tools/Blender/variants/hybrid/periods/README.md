# periods/ — one module per stage: its four time-of-day looks

Spec: `Docs/time_currents_spec.md` (sections 2, 3 and 12). Runner: `hyb_period.py` (read its docstring). Each stage agent
owns ONE file here (`periods/<stage>.py`) and, only when a hook cannot do it, its own `hyb_<stage>.py`. Nobody edits
`hyb_core.py`, `hyb_period.py`, `hyb_period_preview.py` or `build_periods.ps1` from a stage task.

## Commands (PowerShell, Blender 5.2)

```powershell
# one period (front, then back, then the look json); add -Dry to keep it out of Assets
.\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake -Period night
# all four periods + the review sheet _tmp/variants/hybrid/periods/periods_lake.png
.\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake
# the raw calls (from Tools/Blender)
blender -b --python variants/hybrid/hyb_lake.py -- --period night            # front + back + json, installed
blender -b --python variants/hybrid/hyb_lake.py -- front --period night --dry # one layer, _tmp only
blender -b --python variants/hybrid/hyb_lake.py -- json --period night        # only Data/Periods/lake_night.json
blender -b --python variants/hybrid/hyb_period_preview.py -- lake             # the review sheet
```

Outputs of `--period <p>`: `_tmp/variants/hybrid/periods/<stage>_<p>_back.png`, `_front.png`, `<stage>_<p>.json`, and
(not with `--dry`) `Assets/Resources/Sprites/Stages/<stage>_<p>_back.png` / `_front.png` and
`Assets/Resources/Data/Periods/<stage>_<p>.json`. The legacy outputs (`<stage>_back.png`, `stage_<stage>.json`, what
`build_hybrid.ps1 -Install` copies) are never written by a period run. Scratch: `_tmp/variants/hybrid/work/periods/<stage>_<p>/`.

## Module API (`periods/<stage>.py`, pure data at the top level: no bpy import outside functions)

| name | what |
|---|---|
| `NATIVE` | the period the stage depicts today (lake / swamp / ice `evening`, stream / ocean `dawn`, sea / cave `day`). Must match `hyb_period.NATIVE`. |
| `PERIODS` | `{period: preset overrides}` applied on top of the stage's own preset (`hyb_<stage>.py`'s `use_preset` overrides). `PERIODS[NATIVE]` must stay `{}`: the native period reproduces today's images byte for byte (checked on every native run: `HYB PERIOD CHECK ... identical`). Sub-dicts merge (`rim=dict(strength=0.3)` keeps the col / dirs); a dict with `"__whole__": True` replaces the sub-dict; `None` removes a feature (`sun=None`, `glitter=None` - but keep the keys your stage script reads, see below). |
| `CONSTS` | `{period: {NAME: value}}` for stage-script constants read through `PER.const("NAME", default)` (the lake's `WINDOW`). Add a `PER.const` call in your own `hyb_<stage>.py` for any other constant. |
| `LOOK` | `{period: {...}}` Unity look extras written to `Data/Periods/<stage>_<p>.json` (spec 5.1): `actorTint`, `glintCol`, `glintDensity`, `fxAlpha`, `birds`, `fireflies`, `lights` = `[dict(x, y, col, r, blink)]` animated light overlays in canvas pixels (x right, y DOWN from the top of the 640x400 canvas; the sea's is 800 wide: its columns = the 640 layout's + `OX`; `blink` fixed / flicker / flash1 / flash2). Water tint / deep, rim, key light come from the preset. |
| `back_scene(ctx)` | optional: after the back geometry is built (add objects; tag them with `R.tagk`). |
| `front_scene(ctx)` | optional: after the front geometry (`ctx["refl"]` = the list of objects that get water reflections, append to it). |
| `back_post(idx, pal, ctx) -> (idx, pal)` | optional: palette-index post-process just before the back layer is saved (stars are in the preset; use it for lit windows / lamp glows / light streaks on the water: `hyb_period.point_glow`, `hyb_period.light_streak`). |
| `front_post(idx, pal, ctx) -> (idx, pal)` | optional: the same for the front layer. |

Hooks run only for a NON-native period. `ctx`: `stage, period, pr` (the active preset), `stand`, `rc(p)` (world point
-> canvas col, row; Blender axes x right, y forward, z up), `kid` (per-pixel kind tags), `ps` (render passes), `water`,
`sky` (back only; on the ice `water` is the ice surface and `hole` the open hole), `R` (hyb_core), `W`, `H`.

Preset keys each stage script reads (keep them present in every period): lake `glitter{cols,far,width}`; stream
`glitter{cols,far,width}`; sea `glitter{cols,dx,far,width}`; swamp `glow{col,rings}`, `haze`; ice nothing extra;
ocean `glitter{cols}`; cave `shaft{top,bottom}` (set by the stage script: override only col / amount / fade),
`glitter{cols,row0,row1}`, `crystal`. New kit keys for periods: `stars=dict(cols=[dim, bright], dens, up0, seed,
bright, glow_max)` (sky_rgb), `grade.gain` / `grade.tint` (exposure and light colour of every graded palette, linear).

## Rules (spec 3.1)

1. Same camera, same geometry, same silhouettes in all four periods: the renders cross-fade into each other and Unity
   builds the water mask once from the native `<stage>_back.png`. The non-native front run prints its silhouette diff
   against today's front (`front silhouette: N px differ`): 0, or only the pixels of an added light.
2. Geometry anchors come from the NATIVE preset (`PER.native_pr()`): the stream's notch / keep-out boxes, the swamp's
   valley. Only light moves.
3. The play-area water band = `water.tint` (bands[4]); its OKLab L within the spec 3.1 range for the period, so the
   Unity fish shadows stay readable (the review sheet prints water L vs shadow L per fish: `OK` / `TOO FAINT`).
4. Lights stay lighter than their surroundings, floating things lighter than the water, only fish shadows darker; no
   ink outlines; rims on the outer silhouette only; glitter only in the far third (hybrid notes.md rules).
