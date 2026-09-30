# legends/ — one module per legend fish (3D encounter model)

`hyb_legend3d.py` (builder), `hyb_legend_preview.py` (review mock) and `hyb_legend_kit.py` (geometry helpers) are
generic and shared. A legend is **one file, `legends/<fish_id>.py`** (fish id as in `GameDatabase`). Work only in your
own module; if you need a helper, keep it in your module (do not edit the kit, the builder or another module).
Design data for each legend (bones, palette, eyes, poses, texts): `Docs/legends_rollout.md`.

## Add a legend

1. Copy `_template.py` to `<fish_id>.py` (the template builds; it is the smallest working example).
   `coelacanth.py` is the full reference (lobed fins, spots, teeth, skull hinge, 6-beat preview).
2. Fill in the module (API below), then build and check:

```
cd Tools/Blender
blender -b --python variants/hybrid/hyb_legend3d.py -- <fish_id>            # build + check (no install)
blender -b --python variants/hybrid/hyb_legend3d.py -- <fish_id> --install  # + copy into Assets/Resources/Models
blender -b --python variants/hybrid/hyb_legend_preview.py -- <fish_id>      # review mock + turntable
blender -b --python variants/hybrid/hyb_legend3d.py -- --all                # every module not starting with "_"
```

`blender` = `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`.
Errors print as `LEG ERROR ...` and stop the export (nothing is written); warnings print as `LEG WARN ...`.
Scratch renders go to `_tmp/actors3d/_work_legend_<id>/` (per legend, so parallel builds never collide).

## Outputs and names

| File | Where | Use |
|---|---|---|
| `legend_<id>.fbx` | `_tmp/actors3d/` then `Assets/Resources/Models/` | `ActorArt.Model("legend_<id>")` |
| `legend_<id>_palette.json` | same | `ActorArt.Palette("legend_<id>_palette")` |
| `legend_<id>_report.json`, `_check.png`, `.blend` | `_tmp/actors3d/` | review only |
| `<prefix>_encounter_mock.png`, `<prefix>_mock_<n>_<beat>.png`, `legend_<id>_turntable.png` | `_tmp/legend_art/` | review only |

## Module API

| Name | Required | Meaning |
|---|---|---|
| `ID`, `MODEL`, `ARMATURE` | yes | fish id; `legend_<fish_id>`; the FBX root node (CamelCase species name) |
| `PREFIX` | yes | material prefix, lower-case: `coel`, `gcarp`, `arap`, `stur`, `marl`, `gw` |
| `CM` | yes | in-game length range (cm), from GameDatabase; runtime scale = cm / 100 |
| `STAGE`, `BACKDROP`, `PRESET` | yes | surface stage id; `encounter_sets/<BACKDROP>.py`; hyb_core preset |
| `OUTLINE` | no | the one ink colour of every material (default `#0b1322`) |
| `RAMPS` | yes | `{"<prefix>_<slot>": [dark, mid, light]}` (hex) — see palette rules |
| `LURE_LIGHT` | yes | `{"abyss", "fogOutline", "eyeCore", "eyeGlow", "frameLine"}` (hex) → palette `_lureLight` |
| `BONES` | yes | `[(name, parent, head (x, y, z))]`, Root first, heads in Unity model metres |
| `build()` | yes | → `(parts, empties, info)`: `parts {bone or "Eye.L"/"Eye.R": kit.Part}`, `empties {name: (parent bone, pos, facing or None)}`, `info` (any JSON-able dict for the report) |
| `FACE_BONES` | new legends: yes | bones whose parts form the face (captions never cover it) → palette `_face` |
| `RIG` | if species bones move | dict → palette `_rig` (protrusion / roll / limits, format below) |
| `BODY_GROUP` | no | extra part names drawn as one skin with the body in the check renders (e.g. `{"Lips", "Bill"}`) |
| `CHECK_OPEN`, `CHECK_BEND` | no | check-render poses `{bone: (ax, ay, az) or {"rot": (...), "move": (dx, dy, dz)}}` |
| `preview_beats(M, sc, cam, lure, surface)` | for the mock | → `[(beat, 480x270 image)]`; see `coelacanth.py` (M = the preview module's helpers) |
| `TURNTABLE`, `PREVIEW_LURE`, `PREVIEW_CM`, `LURE_R`, `PREVIEW_PREFIX` | no | turntable poses (`open`/`bend`/`lit`/`game`), lure billboard key, mock size, lure-light radius, file prefix |

Kit (`import hyb_legend_kit as K`, all Unity model space): `K.Part`, `K.loft`, `K.plate`, `K.ellipsoid`,
`K.frame_from`, `K.decal`, `K.strip_decal`, `K.Body(table, (back, side, belly) mats, mouth=(hinge_zy, front_zy))`
with `.pt / .sec / .normal / .zone_mat / .stations / .body_rings / .full_ring / .full_mat / .clip_ring / .mouth_y`.

## Model rules

- **Size / axes:** 1.0 m long, the foremost tip (nose, lip or bill) at z +0.50, the tail tip at z −0.50; x = the fish's
  right, y = up, z = towards the nose (Unity model space). The builder converts to FBX (metres, +Y up, faces +Z).
- **Rigid parts:** one mesh `geo_<Bone>` per bone (Root has none); every bone's rest rotation is identity.
  Split the body at the spine joints; the FRONT segment carries an inset extension (`Body.over`, 15 mm) back over
  each joint so bends up to ~15° never open a gap (see `Body.body_rings`).
- **Eyes:** empties `Eye.L` / `Eye.R` with a facing (+Z = where the eye looks) and their glow lenses as parts
  `"Eye.L"` / `"Eye.R"` (→ `geo_Eye.L/R`, riding the empty's bone) using only `<prefix>_eye_glow`. Empty `Mouth` =
  the lure attach point (parent it to the bone that carries the lips: `Head`, or `Lips` when the mouth protrudes).
- **Triangle budget: ≤ 3500** (validated; the coelacanth has 2912). Flat-shade fins / caps / decals, smooth the lofts.

## Bone naming

- `Name`, `Name.L` / `Name.R`, `Name.L1`, `Name.Fan` — CamelCase segments joined by dots, no spaces or underscores,
  unique in the model (Unity finds them by name). Regex: `^[A-Z][A-Za-z0-9]*(\.[A-Z0-9][A-Za-z0-9]*)*$`.
- **Required generic:** `Root, Spine.F, Head, Jaw, Spine.B1, Spine.B2, Spine.B3, Tail, Tail.Upper, Tail.Lower,
  Pec.L, Pec.R` + empties `Eye.L, Eye.R, Mouth`.
- **Optional generic:** `Skull, Pec.L.Fan, Pec.R.Fan, Pel.L, Pel.R, Pel.L.Fan, Pel.R.Fan, Dorsal1, Dorsal2,
  Dorsal2.Fan, Anal, Anal.Fan, Tail.Mid`.
- **Species bones** (rollout 7): `Lips, Barbel.L1, Barbel.R1, Barbel.L2, Barbel.R2, Bill, UpperJaw, EyeRoll.L,
  EyeRoll.R`. Any other name gives a warning: agree it with the code agent first (Legend3D must pose it).
- Pose conventions (Unity localRotation Euler on the rest pose): spine / tail yaw about Y; `Jaw` opens +X; `Skull`
  lifts −X; `Dorsal1` rises +X (model a raisable fin RAISED, then fold it about X to rest, like the coelacanth);
  left paired fins swing forward +Y, right −Y; `Dorsal2` / `Anal` scull about Z. Protrusion bones also move by
  localPosition (`RIG`).

## Palette rules

- Every material is `<prefix>_<slot>` (ActorArt caches toon materials globally by name). Ramps are 3 hex colours
  dark → light (bands at 0.45 / 0.74 of the toon light).
- Required slots: `_back`, `_side`, `_belly`, `_fin`, `_eye_ring`, `_eye_glow`. Usual extras: `_mouth`, `_teeth`,
  `_spots`, `_scale`, `_scute`, `_barbel`, `_lips`, `_bill`, `_gill`, `_eye_white`.
- A slot ending in **`_glow`** is unlit (shows its **light** tone) with no outline. **Eye glow slot:**
  `<prefix>_eye_glow` = `[dim, eyeGlow, eyeCore]` — its light tone is the eye core, matching `LURE_LIGHT.eyeCore`
  (the eyeshine sprites use `eyeCore` / `eyeGlow`). Other `_glow` slots (e.g. `marl_stripes_glow`) are driven by code
  (rollout 7).
- Palette extras (skipped by `ActorArt.Palette`): `_lureLight` (always), `_face` (from `FACE_BONES`), `_rig` (from
  `RIG`, arrays of objects so `JsonUtility` can read them):

```
"_rig": {"protrude": [{"bone": "Lips", "move": [0, -0.010, 0.030], "tilt": [12, 0, 0]}],
         "roll":     [{"bone": "EyeRoll.L", "axis": [1, 0, 0], "deg": 150}],
         "limits":   [{"bone": "Jaw", "max": 30}],
         "glow":     [{"material": "marl_stripes_glow"}]}
"_face": [{"bone": "Head", "at": [x, y, z], "pts": [x, y, z, x, y, z, ...]}]   (rest pose, model metres)
```

The coelacanth keeps its original output byte-identical (no `_face` / `_rig`; its face outline lives in
`Legend3D.cs`).
