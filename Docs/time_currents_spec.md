# 낚시왕 — Time of day, tides and currents (spec v1)

Status: implementable spec for the user request "유동적으로 변하는 유속이나 조류 표현" with the decisions below. It adds a
game clock with four periods (새벽 · 낮 · 저녁 · 밤), four Blender looks per stage, fish that feed by the time of day, a
tide on the sea that follows the clock, and water that moves: a flowing stream, a tidal stream on the sea, a drifting
boat on the ocean and wind drift on the lake and swamp — each with visuals and gameplay.

Units: game space is metres (x right, y up, water surface y = 0, z forward from the angler's feet), current vectors are
`(x, z)` in m/s on the water plane, times are game time unless marked "real". Canvas pixels are the 640x400 stage
canvas (x right, y DOWN from its top) unless marked "RT px" (the 480x270 render target) or "canvas units" (UI).

Constraints kept from the earlier specs: the reel UI stays fixed at the bottom right; there is no whole-body idle bob
(the ocean boat drift moves the water, not the angler); the left hand holds the rod; casting stays drag down + flick
up; the lure 톡 stays a short downward pull; mending uses the existing sideways rod sweep (no new gesture); no
unrequested polish or QA rounds — one capture set per phase (section 15).

---

## 0. Decisions

1. **A game clock runs while the player is on a fishing stage**: 1 real second = 1 game minute, so one game day is
   24 real minutes. It is saved. It stops off-stage (map, shop, aquarium, title), while a dialog is open, on the catch
   card and while the app is in the background. New saves start at **10:00, day 1**.
2. **Four periods**: 새벽 05:00–08:00, 낮 08:00–17:00, 저녁 17:00–20:00, 밤 20:00–05:00 (3 / 9 / 3 / 9 real
   minutes). Each stage has one Blender render (back + front) per period; they cross-fade with an ordered-dither
   dissolve over 30 game minutes centred on each boundary.
3. **Every stage's current look becomes the period it already depicts**, byte for byte: lake 저녁, stream 새벽,
   sea 낮, swamp 저녁, ice 저녁, ocean 새벽, cave 낮. The other 21 looks are new renders (section 3).
4. **Fish feed by the time of day**: every species has an activity per period (section 6). Activity scales how often it
   is stocked and how readily it comes to the bait. Three species are **night-only**: 쏘가리 `mandarin_fish`,
   메기 `catfish`, 모캐 `burbot`. Legends keep their encounters; their encounter meter fills faster or slower by period.
5. **The sea's tide follows the clock**: semi-diurnal, 12-hour cycle, low water 03:30 / 15:30, high water 09:30 / 21:30.
   So 새벽 and 저녁 are always a rising tide (들물), 낮 has the midday ebb (날물) and the mid-afternoon low slack (간조),
   밤 has the high slack (만조) and the night ebb. Bites rise with the tidal stream and drop at slack (section 7).
6. **Currents by stage** (section 8): stream — a strong one-way flow towards the angler that surges every 6–9 s, fast in
   the middle, slack behind the mid-stream rocks; sea — the tidal stream across the breakwater; ocean — a broad slow
   drift of the water past the boat; lake / swamp — mostly calm, occasional wind gusts drift the float; ice / cave —
   none.
7. **Gameplay** (section 9): floats and lures drift with the water; the line bows and drags a float unless it is
   mended with the rod sweep; a lure wound against the current works harder (with it, weaker); a fish that runs with
   the current pulls harder, and side pressure pulls it out of the flow.
8. **Visuals** (section 10): flow streaks at the local current speed, surges, foam lines, drifting leaves / flecks /
   weed, weed that leans with the tide, a wet band on the tetrapods at low water, float wakes, gust "cat's paws".
9. **HUD** (section 11): a clock panel with a period icon in the top row, a tide gauge on the sea and a small current
   arrow; the map shows the (paused) clock, a period tint and, in the stage card, the tide and which fish are active.
10. **Unity reads each look from a small JSON written by the Blender run** (`Data/Periods/<stage>_<period>.json`):
    water tint / deep, the actor rim, the key light, actor tint, glints, birds / fireflies and animated lights (section 5).

---

## 1. The game clock

### 1.1 Speed and storage

| item | value |
|---|---|
| speed | 1 game minute per real second (`GameClock.Scale` = 1; `-fktimescale` changes it) |
| day length | 1440 game minutes = 24 real minutes |
| state | `float Min` (0 <= Min < 1440, game minutes since 00:00) and `int Day` (1, 2, ...) |
| new save | `Min = 600` (10:00), `Day = 1` |
| old saves | the new `SaveData` fields default to the same values (JsonUtility keeps field initialisers) |
| saved | `SaveData.clockMin` / `clockDay` updated every frame the clock runs; `Game.I.Save()` every 30 real s of running, when the fishing scene is destroyed and on application pause (as today) |

`GameClock` (new, `Assets/Scripts/Core/GameClock.cs`, static): `Min`, `Day`, `Scale`, `bool Running`, `Tick(float realDt)`,
`Period Now`, `PeriodBlend Look` (section 2.2), `Tide` (section 7), `event Action<Period> PeriodBegan` (raised when a
cross-fade passes its middle, f = 0.5).

### 1.2 When it runs

`FishingController.Update` calls `GameClock.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f))` once per frame when all hold:

- the fishing scene is loaded (the controller exists) — never on the map / shop / aquarium / title;
- `State != S.Result` (the catch card) — Landing, Encounter and every other state run the clock;
- `!Dialog.Open` (bait picker, settings, tutorial, any window);
- `Application.isFocused` (the app in the background does not advance time; there is no offline catch-up).

A hitch never skips more than 0.1 real s. The encounter (legend window) runs the clock like any other state.

### 1.3 Display

- **HUD**: time shown as `HH:MM` floored to 10 game minutes (so it ticks every 10 real seconds), with the period name:
  `새벽 05:40`, `낮 12:30`, `저녁 18:50`, `밤 23:10`. Panel layout in section 11.1.
- **Period start toast** (`Toast.Show`, 2.0 s, when `PeriodBegan` fires on a stage): 새벽 "새벽이 밝아와요",
  낮 "해가 높이 떴어요", 저녁 "노을이 지기 시작해요", 밤 "밤이 되었어요". Colour = the period's text colour (11.1).
- **Map**: section 11.4.

---

## 2. Periods and cross-fades

### 2.1 Table

| period | 한국어 | hours | real min | full look | cross-fade in (from) | centre (tests) | text colour |
|---|---|---|---|---|---|---|---|
| `dawn` | 새벽 | 05:00–08:00 | 3 | 05:15–07:45 | 04:45–05:15 (밤) | 06:30 | `#f8c8d0` |
| `day` | 낮 | 08:00–17:00 | 9 | 08:15–16:45 | 07:45–08:15 (새벽) | 12:30 | `#fff4d0` |
| `evening` | 저녁 | 17:00–20:00 | 3 | 17:15–19:45 | 16:45–17:15 (낮) | 18:30 | `#ffc890` |
| `night` | 밤 | 20:00–05:00 | 9 | 20:15–04:45 | 19:45–20:15 (저녁) | 00:30 | `#b8c8ff` |

`enum Period { Dawn, Day, Evening, Night }`.

### 2.2 The blend

For a clock minute `m`, with the boundaries `b` = 300, 480, 1020, 1200 (05:00, 08:00, 17:00, 20:00):

- if `|m - b| < 15` for a boundary (wrap at 1440): `From` = the period before b, `To` = the period after b,
  `f = smoothstep(0, 1, (m - (b - 15)) / 30)`;
- else `From = To = PeriodAt(m)`, `f = 0`.

`PeriodBlend { Period From, To; float F; Period Dominant => F < 0.5 ? From : To; }`. Every look value (section 5) is
`lerp(look[From], look[To], F)`; flags (birds, fireflies, dirs of the rim) take the dominant period.

### 2.3 Which render each period uses

| stage | 새벽 | 낮 | 저녁 | 밤 |
|---|---|---|---|---|
| lake | new | new | **today** (golden hour) | new |
| stream | **today** (clear early morning) | new | new | new |
| sea | new | **today** (bright late afternoon) | new | new |
| swamp | new | new | **today** (foggy dusk) | new |
| ice | new | new | **today** (blue hour after sunset + aurora) | new |
| ocean | **today** (sunrise) | new | new | new |
| cave | new (shaft only) | **today** (daylight shaft) | new (shaft only) | new (shaft only) |

Files: `Resources/Sprites/Stages/<stage>_<period>_back.png` / `_front.png` (28 + 28). The "today" ones are byte copies
of `<stage>_back.png` / `_front.png`, already installed and verified (section 12.4). The legacy `<stage>_back.png` /
`_front.png` stay: `WaterFx.BuildMask` keeps building the water mask from `<stage>_back` + `<stage>_front` (all four
periods share the geometry, so one mask serves them all).

### 2.4 The dissolve (StageView)

- `StageView` holds two renderers per layer (back order 0, front order 40): `A` = `From`, `B` = `To`. With `F == 0` only
  A draws; with `F == 1` the layers swap. Sprites of the stage's four periods are loaded when the stage opens
  (8 sprites; `Art.Stage("<stage>_<period>_back")`, falling back to `<stage>_back` if one is missing).
- B draws with a new material `FishingKing/PeriodDissolve` (`Assets/Shaders/PeriodDissolve.shader`): sprite colour, and
  `clip(F16 - bayer4(px))` where `px` = the fragment's pixel in the pixel view's 480x270 render target (`SV_POSITION.xy`),
  `bayer4 = (M[y%4][x%4] + 0.5) / 16`, `M = {0,8,2,10},{12,4,14,6},{3,11,1,9},{15,7,13,5}`, `F16 = floor(F * 16) / 16`.
  So the incoming look replaces 1/16 of the pixels at a time (about every 2 real seconds), never a translucent mix.
- A draws normally beneath B. The front layers have identical silhouettes, so B's front pixels cover A's exactly; an
  added light pixel (section 3) simply appears with its dither step.

---

## 3. The Blender looks (4 per stage)

Pipeline: `Tools/Blender/variants/hybrid/` — each `hyb_<stage>.py` renders one look per run; `--period <p>` applies
`periods/<stage>.py` `PERIODS[p]` on top of the stage's own preset (section 12). The values in the tables below are the
**drafts** already in the period modules: all 21 render, with the checks of 12.4 passing; the stage agents tune them
against the review sheet and add the night lights (3.2–3.8).

### 3.1 Rules and period targets

1. **Only light changes.** Same camera, same geometry, same silhouettes in all four looks (they dissolve into each other
   and Unity builds one water mask). A non-native run prints `front silhouette: N px differ` against today's front:
   N must be 0, or only the pixels of an added lamp. Geometry anchors come from the NATIVE preset (`PER.native_pr()`):
   the stream's notch and keep-out boxes, the swamp's valley (already wired).
2. **Water**: the play-area band is `water.tint` = `bands[4]` (Unity's underwater tint and splashes use it), with the OKLab
   L of the table; fish shadows must read (`hyb_period_preview.py` prints `OK` when the shadow is >= 0.08 darker).
3. **Floating things stay lighter than the local water** (lake pads, sea buoy, swamp pads / duckweed / log): the stage
   scripts print them (`pad L min ... vs water L ...`). At night this sets the exposure: grade gain 0.42 (ice 0.5) with
   the play band at L 0.34.
4. The hybrid rules hold (`notes.md`): glitter only in the far third, rims on the outer silhouette only, hue-shifted
   outlines (no ink), 1 px stars never inside the moon glow. Colour budget: back <= 90, front <= 42.
5. The sun / moon / glow column must have water under it (lake, stream and sea look up the water rows there).
6. **Compass**: each stage faces a fixed way, so the sun is where it should be in every period (dawn east, midday south,
   evening west). Where the sun is behind the angler there is no disc: the sky ahead shows the pink "belt of Venus"
   (up 4–15 px) over the blue-grey earth shadow (0–4 px), the scene is front-lit (key_dir y < 0: no backlight rims,
   long shadows away from him), the rim is a weak top rim.

| | 새벽 | 낮 | 저녁 | 밤 |
|---|---|---|---|---|
| sun / moon | behind (lake, sea, swamp, ice): no disc, belt of Venus; ahead (stream, ocean): today's sunrise | high on the south side, up 86 (above the crop): only the outer glow ring (16 / 36 / 70 px: 0.42 / 0.2 / 0.08, `#fffaf0`) | ahead (sea): low disc up 9 over open water; behind (stream, ocean): golden front light; lake / swamp / ice: today | full moon r 3.4, core `#f8f4e4`, limb `#d6d4c6`, up 15–18 over open water; glow `#8e9cc4` 7 / 16 / 30 px: 0.4 / 0.18 / 0.07 |
| key light (towards it, Blender axes) | behind, low: y -0.8..-0.85, key `#ffd8c8` | high: z 0.89 on the sun side, key `#fff4e0` | towards the low sun, key `#ffcc90`; behind: y -0.8, key `#ffc890` | moon side, z >= 0.78, key `#c8d4f0`, shadow `#161c38` |
| sky horizon -> zenith | `#9ea4c0` -> belt `#e0b4b8` -> `#5076b2` | `#e4ecee` -> `#4482c8` | ahead `#f8c478` -> `#263c70`; behind `#c4a8b8` -> `#4c70b0` | `#34405e` -> `#0c1226` + stars (0.6 % of the sky px above 9 px, 25 % bright `#dce2f4`, rest `#7c88ac`) |
| grade gain / tint (new kit keys) | 0.82–0.96 / `#f0e6f4` | 1.02–1.08 / — | 0.9–0.97 / `#ffe8d4` | 0.42 (ice 0.5, cave 0.85) / `#bccaff` |
| play-area water L | 0.48–0.56 (ice 0.62–0.68, swamp 0.42–0.44) | 0.50–0.60 (ice 0.76–0.80, swamp 0.42–0.45) | 0.46–0.58 | 0.33–0.38 (ice 0.50–0.54, swamp 0.30–0.32, cave 0.40–0.42) |
| rim (actor + props) | weak top `#ffe4dc` 0.12–0.18 | sun side `#fff8ea` 0.15–0.22 | towards the sun `#ffc888` 0.5; behind: top `#ffd0a0` 0.22 | moon side `#c4d4ff` 0.3–0.35 (swamp: top `#b8c4d4` 0.18, through the fog) |
| mist | thick on lake / swamp (0.95–1.0) | thin 0.25–0.35 (swamp 0.7) | today's | `#46506e`-ish 0.3–0.5 (stream valley 0.85, swamp 0.9) |
| glitter | none (dens 0–0.05) | sparse white 0.2–0.3 | long gold 0.5 when the sun is ahead | silver moon path 0.35–0.45 |

New kit keys (hyb_core, backward compatible — the native renders are byte-identical with them in place):
`grade.gain` (linear exposure) and `grade.tint` (linear light colour) on every graded palette; `stars` in `sky_rgb`;
`"__whole__": True` in an override dict replaces the sub-dict instead of merging.

Per stage (drafts; **today** = the native look, unchanged). `light`: the sun / moon position in canvas px (dx right of
the image centre, up above the horizon row 89.5; the game crop shows up to ~24 px of sky).

### 3.2 lake

Lake faces WEST. Dawn sun behind (behind-left), midday sun high on the LEFT (south), today's sunset ahead-right over the bay (dx +122), the moon over the bay (dx +110).

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 | behind the angler (no disc / glow) | (-0.3, -0.8, 0.52) #ffd8c8 / #4c5c8e | #9ea4c0 -> #5076b2 | #4e6684 / #12243a (0.50) | #ffe4dc 0.18 t 1 tl 0.6 | 0.92 / #f0e6f4 | mist 0.95, haze #b8b8d4/800m |
| 낮 | sun dx -200 up 86 (above the crop, glow ring only) | (-0.45, 0.05, 0.89) #fff4e0 / #46628a | #e4ecee -> #4482c8 | #4a7a9e / #10304a (0.56) | #fff8ea 0.22 tl 1 t 0.8 l 0.5 | 1.08 / - | glitter 0.20, mist 0.25, haze #cad8e4/1400m |
| 저녁 **today** | sun dx +122 up 7.5 (in view) | (0.7, 0.1, 0.7) #ffd49a / #4a5c90 | #f7c67c -> #243a6a | #456a8a / #10283a (0.51) | #ffcf8c 0.55 r 1 tr 0.85 t 0.55 | 1.0 / - | glitter 0.45, mist 0.80, haze #dfa58e/900m |
| 밤 | moon dx +110 up 16 (in view) | (0.55, 0.2, 0.81) #c8d4f0 / #161c38 | #34405e -> #0c1226 | #2a3856 / #080e1c (0.34) | #c4d4ff 0.35 r 1 tr 0.85 t 0.5 | 0.42 / #bccaff | stars, glitter 0.40, mist 0.50, haze #2a3452/700m |

### 3.3 stream

Stream faces EAST up the valley. Today's sunrise sits in the notch ahead-left (dx -70); midday sun high on the RIGHT; the evening sun sets BEHIND him (front-lit valley, pink belt); the moon rises in the notch. Geometry stays on the native notch.

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 **today** | sun dx -70 up 16 (in view) | (-0.62, 0.15, 0.77) #fff0c8 / #3c5c6c | #f2e4b8 -> #5286c0 | #3e7a78 / #0c2a2c (0.54) | #fff2c8 0.4 l 1 tl 0.8 t 0.5 | 1.0 / - | glitter 0.30, mist 0.95, haze #d6e2da/500m |
| 낮 | sun dx +150 up 86 (above the crop, glow ring only) | (0.45, 0.1, 0.89) #fff4e0 / #44628a | #e8eee6 -> #4c8ccc | #468680 / #0e2e30 (0.58) | #fff6e0 0.22 tr 1 t 0.8 r 0.5 | 1.04 / - | glitter 0.20, mist 0.35, haze #cfe0dc/900m |
| 저녁 | behind the angler (no disc / glow) | (0.2, -0.8, 0.56) #ffc894 / #4a4a7c | #c4a8b8 -> #4c70b0 | #567e84 / #10282c (0.57) | #ffd2a4 0.22 t 1 tr 0.6 | 0.9 / #ffe8d4 | glitter 0.05, mist 0.60, haze #dcc4c0/600m |
| 밤 | moon dx -70 up 18 (in view) | (-0.6, 0.2, 0.78) #c8d4f0 / #161c38 | #34405e -> #0c1226 | #304458 / #081418 (0.38) | #c0d0f8 0.3 l 1 tl 0.8 t 0.5 | 0.42 / #b8c8f8 | stars, glitter 0.35, mist 0.85, haze #26324a/600m |

### 3.4 sea

Sea faces WEST out to sea. Dawn light from BEHIND; today's late-afternoon sun high ahead-left (above the crop); the evening sun sets over the open sea in the glitter column (dx -150), the moon over the same column.

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 | behind the angler (no disc / glow) | (-0.25, -0.85, 0.46) #ffdcc8 / #4a5c8c | #a4aac4 -> #5480bc | #56769e / #0e2440 (0.56) | #ffe4dc 0.18 t 1 tl 0.6 | 0.82 / #f0e6f4 | glitter 0.05, mist 0.45, haze #c0c0d8/2000m |
| 낮 **today** | sun dx -230 up 72 (above the crop, glow ring only) | (-0.6, -0.12, 0.79) #fff2d6 / #3a5a84 | #e6e6d6 -> #3a7cc4 | #2c7aa4 / #0a2a40 (0.55) | #fff4de 0.3 l 1 tl 0.8 t 0.5 | 1.0 / - | glitter 0.35, mist 0.30, haze #d4e2e8/2500m |
| 저녁 | sun dx -150 up 9 (in view) | (-0.55, 0.3, 0.78) #ffcc90 / #4a4a86 | #f8c478 -> #263c70 | #3e6c9a / #0c2440 (0.52) | #ffc888 0.5 l 1 tl 0.85 t 0.55 | 0.95 / #fff0e0 | glitter 0.50, mist 0.40, haze #e0a890/2200m |
| 밤 | moon dx -150 up 15 (in view) | (-0.55, 0.2, 0.81) #c8d4f0 / #161c38 | #34405e -> #0c1226 | #2a3856 / #060c1c (0.34) | #c4d4ff 0.3 l 1 tl 0.8 t 0.5 | 0.42 / #bccaff | stars, glitter 0.40, mist 0.30, haze #283250/1800m |

### 3.5 swamp

Swamp faces WEST; always foggy. Cold silver dawn from behind, a bright olive day with thinner fog (haze 260 m), today's amber dusk, a moon glow behind the fog (dx +60). PR.glow is needed in every period; geometry stays on the native glow.

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 | no disc; glow #dce0e4 at dx -40 up 6 | (-0.2, -0.6, 0.77) #e4e4dc / #384440 | #b8bcb8 -> #586464 | #485444 / #141c14 (0.43) | #e0e4dc 0.12 t 1 | 0.9 / #e8eef0 | mist 1.00, haze #a4aaa4/110m |
| 낮 | no disc; glow #f0e8c0 at dx -120 up 24 | (-0.3, 0.3, 0.9) #f0e8c8 / #34443a | #c8c8a4 -> #66807c | #4a563e / #141e12 (0.44) | #e8e0b8 0.15 t 1 tl 0.6 | 1.02 / - | mist 0.70, haze #a8ac88/260m |
| 저녁 **today** | no disc; glow #d0a864 at dx -40 up 4 | (0.25, 0.45, 0.86) #c8b48a / #2c3a34 | #b4a070 -> #343e38 | #3a4a30 / #10180e (0.39) | #d8c088 0.2 t 1 tl 0.6 tr 0.6 | 1.0 / - | mist 0.95, haze #96946e/140m |
| 밤 | no disc; glow #a8b4c8 at dx +60 up 16 | (0.25, 0.3, 0.92) #b4c0d0 / #101818 | #2a3430 -> #0e1616 | #2a3430 / #060a08 (0.31) | #b8c4d4 0.18 t 1 tr 0.6 | 0.42 / #c0d0e0 | mist 0.90, haze #2a3430/120m |

### 3.6 ice

Ice faces WEST. Dawn from BEHIND (alpenglow on the snowy range), bright snow and a high sun on the LEFT at midday, today's blue hour with the aurora, and a navy night with stars, the aurora at 0.7 and the moon on the right (dx +150). The preset 'water' is the ice surface.

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 | behind the angler (no disc / glow) | (-0.25, -0.8, 0.55) #ffd8d8 / #4a5a90 | #a8acc8 -> #5a80bc | #7c94c0 / #18284a (0.66) | #ffe0e4 0.15 t 1 tl 0.6 | 0.88 / #f4e8f4 | mist 0.50, haze #b8b8d8/1800m |
| 낮 | sun dx -200 up 86 (above the crop, glow ring only) | (-0.45, 0.1, 0.89) #fffaf0 / #5070a8 | #e8eef4 -> #3c78c0 | #a0bcdc / #1c3458 (0.79) | #ffffff 0.2 tl 1 t 0.8 l 0.5 | 1.05 / - | mist 0.30, haze #d8e2ee/2500m |
| 저녁 **today** | no disc; glow #f0b8b0 at dx -140 up -2 | (-0.3, 0.3, 0.9) #cad6f2 / #2a3260 | #d6a8b2 -> #1c2656 | #6480b0 / #142040 (0.60) | #ffc8d0 0.3 l 1 tl 0.8 t 0.4 | 1.0 / - | aurora 0.55, mist 0.40, haze #a4a0c8/1800m |
| 밤 | moon dx +150 up 17 (in view) | (0.5, 0.25, 0.83) #c4d0f0 / #141c40 | #28345a -> #0a1026 | #566690 / #0a1226 (0.51) | #c8d8ff 0.3 r 1 tr 0.8 t 0.4 | 0.5 / #bccaff | stars, aurora 0.70, mist 0.40, haze #2a3456/1500m |

### 3.7 ocean

Ocean faces EAST from the bow. Today's sunrise ahead-left (dx -90); midday sun high on the RIGHT; the evening sun sets BEHIND the boat (islands, freighter and lighthouse lit gold from the front); the moon rises in the sunrise gap.

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 **today** | sun dx -90 up 6 (in view) | (-0.62, 0.3, 0.72) #ffdcb0 / #3a4c7a | #ffca92 -> #3868b0 | #2c5c8a / #081c34 (0.46) | #ffd8b0 0.5 l 1 tl 0.85 t 0.55 | 1.0 / - | glitter 0.50, mist 0.35, haze #f0c8b2/3000m |
| 낮 | sun dx +160 up 86 (above the crop, glow ring only) | (0.45, 0.1, 0.89) #fff6e4 / #3a5a8a | #e0eaee -> #3470c0 | #2c6698 / #082040 (0.50) | #fff8ea 0.22 tr 1 t 0.8 r 0.5 | 1.04 / - | glitter 0.30, mist 0.25, haze #cadae6/4000m |
| 저녁 | behind the angler (no disc / glow) | (0.1, -0.85, 0.52) #ffc890 / #484a80 | #c4a8b8 -> #4c70b0 | #4a6c96 / #0c2240 (0.52) | #ffd0a0 0.22 t 1 tr 0.5 tl 0.5 | 0.9 / #ffe8d4 | glitter 0.06, mist 0.35, haze #e4c0b8/3000m |
| 밤 | moon dx -90 up 15 (in view) | (-0.55, 0.25, 0.8) #c8d4f0 / #161c38 | #34405e -> #0c1226 | #283656 / #040a18 (0.34) | #c4d4ff 0.32 l 1 tl 0.85 t 0.55 | 0.42 / #bccaff | stars, glitter 0.45, mist 0.30, haze #283250/2500m |

### 3.8 cave

Cave: no sky; only the shaft from the ceiling hole changes (its position is fixed by the stage script): thin pink-gold at dawn, today's white-green by day, amber in the evening, a faint blue moonbeam at night. The rim stays the cyan crystal light.

| period | light | key_dir (Blender: x right, y fwd, z up) / key / shadow | sky horizon -> zenith | water tint / deep (OKLab L) | rim (col, s, dirs) | grade gain / tint | extra |
|---|---|---|---|---|---|---|---|
| 새벽 | shaft #f4d4c8 x0.13 | (0.18, 0.32, 0.93) #dcebe4 / #1e1a34 | #1e1830 -> #0c0a14 | #275463 / #04101a (0.42) | #86dcff 0.45 t 1 tl 0.8 tr 0.8 | 0.96 / #f8f0f4 | glitter 0.32, mist 0.50, haze #141020/60m |
| 낮 **today** | shaft #e6f0d2 x0.20 | (0.18, 0.32, 0.93) #dcebe4 / #1e1a34 | #1e1830 -> #0c0a14 | #275463 / #04101a (0.42) | #86dcff 0.45 t 1 tl 0.8 tr 0.8 | 1.0 / - | glitter 0.42, mist 0.50, haze #141020/60m |
| 저녁 | shaft #ffc88c x0.16 | (0.18, 0.32, 0.93) #dcebe4 / #1e1a34 | #1e1830 -> #0c0a14 | #275463 / #04101a (0.42) | #86dcff 0.45 t 1 tl 0.8 tr 0.8 | 0.97 / #fff4e8 | glitter 0.36, mist 0.50, haze #141020/60m |
| 밤 | shaft #9fb4e0 x0.07 | (0, 0.3, 0.95) #a8e0f0 / #1e1a34 | #1e1830 -> #0c0a14 | #275463 / #04101a (0.42) | #86dcff 0.45 t 1 tl 0.8 tr 0.8 | 0.85 / #dce8ff | glitter 0.18, mist 0.50, haze #141020/60m |

### 3.9 Light sources per stage (what each agent adds, and where)

Baked lights are done in the period module (`CONSTS`, `back_post` / `front_post` with `hyb_period.point_glow` and
`light_streak`); anything that blinks or flickers is listed in `LOOK[p]["lights"]` (canvas px, measured on the final
render) and animated by Unity (5.6).

| stage | 새벽 | 낮 | 저녁 | 밤 |
|---|---|---|---|---|
| lake | cabin window dim `#e8a060` (CONSTS) | window unlit `#6a7c8c` (CONSTS) | today (lit `#ffb85c`) | window `#ffd27a` (CONSTS) + glow rings 3 / 6 / 10 px `#ffc870` (0.6 / 0.35 / 0.15) + a broken streak on the bay water straight below (14 px); fireflies (Unity); LOOK light: window `fixed` r 4 |
| stream | — | — | ridge tops alpenglow (optional hook: top-rim `#ffc8a0` 0.4 on the range / far slopes) | moon + moonlit mist; waterfall foam `#c8d4e8` |
| sea | lighthouse lamp off | off | lamp on `#ffe890` (1 px glow) | lighthouse lamp (mole tip, col ~122) glass `#fff0b0` + rings 3 / 7 / 12 px `#ffe8a0` + streak on the water; buoy lamp `#ff5a4a` + rings 2 / 5 px; 3–5 harbour lights 1 px `#ffd890` along the far coast's waterline; LOOK lights: lighthouse `flash2` r 6, buoy `flash1` r 3 |
| swamp | lantern unlit (glass `#8a7a58`) | unlit | today (lit) | lantern glass `#ffd070`, flame `#fff4c8`, `lantern_light` level 0.3 -> 0.5 and its lit area x2; glow rings 3 / 6 / 11 px `#ffc870` on the post / planks; streak on the water below (18 px); fireflies (Unity); LOOK light: lantern `flicker` r 5 |
| ice | shanty window dim | unlit | today (lit) | shanty window `#ffc870` / `#fff0c0`, light spill on the snow in front: 2 levels `#ffd08c` (0.35 / 0.18) within 6 / 12 px, flat 0.5; the far shanty's window 1–2 px `#ffc870`; aurora 0.7; LOOK light: window `fixed` r 4 |
| ocean | lamp on (today) | lamp off `#d8d0c0` | lamp on `#ffd890` | lamp `#fff4c0` + rings 3 / 7 / 12 px; freighter: 3–4 deck lights 1 px `#ffe0a0`, a red port light `#ff5a4a`, a white masthead light; LOOK lights: lighthouse `flash2` r 6, freighter `fixed` r 2 |
| cave | shaft pink-gold | today | shaft amber | moonbeam shaft `#9fb4e0` 0.07; the lantern's pool +30 %; crystals unchanged; LOOK light: lantern `flicker` r 4 (all periods) |

Stage-script constants that need a `PER.const` call (the owner adds it in their own `hyb_<stage>.py`, keeping the
native output identical): the lake's `WINDOW` is wired; sea `LAMP` (buoy) and the mole lantern colour, swamp `LAMP`,
ice `WINDOW`, ocean `LAMP` / `SHIP` lights, cave `FLAME`.

### 3.10 Review

`build_periods.ps1 -Stage <stage>` renders the four looks and writes `_tmp/variants/hybrid/periods/periods_<stage>.png`:
dawn | day over evening | night, each the 480x270 game crop with 3 fish shadows drawn the Unity way with that period's
water tint and the angler sprite multiplied by its actor tint; it prints per fish `water L`, `shadow L`, `OK` /
`TOO FAINT`. That sheet is the capture of the Blender phase (section 15).

---

## 4. The angler per period (ActorArt)

The actor layer keeps its toon materials and `ActorRim.shader`; three things follow the period blend.

1. **Rim** (`ActorArt.RimFor`): from the look json instead of the switch — `col = rimCol`, `strength = rimStrength x 0.62`
   (RimScale), `edge = clamp01(rimStrength / 0.55)`, `dirs = rimDirs` (3 x (dx, dy, weight), pixel steps, y up, strongest
   first). The native looks carry exactly today's table (lake `#ffcf8c` 0.55 r / tr / t, stream `#fff2c8` 0.4 l / tl / t,
   sea `#fff4de` 0.3, swamp `#d8c088` 0.2 t / tl / tr, ice `#ffc8d0` 0.3, ocean `#ffd8b0` 0.5, cave `#86dcff` 0.45
   t / tl / tr), so today's look does not move. Blend: colour and strength lerp with F, dirs from the dominant period;
   the material is updated when F moves by >= 1/64. The rim of every look is in the section 3 tables.
2. **Key light** of the toon materials (`_KeyDir`): native look = today's `ActorArt.KeyDir` (0.752, 0.647, -0.125);
   other looks = `normalize(k.x, max(k.y, 0.55), k.z)` with `k = keyDir` of the look json (Unity axes: x right, y up,
   z forward; a light behind the angler has z < 0). Blend = normalised lerp. New `ActorArt.SetKey(Vector3)` sets it on
   every cached toon material.
3. **Actor tint**: new `ActorRim.shader` property `_Tint` (Color, default white): body and outline colours x `_Tint`
   (sRGB multiply: the project is in Gamma colour space) before the rim is computed; the rim colour is not tinted. The
   same tint multiplies the 2D fallback sprites, `RodPainter`'s colours, the float / dangling bait sprites and the line
   colour. Values (`actorTint`): native looks `#ffffff`; other looks: 새벽 `#e8e2ee`, 낮 `#ffffff`, 저녁 `#ffffff`,
   밤 `#8e9ac0`; cave 새벽 `#f4f0f4`, 저녁 `#fff4ec`, 밤 `#dce4f0` (lit by its crystals and lantern).

The legend encounter's actors keep their `EncounterSetDef` rim; the period only tints the backdrop (5.5).

---

## 5. Unity: the look of a period

### 5.1 The look json

Written by every Blender period run (section 12): `Assets/Resources/Data/Periods/<stage>_<period>.json`, loaded with
`Resources.Load<TextAsset>("Data/Periods/<stage>_<period>")` + `JsonUtility.FromJson<PeriodLook>` (not `Art.Data`, which
logs an error for a missing file).

```csharp
[Serializable] public class PeriodLight { public float x, y, r; public string col, blink; }   // canvas px, y down
[Serializable] public class PeriodLook
{
    public string stage, period;
    public bool native;
    public string waterTint, waterDeep;            // StageView.WaterTint / WaterDeep of this look
    public string rimCol; public float rimStrength; public float[] rimDirs;   // 9 floats: (dx, dy, w) x 3
    public float[] keyDir; public string keyCol;   // Unity axes, towards the light
    public string skyHorizon, skyZenith;           // for UI tints (map card, toasts)
    public string actorTint, glintCol;
    public float glintDensity, fxAlpha;
    public bool birds, fireflies;
    public PeriodLight[] lights;
}
```

Missing file (a period not rendered yet): use the stage's native look json with `waterTint` / `waterDeep` of the stage
json and the non-native defaults (`actorTint` / `glintCol` / `glintDensity` / `fxAlpha`: 새벽 `#e8e2ee` / `#ffe6e0` / 0.6 /
0.9, 낮 `#ffffff` / `#ffffff` / 1.0 / 1.0, 저녁 `#ffffff` / `#ffe0b0` / 0.8 / 0.9, 밤 `#8e9ac0` / `#c8d4f0` / 0.3 / 0.7),
and log once `[Period] <stage>_<period> missing: native art`. `StageView.Look` = the blend of the two looks (2.2):
colours lerped in sRGB, floats lerped, flags and dirs from the dominant period.

### 5.2 The actor — section 4.

### 5.3 Water, fish shadows, float and line

- `StageView.WaterTint` / `WaterDeep` become the blended look's (private setters, updated in `StageView.Update` when the
  blend changes). `UnderwaterTint`, `FishShadowTint`, `UnderwaterLine`, `Fx.Splash` callers keep their formulas; the
  shadow contrast rule (0.5 on dark water) covers the night water (play band L 0.34).
- `WaterFx.SetLook(Color tint, Color deep, float fxAlpha, float night)` (called by StageView on every blend change):
  recomputes `lit / lighter / dark / foam / crest / speck` with `Configure`'s formulas, then mixes `foam` and `crest`
  towards `#c8d4f0` by `0.3 x night` (night = the Night period's weight in the blend) and multiplies every effect's alpha
  by `fxAlpha` before the quarter-step rounding (`A(c, a * fxAlpha)`).
- Glints (`StageView.glints`): `round(26 x glintDensity)` of them are active, colour `glintCol` (alpha envelope as now).
- The float at night: its sprite x `actorTint`, plus a **케미 light tip**: a 2x2 px `#c8ff70` sprite with a 1 px halo
  (alpha 0.35) on the float's top pixel, alpha = the Night weight, sorting just above the float. It dips with the float,
  so a bite still reads.

### 5.4 Ambient life

Birds only while the dominant look has `birds`; fireflies: created on stages where any look lists them (lake 밤,
swamp 저녁 / 밤), alpha x the blended weight of those looks; snow (ice) and cave sparkles in every period; ambience clips
unchanged, volume x 0.8 at night (Night weight).

### 5.5 The legend encounter's backdrop (no re-render)

`EncounterView` reads the blend when the encounter opens and keeps it for the encounter:

| period | layer tint (multiplies bg / mid / floor / fore / ceiling sprites and the ray colours) | rays alpha x | ray colour | `_SunMix` x | lure light radius x (`lightGlow`, `lightPlain`) | halo alpha x | snow / silt alpha x |
|---|---|---|---|---|---|---|---|
| 새벽 | `#d8d0e4` | 0.6 | mixed 0.4 towards `#ffd8e0` | 0.8 | 1.0 | 1.0 | 0.8 |
| 낮 | `#ffffff` | 1 | — | 1 | 1 | 1 | 1 |
| 저녁 | `#f0d4b8` | 0.7 | mixed 0.4 towards `#ffc890` | 0.85 | 1.1 | 1.1 | 0.8 |
| 밤 | `#5a6a98` | 0.15 | mixed 0.6 towards `#a8c0ff` | 0.35 | 1.25 | 1.3 | 0.6 |

Exceptions: the **cave** set changes only at night (tint `#c8d0e8`, rays x 0.3: the shaft is far, the crystals light
it); the **ice** set at night: the hole's light column (`rayAnchored`) alpha x 0.3 and tint `#6a78a8`. `swamp_top`
(the arapaima's top view) tints its water sprites like the table. The underwater sets were drawn as daylight, so 낮 is
the identity for every set.

### 5.6 Animated lights

`StageView` makes one glow sprite per `lights` entry: a 16 px dithered disc (like `Tackle.Halo`) scaled to `r / 8`, at
canvas (x, y) -> world ((x - 320) / 16, (200 - y) / 16), sorting order 41 (over the front layer), tint `col`. Alpha =
(the blended weight of the looks that list it) x blink(t):

| blink | alpha over time (real s) |
|---|---|
| `fixed` | 1 |
| `flicker` | 0.8 + 0.2 x (0.6 sin(2 pi 7.3 t) + 0.4 sin(2 pi 11.1 t + 1.7)) |
| `flash1` | 1 for 0.4 s every 4.0 s, else 0.15 |
| `flash2` | 1 for 0.3 s at 0.0 and 0.8 s of every 6.0 s, else 0.1 |

---

## 6. Fish activity by the time of day

### 6.1 Rules

- `a(species, period)` from a new static table `TimeActivity.A(string speciesId, Period p)` (`Assets/Scripts/Data/
  TimeActivity.cs`, the table below; unknown ids -> 1). During a cross-fade `a = lerp(a[From], a[To], F)`.
- **Stocking**: `FishSpawner.Pick` multiplies each weight by `a` (0 = never stocked in that period).
- **Appetite**: the approach roll multiplies by `sqrt(a)` (9.5). A lure's strike roll too (9.5).
- **Leaving**: when `PeriodBegan` fires, every *wandering* fish whose new `a` is 0 calls `Flee()` at a random moment in
  the next 60 real s (it swims off and is removed as `Flee` does now); engaged, hooked and lifted fish are left alone.
- **Legends**: `LegendWatch`'s meter fill rate x `a(legend, period)` (their encounter plays as before).
- Night-only species: 쏘가리, 메기, 모캐 (a = 0 outside 밤). Dawn/dusk feeders: the a >= 1.3 at 새벽 and 저녁 rows.
  Day-shy: a <= 0.7 at 낮 (배스, 잉어, 열목어, 우럭, 감성돔, 참다랑어, 북극곤들매기).

### 6.2 Table (a per period; legends: encounter meter fill x)

| stage | species | id | rarity | 새벽 | 낮 | 저녁 | 밤 | character |
|---|---|---|---|---|---|---|---|---|
| lake | 붕어 | crucian_carp | C | 1.3 | 0.8 | 1.3 | 0.9 | dawn / dusk feeder |
| lake | 블루길 | bluegill | C | 1.0 | 1.3 | 1.0 | 0.4 | day feeder |
| lake | 잉어 | carp | U | 1.2 | 0.7 | 1.2 | 1.4 | night-leaning, day-shy |
| lake | 큰입배스 | largemouth_bass | U | 1.5 | 0.6 | 1.5 | 0.7 | dawn / dusk hunter, day-shy |
| lake | 황금잉어 (legend) | golden_carp | L | 1.4 | 0.7 | 1.4 | 1.0 | meter x |
| stream | 피라미 | pale_chub | C | 1.0 | 1.3 | 1.0 | 0.3 | day schooler |
| stream | 산천어 | cherry_salmon | C | 1.5 | 0.8 | 1.4 | 0.4 | dawn / dusk feeder |
| stream | 무지개송어 | rainbow_trout | U | 1.4 | 0.8 | 1.4 | 0.6 | dawn / dusk feeder |
| stream | 쏘가리 | mandarin_fish | R | 0 | 0 | 0 | 1.8 | **night-only**, holds behind rocks |
| stream | 열목어 | lenok | E | 1.6 | 0.5 | 1.6 | 0.8 | dawn / dusk, day-shy |
| sea | 전갱이 | horse_mackerel | C | 1.3 | 1.0 | 1.3 | 1.1 | all day, night under the lights |
| sea | 고등어 | mackerel | C | 1.5 | 1.1 | 1.4 | 0.4 | morning / evening boil |
| sea | 우럭 | rockfish | U | 1.0 | 0.6 | 1.2 | 1.6 | night-leaning, day-shy |
| sea | 광어 | flounder | U | 1.3 | 0.9 | 1.2 | 0.8 | dawn ambusher |
| sea | 감성돔 | black_porgy | R | 1.4 | 0.6 | 1.3 | 1.3 | wary: dawn / dusk / night |
| sea | 참돔 | red_seabream | E | 1.6 | 0.9 | 1.3 | 0.5 | dawn feeder |
| swamp | 피라냐 | piranha | C | 0.9 | 1.4 | 1.0 | 0.5 | day feeder |
| swamp | 메기 | catfish | C | 0 | 0 | 0 | 2.0 | **night-only** (야행성) |
| swamp | 가물치 | snakehead | U | 1.5 | 0.8 | 1.5 | 0.6 | dawn / dusk ambusher |
| swamp | 아로와나 | arowana | R | 1.2 | 1.3 | 1.0 | 0.3 | surface feeder by day |
| swamp | 피라루쿠 (legend) | arapaima | L | 1.3 | 1.1 | 1.0 | 0.6 | meter x |
| ice | 빙어 | smelt | C | 1.3 | 1.2 | 1.0 | 0.5 | day schooler |
| ice | 모캐 | burbot | U | 0 | 0 | 0 | 2.0 | **night-only** |
| ice | 강꼬치고기 | northern_pike | U | 1.4 | 1.0 | 1.3 | 0.3 | dawn / dusk hunter |
| ice | 북극곤들매기 | arctic_char | R | 1.3 | 0.7 | 1.3 | 0.9 | dawn / dusk, day-shy |
| ice | 철갑상어 (legend) | sturgeon | L | 0.8 | 0.6 | 1.0 | 1.6 | meter x, night |
| ocean | 방어 | yellowtail | C | 1.5 | 1.0 | 1.2 | 0.4 | morning feeder |
| ocean | 만새기 | mahi_mahi | U | 1.1 | 1.4 | 1.0 | 0.3 | day, surface |
| ocean | 참다랑어 | bluefin_tuna | R | 1.6 | 0.8 | 1.3 | 0.5 | dawn blitz, day-shy |
| ocean | 개복치 | ocean_sunfish | E | 0.6 | 1.8 | 0.6 | 0 | basks in the sun: never at night |
| ocean | 청새치 (legend) | blue_marlin | L | 1.3 | 1.2 | 0.9 | 0.4 | meter x |
| ocean | 백상아리 (legend) | great_white | L | 1.1 | 0.6 | 1.4 | 1.5 | meter x, dusk / night |
| cave | 장님동굴어 | cave_tetra | C | 1.0 | 1.0 | 1.0 | 1.0 | blind: no clock |
| cave | 수정비단잉어 | crystal_koi | R | 1.0 | 1.3 | 1.0 | 0.8 | drawn to the shaft's light |
| cave | 초롱아귀 | anglerfish | E | 1.0 | 0.8 | 1.0 | 1.4 | hunts in the dark |
| cave | 실러캔스 (legend) | coelacanth | L | 1.0 | 0.8 | 1.0 | 1.4 | meter x |

Every stage keeps at least three stocked species in every period (the lowest: ice 밤 = 빙어 0.5, 모캐 2.0, 강꼬치고기 0.3,
북극곤들매기 0.9). A new player at the lake at 10:00 meets 블루길 1.3 / 붕어 0.8 — the easy biters.

### 6.3 Where the player learns it

- Map stage card (11.4): an activity chip per species cell (caught species only).
- Encyclopedia (`CollectionUI`), caught species: one line `활동: 새벽 · 저녁` (the periods with a >= 1.3), `밤에만`
  (night-only) or `하루 종일` (all a between 0.8 and 1.2).
- The first period change on a stage: hint `timeHint` (11.3).

---

## 7. The tide (sea)

### 7.1 Schedule

Semi-diurnal, a 12 h 00 min cycle locked to the clock (so every day repeats and each period keeps its tide):

- phase `phi = 2 pi (t - 3.5) / 12`, t = game hours (`Min / 60`);
- level `h = -cos(phi)` (-1 low water at 03:30 and 15:30, +1 high water at 09:30 and 21:30);
- rate `r = sin(phi)` (+1 peak flood at 06:30 and 18:30, -1 peak ebb at 00:30 and 12:30);
- flow strength `s = |r|^0.8` (0..1).

| name | condition | HUD |
|---|---|---|
| 들물 (flood) | r >= 0.26 | `들물 ▲` |
| 날물 (ebb) | r <= -0.26 | `날물 ▼` |
| 만조 (high slack) | \|r\| < 0.26 and h > 0 (09:00–10:00, 21:00–22:00) | `만조 ━` |
| 간조 (low slack) | \|r\| < 0.26 and h < 0 (03:00–04:00, 15:00–16:00) | `간조 ━` |

(|r| < 0.26 is +-30 game minutes around high / low water.)

### 7.2 What each period gets

| period | tide |
|---|---|
| 새벽 05:00–08:00 | **들물** the whole time (peak 06:30), the level rising from -0.71 to +0.71 |
| 낮 08:00–17:00 | 들물 to 09:00 -> **만조** 09:00–10:00 -> **날물** 10:00–15:00 (peak 12:30) -> **간조** 15:00–16:00 -> 들물 from 16:00 |
| 저녁 17:00–20:00 | **들물** the whole time (peak 18:30) |
| 밤 20:00–05:00 | 들물 to 21:00 -> **만조** 21:00–22:00 -> **날물** 22:00–03:00 (peak 00:30) -> **간조** 03:00–04:00 -> 들물 from 04:00 |

So the golden periods are always moving water (the best bites: dawn / dusk feeders x flood), the midday has one long
ebb and a quiet low slack, the night has the high slack and the night ebb.

### 7.3 Effects

- **Current** (8.3): the tidal stream along the breakwater, `0.8 m/s x s`, flood towards the harbour (left), ebb out to
  sea (right).
- **Bites**: `m_tide = 0.6 + 0.75 s` (0.6 at the slack's centre, 0.86 at its edges, 1.35 at the peak), for every sea
  species (9.5), and the **reach** `r_tide = 0.85 + 0.45 s` (the running tide carries the bait's scent: a float bait is
  sensed from 4.25 m at slack, 6.5 m at the peak). Together: about **+50 % bites per minute at the peak** against slack
  water (measured x1.56), slack water ~15 % below the old rate (`-fkauto tidebites`, 15).
- **Depth**: on the sea `StageLayout.DepthAt(z) + 0.4 h` (the bottom is 0.4 m deeper at high water, shallower at low);
  `StageLayout.TideOffset` (static, set by the clock on the sea, 0 elsewhere) is added inside `DepthAt`.
- **Visuals** (10.2): flow lines, slack slicks, weed lean, a wet band on the tetrapods at low water, the buoy's wake.
- `-fktide <phase>` forces it (section 14).

---

## 8. Currents (CurrentField)

### 8.1 API

New `Assets/Scripts/Fishing/CurrentField.cs`, created by `StageView.Init` (`StageView.Current`), ticked by
`StageView.Update` with the real dt:

| member | meaning |
|---|---|
| `Vector2 Water(float x, float z)` | the water's surface current (m/s, (x, z)): stream / sea / ocean; zero on lake, swamp, ice, cave |
| `Vector2 Wind` | the wind drift of floating things right now (lake / swamp gusts), uniform; zero elsewhere |
| `float Ref` | reference speed: stream 0.55, sea 0.8, ocean 0.25, lake 0.14, swamp 0.06 (ice / cave 1, unused) |
| `float Lane(x, z)`, `float Pocket(x, z)`, `int PocketIndex(x, z)` | stream only (8.2); 1, 1, -1 elsewhere |
| `float Surge(z)` | stream surge factor S (8.2); 1 elsewhere |
| `float Gust01` | lake / swamp gust envelope 0..1 |
| `static float Mult` | `-fkcurrent` (default 1): multiplies every current and gust |

Depth factor for things under the surface: `kd(depth) = exp(-depth / 4)`.

### 8.2 Stream — a strong one-way flow towards the angler

The angler stands on his boulder at the tail of the pool, facing upstream; the water comes down the channel towards
him (-z) and passes him.

- **Base speed** `U0(t) = 0.55 x (1 + 0.15 sin(2 pi t / 3))` m/s, t = game hours (a slow 3-hour swell of the flow).
- **Surges**: a surge cycle of `T` real seconds, T drawn uniformly in [6.0, 9.0] for each cycle (`System.Random` seeded
  1234 per visit); each surge is a band that starts at z = 60 and runs towards the angler at 3 m/s; at z its local time
  is `tau = t_real - t_k - clamp((60 - z) / 3, 0, 20)` and `g = sin^2(pi tau / 1.8)` for 0 <= tau <= 1.8 (else 0);
  `S(z) = 1 + 0.45 g`.
- **Fast lane**: the thalweg meanders, `xc(z) = 1.6 sin(0.105 z + 0.9)` m; lane factor
  `Lane = 0.35 + 0.65 exp(-((x - xc(z)) / 3.2)^2)` (1 in the middle, 0.35–0.4 at the banks).
- **Slack pockets behind the mid-stream rocks** (the "Mid" boulders of `hyb_stream.py`; each pocket is just downstream,
  i.e. nearer to the angler):

  | rock (x, z, r) | pocket centre (x, z) | semi-axes (ax, az) |
  |---|---|---|
  | (-6.35, 13.5, 0.62) | (-6.35, 12.14) | (0.99, 1.61) |
  | (6.3, 25.0, 0.78) | (6.3, 23.28) | (1.25, 2.03) |
  | (-6.1, 44.0, 1.0) | (-6.1, 41.8) | (1.6, 2.6) |
  | (5.9, 66.0, 1.15) | (5.9, 63.47) | (1.84, 2.99) |
  | (-6.0, 92.0, 1.2) | (-6.0, 89.36) | (1.92, 3.12) |

  (centre = (xr, zr - 2.2 r), axes = (1.6 r, 2.6 r)); `d_k = sqrt(((x - xk) / ax)^2 + ((z - zk) / az)^2)`;
  `Pocket = min_k (0.12 + 0.88 smoothstep(0.35, 1.0, d_k))` (0.12 in the core, 1 outside).
- **Speed** `U = U0 x S x Lane x Pocket`; **direction** along the channel `d = normalize(-xc'(z), -1)` with
  `xc'(z) = 0.168 cos(0.105 z + 0.9)` (up to ~10 degrees sideways). In a pocket core (`d_k < 0.5`) the eddy runs back:
  `Water = 0.1 U0 x normalize(xc'(z), 1)` (slowly upstream).
- Result: ~0.55 m/s in the lane between surges, ~0.8 m/s in a surge, ~0.2 m/s along the banks, ~0.05 m/s back-eddy
  behind the rocks.

### 8.3 Sea — the tidal stream

`Water(x, z) = 0.8 x s(t) x k(x, z) x d`, with `s` from 7.1, `d = normalize(-1, -0.15)` on the flood (towards the
harbour behind the mole on the left, slightly in) and `normalize(1, 0.1)` on the ebb (out to sea on the right);
`k(x, z) = (0.6 + 0.6 smoothstep(4, 40, z)) x (|x| > 5 and z < 8 ? 0.5 : 1)` (weaker close in and in the lee of the
tetrapod mounds, strongest off the head).

### 8.4 Ocean — the boat drifts

In the boat's frame the water slides past: every floating and swimming thing drifts with
`Water = w(t) = v(t) (cos theta, sin theta)`, `v(t) = 0.18 + 0.07 sin(2 pi t / 4 + 1.0)` m/s,
`theta(t) = 20 deg + 25 deg x sin(2 pi t / 6)` from +x towards +z (t = game hours): the rig drifts to his right and a
little away, veering slowly over hours. Uniform in space. The angler and the boat do not move (DeckBob unchanged).

### 8.5 Lake and swamp — calm, with gusts

| | lake | swamp |
|---|---|---|
| gap between gusts (real s) | uniform 20–45, divided by the period factor | uniform 35–70, divided by the period factor |
| period factor | 새벽 0.5 (morning calm), 낮 1.3, 저녁 0.6, 밤 0.4 | 낮 1.0, others 0.5 |
| gust length (real s) | uniform 6–10 | uniform 6–10 |
| envelope | `e = sin^2(pi tau / len)` | same |
| direction | +x (blowing to his right, like today's breeze ripples) turned by uniform(-20, 20) deg per gust | same |
| `Wind` | `(0.02 + 0.12 e) x dir` m/s (a 0.02 m/s breeze between gusts) | `0.06 e x dir` |

`Water` is zero: only floating things feel the wind (9.1). `Gust01 = e`.

### 8.6 Ice and cave

No current, no wind drift (the ice hole and the cave pool are still). Only the clock and the activity table apply.

---

## 9. Gameplay

### 9.1 The rig drifts

In `Tackle.Update` (state Water, not on the ice), after the float / lure motion of today:

| rig | drift velocity `v` |
|---|---|
| float rig | `0.85 Water(S) + Wind + v_bow` (9.2) |
| lure on the film (Float buoyancy, or Depth <= 0.12) | `0.7 Water(S) + 0.5 Wind` |
| other lure | `0.7 kd(Depth) Water(S)`; x 0.1 while `OnBottom`; 0 while it hangs in the current (9.4) |

`Surface += v dt`, also while winding (added to the wind), then clamped: `|x| <= xLim - 0.6`; `z <= zFar - 1`;
the horizontal distance from the anchor at most `castDist + 6` m (projected back onto that circle: the line holds it
and it swings round, as `Tackle.Drag` already does); `z >= zNear + 1.0` (in the shallows at his feet the drift stops;
that is not a retrieve). New `Tackle.RelSpeed` = |the hook's velocity - v without `v_bow`| (smoothed like `MoveSpeed`):
0 for a float riding the water freely, the wind speed while winding, `0.12 |Bow|` for a float dragged by its line.

### 9.2 The line bow and mending

For a float rig, and for a lure at rest (not wound for >= 0.3 s):

- `u = normalize(S - Anchor)` (horizontal), right-hand normal `n = (u.z, -u.x)`; the line's water midpoint
  `mid = (ShorePoint + S) / 2`; `c_perp = dot(Water(mid) + Wind, n)`.
- `Tackle.Bow` (m, + = the belly to his right of the straight line): `dBow/dt = 0.9 c_perp - Bow / 4` while not winding,
  `dBow/dt = -Bow / 0.6` while winding; `|Bow| <= min(0.25 L, 4)` with L = |S - Anchor|.
- **Drag**: the float gets `v_bow = 0.12 Bow n` — a bowed line pulls it faster than the water (unnatural).
- **Drawn**: `Angler.LineBow = Bow n` (world, horizontal); the line's Bézier control point moves by `2 LineBow`, so the
  line's middle sits Bow metres off the straight line and lies on the water as it does now.
- **Mend** (the rod sweep): on the frame `Slide.SlideNow` is true (a committed slide; or A / D pressed, `KeyHeld`
  turning on) in Waiting with such a rig, `|Bow| >= 0.5`, the new sweep `|Slide.Value| >= 0.5` (keys: 1) and
  `sign(Slide.Value) = -sign(Bow)` (swept against the belly, i.e. upstream): `Bow -> 0.2 Bow` over
  0.25 s (ease-out), `Tackle.Drag` does nothing for that stroke (the float moves at most 0.15 m), the feedback word
  `멘딩!` shows (lure-feedback slot, `UIKit.Sky`, 0.8 s) and `Sfx.Whoosh` plays at 0.25. A stroke the other way is the
  existing drag: it leads the float across (into a slack pocket, for example).
- **Hook set**: `biteWindow -= 0.12 max(0, |Bow| - 1.0)` s, never below 0.45 s.

### 9.3 A drifting float is not "moving"

`WantsToApproach` and `FishAgent.UpdateApproach` use `Tackle.RelSpeed` instead of `MoveSpeed` for float rigs:
activity 1.0 when `RelSpeed < 0.4`, else 0.35; a follower loses interest when `RelSpeed > 1.3`. A float drifting with
the stream fishes; a float dragged by a big bow (|Bow| >= 3.3 m) does not.

### 9.4 Lure action against / with the current

`c_along = dot(Water(hook) kd(Depth) + (film ? 0.5 Wind : 0), u_out)`, `u_out = normalize(hook - ShorePoint)`
(horizontal): > 0 when the water runs away from him past the lure (he winds against it), < 0 when it runs towards him.

- **감기 lures** (`LureRhythm.UpdateSteady`): the band test uses `sp_eff = sp + c_along / Reel.retrieve` while winding.
  Not winding, with `c_along >= 0.15` and `PauseT >= 0.5` s, the lure **hangs in the current**: `sp_eff =
  c_along / Reel.retrieve` counts as winding for the band, `InBand` and Q, the rig does not drift (9.1) and keeps its
  depth. With `c_along < 0` nothing is added (it drifts back on a slack line). Examples: the sea at peak ebb, a spinner cast
  to (15, 10) on the ebb side hanging 0.5 m deep, the basic reel (0.8 m/rev): c_along = 0.8 (Vmax) x 0.64 (k) x 0.94
  (the ebb along u_out) x 0.88 (kd) = 0.43 m/s, i.e. 0.53 rev/s, just inside its 0.5–1.1 band; in the stream (c_along
  about -0.43 at 1 m in the lane) a spoon
  (0.8–1.6) needs 1.35–2.15 rev/s.
- **Flick lures**: `m = clamp(1 + c_along, 0.5, 1.5)`; the hop, pull and dart of `Tackle.Twitch` x m, and the rhythm's
  strength windows use `clamp01(strength x m)` — against the current a light 톡 is enough, with it he pulls harder.
- `LureRhythm.Update(bait, input, tackle, dt, cAlong)` gets the value from the controller.

### 9.5 Bite chances

`WantsToApproach`: `p = appeal x stealth x biteMult x activity x 0.45 x M`, `M = clamp(sqrt(a) x m_tide x m_spot x
m_drift, 0.25, 2.0)`, `p <= 0.95`:

| factor | value |
|---|---|
| `a` | the species' activity now (6.1) |
| `m_tide` | sea: `0.6 + 0.75 s` (7.3); elsewhere 1 |
| `m_spot` | stream: the hook in a pocket (`Pocket <= 0.4`) 1.3; in the fast lane (`Lane >= 0.8`, `Pocket = 1`) 0.85; else 1 |
| `m_drift` | float rigs where the water at the hook moves (`|Water| >= 0.05` m/s: not the sea at dead slack): `RelSpeed <= 0.08` (a natural, mended drift) 1.15; else 1 |

The lure strike roll (`FishAgent.UpdateApproach`) multiplies its chance by `clamp(sqrt(a) x m_tide, 0.5, 1.5)`.
Examples: 고등어 at 새벽 peak flood `sqrt(1.5) x 1.35 = 1.65`; at 밤 low slack `sqrt(0.4) x 0.6 = 0.38`.

**The reach** (`FishingController.SenseRange`): a fish rolls only within `sense x r_tide` of the hook (horizontal), `sense`
= 5 + 4 Q for a lure, 5 for a float bait (+2 for a glowing one in the cave / under the ice), `r_tide` = sea `0.85 + 0.45 s`
(7.3), elsewhere 1. Why both: a wandering fish within reach rolls about once a second until it comes, so `m_tide` alone
mostly changes how many rolls a fish needs, not how many fish come: x2.25 per roll at the peak gave only x1.23 bites per
minute with the float cast again whenever the flood pinned it, and x0.82 with the float left where the flood takes it (the
left edge / the tetrapods, thin water); the line's drag at the peak also loses `m_drift`, which the still float at dead
slack wrongly kept. `r_tide` changes how many fish come within reach (about x2.3 the area at the peak against slack), and
that sets the bite rate: x1.56 (15, `-fkauto tidebites`).

### 9.6 Fish in the current

- Wandering fish drift with `0.25 kd(depth) Water(Pos)` (they hold station against most of it).
- **Stream holders** pick their wander target inside a random slack pocket (uniformly in its ellipse) with probability
  쏘가리 0.8, 열목어 0.5, 무지개송어 0.5, 산천어 0.4, 피라미 0.1 — so a float led into a pocket meets them (m_spot 1.3).

### 9.7 Fights in the current

The controller computes each step, from the hooked fish's position:

- `cF = Water(fish)`, `rr = normalize(fish - Anchor)` (horizontal), the run direction `t = FishRun x (rr.z, -rr.x)` for
  a side run, `rr` for a straight run; **alignment** `al = clamp(dot(cF, t) / Ref, -1, 1)`.
- **Stream downstream runs**: on the stream 35 % of new `Run` phases (not bursts, not jumps) are downstream runs: the
  fish turns and rides the lane down past his side: `al = +1` for the run, `fightYawTarget` goes to the side with more
  room (`+-maxYaw`), its depth target is `min(1.0, depthMin)`, and `FightModel.Downstream = true` (below).
- **Current load** on the line from the fish holding in the flow: `CurrentLoad = 0.25 x Power x |cF| / Ref` kgf
  (x 0.5 when exhausted, x 1.5 on a downstream run, x 0.3 for 4 s after side pressure turned a with-current run).
- **Line drag**: `LineDrag = 0.02 x Line x c_perp^2` kgf, c_perp = the current across the line at its midpoint.
- **Swing**: while the fish rests, `fightYawTarget += 0.2 (dot(cF, (rr.z, -rr.x)) / Ref) dt` (clamped to +-maxYaw): a
  resting fish swings down-current on the line.
- **Side pressure**: against a with-current run the alignment is cut, `al x (1 - 0.8 good)`; when such a run is turned
  (`FightModel.Turn`), the fish is "pulled out of the flow": for 4 s `al = 0` and `CurrentLoad x 0.3`, and the flash is
  `물살에서 빼냈다!` instead of `방향을 꺾었다!`.

`FightModel` gets `public float CurrentAlign, CurrentLoad, LineDrag; public bool Downstream;` and in `Step`:

| term | change |
|---|---|
| run force (Run / Burst) | `F x (1 + 0.5 max(al, 0) + 0.3 min(al, 0))` — up to x1.5 with the current, x0.7 against |
| run line-taking (Run / Burst) | `u x (1 + 0.6 max(al, 0) + 0.4 min(al, 0))`; a downstream run: `u = -0.25 x U(fish)` (it comes nearer, taking no line) |
| tension target | `targetT += CurrentLoad + LineDrag` (half of it while giving line) |
| stamina drain | `x (1 - 0.25 al)` (with the current it tires slower, against it faster) |

The side-pressure strip shows the alignment (11.2). Legend fights use the same rules (their stage's current).

### 9.8 What stays as it is

Casting (drag down + flick up), the reel UI, the 톡, the rod sweep and side pressure mechanics (their numbers), the
fight's phases and jumps, the ice (no current) and the encounter window's own controls.

---

## 10. Visuals of the moving water (WaterFx)

All in game space as today, runs of 1 px, quarter-step alpha, `x fxAlpha` (5.3). New dash kinds: `DFlow`, `DWake`,
`DDebris`. Visual gain = how much faster than the true water a streak slides (a readability exaggeration); debris and
the rig move at the true speed.

### 10.1 Stream

- **Flow streaks** (the 46 existing): velocity `Water(x, z) x 3.2 x uniform(0.85, 1.15)` instead of a fixed -1.5..-2.5;
  70 % spawn in the lane (`|x - xc(z)| < 3.2`), 30 % elsewhere; streaks spawned inside a pocket are `DDark` only and
  move with the pocket's slow / reversed water (the eddy reads as dark, slow, curling dashes).
- **Riffles** (18): velocity `Water x 5.5`.
- **Surges**: while `S(z) >= 1.2` at a streak's z its alpha +0.25; foam at the rocks gets its gate +0.2 inside the
  surge band — a surge is seen as a brighter, faster band sliding down the stream.
- **Drifting debris**: 6 leaves (2x1 px: `#c8a040`, `#8a6a2c` or `#6a8a3a`, the second pixel one step darker) and 10
  foam flecks (1–2 px, the foam colour), spawned at z 45–65 in the lane, carried at the true `Water` speed with a
  +-0.1 m sideways wobble at 1.3 Hz, recycled at `z < zNear + 0.5` or off the water; leaves at `OrderCrest`, flecks at
  `OrderFoam`.
- **Float wake**: when `Tackle.RelSpeed >= 0.08`, every 0.25 s a V of two 1 px dashes (2–3 px long, `lit`, alpha 0.75)
  2 px behind the float relative to its motion through the water, opening 1 px per 0.1 s, life 0.6 s (`DWake`). A
  mended float riding the water leaves none.

### 10.2 Sea

- **Flow lines**: 20 `DFlow` dashes, 0.6–1.4 m long, drawn along x, velocity `Water x 2.5`, alpha `0.35 + 0.45 s`
  (off while s < 0.15), colour `lit`, spawned at z 6–45.
- **Slack slicks**: while s < 0.3, 4 glassy slicks: 3 rows each of long `DLight` runs (1.5–3 m, alpha 0.25) almost
  still, fading in over 3 real s — the calm of 만조 / 간조.
- **Weed lean**: one in five foam spots of kind 1 / 2 (water below a tetrapod) carries a 2 px weed tuft (`#3c5a2c`,
  top pixel `#5a7a3a`): at s < 0.3 upright; s >= 0.3 the top pixel 1 px towards the flow (sign of the screen x of
  `Water`); s >= 0.7 both pixels 1 px over (combed flat).
- **Foam at the tetrapods**: the crest-arrival foam x 1.5 on the up-current side (spots whose x is on the side the
  current comes from, relative to the mean x of all spots), x 0.8 on the lee side.
- **Wet band (the tide level)**: on front-layer pixels just above the water at the near tetrapods (kind-1 spots,
  z < 20), `n = round(3 (1 - h) / 2)` rows (0 at high water, 3 at low water) are drawn darker and greener — the front
  texture colour x (0.72, 0.78, 0.72) — at sorting order 41 (`WaterFx.BuildMask` keeps the front pixels for this).
- **Buoy wake**: the painted buoy (the island region) trails a down-current V of 2 dashes, 3–6 px, alpha `0.5 s`.

### 10.3 Ocean

- The swell crests slide with the drift: `DrawSwell` offsets the crest pattern by the accumulated drift
  `(X, Z) = integral of w dt` (x on the cell index, z on the crest phase).
- 8 patches of flotsam: 3–6 px clusters of sargassum (`#a88a3a` / `#c8a848`) or foam (`#e8f0f4`), carried at the true
  `w`, spawned up-drift at the play area's edge and recycled down-drift.

### 10.4 Lake and swamp

- **Cat's paw**: a gust sweeps a patch (radius 8 m lake / 5 m swamp) of 14 `DDark` dashes (0.3–0.6 m, alpha `0.55 e`)
  across the water at 2.5 m/s (lake) / 1.5 m/s (swamp) along the gust, starting from the up-wind edge of the play area.
- The breeze ripple dashes move `x (1 + 2 e)`; the swamp specks drift with `Wind`.
- The float wake (10.1) shows when the gust drags the float's line.

### 10.5 Ice, cave

Unchanged.

---

## 11. HUD and map

### 11.1 The clock panel (FishingHUD)

- `panel_dark`, anchored top-left, position (438, -12), size 140 x 52 canvas units: in the top row between the settings
  button (ends at 430) and the top bar (starts at 588 on the narrowest 960-unit canvas). Hidden with the stage name
  while fighting or in the encounter.
- Row 1: the period icon (a 12 x 12 sprite drawn in code, `Art.PeriodIcon(Period)`, shown at 24 x 24) at (8, -4) and
  the label at (36, -4), 20 px, the period's text colour (2.1): `새벽 05:40`.
- Row 2 (20 units high at the bottom), by stage:
  - stream / ocean: the current arrow (`Art.Arrow` rotated to the current's screen direction at the rig — or at
    (Angler.X, 0, 15) with no rig: the angle of `P.To2D(p + dir) - P.To2D(p)`) at (10, 4), 3 pips (6 x 6 at x 32 / 40 /
    48: 0 pips and no arrow below 0.05 m/s, 1 below 0.25, 2 below 0.6, else 3; lit `#a8e0ff`, off `#1c2a3a`) and the
    word `물살` (15 px, `#a8e0ff`) at x 60;
  - sea: the tide word (`들물 ▲` / `날물 ▼` / `만조 ━` / `간조 ━`, 15 px) at (10, 4), a 48 x 8 level bar at x 70 (fill
    `(h + 1) / 2`, `#6ab8ff` on `#1c2a3a`) and the current arrow at 12 x 12 at x 122;
  - lake / swamp: during a gust (`Gust01 > 0.2`) the arrow and `바람` (`#d8e8f0`); else empty;
  - ice / cave: empty.
- Icons (12 x 12): 새벽 a half sun r 4 `#ffb8a0` on a horizon line `#b8c8e8` at row 8; 낮 a disc r 3 `#ffe070` with 8
  one-pixel rays at distance 5; 저녁 a half sun r 4 `#ff9a50` on a horizon `#8a6a9a`; 밤 a crescent (disc r 4 `#e8ecff`
  minus a disc r 4 offset (+2, -1)) and one star pixel `#b8c8ff`.

### 11.2 The fight strip

The side-pressure strip gets a current chevron: while the run's `|al| >= 0.3`, a `>>` sprite (7 x 5 px at 2x, `#6ab8ff`)
under the fish icon, pointing the run's screen direction. The first time in a run that `al >= 0.5`: flash
`물살을 탄다! 반대로 버텨요` (`UIKit.Sky`, 1.0 s). A turned with-current run flashes `물살에서 빼냈다!` (`UIKit.Gold`).

### 11.3 One-time hints (saved flags)

| flag | when | text (`UIKit.Sky`, 2.8 s) |
|---|---|---|
| `timeHint` | the first `PeriodBegan` on a stage | `시간이 흘러 {period}이 됐어요 — 물고기의 활동도 달라져요` (새벽이 / 낮이 / 저녁이 / 밤이) |
| `tideHint` | the first sea visit, after the first cast | `물때: 들물·날물엔 입질이 활발하고, 만조·간조엔 뜸해요` |
| `driftHint` | the first stream cast whose float drifts to z <= zNear + 1.5 | `찌가 물살에 흘러왔어요 — 감아서 다시 던져요` |
| `mendHint` | the first time `|Bow| >= 1.5` | `줄이 휘어 찌가 끌려가요 — 휜 반대쪽으로 밀어 줄을 고쳐요` |

### 11.4 The map

- A clock panel (`panel_dark`, (300, -10), 150 x 56): the icon and `밤 23:40` on row 1, `낚시터에서만 흘러요` (15 px,
  `#a8a8b0`) on row 2 (the clock is stopped here).
- A period tint over the map sprite: a full-screen `Art.Pixel` renderer at sorting order 2 (over the map and its waves):
  새벽 `#c8a8d0` alpha 0.16, 낮 none, 저녁 `#ff9a50` alpha 0.12, 밤 `#1a2448` alpha 0.42 (by the clock's blend).
- The stage card (`ShowStage`): under the subtitle `지금 {period} {HH:MM}` (+ ` · {tide}` for the sea, e.g.
  `지금 밤 23:10 · 날물`); for caught species a 16 x 16 chip in the cell's top-right corner: a ≥ 1.3 now: a green `▲`
  (`#6ad06a`); 0 < a <= 0.6: a grey `▼` (`#9a9aa4`); a = 0 (night-only by day): the night icon and the fish sprite at
  alpha 0.35.

---

## 12. Blender scaffolding (done — for the 4 stage agents)

### 12.1 Files

| file | what | who edits |
|---|---|---|
| `Tools/Blender/variants/hybrid/hyb_period.py` | the period runner: command line, preset merge, outputs, install, checks, look json, light helpers | nobody (shared) |
| `Tools/Blender/variants/hybrid/periods/<stage>.py` x 7 | a stage's 4 looks: `NATIVE`, `PERIODS`, `CONSTS`, `LOOK`, optional hooks; the drafts of section 3 | the stage's agent only |
| `Tools/Blender/variants/hybrid/periods/README.md` | module API, commands, rules | nobody |
| `Tools/Blender/variants/hybrid/hyb_period_preview.py` | the review sheet `periods_<stage>.png` + the fish-shadow check | nobody |
| `Tools/Blender/variants/hybrid/build_periods.ps1` | renders a stage's periods (+ `-Period`, `-Dry`, `-Parallel`) then the sheet | nobody |
| `hyb_core.py` | + `grade.gain` / `grade.tint`, `stars`, `"__whole__"` (backward compatible) | nobody |
| `hyb_<stage>.py` x 7 | wired to `hyb_period` (`PER.use_preset`, `PER.work`, `PER.hook` x 4, `PER.save`, `PER.stage_json`, `PER.which`); the lake's `WINDOW` through `PER.const`; the stream's notch and the swamp's valley from `PER.native_pr()` | the stage's agent, only if a hook cannot do it, keeping the native output byte-identical |

### 12.2 Commands (PowerShell; Blender 5.2 at `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`)

```powershell
# a stage's four looks + its review sheet (installs into Assets unless -Dry)
.\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake
# one period, not installed, the four periods of a stage in parallel
.\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake -Period night -Dry
.\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage sea -Parallel
# raw (from Tools/Blender)
blender -b --python variants/hybrid/hyb_lake.py -- --period night              # front + back + look json
blender -b --python variants/hybrid/hyb_lake.py -- front --period night --dry  # one layer, _tmp only
blender -b --python variants/hybrid/hyb_lake.py -- back --period night         # the back needs the front's overlay: run front first
blender -b --python variants/hybrid/hyb_lake.py -- json --period night         # only the look json
blender -b --python variants/hybrid/hyb_period_preview.py -- lake sea          # review sheets
blender -b --python variants/hybrid/hyb_lake.py                                # legacy run: today's files, unchanged
```

Outputs of `--period <p>`: `Tools/Blender/_tmp/variants/hybrid/periods/<stage>_<p>_back.png`, `_front.png`,
`<stage>_<p>.json`; without `--dry` also `Assets/Resources/Sprites/Stages/<stage>_<p>_back.png` / `_front.png` and
`Assets/Resources/Data/Periods/<stage>_<p>.json`. Scratch: `_tmp/variants/hybrid/work/periods/<stage>_<p>/`, so stages
and periods render in parallel without sharing a file. A period run never writes `<stage>_back.png`, `_front.png` or
`stage_<stage>.json` (the legacy files `build_hybrid.ps1 -Install` copies).

### 12.3 Module API (`periods/<stage>.py`)

```python
NATIVE = "evening"                      # the look hyb_<stage>.py renders today (must match hyb_period.NATIVE)
PERIODS = {                             # preset overrides on top of the stage's own preset
    "dawn": dict(sky=[...], sun=None, key_dir=(...), water=dict(...), grade=dict(..., gain=0.92, tint="#f0e6f4")),
    "day": dict(...),
    "evening": {},                      # native: must stay {} (byte-identical to today)
    "night": dict(..., stars=dict(cols=[dim, bright], dens=0.006, up0=9, seed=7, bright=0.25, glow_max=0.1)),
}
CONSTS = {"night": {"WINDOW": "#ffd27a"}}              # read in hyb_<stage>.py by PER.const("WINDOW", default)
LOOK = {"night": dict(birds=False, fireflies=True, lights=[dict(x=0.0, y=0.0, col="#ffc870", r=4, blink="fixed")])}  # x, y: measured canvas px
def back_post(idx, pal, ctx): ...  return idx, pal    # optional hooks, non-native periods only
def front_post(idx, pal, ctx): ... return idx, pal
def back_scene(ctx): ...                              # after the back geometry
def front_scene(ctx): ...                             # after the front geometry; ctx["refl"] = reflected objects
```

Merging: sub-dicts merge one level (`rim=dict(strength=0.3)` keeps `col` / `dirs`), `{"__whole__": True, ...}` replaces
a sub-dict, `None` removes a feature (keep the keys the stage script reads: README). `ctx` holds `stage, period, pr,
stand, rc, kid, ps, water, sky` (`hole` on the ice), `R` (hyb_core), `W, H`. Helpers for lights:
`hyb_period.point_glow(idx, pal, col_px, row_px, hex, rings=((2.5, 0.7), (5, 0.4), (9, 0.18)), mask=None, flat=1.0)` and
`hyb_period.light_streak(idx, pal, water, col_px, row0, row1, hex, half=(1, 3), dens=0.55)`.

### 12.4 Verification (done)

- Baseline: the untouched scripts re-rendered all 14 layers byte-identical to `Assets/Resources/Sprites/Stages` (the
  pipeline is deterministic).
- After the scaffolding, each stage's native period (`lake evening`, `stream dawn`, `sea day`, `swamp evening`,
  `ice evening`, `ocean dawn`, `cave day`) printed `HYB PERIOD CHECK ... native: identical to <stage>_<layer>.png` for
  all 14 layers; those 14 files are installed as `<stage>_<native>_back.png` / `_front.png`, and the 7 native look
  jsons as `Data/Periods/<stage>_<native>.json` (actor tint white, today's rim, glints, birds / fireflies).
- All 21 drafts render (dry, `_tmp/.../periods/` only) with `front silhouette: 0 px differ` (same geometry), the
  floating-thing checks passing (pads / buoy / duckweed lighter than their water) and every review-sheet fish shadow
  `OK`. Review sheets: `_tmp/variants/hybrid/periods/periods_<stage>.png`.

### 12.5 Split for the 4 Blender agents

| agent | owns | renders |
|---|---|---|
| B1 | `periods/lake.py`, `periods/swamp.py` (+ `hyb_lake.py`, `hyb_swamp.py` if needed) | lake dawn / day / night, swamp dawn / day / night (the swamp is the slowest render, ~140 s) |
| B2 | `periods/stream.py`, `periods/ice.py` (+ their scripts) | stream day / evening / night, ice dawn / day / night |
| B3 | `periods/sea.py`, `periods/ocean.py` (+ their scripts) | sea dawn / evening / night, ocean day / evening / night |
| B4 | `periods/cave.py` (+ `hyb_cave.py`) | cave dawn / evening / night, then the `LOOK["lights"]` coordinates of every stage's night render (measured on the installed PNGs, handed to B1–B3 to paste into their modules) |

Each agent: tune the drafts on the review sheet, add the section 3.9 lights, run `build_periods.ps1 -Stage <stage>`
(installs), and deliver its sheets. The native period is never re-tuned.

---

## 13. Save fields (`SaveData`)

| field | type | default | meaning |
|---|---|---|---|
| `clockMin` | float | 600 | game minutes since 00:00 |
| `clockDay` | int | 1 | game day |
| `timeHint` | bool | false | the first period change's hint was shown |
| `tideHint` | bool | false | the sea's tide hint was shown |
| `driftHint` | bool | false | the stream drift hint was shown |
| `mendHint` | bool | false | the mending hint was shown |

`SaveSystem.Sanitize`: `clockMin = clamp(clockMin, 0, 1439.99)` (a NaN -> 600), `clockDay = max(1, clockDay)`.

---

## 14. Test switches

| switch | effect |
|---|---|
| `-fktime <hh:mm>` | sets the clock at boot (after the save loads), e.g. `-fktime 18:30` |
| `-fkperiod <dawn\|day\|evening\|night>` | sets the clock to the period's centre (06:30 / 12:30 / 18:30 / 00:30) |
| `-fktimescale <x>` | game minutes per real second (default 1; 0 = frozen; 30 = a cross-fade in 1 s) |
| `-fktide <low\|flood\|high\|ebb\|0..1>` | the sea's tide phase fixed, independent of the clock (0 = low water, 0.25 = peak flood, 0.5 = high water, 0.75 = peak ebb) |
| `-fkcurrent <x>` | every current and gust x x (0 = still water) |
| `-fkgust` | lake / swamp: a gust starts 2 real s after the stage opens (then every 12 s) |
| `-fkcurrentvivid` | WaterFx draws the current field: an arrow every 2 m in x and 4 m in z over the play area (lane red, pockets cyan, eddies magenta, sea / ocean yellow) |
| `-fkclocklog` | once a real second: `[CLOCK] 05:40 dawn f 0.00 tide flood r +0.71 h -0.71 cur (-0.55,-0.08) 0.56 m/s bow 0.8` |
| `-fkauto periods` | the capture scenario of phase 2 (15) |
| `-fkauto current` | the gameplay test of phase 4 (15) |
| `-fkauto tidebites` | the tide on bites per minute (15, phase 4b); `-fktidesecs <s>` the soak per run (default 2400 game s) |

`-fkwaterstrip` (existing) combines with `-fktime`, `-fktide`, `-fkgust` for the motion strips.

---

## 15. Capture plan (one set per phase)

| phase | what | how | files |
|---|---|---|---|
| 1 Blender looks | each stage's four looks | `build_periods.ps1 -Stage <stage>` (installs) | `periods_<stage>.png` x 7 + the `HYB PERIOD CHECK` / `HYB PREVIEW` lines |
| 2 Unity time of day | every stage at the four period centres, one mid-dissolve frame per stage, the map in the four periods | `-fkrich -fkauto periods -fktimescale 0 -fkshots <dir>`: for each stage and period, 3 s settle, then the RT (480x270, no UI) and the full screen | `per_<stage>_<period>.png`, `per_<stage>_<period>_hud.png` (28 + 28), `per_<stage>_x.png` at F = 0.5 of 07:45–08:15 (7), `map_<period>.png` (4) |
| 3 moving water | motion strips: the stream (a surge passing), the sea at flood / high slack / ebb, the ocean drift, a lake gust | `-fkrich -fkwaterstrip <dir> -fkstripn 6 -fkstripdt 0.5` with `-fkwaterstages stream` / `sea -fktide flood` / `sea -fktide high` / `sea -fktide ebb` / `ocean` / `lake -fkgust` | 6 strips |
| 4 gameplay | drift, bow and mend, tide bites, the hanging lure, fights in the current | `-fkrich -fkauto current -fkshots <dir>` (below) | one log with `[CUR] CHECK` lines + 4 shots |
| 4b tide bites | bites per minute at high slack vs the flood's peak | sea: `-fkfresh -fkrich -fkscene Fishing -fkstage sea -fkbait bait_shrimp -fkfish mackerel -fkauto tidebites`; ocean (no tide, the control): `... -fkstage ocean -fkbait bait_squid -fkauto tidebites` (below) | one log each with `[TIDE] CHECK` lines |

`-fkauto current` (new `AutoPilot.Current.cs`):

1. **stream drift + mend**: a float cast to (0, 25) in the lane, no input for 12 s: logs z and Bow each second;
   `CHECK drift` = the mean dz/dt within 0.85 x the lane speed +-20 %; then one sweep against the bow: `CHECK mend` =
   |Bow| drops >= 70 % within 0.5 s and the float moves <= 0.15 m. Shot `cur_stream_bow.png` before the mend.
2. **tide bites**: the sea with the tide phase set in-scenario, the stage stocked with 고등어 only
   (`FishSpawner.OnlySpecies`), a shrimp float soaked 120 s at the flood peak, then 120 s at high slack (every approach
   let go, no hook set): `CHECK tide` = approaches(flood) / approaches(slack) >= 1.5.
3. **hanging lure**: the sea at peak ebb, a spinner cast to (15, 10) on the ebb side and held 0.5 m deep, no winding
   for 6 s: `CHECK hang` = Q >= 0.5 and the rig moved < 0.3 m. Shot `cur_sea_hang.png`.
4. **fight**: the stream, a hooked rainbow trout, 6 runs with side pressure against every with-current run:
   `CHECK downstream` = at least one downstream run logged; `CHECK turn` = a with-current run turned and the next 4 s
   show `CurrentLoad` x 0.3. Shots `cur_fight_run.png`, `cur_fight_turned.png`.

`-fkauto tidebites` (`AutoPilot.TideBites.cs`): the bite rate itself. The clock frozen at 12:30, one rig (카본 루어 로드,
하이기어 릴, 나일론 4호, the `-fkbait` bait on a float), the tide fixed at high slack (0.5) and at the flood's peak (0.25);
for each, the stage reloaded from the same seed, the float laid at (Angler.X, 14) and soaked 2400 game s on a fixed 1/60 s
step (as fast as the machine draws). Every bite is counted and let go (the fish swims off, the bait stays on, the rig stays
out). Bites per minute of soak, the approaches and rolls and the fish within reach while none is coming are logged
(`[TIDE]`). On the sea it runs with the rule before the reach
(`FishingController.DebugOldTide`) and with the rule now: `CHECK` bites/min at the peak >= 1.3 x at slack (now), the
factors (bite x0.60 / x1.35, reach x0.85 / x1.30) and, on a fresh save, the first cast's `tideHint`. Elsewhere (the ocean
with 오징어 as the control): `CHECK` the factors are 1 at both phases and both runs bite; the two rates are logged, not
checked (the same rules run at both: they show the run-to-run spread, about x0.7–1.4 for the ocean's mixed stock of 7).
The float is laid again when the water has carried it 8 m off or holds it against an edge (moving less than half its
free drift over a second): at the sea's flood peak a float left alone is carried to the left edge / the tetrapods in
about 10 s and slides along it towards the shore, in thin water (measured before the reach: x0.82 of slack, left there).

Measured (the sea, 고등어 only, 새우, 2400 s per run): before the reach slack 4.57 / peak 5.65 bites/min (x1.23); now
slack 3.85 / peak 6.00 (x1.56; slack x0.84 of before).

No other capture rounds.

---

## 16. Unity implementation split (after the Blender looks are in)

| agent | owns | sections |
|---|---|---|
| U1 clock, save, HUD, map | `Core/GameClock.cs` (new), `Core/SaveData.cs`, `Core/Game.cs` (switches), `Fishing/FishingHUD.cs` (clock panel, fight chevron, hints), `Scenes/MapScene.cs`, `UI/CollectionUI.cs` | 1, 2.1–2.2, 11, 13, 14 |
| U2 looks | `Fishing/PeriodLook.cs` (new), `Fishing/StageView.cs` (period layers, dissolve, glints, birds, fireflies, lights), `Shaders/PeriodDissolve.shader` (new), `Fishing/ActorArt.cs`, `Shaders/ActorRim.shader`, `Fishing/Tackle.cs` render part only (float tint, 케미 tip), `Fishing/Legend/EncounterView*.cs` (5.5) | 2.3–2.4, 4, 5 |
| U3 water | `Fishing/CurrentField.cs` (new), `Fishing/WaterFx.cs` (visuals + `SetLook`), `Fishing/Tackle.cs` drift / bow / RelSpeed, `Fishing/Angler.cs` (`LineBow`), `Fishing/FishAgent.cs` (drift, holders), `Art.cs` (`StageLayout.TideOffset`) | 7.1, 7.3 depth, 8, 9.1–9.3, 9.6, 10 |
| U4 fishing rules | `Fishing/FishingController.cs` (bites, mend, c_along, fight inputs, downstream runs), `Fishing/FightModel.cs`, `Fishing/LureInput.cs` (LureRhythm), `Fishing/FishSpawner.cs`, `Fishing/Legend/LegendWatch.cs`, `Data/TimeActivity.cs` (new), `Debug/AutoPilot.Current.cs` (new) | 6, 7.3 bites, 9.4–9.8, 15 phase 4 |

`Tackle.cs` is shared by U2 (render only) and U3 (motion): U3 goes first. `FishingController.cs` is the rod-sweep
agent's file until it finishes; U4 starts after that and keeps its sweep / side-pressure constants unchanged.
