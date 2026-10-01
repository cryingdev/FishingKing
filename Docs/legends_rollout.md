# 낚시왕 — Legend encounter rollout: the other five legends (addendum to spec v1)

Status: implementable addendum to `Docs/lures_legend_spec.md` (the spec). It turns the spec's section 5 table into
concrete rows for 황금잉어 `golden_carp`, 피라루쿠 `arapaima`, 철갑상어 `sturgeon`, 청새치 `blue_marlin` and
백상아리 `great_white`. Everything not named here works exactly as for the coelacanth (spec 2.x). The coelacanth row
and its assets do not change.

The signature image is the same for every legend: in the dark, two eyes glint → a 3D silhouette comes out of the
water towards the lure or bait → the bite. The staging is the same too: an overlay window over the dimmed surface
that expands to full screen at the bite, with the lure worked directly (감기 / 톡 = a short DOWNWARD pull / 멈춤,
tap to set the hook).

The spec's constraints still hold: the left hand holds the rod, the reel UI is fixed at the bottom right, there is no
whole-body idle bob, casting stays drag down + flick up, and there are no unrequested polish or QA rounds (one capture
set per legend).

---

## 0. Decisions

1. **황금잉어 eats bait.** Its key baits are **황금 떡밥 `bait_golden` 1.0**, 옥수수 `bait_corn` 0.5 and 소프트 웜
   `bait_softworm` 0.4, so the golden bait keeps a purpose. Natural baits have no lure Q, so they use a *stillness Q*
   instead (1.2).
2. **The golden bait no longer summons the other legends.** All six legends are now encounter legends: the
   `golden:` preference is removed from all of them, and `FishSpawner.Pick` never spawns them. `rareBoost` (×3 for
   Rare fish) stays. The shop and item description becomes: **"황금잉어를 부르는 신비한 미끼. 희귀어 확률 3배."**
3. **The other four legends use the spec table's lures:** arapaima frog / popper, sturgeon softworm / jig, and marlin
   and great white kona / jig.
4. **The ocean has one lurk point shared by two legends.** Retrieve speed and depth decide which one comes, and the
   line gates the great white (5).
5. **The great white's 경계 verb is a slow crawl**, not the table's "fast wind", so the two ocean legends play
   differently: the marlin chases speed, the great white stalks a crippled fish.
6. **Each legend has one fake-out** (except the arapaima). This is its pre-bite action: the carp tastes, the sturgeon
   feels with its barbels, the marlin slashes with its bill and the great white bumps. A tap during a fake-out counts
   as the existing early tap (gauge 65, back to Tease). The fake-out has no gold flash and no "ting", so it is
   distinct from the real tell.
7. **The surface cue stays generic:** bubble rings and a two-pixel eye glint in the legend's eye colour. On the ice it
   is shown inside the hole (2.3).

---

## 1. Framework changes (code) needed by the new rows

These are additive. With the defaults, the coelacanth plays and renders exactly as today.

### 1.1 `EncounterDef` additions

| Field | Default | Meaning |
|---|---|---|
| `depthMax` | 0 (off) | the lure must be at most this deep (surface lures, marlin) |
| `lurkDepth` | 0 | lurk depth below the surface in m (0 = `DepthAt(z) − 0.5`, as now) |
| `lureAt` | `Floor` | `Floor` / `Surface` / `Mid`: where the lure rests in the set (4.2) |
| `keyRules` | empty | per key lure: `{depthMin, depthMax, bottomBand, meterQ}` overriding the row (ocean) |
| `meterQ` | `Lure` | the quality that fills the meter: `Lure` = `ctl.Rhythm.Q`; `Still` = stillness (natural baits); `Crawl` = slow-crawl EMA (great white kona) |
| `fakeOuts` | 0 | fake-outs in NoseIn (0 or 1) |
| `fakeOutText` | – | caption of the fake-out |
| `biteText` | "덥석!" | the bite caption |
| `lungeT`, `hitStop`, `shake` | 0.30 / 0.12 / (0.15, 0.15) | lunge length (the full-screen grow stays 0.30 s), hit-stop, shake |
| `lurkText` | "깊은 곳에서 무언가 눈을 떴다…" | the first appearance in a visit |
| `eyesText` | "어둠 속에서 무언가 다가온다…" | the Eyes-beat caption |
| `tellText` | "…노려본다" | the first tell caption (then "온다…!") |
| `hookedText`, `lostText` | "{name}가 걸렸다! 원을 그려 감아요!" / "{name}가 어둠 속으로 사라졌다…" | results |
| `failTips[4]` | spec 2.12 | keyed by the worst mistake: too fast / too strong / moved when excited / early tap |
| `catchLine` | – | an extra line on the catch card |
| `choreo` | coelacanth values | swim profile + approach (1.4) |
| `camScale` | 1.0 | camera rest distance × (huge fish) |

The gear tip's "(20kg 이상)" is generated from `minLine`. The great white's tip replaces "더 튼튼한 줄" with "금속 줄".

**Stillness Q** (natural baits): `clamp01(t / 3 s)`, where `t` is the time since the last wind or flick. Any wind or
flick resets it to 0.

**Crawl Q:** an EMA with τ 2 s of `s`.
- `s` = 1 while winding at 0.4–1.2 rev/s.
- `s` = 0.5 during a pause ≤ 1.5 s.
- `s` = 0 otherwise.
- A flick costs −0.2 once.

**Wrong-lure tip:** for a bait-eater legend (the golden carp), a wrong *natural* bait counts as well as a wrong lure.

### 1.2 `MoodDef`: new verb `RunPause` and a "too slow" rule for `Wind`

**`Verb.RunPause`: a short wind run, then a pause.**
- A run is credited when **all** of these hold:
  - its length is inside `runMin`–`runMax`;
  - it spent ≥ 70 % of its time inside `lo`–`hi` rev/s;
  - the pause after it reaches `pauseMin`.
- A credit gives `+gain`, once per run, with a credit caption.
- Losses:
  - above `tooFast`: `fastLoss`/s;
  - a run longer than `overRun`: `overRunLoss`/s;
  - no run for longer than `boredAfter`: `boredLoss`/s;
  - any flick: `flickLoss` once.
- The same 0.8 s fairness rule applies.

New fields: `runMin`, `runMax`, `pauseMin`, `pauseMax`, `overRun`, `overRunLoss`.

**`Verb.Wind` gains `tooSlow` / `slowLoss`.** The loss applies per second while winding below `tooSlow`, or when not
winding for > 0.5 s (the marlin) or > 1.0 s (the great white). The default `tooSlow` of 0 leaves it off.

**Per-mood caption overrides:** `creditText`, and optionally `fastText`, `slowText`, `overRunText` and `boredText`.

### 1.3 Legends per stage, ice, ocean (`LegendWatch`)

- `LegendWatch.For` holds **every** encounter legend of the stage (the ocean has two). They share one lurk point and
  one cue schedule, and each legend has its own meter, cooldown and pity. The first meter to reach 1.0 rolls. A
  started encounter zeroes the other meter, and there is still one encounter per cast. The glint uses the eye colour
  of the legend whose meter is higher (the marlin's when tied).
- **Ice is allowed.**
  - The lurk is at `x = holeX ± 5`, `z = holeZ + 3 … holeZ + 11`, on the bottom.
  - Because the lure only lives in the hole, `nearLurk` 12 always holds.
  - Cue: 3 bubble rings **inside the hole**, and the 0.5 s eye glint at the hole's centre (α 40 %).
- `-fkencounter now` checks "the equipped bait is a key of a stage legend" instead of `isLure`.

### 1.4 Choreography profile and `Legend3D`

**`choreo` fields** (numbers per legend in 3.x):
- `swim` (Spine.F, B1, B2, B3, Tail amplitudes in °);
- `tailHz` (경계, 호기심, 흥분, lunge);
- `cruise` (m/s);
- `eyes0` / `eyes1` (the Eyes-beat drift in the set frame);
- `deepDir` (the approach direction);
- `orbitSpeed` (경계 / 호기심, rad/s);
- `orbitDepth` (offset from the lure);
- `hover` (the 흥분 pose: a nose offset and a pitch).
Existing fields keep their meaning: `orbitFar`, `orbitNear`, `noseDist`.

**`Legend3D.Pose` gains channels.** Each drives a bone only when the bone exists:

| Channel | Bones |
|---|---|
| `headPitch` | Head X |
| `protrude` 0..1 | `Lips` / `UpperJaw`, from the palette's `_rig.protrude` |
| `barbel` 0..1 | `Barbel.*` |
| `sail` | `Dorsal1`, clamped by `_rig.limits` |
| `eyeRoll` 0..1 | `EyeRoll.*` |
| `bill` | `Bill` |
| `glow` 0..1 | the `_glow` materials listed in `_rig.glow`: `_Light` = lerp(ramp[1], ramp[2], glow) |

Also:
- The swim amplitudes come from `choreo.swim`.
- The Jaw / Dorsal1 clamps come from `_rig.limits`. The coelacanth has none: Jaw 40, Dorsal1 60 as today.
- The face outline for `FaceRect` is read from the palette's `_face` when present (every new legend), otherwise from
  the current coelacanth constants.
- The species bones are added to `Legend3D.Names`.

### 1.5 `ActorToon` and `EncounterView`

**`ActorToon`: new `_SunMix`** (0 = today, bit-identical). The light direction becomes
`L = normalize(lerp(toLure, _KeyDir, _SunMix))`.
- In daylight sets the fish is shaded from above.
- The lure light becomes a **visibility sphere**: outside radius R the body is `_Abyss`, the water colour, so it
  melts into the murk rather than turning black.

**`EncounterView`** reads one **`EncounterSetDef`** per backdrop (4.1) instead of today's cave constants: `#04101a`,
`#03080c`, `#1f5f70`, `#86dcff`, `#9affea`, `#3a6a7a`, `#9fd8e0`, `#8fb8b8`, `#8fb8c0`, `CamRest`, `LureRest`,
`LineUp`, `DragDir`, `Eyes0/1` and `DeepDir`.
- Optional layers (`floor`, `ceiling`, `<set>_ray`) are hidden when missing; they no longer fall back to the cave
  art.
- The new **ceiling** layer: its bottom edge sits on the projected point `(cam.x, surfaceY, cam.z + 30)`, with
  parallax 0.6.
- A **veil** darkens the back layers during Eyes.

**Surface FX in code (no new art):**
- *pop ring*: a 1 px ellipse at the ceiling line;
- *boil*: 20 `bubble_*` sprites, plus a 2-frame white ellipse flash at the ceiling line;
- *crumb puff*: golden 1 px particles `#ffd24a`.

**Importer:** `PixelArtImporter.Borders` gains `enc_frame_lake`, `enc_frame_swamp`, `enc_frame_ice` and
`enc_frame_ocean` (6 px each); bump `GetVersion()`.

### 1.6 Data

**`GameDatabase` preference strings:**

| Fish | Preferences |
|---|---|
| golden_carp | `golden:1,corn:0.5,softworm:0.4` |
| arapaima | `frog:1,popper:0.8` |
| sturgeon | `softworm:1,jig:0.5` |
| blue_marlin | `kona:1,jig:0.5` |
| great_white | `kona:1,jig:0.6` |

These strings are also the key weights and the shop fan list. `bait_golden.desc` is set as in decision 2. The five
`EncounterDef` rows are in 3.x, and the four set rows in 4.x.

---

## 2. Summary

| | 황금잉어 | 피라루쿠 | 철갑상어 | 청새치 | 백상아리 |
|---|---|---|---|---|---|
| Stage / backdrop | lake / `lake` | swamp / `swamp` | ice / `ice` | ocean / `ocean` | ocean / `ocean` (shared) |
| Lurk | weed edge of the drop-off, z 14–30, on the bottom (5–7 m) | drowned-forest shade, z 10–30, **1.2 m under the surface** | under the ice beside the hole, z 6–14, on the bottom (~4.3 m) | blue water off the drift, z 20–45, 6 m deep (shared) | same lurk |
| Key lures (weight) | golden 1.0, corn 0.5, softworm 0.4 | frog 1.0, popper 0.8 | softworm 1.0, jig 0.5 | kona 1.0, jig 0.5 | kona 1.0, jig 0.6 |
| Depth rule | ≥ 4.0 m, within 0.8 m of the bottom | ≤ 0.4 m (at the surface) | ≥ 3.5 m, within 0.8 m of the bottom | ≤ 3.0 m | kona any depth (crawl); jig ≥ 6 m |
| Meter Q | Still (baits) / Lure (worm) | Lure | Lure | Lure (fast band) | Crawl (kona) / Lure (jig) |
| minLine | **11 kgf** (플로로카본 6호) | **22** (PE 3호) | **22** (PE 3호) | **45** (PE 8호) | **100** (티타늄 와이어) |
| 경계 / 호기심 / 흥분 | Wind slow / RunPause slow / Hold | RunPause / FlickPause / Hold | Wind slow drag / FlickPause light / Hold | Wind fast / RunPause bursts / Hold | Wind crawl / FlickPause / Hold |
| Fake-out | suck and spit | – | barbel touch | bill slash | bump |
| Bite | slow suck, lips out | explosive strike from below, boil | vacuum from below, silt cloud | fast pass → slash → side grab, lit up | huge circle → bump → eyes roll → jaws out |
| Eyes core / glow | `#fff6d0` / `#ffc830` | `#ffe6c8` / `#ff7a3a` | `#f0fbff` / `#9ad8ff` | `#e8f8ff` / `#4ab0ff` | `#f4f8fa` / `#9aa8b4` |
| Species bones | Lips, Barbel.L1/R1/L2/R2 | none | Lips, Barbel.L1/R1/L2/R2 | Bill (+ Dorsal1 = sail) | UpperJaw, EyeRoll.L/R |

Common to all rows unless stated:
- gaugeStart 20, pity 10 / 30, decay 3 per s, timeoutStrike 60;
- appear 20–40 s, relocate 90–150 s (ice: 120–180 s);
- coolFail 60 s / coolFight 180 s;
- tell 0.35 s, perfectT 0.25 s, buffer 0.15 s;
- curiousAt 40, excitedAt 75, hysteresis 5, turnAwayBelow 15, earlyGauge 65.

---

## 3. Legend rows

The mood tables use the spec's notation. "Loss" captions are the spec's generic ones unless given.

### 3.1 황금잉어 golden_carp — lake, bait-eater

**Trigger:**

| Field | Value |
|---|---|
| model / backdrop | `legend_golden_carp` / `lake` |
| keyLures | golden 1.0, corn 0.5, softworm 0.4 |
| meterQ | `Still` for golden / corn, `Lure` for softworm (keyRules) |
| depthMin / bottomBand | 4.0 m / 0.8 m. The float depth must reach the bottom ("바닥 찍기"); the worm lies on it. |
| lurkZ / nearLurk / lurkDepth | 14–30 / 7 m / 0 (on the bottom) |
| minQ / minSoak / fillTime / chance | 0.6 (1.8 s still) / 10 s / 18 s / 0.6 |
| minLine | 11 kgf |
| teaseLimit | 30 s |
| noseIn / hookWindow | 1.0–1.6 s (+ 0.45 s fake-out) / 0.7 s + rod.hookBonus |
| light R / orbitFar / orbitNear / noseDist | 3.6 m (lake visibility) / 2.2 / 1.1 / 0.4 m |
| camScale | 1.0 |

**Moods:**

| Mood | Verb and numbers | Prompt | Credit |
|---|---|---|---|
| 경계 | Wind 0.1–0.4 rev/s +8/s; above 0.8 −12/s; flick −15 | 살살… 아주 천천히 끌어요 | 좋아요 |
| 호기심 | RunPause: run 0.1–0.5 rev/s for 0.3–1.2 s, pause 1.0–2.5 s → +15. Above 0.8 −10/s; run > 2.0 s −6/s ("멈춰요, 살짝만!"); no run > 4 s −4/s; flick −12 | 살짝 끌고… 멈춰요 | 따라와요! |
| 흥분 | Hold: +11/s after 0.5 s still; flick −20; circling −15/s after a 0.25 s grace | 멈춰요! 빨아들이려 해요 | 입술을 내민다…! |

Pacing: 4 s + 6 s + 3.6 s ≈ **14 s of Tease**.

**Choreography — from the murk beyond the weeds:**
- `swim` 2 / 5 / 8 / 11 / 16°; `tailHz` 0.6 / 0.8 / 0.5 / 1.8; cruise 0.9 m/s.
- **Eyes:** two gold glints low in the green murk, drifting from (4.6, 0.3, 7.6) to (3.4, 0.25, 5.6); `deepDir`
  (0.55, 0, 0.83).
- **Approach:** a slow S-curve along the bottom, head slightly down; the gold body melts out of the murk.
- **경계:** a CCW ellipse at 2.2 m (0.30 rad/s), half in the murk; Dorsal1 up 40°.
- **호기심:** 1.1 m (0.45 rad/s) in the grubbing posture: nose-down 20°, nose 0.2 m above the floor, barbels
  trailing. It follows every drag of the bait and gives a silt puff every 3–5 s when it digs.
- **흥분:** hovers nose-down 30° with its lips 0.4 m from the bait. The barbels touch the floor (`barbel` 1) and the
  lips pulse (`protrude` 0–0.3 at 1.5 Hz).
- **Idle:** pectorals scull to hover; a slow roll shows the flank to the light.
- **Lure in the set:** a circle drags the bait 0.05 m per rev and sheds a golden crumb puff every 0.1 m. A flick
  hops the bait 0.1 m and it settles back.

**Bite — slow suck, lips out:**
- **Fake-out** (at 35–60 % of NoseIn, 0.45 s): lips out to 0.6; the bait vanishes for 0.25 s and pops back out with
  a golden puff; the rod tip dips (nibble). Caption "…맛을 본다".
- **Tell:** eyes flare, gill covers flare, "ting". Captions "…입술이 움직인다" then "온다…!".
- **Lunge** (`lungeT` 0.45): head tips down 25°; lips protrude to 1.0 over 0.35 s; the bait, crumbs and silt stream
  into the mouth. Hit-stop 0.10 s; `biteText` "쪼옥!".

**Texts:**
- nameSub "백 년을 산 호수의 주인"
- lurkText "물속 깊은 곳에서 황금빛이 번뜩였다…"
- eyesText "물풀 너머에서 무언가 다가온다…"
- tipWrongLure "바닥에 가만히 놓인 달콤한 먹이를 좋아하는 것 같다…"
- lostText "황금잉어가 물풀 속으로 사라졌다…"
- failTips "다음엔: 미끼는 아주 살짝만 끌기!" · "다음엔: 톡 당기지 말고 끌기" · "다음엔: 흥분하면 멈추기!" · "다음엔: 맛볼 때는 참았다가 챔질"
- catchLine "비늘 하나하나가 금화처럼 빛난다."

**Model** (`gcarp_`, 50–100 cm):
- A deep body: depth about 0.30 L, humped just behind the head.
- One long `Dorsal1` from mid-back to the caudal peduncle (raisable 0–40°); a forked tail; pectoral, pelvic and anal
  fins.
- Large scales: V-arc edge decals in 4–5 rows (mind the budget).
- A protrusible lip ring `Lips` at the snout (its rest front = z +0.50), with the lower lip `Jaw` and the `Mouth`
  empty on `Lips`.
- Two barbel pairs: `Barbel.L1/R1` short on the upper lip, `Barbel.L2/R2` longer at the corners, both on `Lips`.

Palette ramps:

| Material | Ramp |
|---|---|
| gcarp_back | `#8a5a12 #b8801a #d8a42a` |
| gcarp_side | `#b8801a #e0aa2a #ffd24a` |
| gcarp_belly | `#c89a4a #ecc878 #fff0b8` |
| gcarp_scale | `#6a4210 #8a5a18 #b07a24` |
| gcarp_fin | `#a8601a #d88a2a #f4b04a` |
| gcarp_lips | `#b86a3a #e0946a #f8c0a0` |
| gcarp_barbel | `#a86a3a #d0946a #f0c09a` |
| gcarp_mouth | `#4a1a1a #7a2e2e #a04a44` |
| gcarp_eye_ring | `#1a0e04 #2a1a08 #4a3010` |
| gcarp_eye_glow | `#a07a20 #ffc830 #fff6d0` |

Other data:
- `FACE_BONES` `["Head", "Lips", "Jaw"]`.
- `RIG`: protrude `Lips` move (0, −0.010, 0.030), tilt (12, 0, 0); limits Jaw 30, Dorsal1 40.

### 3.2 피라루쿠 arapaima — swamp, surface lures

**Trigger:**

| Field | Value |
|---|---|
| model / backdrop / lureAt | `legend_arapaima` / `swamp` / `Surface` |
| keyLures | frog 1.0, popper 0.8 |
| depthMin / depthMax / bottomBand | 0 / 0.4 m / off (99) |
| lurkZ / nearLurk / lurkDepth | 10–30 / 7 m / 1.2 m |
| minQ / minSoak / fillTime / chance | 0.55 / 6 s / 16 s / 0.6 |
| minLine | 22 kgf |
| teaseLimit | 28 s |
| noseIn / hookWindow | 0.8–1.4 s (no fake-out) / 0.5 s + rod.hookBonus (bony mouth) |
| light R / orbitFar / orbitNear / noseDist | 2.2 m (swamp visibility) / 2.0 / 1.2 / 1.0 m (below the lure) |
| camScale | 1.15 |

**Moods:**

| Mood | Verb and numbers | Prompt | Credit |
|---|---|---|---|
| 경계 | RunPause: run 0.3–0.8 rev/s for 0.5–2.0 s, pause 0.5–2.0 s → +12. Above 1.2 −12/s; run > 2.5 s −6/s; no run > 3.5 s −4/s; flick −8 | 개구리처럼… 살살 감다 멈춰요 | 좋아요 |
| 호기심 | FlickPause: a 톡 ("퐁") of any strength, then a pause of 1.0–2.5 s → +14. Strength > 0.95 −8; re-flick < 1.0 s −6; circling > 1.5 s −6/s; no flick > 3.5 s −5/s | 톡! 퐁— 물결이 잦아들 때까지 | 올려다봐요! |
| 흥분 | Hold: +12/s after 0.6 s still (the rings must fade); flick −20; circling −15/s after 0.25 s | 멈춰요! 바로 밑에 있어요 | 떠오른다…! |

Pacing ≈ 5 + 5.5 + 3.4 = **14 s**.

**Choreography — from the murk below:**
- `swim` 2 / 4 / 7 / 10 / 14°; `tailHz` 0.5 / 0.7 / 0.9 / 3.0; cruise 0.8 m/s.
- **Eyes:** red-amber glints low and ahead in the brown murk, drifting from (2.4, −2.4, 5.2) to (1.8, −1.8, 4.0),
  rising.
- **Approach:** a long slow rising spiral (radius 2 m) out of the murk.
- **경계:** a huge dark shape passing under the lure at 0.8 m depth (orbit 2.0 m, 0.28 rad/s); the red tail edge
  shows on the turns.
  - The breath roll happens once per encounter, only in 경계 and only below gauge 40: it rises to the surface, gulps
    (a boil with a "꿀꺽" bubble burst) and sinks back.
- **호기심:** 1.2 m orbit, 0.5 m under the surface; the head tilts up at the lure after every pop.
- **흥분:** hangs 1.0 m directly under the lure, head up 50°, pectorals sculling, tail sweeping slowly.
- **Lure in the set:** it stays on the surface line.
  - A circle skims it 0.12 m per rev along the surface, the frog kicking.
  - A flick is a "pop": a 0.06 m dip, 6 bubbles, a pop ring and frame 1 for 0.2 s.

**Top view (the frog, `teaseView = Top`, `TopViewDef` set `swamp_top`; EncounterView.Top.cs):**
- Omen, open, eyes and the approach stay underwater; the approach's spiral ends on the 경계 pass.
- The approach's last 0.7 s rise through the waterline: the window splits at a moving waterline band, the underwater
  view sliding down below it and the top view coming down above it.
- The tease and the nose-in are seen straight down over the frog (2.5 m up, 56 px/m on the surface). The frog stays
  put and the water slides past it (0.3 m per turn): winding kicks it with a V-wake, a 톡 pops it ("퐁" spray, a ring, a
  hop), a pause lets it float (a settle ring, calm).
- The legend is its 3D model seen from above as a shadow (EncShadow.shader, `#10180e`): opacity 0.75 at the surface to
  0.2 at 1.8 m and blur 0.6 to 3.2 px, run from the head to the tail.
  - 경계: a slow far pass on a flat ellipse 1.9 × 0.62 m, 1.1 m down (the breath roll gulps at the surface).
  - 호기심: 1.25 × 0.42 m, 0.6 m down, bubbles over its head.
  - 흥분: head-up (−35°) with the nose 0.45 m under the frog, the surface bulging over its head.
  - Nose-in: it rises to 0.25 m (−50°) and the bulge grows; the tell streams bubbles.
- Captions from above: the 흥분 credit "…아래에서 노려본다", the nose-in "떠오른다…!". The frog is kept clear like the face.
- The lunge cuts straight to the full-screen underwater view (the strike from below); the fight starts with a 1.8×
  boil. The popper has no top frames, so it keeps the underwater tease.

**Bite — explosive strike from below:**
- No fake-out.
- **Tell:** gills flare, eyes flare, a bubble stream, "ting". Captions "…아래에서 노려본다" then "온다…!".
- **Lunge** (`lungeT` 0.20, `hitStop` 0.14, `shake` (0.25, 0.25)): the fish rockets 1.2 m up; the upturned `Jaw`
  opens 45° and the gill covers flare; a **boil** breaks at the surface line. `biteText` "펑!".
- Sfx: Splash plus Chomp at pitch 0.8.

**Texts:**
- nameSub "숨 쉬러 떠오르는 늪의 거인"
- lurkText "탁한 물속에서 무언가 숨을 쉬러 떠올랐다…"
- eyesText "탁한 물 아래에서 붉은 눈이 떠오른다…"
- tipWrongLure "수면에서 퍼덕이는 먹이를 노리는 것 같다…"
- lostText "피라루쿠가 탁한 물속으로 가라앉았다…"
- failTips "다음엔: 감다가 꼭 멈추기" · "다음엔: 톡 당긴 뒤 물결이 잦아들 때까지" · "다음엔: 흥분하면 멈추기!" · "다음엔: 솟구치기 전엔 챔질 참기"
- catchLine "붉은 비늘이 달아오른 숯불처럼 번진다."

**Model** (`arap_`, 150–300 cm, no species bones):
- A long cylindrical body, depth about 0.14 L.
- A **flat, broad bony head**: half-width greater than half-height, with plate decals.
- An **upturned mouth**: the mouth line rises towards the snout, the `Jaw` hinge sits low and behind, and the `Mouth`
  empty sits at the upper front.
- Small eyes high on the head.
- `Dorsal2` and `Anal` far back (z −0.25 … −0.40), forming a paddle with the rounded tail; small low pectorals; small
  pelvics; no `Dorsal1`.
- Big scales; red scale edges on the rear third.

Palette ramps:

| Material | Ramp |
|---|---|
| arap_back | `#1a2418 #26341f #34462a` |
| arap_side | `#2e3c2a #44543a #5e6e4a` |
| arap_belly | `#4a5242 #6a7258 #8a9272` |
| arap_red | `#6a1e14 #a0301e #d85a34` |
| arap_scale | `#1e2a1a #2a3822 #3a4a2e` |
| arap_head | `#1a1e16 #2a3024 #3e4634` |
| arap_fin | `#3a1a14 #6a2a1e #a04028` |
| arap_mouth | `#3a1410 #6a2a20 #8a4030` |
| arap_eye_ring | `#0a0806 #18120c #2a2016` |
| arap_eye_glow | `#8a3a18 #ff7a3a #ffe6c8` |

Other data: `FACE_BONES` `["Head", "Jaw"]`; `RIG` limits Jaw 45.

### 3.3 철갑상어 sturgeon — ice, bottom

**Trigger:**

| Field | Value |
|---|---|
| model / backdrop / lureAt | `legend_sturgeon` / `ice` / `Floor` |
| keyLures | softworm 1.0, jig 0.5 |
| depthMin / bottomBand | 3.5 m / 0.8 m. The hole's bottom is at about 4.35 m: drop the lure all the way. |
| lurk / nearLurk | x holeX ± 5, z 6–14, on the bottom (1.3) / 12 m |
| minQ / minSoak / fillTime / chance | 0.5 / 8 s / 18 s / 0.6 |
| minLine | 22 kgf |
| relocate | 120–180 s |
| teaseLimit | 30 s |
| noseIn / hookWindow | 1.2–1.8 s (+ 0.45 s fake-out) / 0.7 s + rod.hookBonus |
| light R / orbitFar / orbitNear / noseDist | 3.0 m, centred 1.2 m above the lure in the hole's light column (4.4) / 2.6 / 1.4 / 0.35 m |
| camScale | 1.1 |

**Moods:**

| Mood | Verb and numbers | Prompt | Credit |
|---|---|---|---|
| 경계 | Wind 0.1–0.35 rev/s +8/s (a slow drag along the bottom); above 0.7 −12/s; flick −15 | 살살… 바닥을 천천히 끌어요 | 좋아요 |
| 호기심 | FlickPause: a 톡 of strength ≤ 0.5, then a pause of 1.2–3.0 s (the worm settles back) → +14. Strength > 0.7 −10; re-flick < 1.2 s −6; circling > 1.5 s −6/s; no flick > 4 s −4/s | 바닥에서 살짝 톡… 기다려요 | 더듬어 봐요! |
| 흥분 | Hold: +10/s after 0.6 s; flick −20; circling −15/s after 0.25 s | 멈춰요! 수염이 닿았어요 | 입이 내려온다…! |

Pacing ≈ 4 + 7.3 + 4.2 = **15.5 s**.

**Choreography — along the bottom, out of the dark under the ice:**
- `swim` 1 / 3 / 6 / 9 / 14° (heterocercal, shark-like); `tailHz` 0.45 / 0.6 / 0.5 / 1.5; cruise 0.6 m/s.
- **Eyes:** two small pale eyes at floor level, drifting from (5.0, 0.2, 7.0) to (3.8, 0.2, 5.0); `deepDir`
  (0.7, 0, 0.7).
- **Approach:** a straight, slow line along the floor. The barbels drag. The scute rows catch the light column one by
  one as it crosses it ("the armour gleams").
- **경계:** an elongated oval pass (mowing back and forth past the worm) at 2.6 m, 0.25 rad/s, outside the column,
  as a silhouette.
- **호기심:** 1.4 m, passing through the light column on each lap. **Bottom sweep:** the snout swings ±12° every 3 s.
- **흥분:** glides in and stops with its snout 0.35 m over the worm, pectorals planing, barbels curled onto it
  (`barbel` 1).
- **Lure in the set:** a circle drags the worm 0.06 m per rev along the floor, with a silt puff every 0.3 m. A flick
  hops it 0.15 m; it settles in 0.4 s with a silt puff.

**Bite — vacuum from below, off the bottom:**
- **Fake-out:** the barbels sweep across the worm (a curl twitch); the line twitches (nibble). Caption
  "…수염으로 더듬는다".
- **Tell:** the mouth tube starts to drop (`protrude` 0.3), eyes flare, "ting". Captions "…입이 내려온다" then
  "온다…!".
- **Lunge** (`lungeT` 0.30): `Lips` protrude to 1.0 over 0.18 s; head pitches down 10°; a big silt cloud
  (6 puffs); the worm is sucked up into the tube. `biteText` "후읍!".

**Texts:**
- nameSub "공룡과 함께 살았던 갑옷 물고기"
- lurkText "얼음 아래에서 무언가 바닥을 훑고 지나갔다…"
- eyesText "얼음 밑 어둠 속에서 무언가 다가온다…"
- tipWrongLure "바닥을 천천히 기어가는 먹이를 찾는 것 같다…"
- lostText "철갑상어가 얼음 밑 어둠으로 사라졌다…"
- failTips "다음엔: 바닥은 아주 천천히 끌기!" · "다음엔: 톡은 아주 살짝" · "다음엔: 흥분하면 멈추기!" · "다음엔: 수염이 더듬을 땐 참기"
- catchLine "등의 뼈 비늘이 갑옷처럼 단단하다."

**Model** (`stur_`, 120–280 cm):
- An elongated body with a pentagonal section: 5 scute rows (1 dorsal, 2 lateral, 2 ventral) of diamond plates.
- A long flat pointed rostrum: the snout covers z +0.38 … +0.50.
- The mouth underneath at z about +0.36: `Lips` (the tube) with the lower lip `Jaw` and the `Mouth` empty on it.
- **Four barbels in a row** across the underside of the snout at z about +0.42: `Barbel.L1/R1` inner and
  `Barbel.L2/R2` outer, all on `Head`.
- A heterocercal tail: `Tail.Upper` long and continuing the body line, `Tail.Lower` small.
- `Dorsal2` and `Anal` far back; low wide pectorals; pelvics.

Palette ramps:

| Material | Ramp |
|---|---|
| stur_back | `#1a1e24 #262c34 #343c46` |
| stur_side | `#2e343a #444c54 #5e6870` |
| stur_belly | `#8a8a7e #b4b2a4 #dcd8c8` |
| stur_scute | `#6a6a60 #a8a494 #e0dccb` |
| stur_fin | `#1e2228 #2e343c #424a54` |
| stur_barbel | `#8a7a78 #b4a2a0 #dccac6` |
| stur_lips | `#7a6a68 #a8908c #d0b8b2` |
| stur_mouth | `#3a2424 #5a3432 #7a4a46` |
| stur_eye_ring | `#08090c #101218 #1e222a` |
| stur_eye_glow | `#4a7a9a #9ad8ff #f0fbff` |

Other data:
- `FACE_BONES` `["Head", "Lips", "Jaw"]`.
- `RIG`: protrude `Lips` move (0, −0.030, 0.008), tilt (20, 0, 0); limits Jaw 25.

### 3.4 청새치 blue_marlin — ocean (shared lurk), speed

**Trigger:**

| Field | Value |
|---|---|
| model / backdrop / lureAt | `legend_blue_marlin` / `ocean` / `Mid` |
| keyLures | kona 1.0, jig 0.5 |
| depthMax | 3.0 m (both lures); meterQ `Lure` |
| lurkZ / nearLurk / lurkDepth | 20–45 / 10 m / 6 m (shared) |
| minQ / minSoak / fillTime / chance | 0.6 / 5 s / 14 s / 0.6 |
| minLine | 45 kgf |
| teaseLimit | 26 s |
| noseIn / hookWindow | 0.6–1.0 s (+ 0.45 s fake-out) / 0.5 s + rod.hookBonus |
| light R / orbitFar / orbitNear / noseDist | 6.0 m (ocean visibility) / 3.0 / 1.5 / 0.9 m (from the mouth; the bill is longer) |
| camScale | 1.2 |

**Moods:**

| Mood | Verb and numbers | Prompt | Credit |
|---|---|---|---|
| 경계 | Wind 1.6–2.6 rev/s +9/s; **tooSlow** 1.2 (or stopped > 0.5 s) −8/s ("느려요! 계속 달려요"); flick −10 | 빠르게! 쉬지 말고 감아요 | 좋아요 |
| 호기심 | RunPause bursts: run 1.8–3.0 rev/s for 0.5–1.4 s, pause 0.3–1.0 s → +13. Run > 2.2 s −4/s ("툭 멈췄다 다시!"); no run > 1.6 s −8/s ("너무 오래 멈췄어요"); flick −10 | 휙 감다가 툭! 멈췄다 다시 | 몸이 빛나요! |
| 흥분 | Hold (the drop-back): +14/s after 0.3 s still; flick −20; circling −15/s after 0.25 s | 멈춰요! 먹이를 놓아줘요 | 푸른 줄무늬가 타오른다…! |

The keyboard wind (2.6 rev/s) fits both bands. Pacing ≈ 3.3 + 4.5 + 2.6 = **10.5 s** (a fast fish).

**Choreography — up from the blue deep, fast:**
- `swim` 0.5 / 1 / 2 / 5 / 18° (thunniform: a stiff body, a hard tail); `tailHz` 1.2 / 1.6 / 1.4 / 3.5; cruise
  3.0 m/s.
- **Eyes:** blue-white glints deep below and far away, drifting from (2.5, −4.5, 9.0) to (1.5, −2.0, 6.0), then a
  streak; `deepDir` (0.5, −0.5, 0.7).
- **Approach:** rises fast and straight, then flattens out behind the lure, the sail folded and the stripes dim.
- **경계:** *tails* the lure 3.0 m behind it, weaving ±1 m at 0.5 Hz. This is a weave, not an orbit.
- **호기심:** fast figure-eight passes at 1.5 m (2.5 m/s) that cross in front of the camera; sail half up (35°);
  the stripes flicker with each credit.
- **흥분:** lit up (`glow` 1), sail up (75°), pectorals flared; cruises 1.0 m behind the lure, matching it, the bill
  tip about 0.5 m from the lure.
- **`glow`** = clamp01((gauge − 40) / 35), plus a 0.3 s flash on each credit.
- **Lure in the set:** winding moves the kona forward 0.15 m per rev along `DragDir`, with a bubble trail. When
  paused it sinks visually at 0.1 m/s (at most 0.6 m). A flick darts it 0.2 m.

**Bite — fast pass, bill slash, then the grab:**
- **Fake-out:** the head whips ±25° in 0.12 s (with `bill` ±6°); the lure tumbles 0.25 m sideways, spinning; the
  line twitches. Caption "휙— 부리로 친다!".
- **Tell:** the sail snaps up, the stripes flare, "ting". Captions "…돛을 세운다" then "온다…!".
- **Lunge** (`lungeT` 0.25, `hitStop` 0.10): it comes in from the side at 3 m/s; `Jaw` 30°; it grabs the kona
  crosswise; `glow` 1. `biteText` "콱!".

**Texts:**
- nameSub "대양을 가르는 푸른 창"
- lurkText "수평선 아래에서 푸른 빛이 번뜩였다…"
- eyesText "깊고 푸른 곳에서 무언가 치솟는다…"
- tipWrongLure "빠르게 달아나는 먹이를 쫓는 것 같다…"
- lostText "청새치가 푸른 바다 너머로 사라졌다…"
- failTips "다음엔: 경계할 땐 쉬지 말고 빠르게!" · "다음엔: 감다가 짧게만 멈추기" · "다음엔: 흥분하면 멈추기!" · "다음엔: 부리로 칠 땐 참았다가 챔질"
- catchLine "푸른 줄무늬가 아직도 희미하게 빛난다."

**Model** (`marl_`, 200–400 cm; the 1.0 m includes the bill):
- `Bill`: a dark spear at z +0.50 … +0.36, on `Head`.
- A torpedo body, deepest at z about 0.10 (depth 0.20 L), narrowing to a keeled peduncle.
- A tall lunate tail (`Tail.Upper/Lower`).
- **The sail `Dorsal1`:** tall at the front (z 0.22), long and low back to z −0.10, cobalt with spots. Model it
  RAISED and fold it −75° about X to rest.
- Long rigid falcate pectorals (rest folded flat; flare 0–40°); thin pelvic spikes; `Dorsal2` / `Anal` small finlets.
- 12–15 vertical flank stripes as `marl_stripes_glow` decals.

Palette ramps:

| Material | Ramp |
|---|---|
| marl_back | `#0c1630 #142448 #1e3462` |
| marl_side | `#1e3a78 #2c56a4 #4a7ac8` |
| marl_belly | `#8e9cb4 #c4ceda #eef2f8` |
| marl_stripes_glow | `#1a3a6a #2a5a9a #7ae0ff` (ramp[1] dim, ramp[2] lit) |
| marl_sail | `#0e1a44 #1a3070 #2e4ea0` |
| marl_fin | `#0e1a3a #182c5a #26407e` |
| marl_bill | `#0a1224 #16203a #26344e` |
| marl_mouth | `#2a1a2a #44283a #603a4e` |
| marl_eye_ring | `#04060a #0a0e16 #161e2a` |
| marl_eye_glow | `#1a5a9a #4ab0ff #e8f8ff` |

Other data:
- `FACE_BONES` `["Head", "Bill", "Jaw"]`.
- `RIG`: limits Jaw 30, Dorsal1 75; glow `marl_stripes_glow`.

### 3.5 백상아리 great_white — ocean (shared lurk), stalker

**Trigger:**

| Field | Value |
|---|---|
| model / backdrop / lureAt | `legend_great_white` / `ocean` / `Mid` |
| keyLures | kona 1.0 (meterQ `Crawl`, any depth), jig 0.6 (meterQ `Lure`, depthMin 6 m) |
| lurk | shared with the marlin (z 20–45, 6 m deep) |
| nearLurk | 10 m |
| minQ / minSoak / fillTime / chance | 0.55 / 6 s / 20 s / 0.55 |
| minLine | **100 kgf** (titanium wire) |
| coolFail / coolFight | 90 s / 240 s |
| teaseLimit | 30 s |
| noseIn / hookWindow | 1.0–1.6 s (+ 0.45 s fake-out) / 0.6 s + rod.hookBonus |
| light R / orbitFar / orbitNear / noseDist | 6.0 m / 3.4 / 2.0 / 1.5 m |
| camScale | 1.4 |

**Moods:**

| Mood | Verb and numbers | Prompt | Credit |
|---|---|---|---|
| 경계 | Wind crawl 0.4–1.2 rev/s +8/s; above 1.8 −10/s; **tooSlow** 0.25 (or stopped > 1.0 s) −4/s ("계속 비틀비틀 감아요"); flick −12 | 비틀비틀… 천천히 감아요 | 좋아요 |
| 호기심 | FlickPause: a 톡 of strength ≤ 0.7, then a pause of 0.8–2.0 s → +13. Strength > 0.85 −10; re-flick < 0.8 s −6; circling > 1.5 s −6/s; no flick > 3 s −5/s | 톡! 다친 물고기처럼… 멈춰요 | 다가와요… |
| 흥분 | Hold: +11/s after 0.5 s; flick −20; circling −15/s after 0.25 s | 멈춰요! 움직이면 끝이에요 | 입을 벌린다…! |

**Choreography — from below, out of the blue gloom (below the thermocline):**
- `swim` 1 / 2 / 4 / 7 / 14°; `tailHz` 0.35 / 0.45 / 0.55 / 2.2; cruise 1.0 m/s. The jaw hangs 5° open when
  cruising.
- **Eyes:** dead-silver glints far below, drifting from (1.5, −4.0, 8.0) to (1.0, −2.8, 6.5); `deepDir`
  (0.3, −0.8, 0.5).
- **Approach:** the huge silhouette rises slowly and fills the window.
- **경계:** a **huge slow circle**, CCW, 3.4 m, 0.22 rad/s, 0.6 m below the lure. Its far point passes between the
  camera and the lure as a grey wall sweeping across.
- **호기심:** 2.0 m, 0.30 rad/s, closer and lower.
- **흥분:** a spiral that tightens to 1.6 m, then stops head-on 1.5 m out and closes slowly.
- **Lure in the set:** as the marlin's. When crawled, the kona "limps": 0.08 m per rev, yawing ±15° side to side.

**Bite — bump, eyes roll back, jaws out:**
- **Fake-out:** the snout bumps the lure; the lure is knocked 0.3 m and the line jerks (rod-tip dip). Caption
  "쿵… 코로 들이받는다".
- **Tell:** **the eyes roll back** (`eyeRoll` 1 over 0.2 s). The eyeshine goes out by its facing test and the white
  shows. The mouth starts to open, "ting". Captions "…눈이 뒤집힌다" then "온다…!".
- **Lunge** (`lungeT` 0.30, `hitStop` 0.15, `shake` (0.3, 0.3)): `headPitch` −12° (the snout lifts); `UpperJaw`
  protrudes (`protrude` 1); `Jaw` 50°; rows of teeth. `biteText` "콰직!".

**Texts:**
- nameSub "바다의 절대 포식자"
- lurkText "배 밑으로 거대한 그림자가 지나갔다…"
- eyesText "푸른 어둠 아래에서 거대한 그림자가 떠오른다…"
- tipWrongLure "천천히 비틀거리는 먹이를 노리는 것 같다…"
- gear tip "이 녀석을 상대하려면 금속 줄이 필요할 것 같다… (100kg 이상)"
- lostText "백상아리가 푸른 어둠 속으로 가라앉았다…"
- failTips "다음엔: 경계할 땐 천천히 비틀비틀" · "다음엔: 톡은 짧게, 너무 세지 않게" · "다음엔: 흥분하면 멈추기!" · "다음엔: 들이받을 땐 참았다가 챔질"
- catchLine "이빨 한 줄 한 줄이 칼날처럼 늘어서 있다."

**Model** (`gw_`, 300–500 cm):
- Fusiform and heavy: depth 0.24 L, with real girth.
- A conical snout at z +0.50; an underslung gape at z 0.32 … 0.44.
- `UpperJaw` (with the upper tooth row) and `Jaw` (with the lower row); about 10 teeth per side per jaw.
- 5 gill slits as `gw_gill` decals at z 0.26 … 0.30.
- A big rigid triangular dorsal (`Dorsal1`, limit 0: it never rises); long falcate pectorals; small pelvics, 2nd
  dorsal and anal; a crescent tail with the upper lobe a little bigger; caudal keels.
- A sharp, jagged countershading line between `gw_side` and `gw_belly`.
- **Eyeballs on `EyeRoll.L/R`:** the dark front hemisphere carries the `Eye.*` empty and the `geo_Eye.*` lens; the back
  hemisphere is `gw_eye_white`.

Palette ramps:

| Material | Ramp |
|---|---|
| gw_back | `#2a3038 #3a424c #525c68` |
| gw_side | `#3a424c #525c68 #707c88` |
| gw_belly | `#a0a8b0 #d2d8de #f4f6f8` |
| gw_fin | `#22282e #333a42 #4a525c` |
| gw_gill | `#14181c #1e2428 #2a3036` |
| gw_mouth | `#4a1a22 #7a2e38 #a44a52` |
| gw_teeth | `#b8b4a8 #e0dccf #f8f6ee` |
| gw_eye_ring | `#020304 #06080a #101418` |
| gw_eye_white | `#8a8e92 #c8ccd0 #eef0f2` |
| gw_eye_glow | `#3a4450 #9aa8b4 #f4f8fa` |

Other data:
- `FACE_BONES` `["Head", "UpperJaw", "Jaw"]`.
- `RIG`:
  - protrude `UpperJaw` move (0, −0.012, 0.025), tilt (8, 0, 0);
  - roll `EyeRoll.L` / `EyeRoll.R` axis (1, 0, 0), 150°;
  - limits Jaw 50, Dorsal1 0.

---

## 4. Backdrop sets and light profiles

### 4.1 `EncounterSetDef` rows (code) and set modules (art)

| Field | cave (today) | lake | swamp | ice | ocean |
|---|---|---|---|---|---|
| lureAt / lure rest | Floor, 0.04 above the floor | Floor, 0.04 | Surface, the surface line 0.05 above the lure | Floor, 0.04; ice ceiling at y +4.3 | Mid, the surface 1.2 above, no floor |
| camRest / frameAt | (−1.2, 0.45, −2.7) / (0.33, 0.30) | same | (−1.0, −0.9, −2.6) / (0.40, 0.72) | (−1.2, 0.45, −2.7) / (0.33, 0.30) | (−1.3, −0.7, −3.0) / (0.36, 0.62) |
| lineUp | (−0.9, 2.2, −1.6) | same | (−1.4, 0.05, −1.2), along the surface | (0, 4.3, 0), straight up to the hole | (−1.0, 1.2, −1.4), up to the boat |
| dragDir | (−0.45, 0, −0.9) | same | (−0.5, 0, −0.85) | (−0.45, 0, −0.9) | (−0.6, 0, −0.8) |
| clear | `#04101a` | `#1a3024` | `#10180e` | `#04080f` | `#081e44` |
| abyss / fogOutline | `#03080c` / `#1f5f70` | `#1a3024` / `#3a5a3a` | `#10180e` / `#2e3a22` | `#04080f` / `#2a4a6a` | `#081e44` / `#2a5a90` |
| sunMix | 0 | 0.75 | 0.8 | 0.6 (key straight down; light centred 1.2 m above the lure) | 0.8 |
| visibility (light R) | 3.0 / 2.0 | 3.6 | 2.2 | 3.0 | 6.0 |
| rays (tint, α) | 1 × `#86dcff` 0.08 | 2 × `#f0ffc0` 0.14 | 1 × `#c8b478` 0.05 | `uw_ice_ray`: the hole column, anchored at the projected hole | 3 × `#c8ecff` 0.12, swaying |
| halo | `#9affea` (glow 0.9 / plain 0.35) | `#fff0a0`, golden bait 0.5 (others 0) | none | `#bfe8ff` 0.25 | none (kona: bubble trail) |
| snow far / near | `#3a6a7a` / `#9fd8e0` | `#6a8a5a` / `#c8e0a8` (pollen) | `#6a6a44` / `#a8a070` | `#3a5a7a` / `#cfe8ff` (ice motes) | `#4a80b8` / `#b8e0ff` (plankton) |
| silt tint / line | `#8fb8b8` / `#8fb8c0` | `#a8966a` / `#d8e8c8` | `#8a7a50` / `#c8c098` | `#8aa0b8` / `#d8e8f8` | – (bubbles) / `#d0e8f8` |
| rim (col, strength, dirs) | `#86dcff` 0.45 T/TL/TR | `#e8f8c0` 0.35 T/TL/TR | `#d8c888` 0.25 T | `#cfefff` 0.4 T | `#bfe8ff` 0.45 T/TL/TR |
| veil in Eyes (col, α) | – | `#0c1a10` 0.55 | `#080c06` 0.6 | `#02040a` 0.5 | `#020a1a` 0.45 |
| frameLine | `#6affea` | `#a8e878` | `#d8e070` | `#bfefff` | `#7fd4ff` |

- **Rim** dirs: T = top, TL = top-left, TR = top-right.
- **Veil:** it fades out over the first 1.5 s of Approach.
- **Per legend:** `camScale` multiplies camRest's distance from the lure. `eyes0/1` and `deepDir` come from the legend
  row (3.x), not the set.

**Set modules** (`Tools/Blender/variants/hybrid/encounter_sets/<set>.py`; the API is in its README) mirror these
values in `PROFILE` for the preview mock, and list their lure billboards:

| Set | LURES | New lures and their frame 1 (`LURE_TWEAKS`) |
|---|---|---|
| lake | golden, corn, softworm | golden: the glitter crumbs shift; corn: the kernels swing |
| swamp | frog, popper | frog: the legs kick; popper: nose up (the pop) |
| ice | softworm, jig | – |
| ocean | kona, jig | kona: the skirt flares |

### 4.2 lake — murky green daylight

- **bg** (HORIZON 150): a gradient from `#6a8a4a` (the bright upper water) through `#3a5a36` and `#243e2a` to
  `#16281c`. The top ~90 rows are the surface far above: a bright band carrying **lily-pad shadows** (round dark
  blobs with a notch, `#2a4a26`) and dash-dithered caustic streaks. Far wavy weed-forest silhouettes `#2a4a2c` rise
  into the haze band.
- **mid:** weed beds — eelgrass and pondweed ribbons, `#2e5a2a` … `#6a9a3a`, which can be rendered in 3D like the
  cave pillars. Also a sunken log and reed stems at both edges; the tops dissolve into the murk.
- **floor:** mud and silt `#3a3a24` … `#6a6a44` with pebbles, fallen leaves and a few mussel-shell glints (1 px).
- **fore:** out-of-focus weed fronds, `#10200e`.
- **frame:** mossy wood with lily-pad corner studs.
- **Light:** sunbeams from the top-right. The fish is lit from above and melts into the green at 3.6 m.

### 4.3 swamp — brown-green murk, very low visibility (surface lures)

- **ceiling** (768×120, required for this set): the underside of the surface.
  - A dull olive-bright band `#6a6a3a`, with a dim amber Snell's-window disc overhead (`#a89a5a`, the dusk glow).
  - Duckweed, fallen leaves and lily pads as silhouettes `#1e2616`.
  - The far edge (the bottom row) turns to a mirror and dissolves into the murk.
  - The frog or popper is seen from below against it.
- **bg:** murk from `#4a4a2a` (just under the surface) through `#2e3620` and `#1c2416` to `#10180e`; far drowned
  trunks `#1a2214`; the haze band.
- **mid:** a drowned forest — vertical trunks with branches, and roots hanging from the top, `#1a2412` … `#3a4228`.
- **floor** (optional): dim leaf litter `#1a2014` … `#2e3620`, dissolving fast.
- **fore:** a big out-of-focus root or branch at the bottom-left, `#0a0e08`.
- **frame:** gnarled roots with firefly corner studs (yellow-green dots).
- **Light:** almost none, from the surface. The fish is invisible beyond 2.2 m.

### 4.4 ice — dark under the ice, a light column from the hole

- **ceiling:** the ice underside — dark blue-grey ice `#1a2640` … `#2a3a5a` with cracks, pressure ridges and frost
  texture.
- **uw_ice_ray** (64×256): the **hole** as a bright ellipse (48×10, `#bfe8ff` / `#ffffff`) at the top, with a
  vertical light column fading down. It runs from the projected hole point (set (0, 4.3, 0)), directly above the lure,
  along the light (−`keyDir`: straight down) to the projected floor under it. It is not a top-right god-ray. It is
  drawn as a sheared mesh, not a sprite: in the level rest view it stays vertical; when the camera pitches (the lunge's
  close-up looking down at the bite) it leans the way the 3D column projects. Each row keeps its 64 texels 1:1 (point
  filtering), shifted by whole pixels, and the rows are spread along it by their 3D height.
- **bg:** from `#1a2438` (just under the ice) through `#0e1628` and `#080e1c` to `#04080f`; far bottom-slope
  silhouettes; the haze band.
- **mid:** boulders, sunken dead branches and weed, `#101a2c` … `#24344e`.
- **floor:** gravel and silt `#141c2a` … `#2a3448`.
- **fore:** a dark out-of-focus rock at the bottom-left.
- **frame:** ice blocks (lit `#dff4ff`, mid `#6a90b8`, dark `#2a4060`) with frost-crystal studs.
- **Light:** only the column. The worm glints in it; the sturgeon is a silhouette until it crosses the column.

### 4.5 ocean — deep blue open water (shared by the marlin and the great white)

- **ceiling:** the surface seen from below. A bright blue-white Snell band `#8ad0f0` … `#4a90c8` with
  dash-dithered caustic lines. **The boat hull shadow:** a dark elongated hull `#0a1a30` with its keel, left of
  centre, where the line comes from, with a wake-bubble trail.
- **bg:** from `#2a70b0` through `#1a5090`, `#0e3468` and `#081e44` to `#040e24` (the depth fog below). There is no
  terrain; there are faint distant baitfish specks at the horizon band.
- **mid:** open water — a distant swirling baitball silhouette `#1a4a80` and sparse plankton.
- **floor:** none (the bottom is 18–20 m down).
- **fore:** a large blurred moon-jelly silhouette at the bottom-left (`#6aa0d0`, α 0.5 look, dithered).
- **frame:** navy with a brass porthole (ink `#0a0e18`, dark `#1a2640`, mid `#2a3a5a`, lit `#3a4e74`) and brass
  rivet studs `#6a4a1a` / `#c8962a` / `#ffe08a`.
- **Light:** god rays from above; visibility 6 m. Fish below the lure fade into the deep blue.

---

## 5. The ocean: one lurk, two legends

1. **One lurk point** for the ocean: z 20–45, 6 m deep, with one cue schedule. Two meters run on it, one per legend,
   each with its own conditions.
2. **Speed picks with the kona:**
   - fast, in the kona's band 1.6–2.6 rev/s (lure Q ≥ 0.6), running at ≤ 3 m → the **marlin** meter;
   - a slow crawl at 0.4–1.2 rev/s (Crawl Q ≥ 0.55) → the **great white** meter.
   - The two are exclusive: a fast kona gives zero Crawl Q, and a crawling kona has a low lure Q.
3. **Depth picks with the jig:** a jig ripped at ≤ 3 m → marlin (0.5); a jig worked at ≥ 6 m → great white (0.6);
   between 3 and 6 m, neither.
4. **The line gates:**
   - the marlin needs ≥ 45 kgf (PE 8호);
   - the great white needs **≥ 100 kgf (티타늄 와이어)**.
   - Below the gate, that legend's meter stops at 0.7 and gives its gear tip once per visit. With PE 8호 a player can
     only meet the marlin.
5. **Cooldowns and pity are per legend,** both saved in its `LegendRecord` (the cooldown as a wall-clock end, so it
   survives a relaunch: Docs/lures_legend_spec.md 2.11). After a marlin fight the great white can still come, and vice versa.
6. **Tips:**
   - A non-key lure near the lurk for 30 s gets the marlin's `tipWrongLure` first, then the great white's on the next
     visit.
   - A key lure near the lurk for 30 s that meets neither pattern gets, once per visit:
     "청새치는 빠르게 달리는 먹이를, 백상아리는 천천히 비틀거리는 먹이를 쫓는다…".
7. **The name card** stays "??? · 전설의 물고기" until reveal, as in the spec.

---

## 6. Test switches and the capture set

**`-fkencounter now|natural`** with `-fkstage <stage>` owns and equips the stage legend's top key and its `minLine`
line:

| Stage | Top key | Line |
|---|---|---|
| lake | bait_golden | line_fluoro6 |
| swamp | bait_frog | line_pe3 |
| ice | bait_softworm | line_pe3 |
| ocean (marlin) | bait_kona | line_pe8 |
| ocean (great white) | bait_kona | line_titan |

- **`-fklegend <fish_id>`** picks the ocean legend (default blue_marlin).
- **natural mode:**
  - The lurk goes at (Angler.X, −, 16); on the ice at (holeX, −, holeZ + 4); on the ocean at z 24.
  - The AutoPilot plays the key's trigger pattern:
    - golden bait: float depth to the bottom, then still;
    - frog: run and pause;
    - softworm: hop and rest on the bottom;
    - kona: 2.1 rev/s for the marlin, 0.8 rev/s for the great white.

**`-fkauto encounter` plays each mood generically by its verb:**

| Verb | Play |
|---|---|
| Wind | the band midpoint |
| RunPause | the band midpoint for the mid run length, then a pause of `pauseMin` + 0.3 s |
| FlickPause | a downward 톡 at strength min(0.4, 0.7·maxStrength), then a pause at the window midpoint |
| Hold | no input |

- NoseIn: it waits through the fake-out and taps 0.1 s after `Close`.
- `early` taps on the fake-out.

**One capture set per legend**, `enc_<fish_id>_<n>_<beat>.png`: `1_eyes`, `2_approach`, `3_pass`, `4_excited`,
`5_bite_full`, `6_wipe`, `7_fight`. The checks are as in the spec, with `fight species=<fish_id>`.

---

## 7. Species bones and what they animate

These are generic bones (spec 3.3) plus the species bones below. Names are case-exact; a bone the model lacks is
skipped by `Legend3D`.

| Bone | Legend | Parent | Channel | Motion |
|---|---|---|---|---|
| `Lips` | golden_carp, sturgeon | Head | `protrude` 0..1 | localPosition = rest + p·`move`; localRotation X += p·`tilt` (`_rig.protrude`). Carp: forward-down tube (0, −0.010, 0.030), 12°. Sturgeon: drops under the snout (0, −0.030, 0.008), 20°. `Jaw` (the lower lip) and the `Mouth` empty ride it, so the lure goes into the tube. |
| `Barbel.L1/R1`, `Barbel.L2/R2` | golden_carp (2 pairs: 1 = short upper lip, 2 = long corner; on Lips), sturgeon (4 in a row: 1 = inner, 2 = outer; on Head) | Lips / Head | `barbel` 0..1 + swim | They trail back at rest and sway Y ±8° (the outer pair ±12°) with the swim wave, lagging 0.6 rad. "Feel": X +25°·b, curling down onto the floor or the lure. Fake-out twitch: X +35° for 0.15 s. |
| `Bill` | blue_marlin | Head | `bill` | A stiff spear: ±4° yaw flex lagging the head. The slash is Head yaw ±25° in 0.12 s with Bill ±6°. |
| `Dorsal1` = the **sail** | blue_marlin | Spine.F | `sail` 0..75 | Modelled raised and folded −75° about X. Raise +X: 0 cruising, 35 curious, 75 excited and at the tell. |
| `UpperJaw` | great_white | Head | `protrude` | (0, −0.012, 0.025), 8°, on the lunge together with `headPitch` −12° (the snout lifts) and `Jaw` 0..50. |
| `EyeRoll.L/R` | great_white | Head | `eyeRoll` 0..1 | localRotation X += 150°·r. The `Eye.*` empties and `geo_Eye.*` ride these bones, so the eyeshine fades by its facing test and the white back of the eyeball comes round. r = 1 in the tell and the lunge. |

**Clamps per legend** (`_rig.limits`):

| | Jaw | Dorsal1 |
|---|---|---|
| coelacanth | 40 | 60 (today's constants) |
| golden_carp | 30 | 40 |
| arapaima | 45 | – |
| sturgeon | 25 | – |
| blue_marlin | 30 | 75 |
| great_white | 50 | 0 |

**Budget and rules:** ≤ 3500 triangles per model. Everything else (1.0 m length with the foremost tip at z +0.50,
identity rest, `geo_<Bone>`, `<prefix>_*` materials, the `_eye_glow` lens, `_face`) is in
`Tools/Blender/variants/hybrid/legends/README.md`.

---

## 8. Asset list and suggested split for 7 parallel agents

Each agent owns only the files in its row.

| Agent | Owns | Produces |
|---|---|---|
| 1 golden carp | `legends/golden_carp.py`, `encounter_sets/lake.py` | `legend_golden_carp.fbx` / `_palette.json`; `uw_lake_bg/mid/floor/fore`, `enc_frame_lake`; `lure_golden_0/1`, `lure_corn_0/1` |
| 2 arapaima | `legends/arapaima.py`, `encounter_sets/swamp.py` | `legend_arapaima.*`; `uw_swamp_bg/ceiling/mid/floor/fore`, `enc_frame_swamp`; `lure_frog_0/1`, `lure_popper_0/1` |
| 3 sturgeon | `legends/sturgeon.py`, `encounter_sets/ice.py` | `legend_sturgeon.*`; `uw_ice_bg/ceiling/ray/mid/floor/fore`, `enc_frame_ice` |
| 4 blue marlin | `legends/blue_marlin.py`, `encounter_sets/ocean.py` | `legend_blue_marlin.*`; `uw_ocean_bg/ceiling/mid/fore`, `enc_frame_ocean`; `lure_kona_0/1` |
| 5 great white | `legends/great_white.py` | `legend_great_white.*` (its preview uses the ocean set once agent 4 has built it) |
| 6 code framework | `Models.cs` (EncounterDef, MoodDef, EncounterSetDef), `LegendWatch.cs`, `LegendEncounter.cs`, `EncounterView.cs`, `Legend3D.cs`, `EncounterHUD.cs`, `ActorToon.shader`, `PixelArtImporter.cs` | sections 1, 4.1, 5 and 7 |
| 7 data and tests | `GameDatabase.cs` (5 rows, 4 set rows, preferences, golden bait text), `Game.cs`, `AutoPilot.Encounter.cs` | sections 3, 6 and 1.6 |

**Build commands** (Blender 5.2, run from `Tools/Blender`):
- `blender -b --python variants/hybrid/hyb_legend3d.py -- <fish_id> --install`
- `blender -b --python variants/hybrid/hyb_encounter.py -- <set> --install`
- `blender -b --python variants/hybrid/hyb_legend_preview.py -- <fish_id>`

The shared runners, kits and the coelacanth / cave modules are not edited.
