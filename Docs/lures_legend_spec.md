# 낚시왕 — Lure subdivision + legend encounter (spec v1)

Status: implementable spec. It merges the FEEL and SYSTEMS proposals and was checked against the current code (Unity 6000.3.22f1, Built-in RP, gamma).
Prototype: the cave **실러캔스 (coelacanth)** encounter only. The framework is generic, so each of the other 5 legends later needs one data row, one model and one backdrop set.

User decisions this spec implements:

1. **Staged presentation.** An overlay window opens over the dimmed surface scene while the fish approaches. It expands to full screen at the bite, then the game returns to the normal surface fight.
2. **Direct control.** The player works the lure with the inputs the game already has: reel circles, pause, and a 톡 (a short quick pull DOWN the screen, which jerks the rod up). The goal is to keep an interest gauge up; overdoing it, or doing the wrong thing, makes the legend turn away.
3. **Lures split by type and action.** There are 10 lure types across 5 actions. Each type rewards its own retrieve input, and fish prefer both lure types and actions. No colour variants.

Constraints that are not negotiable:
- The left hand holds the rod and the right hand cranks.
- The reel UI stays fixed at the bottom right.
- No whole-body idle bob. The only new body motion is a short rod jerk on a 톡 (the rod lifts up and back; the 3D rod fist rises a little with it).
- No unrequested polish or QA rounds. One verification capture set is allowed.

---

## 0. Conflicts resolved

| Topic | FEEL | SYSTEMS | **This spec** (why) |
|---|---|---|---|
| 야광 웜 `bait_glow` | Becomes a reusable soft lure | Stays a consumable float bait | **Stays a float bait**; it gains `glow=true`. 8 species (smelt, cave_tetra, crystal_koi, catfish, burbot, arctic_char, anglerfish, coelacanth) and the ice/cave float loop depend on it. The glowing-lure role goes to the new `bait_egi`. |
| The 10th lure | 빅 스윔베이트 | 트롤링 루어 (kona) | **kona**. The ocean stage is the trolling stage, and marlin / great white will need a fast steady lure later. |
| Coelacanth key lure | 야광 웜 + jig | egi 1.0 / softworm 0.6 / jig 0.4 | **egi 1.0, softworm 0.6, jig 0.4**, because a glowing squid lure fits both the fish's diet and the dark cave. |
| Action names in the UI | 감기 · 저킹 · 수면 · 바닥 · 수직 | 정속 · 트위칭 · … | **감기 · 저킹 · 수면 · 바닥 · 수직** (FEEL wording, plainer for players). |
| Input verbs | wind / flick / pause, flick strength ≥ 0.6 = "lift" | circle / twitch / held LIFT / tap | **wind / flick / pause** (+ tap for the hook set). No held-lift verb, because the user asked for the existing inputs only. A strong flick lifts higher. |
| Lure rhythm model | Per-action rules | Per-lure windows | **Per-lure data**: work (N flicks, or a wind run), then rest. This is one generic tracker. |
| Legend presence | A FishAgent must exist; its eyes blink where it is | The legend never spawns; a meter with escalating tells | **Both, combined.** A *lurk point* with no agent, cued by eye glints and bubbles (FEEL's "where to cast"), plus a meter with tells (SYSTEMS' build-up). The legend agent is spawned only at the hook set. |
| Minigame | 3 moods, each wanting a different verb (wind → flick-pause → hold) | 1 taste (fall timing) + moods from state | **FEEL's 3-mood ladder**, because it uses all three verbs, escalates visibly and is prompted. The data per mood is generic (SYSTEMS style). |
| Window size / place | 288×144, y 34–178 | 208×120, 36 px from top, x follows the lure | **288×136, 10 px below the top, centred.** In the cave the angler's hat top is ~100 px above the bottom of a 270 px view, so a window from 10 to 146 px from the top clears the head and the fixed reel. The top HUD is hidden during the encounter. |
| Window growth / return | Circular iris / waterline wipe | pixelRect grow / shrink back into the surface point | **Crop rectangle** of a full-view RT, drawn through quad UVs (always 1:1 pixels). It grows as a rectangle from the lure point, with no shader mask. The return to the fight is FEEL's **bottom-up waterline wipe**. |
| Darkness lighting | `_LURELIGHT` keyword, per pixel | Per-frame `_KeyDir` + uniform fog, no shader change | **Per-pixel lure light** added to ActorToon behind a uniform (`_LurePos.w > 0`), with no keyword. The angler renders exactly as before. |
| Eye colour | Cyan | Yellow-green tapetum | **Yellow-green** (`#f0ffd8` / `#b8ff8a`), so the eyes read against the cyan lure glow and crystals. The frame line stays cyan. |
| States | `Encounter` + `Strike` | One `Encounter` + pure phase model | **One `S.Encounter`**, with phases in a pure `LegendEncounter` model (like FightModel). |
| Hook window | 0.8 s + hookBonus, perfect ≤ 0.25 s | 0.45 s + hookBonus, perfect ≤ 0.2 s | **0.6 s + hookBonus**, perfect ≤ 0.25 s, taps buffered 0.15 s before the close. |
| Early tap | Gauge set to 70 | −40 and back to wary | **Gauge set to 65** (curious band). It is a second chance, not a fail. |
| Cooldown | 45 s, saved | 90 s / 180 s, runtime | **60 s after a fail, 180 s after any fight.** Saved per legend as a wall-clock end, so it runs on while the app is closed (see 2.11). |
| Seen / first-encounter data | `SpeciesRecord.seen` | `SpeciesRecord.seen` | **A new `LegendRecord` list.** Adding seen-only `SpeciesRecord`s would break `RegisterCatch` (new species = `rec == null`) and the collection counter (`records.Count`). |
| Ice | Grey out steady / topwater lures | Grey out or leave useless | **Grey out every lure except 바닥 and 수직**, with the note "얼음 구멍에서는 쓸 수 없어요". |
| 황금 떡밥 | A spawn boost only | No role for converted legends | For **converted** legends it has no effect (the key lure is what matters). The 5 unconverted legends keep the current golden-bait path unchanged. |

---

## 1. Lures

### 1.1 The three verbs (`LureInput`)

All lure play, and the legend minigame, is built from three verbs the player already knows. A fourth input, tap, only sets the hook.

| Verb | Input | How it is detected |
|---|---|---|
| **감기** (wind) | Reel circles anywhere | `CircleGesture.Speed` in rev/s. "Winding" means `Speed ≥ 0.1`. `Circling` is not required, so the keyboard Space / R keys (2.6 rev/s, with no circling) also count. |
| **톡** (flick) | A short quick DOWNWARD swipe (pulling down feels like jerking the rod up) | See the rules below. An upward swipe is no 톡 (that is the cast's gesture). |
| **멈춤** (pause) | No circling and no flick | `PauseT` is the time since the last flick or wind. A finger resting still on the screen counts as a pause. |
| tap | A press with almost no movement | Duration ≤ 0.35 s and travel < 0.02 H. It does nothing to a lure; in Biting and the hook window it sets the hook, as today. |

**Flick rules.** A flick is classified at the release of a world press (a press where `!PointerInput.StartedOverUI`), from `PointerInput.Samples`. All of these must hold:
- the press lasted ≤ 0.35 s;
- the net DOWNWARD travel is ≥ 0.05 H;
- the direction is within 60° of straight down;
- `Gesture.Circling` was never true during the press;
- the peak downward speed (FlickCast's 0.05 s window: `FlickCast.Analyze(…, down: true)`, the same analysis with the screen's y mirrored) is ≥ `FlickCast.UnitsNow(H).min`.

A quick upward swipe (≥ 0.05 H up, within 60° of straight up) is rejected with `LastReject = "up"` (logged as `[Lure] no flick (up)`); in Waiting with a lure the HUD reminds "톡은 아래로 당겨요!" (at most every 6 s).

Its **strength** is `InverseLerp(units.min, 0.5·units.full, peak)`, from 0 to 1. For touch that maps 5–30 cm/s onto 0–1.

**Rod jerk.** Every 톡 jerks the rod up and back towards the angler: `Angler.RodJerk01` rises to `0.35 + 0.5·strength` over 0.06 s (ease-out), holds 0.05 s and eases back to 0 over 0.28 s. It slerps the rod from its hold towards just behind upright (tip ~7° back towards him), takes up to 85 % of the line's sag out, and lifts the 3D figure's rod fist 0.07 m / draws it back 0.05 m (scaled by the jerk; the pose sprites keep their fist, only the painted rod lifts). No body motion.

**Keyboard** (desktop and AutoPilot): T is a flick at strength 0.45, Shift+T is a flick at strength 0.9.

`LureInput` is updated right after `Gesture.Update` in the states `Waiting` and `Encounter`. `S.Encounter` is added to `gestureOn`.

### 1.2 The ten lures

All ids use the `bait_` prefix. The four existing ids are kept, so saves stay valid.

- **Work** is what the player does; **rest** is the pause after it.
- **Strike window** is when a following fish may strike.

| id | 이름 | 가격 | 액션 | In the water | Flick | Good input (work → rest) | Strike window | Look | Stages · main fish |
|---|---|---|---|---|---|---|---|---|---|
| `bait_spinner` (new) | 스피너 | 1200 | 감기 | Sinks 0.9 m/s. Winding lifts 0.35 m per m. | Small penalty (Q −0.2) | Wind steadily at 0.5–1.1 rev/s | While in band | Blade glint every 0.2 s | 호수·계곡 · 산천어, 무지개송어, 블루길, 열목어, 피라미 |
| `bait_spoon` | 스푼 | 1500 | 감기 | Sinks 1.8 m/s and flutters down at 1.2 after a stop. Lift 0.30 per m. | Small penalty | Wind steadily at 0.8–1.6 rev/s | In band, plus the first 1.0 s of a pause (flutter) | Wobble flash | 계곡·바다 · 무지개송어, 열목어, 전갱이, 고등어 |
| `bait_crank` (new) | 크랭크베이트 | 2200 | 감기 | Floats and rises 0.3 m/s at rest. Winding dives it 0.6 m per m, down to 2.5 m. | Small penalty | Wind fast at 1.2–2.2 rev/s | In band | Tight fast wobble | 호수·늪 · 큰입배스, 가물치 |
| `bait_kona` (new) | 트롤링 루어 | 12000 | 감기 | Sinks 0.3 m/s. Lift 0.45 per m, so it runs just under the surface. | Small penalty | Wind very fast at 1.6–2.6 rev/s | In band | Skirt with a bubble trail | 먼바다 · 만새기, 참다랑어, 방어 |
| `bait_minnow` | 미노우 | 2500 | 저킹 | Suspends: sinks at 0.9 to 1.2 m and hangs there (today it sinks to the bottom). Lift 0.10 per m. | Dives 0.15 m (max 1.5), darts 0.3 m sideways (alternating) and moves 0.25 m to shore | 1–3 flicks, 0.2–0.6 s apart → rest 0.5–1.5 s | From 0.3 s into the rest until it ends | Dart plus a silver flash | 호수·계곡·늪·먼바다 · 배스, 쏘가리, 가물치, 만새기 |
| `bait_popper` (new) | 포퍼 | 3000 | 수면 | Floats at 0.05 m. | "퐁": splash and 0.2 m to shore | 1 flick → rest 1.0–2.5 s | 0.3 s into the rest until it ends | Splash ring and bubbles | 호수·늪·먼바다 · 배스, 아로와나, 가물치, 만새기 |
| `bait_frog` | 개구리 루어 | 4000 | 수면 | Floats (today it sinks at 0.5). Winding moves it along the surface. | Hops 0.2 m | Wind 0.3–0.8 rev/s for ≤ 2 s → rest 0.5–2.0 s | 0.3 s into the rest until it ends | Kicking legs, V-wake | 늪·호수 · 가물치, 아로와나, 배스 |
| `bait_softworm` (new) | 소프트 웜 | 1800 | 바닥 | Sinks 1.2 and falls 1.0 after a hop. Lift 0.15 per m. | Hops 0.4 m. A flick stronger than 0.6 scores as "너무 세요". | 1 light flick, or a slow drag at 0.1–0.5 rev/s for ≤ 2 s → rest 1.0–3.0 s **on the bottom** | The fall, plus the first 1.5 s on the bottom | Curly tail; silt puff on every touch-down | 호수·바다·늪·얼음 · 배스, 우럭, 광어, 메기, 강꼬치 |
| `bait_jig` | 메탈 지그 | 8000 | 수직 | Sinks 2.6 on the first drop, then flutters down at 1.2. Lift 0.45 per m. | Lifts 0.6 + 0.9·strength m and moves 0.15 m to shore | A burst of 2–4 flicks, 0.25–0.6 s apart → rest (the fall) 0.8–2.0 s | During the burst and the first 0.6 s of the fall | Streak on the rise, flutter glint on the fall | 바다·먼바다·얼음 · 고등어, 방어, 참다랑어, 강꼬치, 북극곤들매기 |
| `bait_egi` (new) | 야광 에기 | 6000 | 수직 | A slow nose-down glide: sinks and falls at 0.6 m/s. `glow`. Lift 0.30 per m. | Lifts 0.5 + 0.7·strength m and moves 0.2 m to shore | 1–2 flicks, 0.2–0.8 s apart → rest (the fall) 1.5–4.0 s | From 0.5 s into the fall until 1 s after landing | Cyan glow (a 12 px halo, visible from the surface) and fluttering cloth | 바다·동굴·얼음 · 참돔, 초롱아귀, 북극곤들매기, **실러캔스** (encounter) |

The existing lures change as follows:
- 스푼 → 감기
- 미노우 → 저킹, and its sink changes to *suspend*
- 개구리 루어 → 수면, and it now floats
- 메탈 지그 → 수직

### 1.3 How lures move (`Tackle`)

These changes replace the current lure rules: the flat `Wind` lift of 0.45 per metre and sinking to the bottom.

**Buoyancy at rest**, handled in `Update` while `State == Water`:
- **Sink**: moves towards `bottom = DepthAt(z) − 0.25` at `sinkSpeed`. After a flick it uses `fallSpeed`.
- **Suspend**: sinks only to `swimDepth`.
- **Float**: rises to 0.05 m at `riseSpeed`.

**`Wind(m, shore)`** moves the rig towards the shore (unchanged). Depth becomes:

```
Depth = Clamp(Depth − m·windLift, floats ? 0.05 : 0.15, maxDepth > 0 ? min(maxDepth, bottom) : bottom)
```

A negative `windLift` dives the lure (the crank).

**New `Twitch(float strength, Vector3 shore)`**:
- `Depth −= hopM · (hopBase + (1 − hopBase)·strength)`, clamped. A negative `hopM` dives the lure (the minnow).
- The rig moves `pullM` towards the shore.
- `dartM` shifts x, alternating sides.
- Effects:
  - 수면 lures: `Fx.Splash` (small), `Fx.Ripple` and `Sfx.Plop`.
  - 저킹 and 수직 lures: a 2-px sparkle.
- It starts the fall timer. `Falling` is true while the lure is moving down after a flick and has not reached its rest depth.

**New signals** for the tracker:
- `OnBottom`: `Depth ≥ bottom − 0.3`.
- `BottomTouch`: a one-frame event. It plays a silt puff (`Fx.Burst`, water-tinted, 4 px) and a soft "톡" (`Sfx.Nibble`, pitch 1.4).
- `Falling` and `FallT`.

**New `Resume()`** sets `State = Water` after an encounter that fails. **`Hold()`** already exists.

**Ice:** `Raise(m)` is unchanged, and a flick on the ice calls `Twitch`. That gives real ice jigging for 바닥 and 수직 lures.

### 1.4 Action quality Q and strike windows (`LureAction` tracker)

`LureAction.Update(bait, input, tackle, dt)` keeps three outputs:
- **Q**, from 0 to 1;
- **`StrikeOpen`**, a bool;
- **`Feedback`**, a string that is null when unchanged.

**Steady lures** (`work = Wind`, rest (0,0)):
- The instant score is 1 inside `reelBand`, falling to 0 over ±0.3 rev/s outside it.
- A pause scores 0 (the lure is sinking or rising).
- A flick costs Q −0.2 once.
- Q is an EMA with τ = 2 s.

**Cycle lures** (every other lure): a cycle is **work** (a flick burst, or a wind run no longer than `runMax`) followed by **rest**. It is scored when the next work starts, or when the rest has lasted `rest.y + 1 s`.

The cycle score:
- **1** if all of these hold:
  - the flick count is inside `flicks`;
  - every gap is inside `gap`;
  - the rest is inside `rest`;
  - every flick's strength is ≤ `maxStrength`;
  - for `needBottom` lures, the rest was spent `OnBottom`.
- **0.5** if exactly one of those is off, by no more than 50 % of its window's width.
- **0.15** otherwise.

Then `Q += (score − Q)·0.5`, so two good cycles bring Q to about 0.75.

Two rules make Q decay:
- No cycle for `rest.y + 2 s`: Q drifts towards 0.1 at 0.3/s.
- Winding faster than 0.8 rev/s on a `work = Flick` lure costs Q −0.3/s. Slow circles to take up slack are fine.

**Strike windows** follow the table in 1.2. For steady lures a strike is rolled every 0.5 s while the window is open; for cycle lures it is rolled once each time a window opens.

**Feedback words** appear at most one per 2 s and only when the word changes:
- 좋아요! (the score reaches ≥ 0.75)
- 완벽! (three perfect cycles, or 4 s in band)
- 너무 빨라요 · 너무 느려요 (steady lures)
- 멈춰요 (winding a flick lure)
- 톡 당겨요 (idle)
- 너무 세요 (too strong)
- 바닥까지 기다려요 (resting off the bottom)

### 1.5 How Q feeds bites (`FishingController.WantsToApproach`, `FishAgent.UpdateApproach`)

For lures, `Tackle.MoveSpeed` is no longer used.

**Sense range**, horizontal: `5 + 4·Q` m. A `glow` bait or lure adds +2 m in the `cave` and `ice` stages. Float baits keep 5 m (+2 if glow).

**Activity:** `0.15 + 1.25·Q`. The rest of the formula is unchanged: `p = Appeal · line.stealth · stage.biteMult · activity · 0.45`. The depth-gap test (≤ 2.5 m) is unchanged.

**While following:**
- If Q < 0.25 for 2 s, the fish calls `LoseInterest()`. Its shadow turns away, and the HUD flashes "따라오다 돌아섰어요", at most once every 6 s.
- At ≤ 0.3 m, when a strike roll comes due (see 1.4), the chance is `(0.35 + 0.5·aggression) · (0.5 + 0.7·Q)`. A success calls `StartBite()`; otherwise the fish keeps following, with the existing 14 s limit.

**Only `needBottom` lures (the softworm) keep the nibble step.** For everything else the "too early" scare now fires only for float baits, or for a softworm whose release was a **tap**. It no longer fires on the press.

**Encounter-only species** (`sp.encounter != null`) always return false.

### 1.6 Data format

In `Models.cs`:

```csharp
public enum LureAction { None, Steady, Twitch, Topwater, Bottom, Vertical }
public enum Buoyancy { Sink, Suspend, Float }
public enum Work { Wind, Flick }

// BaitDef additions (natural baits keep the defaults; action None)
public LureAction action;
public Buoyancy buoyancy;            // Sink
public float fallSpeed;              // m/s after a flick (0 = sinkSpeed)
public float riseSpeed = 0.35f;      // Float: back up to 0.05 m at rest
public float swimDepth;              // Suspend: hang depth
public float maxDepth;               // deepest running depth (0 = bottom)
public float windLift = 0.45f;       // depth lost per metre wound (negative dives)
public float hopM, hopBase = 1f;     // flick lift = hopM * (hopBase + (1-hopBase)*strength); negative dives
public float pullM = 0.25f, dartM;   // flick drag to shore; sideways dart
public Work work;
public Vector2 reelBand;             // rev/s
public Vector2Int flicks = new Vector2Int(1, 1);
public Vector2 gap, rest;            // s
public float runMax;                 // Wind work with a rest (frog 2 s)
public float maxStrength = 1f;
public bool needBottom, glow;
public string hint;
```

Lure rows, with only the non-default fields listed:

| id | fields |
|---|---|
| spinner | Steady, sink 0.9, windLift 0.35, band (0.5, 1.1) |
| spoon | Steady, sink 1.8, fall 1.2, windLift 0.30, band (0.8, 1.6) |
| crank | Steady, Float, rise 0.3, windLift −0.6, maxDepth 2.5, band (1.2, 2.2) |
| kona | Steady, sink 0.3, windLift 0.45, band (1.6, 2.6) |
| minnow | Twitch, Suspend, sink 0.9, swimDepth 1.2, maxDepth 1.5, windLift 0.10, hopM −0.15, dartM 0.3, Flick, flicks (1,3), gap (0.2,0.6), rest (0.5,1.5) |
| popper | Topwater, Float, rise 0.4, windLift 0, pullM 0.2, Flick, flicks (1,1), rest (1.0,2.5) |
| frog | Topwater, Float, rise 0.4, windLift 0, hopM 0, pullM 0.2, Wind, band (0.3,0.8), runMax 2, rest (0.5,2.0) |
| softworm | Bottom, sink 1.2, fall 1.0, windLift 0.15, hopM 0.4, pullM 0.3, Flick, flicks (1,1), rest (1.0,3.0), maxStrength 0.6, needBottom |
| jig | Vertical, sink 2.6, fall 1.2, windLift 0.45, hopM 1.5, hopBase 0.4, pullM 0.15, Flick, flicks (2,4), gap (0.25,0.6), rest (0.8,2.0) |
| egi | Vertical, sink 0.6, fall 0.6, windLift 0.30, hopM 1.2, hopBase 0.42, pullM 0.2, Flick, flicks (1,2), gap (0.2,0.8), rest (1.5,4.0), glow |
| `bait_glow` | Unchanged float bait, plus `glow = true` |

The softworm also accepts a slow drag as work: a Wind run at 0.1–0.5 rev/s, `runMax` 2.

**Fish preferences.** The string format is kept, with `@action` tokens added: `"minnow:1,crank:1,@twitch:0.8"`. `GameDatabase.F()` sends tokens starting with `@` into `FishSpecies.actionPrefs` (a `Dictionary<LureAction, float>`); every other token keeps the `bait_` prefix. New method:

```csharp
public float Appeal(BaitDef b) => b.action == LureAction.None ? Pref(b.id)
    : Mathf.Max(Pref(b.id), 0.7f * (actionPrefs.TryGetValue(b.action, out var a) ? a : 0f));
```

Everything that asks "does this fish like it" switches from `Pref` to `Appeal`: `WantsToApproach`, the "이곳 물고기는 이 미끼를 안 좋아하는 것 같아요" check in `OnLanded`, and the shop's fan list. The `OnLanded` check also counts a stage legend's key lures as liked.

### 1.7 Preference strings that change

Species not listed here are unchanged.

```
bluegill        worm:1,paste:0.5,shrimp:0.6,spinner:0.6,spoon:0.3,golden:0.3
largemouth_bass minnow:1,crank:1,softworm:1,frog:0.9,popper:0.9,spinner:0.6,spoon:0.6,worm:0.4,golden:0.4,@twitch:0.8,@topwater:0.8,@bottom:0.8,@steady:0.7
pale_chub       worm:1,paste:0.6,spinner:0.4,golden:0.3
cherry_salmon   worm:1,spinner:1,spoon:0.7,minnow:0.5,shrimp:0.4,golden:0.3
rainbow_trout   spoon:1,spinner:0.9,minnow:0.8,worm:0.6,corn:0.3,golden:0.4,@steady:0.7
mandarin_fish   minnow:1,softworm:0.8,shrimp:0.6,spoon:0.5,golden:0.5
lenok           spoon:1,spinner:0.8,minnow:0.8,worm:0.3,golden:0.7
horse_mackerel  shrimp:1,sandworm:0.7,jig:0.6,spoon:0.4,golden:0.3
mackerel        jig:1,shrimp:0.9,spoon:0.6,squid:0.5,sandworm:0.5,golden:0.3,@vertical:0.7
rockfish        sandworm:1,softworm:0.9,squid:0.8,shrimp:0.7,jig:0.5,golden:0.4,@bottom:0.7
flounder        squid:1,minnow:0.8,softworm:0.8,sandworm:0.6,golden:0.4
red_seabream    squid:1,jig:0.8,egi:0.7,shrimp:0.7,golden:0.7
piranha         worm:0.8,squid:0.6,shrimp:0.7,minnow:0.6,spinner:0.5,golden:0.3
catfish         worm:1,sandworm:0.5,squid:0.6,softworm:0.5,glow:0.4,golden:0.3
snakehead       frog:1,popper:0.8,minnow:0.8,crank:0.6,worm:0.3,golden:0.4,@topwater:0.8
arowana         frog:1,popper:1,minnow:0.7,shrimp:0.4,golden:0.5
burbot          worm:0.8,sandworm:0.6,glow:1,squid:0.5,softworm:0.5,golden:0.4
northern_pike   jig:1,softworm:0.7,egi:0.5,golden:0.4,@vertical:0.7        (ice: 바닥/수직 only)
arctic_char     jig:1,egi:0.7,glow:0.6,golden:0.5
yellowtail      jig:1,squid:0.8,kona:0.7,minnow:0.6,golden:0.3
mahi_mahi       popper:1,minnow:1,kona:0.9,squid:0.8,jig:0.6,golden:0.4
bluefin_tuna    jig:1,kona:0.9,squid:0.9,golden:0.6
blue_marlin     kona:0.8,jig:0.6,squid:0.6,golden:1                          (not converted: keeps the golden path)
anglerfish      glow:0.8,egi:0.8,squid:0.6,jig:0.5,golden:0.7
coelacanth      egi:1,softworm:0.6,jig:0.4                                   (encounter-only; used as key-lure weights and for the shop fan list)
```

### 1.8 UI

- **Waiting hint** (top-centre plate): for a lure, the plate shows the lure's `hint` (1.9). Float baits keep today's text.
- **Tackle panel** (bottom-left): for lures, the depth slot that is hidden today becomes `[action chip] ●●●●○  수심 3.2m`.
  - The 5 pips show Q. They turn gold at Q ≥ 0.75 and blink while `StrikeOpen`.
  - A small feedback label (Galmuri 16) sits just above the panel and fades after 1.2 s. The central flash is **not** used, to avoid spam.
- **Bait picker** (`FishingHUD.OpenBaitPicker`): two sections, **미끼** and **루어**.
  - A lure row shows its action chip and its hint (size 15).
  - A small fish mark appears when a species already **caught** in this stage has Appeal ≥ 0.6.
  - An eye icon appears on this stage legend's key lures once that legend has been seen.
  - On the ice, every lure other than 바닥 / 수직 is greyed out: "얼음 구멍에서는 쓸 수 없어요". A banned lure still equipped when entering the ice is swapped for 떡밥, with a toast.
- **Shop** (`ShopUI`, 미끼 tab): two sub-tabs, **미끼 | 루어**. Lures are grouped under the headers 감기 / 저킹 / 수면 / 바닥 / 수직. Each row shows:
  - the action chip;
  - "사용법: <hint>";
  - a depth chip (수면 / 중층 / 바닥);
  - chips for **unlocked** stages where some species has Appeal ≥ 0.6.
- **Action chip colours:** 감기 sky `#6ab8ff`, 저킹 green `#6ad06a`, 수면 yellow `#ffd24a`, 바닥 brown `#b8865a`, 수직 purple `#b58aff`.

### 1.9 One-line hints (`BaitDef.hint`)

- 스피너: 천천히 일정하게 감기 — 날개가 반짝여요
- 스푼: 보통 속도로 꾸준히, 멈추면 팔랑팔랑 가라앉아요
- 크랭크베이트: 빨리 감을수록 깊이 잠수, 멈추면 떠올라요
- 트롤링 루어: 빠르게 쉬지 않고 감기
- 미노우: 아래로 톡톡 당기고(1~3번) 잠깐 멈추기
- 포퍼: 톡 당겨 '퐁'! — 물결이 잦아들 때까지 기다리기
- 개구리 루어: 아주 천천히 감다가 멈추기를 반복
- 소프트 웜: 바닥까지 가라앉힌 뒤 살짝 톡 당기고, 바닥에서 기다리기
- 메탈 지그: 바닥까지 내린 뒤 빠르게 연속으로 톡톡 당겨 올리기
- 야광 에기: 톡 당겨 올린 뒤 손을 떼고 가라앉히기 — 입질은 떨어질 때!

---

## 2. Legend encounter (coelacanth)

### 2.1 Data

`FishSpecies.encounter` is a new `EncounterDef`. When it is **null** the species keeps the old spawn-and-bite path, which is how the 5 unconverted legends keep working.

```csharp
public enum Verb { Wind, FlickPause, Hold }
public class MoodDef {
    public string name, prompt; public Verb verb;
    public float lo, hi;          // Wind: rev/s band; FlickPause: pause window (s); Hold: grace before gains (s)
    public float gain;            // /s (Wind, Hold) or per credited cycle (FlickPause)
    public float maxStrength = 1f, strongAt = 2f, strongLoss;   // FlickPause
    public float hurryLoss, circleAfter = 99f, circleLoss, boredAfter = 99f, boredLoss;
    public float tooFast = 99f, fastLoss, flickLoss;            // Wind / Hold
    public float circleGrace;                                   // Hold
}
public class EncounterDef {
    public string model, backdrop, nameSub, tipWrongLure;
    public Dictionary<string, float> keyLures;
    public float depthMin, bottomBand, lurkZMin, lurkZMax, nearLurk;
    public float minQ, minSoak, fillTime, chance, minLine;
    public float coolFail, coolFight; public Vector2 appear, relocate;
    public int gaugeStart, pityStep, pityMax; public float decay, teaseLimit; public int timeoutStrike;
    public MoodDef[] moods;       // [0] 경계, [1] 호기심, [2] 흥분
    public int curiousAt = 40, excitedAt = 75, hysteresis = 5, turnAwayBelow = 15, earlyGauge = 65;
    public Vector2 noseIn; public float tell, hookWindow, perfectT, buffer;
    public float lightGlow, lightPlain, orbitFar, orbitNear, noseDist;
    public string eyeCore, eyeGlow, frameLine;
}
```

The coelacanth row, set in `GameDatabase.BuildFish`:

| Field | Value |
|---|---|
| model / backdrop | `legend_coelacanth` / `cave` |
| keyLures | egi 1.0, softworm 0.6, jig 0.4 |
| depthMin / bottomBand | 5.0 m / 1.5 m (the lure must be within 1.5 m of the bottom) |
| lurkZMin / lurkZMax / nearLurk | 14 / 30 / 8 m |
| minQ / minSoak / fillTime / chance | 0.55 / 8 s / 16 s / 0.6 |
| minLine | 20 kgf (PE 3호 22 kg is the first line that qualifies) |
| coolFail / coolFight | 60 s / 180 s |
| appear / relocate | 20–40 s / 90–150 s |
| gaugeStart / pityStep / pityMax | 20 / 10 / 30 |
| decay / teaseLimit / timeoutStrike | 3 per s / 28 s / 60 |
| 경계 | Wind, 0.15–0.7 rev/s, +9/s; above 1.2 rev/s −12/s; any flick −15 |
| 호기심 | FlickPause: a pause of 0.6–2.0 s after a flick of strength ≤ 0.6 gives +14. Penalties: strength > 0.8 −10; re-flick < 0.6 s −6; circling > 1.5 s −6/s; no flick > 3 s −5/s. |
| 흥분 | Hold: +12/s after 0.4 s still; flick −20; circling −15/s after 0.25 s grace |
| noseIn / tell / hookWindow / perfectT / buffer | 0.6–1.4 s / 0.35 s / 0.6 s (+ rod.hookBonus) / 0.25 s / 0.15 s |
| light radius | 1.6 m for a glow lure, 1.0 m otherwise |
| orbitFar / orbitNear / noseDist | 2.4 / 1.3 / 0.6 m |
| eyes / frame | core `#f0ffd8`, glow `#b8ff8a` / `#6affea` |
| nameSub | 4억 년을 살아남은 고대어 |
| tipWrongLure | 어둠 속에서 빛나며 가라앉는 먹이에 끌리는 것 같다… |

`FishSpawner.Pick` gives weight 0 to any species with an encounter, so the legend never spawns as a normal agent. Its old spawn weight (cave 2.0) is ignored.

### 2.2 Presence and trigger (`LegendWatch`, ticked by the controller)

**The lurk point.** While the stage legend is off cooldown, a lurk point exists:
- `x` inside the visible half-width;
- `z` from 14 to `min(FishZMax, 30)`;
- depth = `DepthAt(z) − 0.5`.

It appears 20–40 s after the stage opens or after a cooldown ends. It moves every 90–150 s.

**The first appearance in a visit** flashes "깊은 곳에서 무언가 눈을 떴다…".

**The cue**, every 15–25 s:
- 3 small bubble rings on the surface above the lurk point, 0.25 s apart (`Fx.Ripple`);
- one 0.5 s eye glint: two 1 px pixels 2 px apart, in the eye colour at 70 % alpha. It sits at `P.To2D(P.Apparent(lurk))`, sorted at order 19 (the fish-shadow band).

**The meter** runs in `Waiting` with the lure in the water. It fills only while **all** of these hold:
- the equipped bait is a key lure;
- the lure has soaked ≥ 8 s;
- the lure's depth is ≥ 5.0 m and within 1.5 m of the bottom;
- the lure is within 8 m of the lurk point horizontally;
- Q ≥ 0.55.

```
meter += dt · keyWeight · Q / fillTime      (all hold)
meter −= 0.5 · dt / fillTime                (otherwise), clamped to 0..1
```

With the egi at Q ≈ 0.8, the meter fills in ~20 s after the soak.

**Tells:**

| Meter | Effect |
|---|---|
| 0.4 | Flash "…깊은 곳에서 무언가 움직인다" |
| 0.5 | Ordinary fish within 12 m `Flee()`, and `CanFishEngage` refuses new fish. Flash "물고기들이 흩어졌다…!" |
| 0.7 | A `LineRing` at `WaterEntry` every 0.6 s, a low `Sfx.Drone`, and flash "줄이 미세하게 떨린다…" |
| 1.0 | Roll `min(0.95, chance · rod.luck)`. Success starts `S.Encounter`; a failure drops the meter to 0.5 with "기척이 멀어졌다…". |

Other rules:
- **Gear gate.** If `Game.I.Line.strength < minLine`, the meter is capped at 0.7. At the 0.7 tell it flashes, once per visit: "이 녀석을 상대하려면 더 튼튼한 줄이 필요할 것 같다… (20kg 이상)".
- **Wrong-lure tip.** A non-key lure spending 30 s in total within 8 m of the lurk point flashes `tipWrongLure`, once per visit.
- **One encounter per cast.**

### 2.3 States and phases

`FishingController.S` gains **`Encounter`**, with `UpdateEncounter(dt)`. While it is active:
- `CanLeave` is false;
- the bait picker, the retrieve button and the top HUD are hidden;
- the reel UI stays at the bottom right, with the label "원 = 감기 · ↓톡 = 당기기".

The phases live in the pure model `LegendEncounter`.

```
Waiting ─roll ok─▶ Omen 0.8 ▶ Open 0.5 ▶ Eyes 3.0 (repeat: 1.2) ▶ Approach 3.0 ▶ Tease ≤28
                                                                                 │ gauge=100, or timeout with gauge≥60
                   ◀─ Close 0.3 ◀─ TurnAway 1.2 ◀── gauge=0 / timeout<60 / missed hook
                                                                                 ▼
               NoseIn 0.6–1.4 (last 0.35 = tell) ▶ Lunge 0.30 (grow to full) ▶ Bite (hit-stop 0.12)
                    │ tap before the buffer: gauge=65 → Tease                    ▼
                                                        HookWindow 0.6+hookBonus ─miss─▶ TurnAway
                                                                                 │ tap / flick / circle
                                                        Hooked 0.8 ▶ Surface wipe 0.45 ▶ Fighting (grace 0.5)
```

**On entry:**
- `Tackle.Hold()` freezes the surface rig;
- the spawner is paused (`FishSpawner.Paused = true`);
- every `Engaged` fish calls `LoseInterest()`;
- the fish still left within 12 m `Flee()`;
- `Angler.LineTarget = Tackle.LineEnd`.

**On a fail:** `Tackle.Resume()` returns to `Waiting` with the lure where it was. Nothing is consumed or lost.

**Inputs** are ignored by the gauge during Omen, Open, Eyes and Approach. The lure still moves locally in the window, so input is never dead.

### 2.4 Staging: overlay, then full screen

Everything is in pixel-view pixels (PPU 16). The origin of a rect is the bottom-left of the pixel-view RT (`w × h`, 480–640 × 270–400).

**Window rect.** 288 × 136, horizontally centred, top edge 10 px below the view top.
- On views taller than 270, the window is vertically centred in the span from `hatTop + 16` to `h − 10`, where `hatTop` is `P.To2D(Angler.Feet + (0, 1.8, 0))` in RT pixels. Its height is `min(136, span)`.
- In the cave at 270 high this puts the window at 124–260 px from the bottom. The hat top is at ~100, so the hands and crank stay visible below it.
- The lure's surface point, at z 14–20, falls inside the window.

**Dim.** A full-view black sprite at **order 45**. That is above the water, fish shadows, the float and splashes (≤ 44), and below the line (46), the rod (47/53) and the 3D angler (50). Its alpha goes from 0 to 0.55 over the Omen (0.6 s).
- The angler, rod, line and 3D crank stay at full brightness, so the surface echoes every input: the crank turns with circles (`Angler.ReelRevs`) and the rod jerks up on a 톡 (`Angler.RodJerk01`).
- While the player circles the angler uses the reel pose (`ReelPose`); otherwise "idle". There is no body bob.

**Open (0.5 s, ease-out).** The crop rect grows from 8 × 8 px at the lure's apparent screen point (`P.To2D(P.Apparent(Tackle.HookPos))`) to the window rect. The frame is drawn at the growing border.

**Frame.** `Encounter/enc_frame_cave` is a sliced SpriteRenderer (6 px border): 3 px of dark stone, a 1 px `#6affea` inner line and crystal studs at the corners. It sits 3 px outside the crop, at order 94.
- **Timer:** during Tease the inner top line (a 1 px sprite, order 95) shrinks from both ends in step with `teaseLimit`.

**Full screen (Lunge, 0.30 s, ease-out).** The crop rect grows to the whole view; the frame fades out as it reaches the edges. The underwater view covers the angler, while the canvas reel UI stays on top.

**Return (0.45 s).** The crop's bottom edge rises from 0 to `h`, with `Encounter/waterline` (a 1 px `#bfffff` line with 2 px of foam, tiled) at the edge. The dim is 0 from the start of the wipe, so the revealed surface is undimmed.

**Fail close (0.3 s).** The crop shrinks back into the lure's surface point, and the dim fades out.

**Sort orders in the pixel view:**

| Order | What |
|---|---|
| 45 | Dim |
| 91 | Back quad (backdrop RT) |
| 92 | Fish quad (3D RT, with the cave rim) |
| 93 | Front FX: bubbles, silt, near snow, the line in the window, eyeshine. Masked by a `SpriteMask` sized to the crop. |
| 94 | Frame |
| 95 | Timer line |
| 96 | Bite flash and waterline |

**HUD (`EncounterHUD`, children of `FishingHUD.Canvas`).** Positions come from the crop rect, via `PixelView.WorldToScreen` and `RectTransformUtility`.
- **Prompt plate:** inside the window's top edge (4 px in), centred: `verb_*` icon plus the mood prompt (Galmuri 18).
- **Gauge:** inside the bottom edge, 4 px in: a `mood_*` icon, a 180 × 7 px bar and the mood label.
  - The bar's zones are 경계 0–39 (`#7a8aa8`), 호기심 40–74 (`#6affea`) and 흥분 75–100 (`#ffc830`).
  - The fill flashes red for 0.2 s on each penalty.
- **Caption:** one line (or a line and a tip) on a dark translucent `panel_dark` plate (60 %), Galmuri 20, 1.2 s, in a band at the window's bottom, just above the gauge. While `State == Encounter`, `FishingHUD.Flash` is routed here, because the normal top-centre flash would sit inside the window. Two messages are never stacked: "완벽한 챔질!  완벽한 유혹!" and "덥석! 지금! 챔질!" are one line each.
- **Name card:** slides in from the window's top-left inside edge during Approach and stays 2.2 s.
- **From the Lunge on,** the prompt and gauge hide and captions show large (Galmuri 28) in the screen's lower third.
- **Never on the face.** Every frame `EncounterView.FaceRect` projects the legend's face (eyes, mouth and the head from the gill cover to the snout, the jaw as posed; only the eyes in the Eyes beat) through the encounter camera. A caption whose band would cover it (with a 10-unit margin) flips to the top band (under the prompt, or the screen's upper band), else slides to a side; the prompt (to a side), the gauge (under the prompt) and the name card (top-right, then low) step aside the same way. Each overlay also keeps clear of the ones placed before it, the lure and the reel. Hysteresis: a spot is left at once when the face reaches it, but a preferred spot is only taken back when clear by 14 more units for 0.5 s (0.8–1.0 s for the prompt, gauge and card); a new caption takes the best spot at once.

### 2.5 The underwater scene (`EncounterView`)

The scene uses two cameras that render only while the window is visible, and releases its RTs when it closes. It lives at world (0, −200, 0) and is called the **set**. In the set frame the lure rest point is the origin and the floor is at y = 0.

1. **Back camera**, layer 27. Orthographic, size = `h/2/PPU`. It renders into `rtBack` (the pixel-view size, point-filtered, clear `#04101a`). Its content is 2D pixel-art layers with parallax:

| Layer | Sprite | Placement | Parallax (× camera yaw shift in px = yaw·f) |
|---|---|---|---|
| Abyss + far arches | `uw_cave_bg` 640×400 | Fixed, 1:1 | 0.1 |
| God-ray | `uw_ray` 64×256 | Top-right, tint `#86dcff` at α 0.08, 0.1 Hz sway | 0.2 |
| Pillars + crystals | `uw_cave_mid` 768×200 | Base on the floor horizon | 0.35; crystal pixels pulse ±10 % at 0.3 Hz |
| Silt floor band | `uw_cave_floor` 768×120 | Top edge on the projected floor horizon (project set point (cam.x, 0, cam.z + 30)) | 0.8 |
| Far marine snow | code, 30 × 1 px `#3a6a7a` | Drifts down at 0.05 m/s | 0.5 |
| Lure halo | `lure_halo` 24×24 | At the lure's projected point, tint `#9affea`, 2 frames (22 / 26 px) at 1.5 Hz. The 1.0 m-light (non-glow) lures use α 0.35. | — |
| Foreground rock | `uw_cave_fore` 256×128 | Bottom-left | 1.3 |

2. **Fish camera**, layer 28. Perspective with an off-centre projection: the `ActorLayer.Projection` math with focal `fPx` and the principal point at the **window centre**. It renders into `rtFish` (clear (0,0,0,0), depth 24). Its content:
   - the coelacanth model, `ActorArt.Spawn(model, model + "_palette", 28)`, scaled `cm/100`;
   - the lure: a billboard `SpriteRenderer` with the `Encounter/lure_<id>_0/1` frames. It is scaled to an integer multiple of its native size (16 × 8 px), 1× at rest.

3. **Quads.** Both RTs are shown through MeshRenderer quads whose vertices are the crop rect and whose UVs are `crop / rtSize`, so pixels stay exactly 1:1.
   - `quadBack` uses the plain sprite material.
   - `quadFish` uses `ActorArt.RimMaterial("cave")`, which gives a cyan top rim: the "silhouette approaching" read. `_RimStrength` is 0 during Eyes and ramps to the preset value over the first 1 s of Approach.

4. **The line in the window.** A chain of 1 px sprites (order 93) from the projected set point `lure + (−0.9, 2.2, −1.6)` (towards the angler) to the lure. It is taut while winding or rising, and sags with 3 px of quadratic sag when still. Colour: `stage.UnderwaterLine(line.color)`.

5. **Front FX** (order 93, masked):
   - 4–6 bubbles (`bubble_s` / `bubble_m`) rise from the lure on each flick;
   - a silt puff (`silt_0..3`, 0.4 s) on each touch-down and every 0.3 m of dragging;
   - 12 near marine-snow flecks (1 px `#9fd8e0`, α 0.3–0.5).

6. **Lighting (the darkness).** Every `coel_*` material gets `_LurePos = (lureWorld, R · pulse)` each frame, with R = 1.6 or 1.0 m and a pulse of 1 ± 0.06 at 1.5 Hz. It also gets `_Abyss = #03080c` and `_FogOutline = #1f5f70` (see 4.2).
   - Outside the light the body renders abyss-black with a dim teal contour.
   - Near the light the bands come back, with the key coming from the lure.
   - The eye meshes (`coel_eye_glow`) are unlit and always visible.

7. **Eyeshine.** Two `eyeshine` sprites (7×7: a 2×2 core plus a halo at 40 %) at order 93.
   - Position: `Eye.L` / `Eye.R` projected through the fish camera into RT pixels, then into pixel-view world coordinates.
   - The two are held at least **6 px apart**, spread symmetrically along the projected eye axis, so "two eyes" reads even at 9 m.
   - Alpha: `smoothstep(−0.3, 0.3, dot(eyeForward, toCamera))`, times the phase multiplier (for example 0.6 while wary).
   - Colours: core `eyeCore`, halo `eyeGlow`.

### 2.6 Camera

In the set frame (lure = origin):

| | Value |
|---|---|
| Rest | Camera at (−1.2, 0.45, −2.7), 3.0 m from the lure; looks at (0.6, 0.1, 2.0) |
| Framing | The lure sits lower-left, at about (0.33 W, 0.30 H) of the window; deep water is centre-right |
| Focal | `fPx = (windowH / 2) / tan(21°)`, 177 px for 136 px (42° vertical inside the window) |
| Approach | Dollies to 2.6 m |
| Following | Yaws ±6° towards the fish (smoothed over 0.5 s) |
| Lure tracking | The camera and target follow the lure's local position (smoothed over 0.4 s) |

At full screen `fPx` is unchanged, so the view simply reveals more around the window. The Lunge dollies onto the head and raises `fPx` to 1.5× over 0.3 s (roughly 42° → 28°).

### 2.7 Choreography (`Legend3D` poser + choreo in `EncounterView`)

**Swim cycle.** The spine yaw amplitudes are Spine.F 2°, B1 4°, B2 6°, B3 8° and Tail 12°, with a phase step of 0.9 rad per joint. The body is nearly stiff, like the real fish.

Tail frequency by mood:

| Mood / phase | Tail frequency |
|---|---|
| 경계 | 0.6 Hz |
| 호기심 | 0.8 Hz |
| 흥분 | 1.0 Hz |
| Lunge | 2.5 Hz |

The fish banks ±10° into turns.

**The signature gait.** The lobed paired fins trot in diagonal pairs: Pec.L with Pel.R, and Pec.R with Pel.L, in opposite phase. They swing ±25° at 0.5–0.7 Hz, and each `.Fan` bends 15°, lagging 0.3 rad. Dorsal2 and Anal scull in anti-phase at ±25°.

**Dorsal1** is folded normally, raised 60° while wary or after a flinch, and half-raised (30°) when excited.

**Beats:**

1. **Eyes** (3.0 s, 1.2 s on repeat encounters). The body renderers are off; only the eye meshes and eyeshine show.
   - The eyes open at 0.2 s over 2 frames, drifting from (1.6, 0.35, 9.0) to (1.3, 0.3, 7.0).
   - They blink once at 1.6 s (0.1 s shut).
   - `Sfx.Heartbeat` plays at 0.5 Hz.
   - Caption: "어둠 속에서 무언가 다가온다…".
2. **Approach** (3.0 s). The body turns on and the fish comes head-on along an S-curve to the orbit entry at 2.4 m, at about 1.7 m/s.
   - It emerges as a black silhouette with a cyan top rim. Entering the light, the 1 px rim is followed by glinting spots, then the full body.
   - The heartbeat rises to 0.8 Hz.
   - Name card: on the first encounter it reads "??? · 전설의 물고기", and "실러캔스" fades in when the gauge first reaches 60. On repeat encounters it reads "실러캔스 · 4억 년을 살아남은 고대어" straight away.
3. **Tease.** The fish orbits the lure (counter-clockwise from above) on an ellipse whose semi-axis towards the camera is 1.4 × r, so one pass comes to about 1.2 m from the camera (~230 px long): the 3D moment. The orbit phase is chosen so the first 호기심 pass crosses between the camera and the lure about 4 s into the mood, briefly blocking the halo.

| Mood | Radius | Speed | Look | Extra |
|---|---|---|---|---|
| 경계 | 2.4 m (mostly outside the light) | 0.35 rad/s | Rim and eyes at 60 % | Dorsal1 up |
| 호기심 | 1.3 m (fully lit) | 0.5 rad/s | Whole body | The head yaws up to 30° to track every hop and fall of the lure |
| 흥분 | Stops 0.6 m from the lure, facing it | Hovers | Eyes 100 % | Nose-nudges of 0.1 m at 1.2 Hz; jaw mouths 0–8°; pectoral lobes flare |

**Lure motion in the set** (local only; the surface rig stays frozen):
- A circle drags the lure 0.08 m per revolution towards (−0.45, 0, −0.9), with a silt puff every 0.3 m.
- A flick hops it up:
  - egi: 0.25 + 0.35·strength m;
  - softworm: 0.2 + 0.2·strength m;
  - jig: 0.3 + 0.5·strength m.
- It then falls nose-down 20°: egi at 0.35 m/s, softworm at 0.6, jig flutters at 0.8. Visual fall speeds are slowed for readability.
- Holding still, it rests and the halo pulses.

**Reactions:**
- **Flinch** (each penalty): the fish jerks back 0.3 m in 0.15 s with a tail snap (25°), its eyes dim to 50 % for 0.3 s and Dorsal1 goes up.
- **Dropping a band:** it visibly backs out to that band's radius.
- **Below 15:** it yaws 90° away and drifts to 3 m. The prompt changes to "돌아서려 해요! " + that legend's 경계 prompt (e.g. the marlin: "돌아서려 해요! 빠르게! 쉬지 말고 감아요"), since the 경계 action wins it back.
- **Turn away (fail):** the light radius shrinks to 0 over 0.7 s, so the body sinks into the dark first. The eyes fade last, over 0.5 s.
- **NoseIn:** it lines up head-on 0.6 m from the lure. In the last 0.35 s (the tell) the eyes flare, the fins flare, `Sfx.Glint` plays ("ting") and the gauge frame flashes gold. Caption: "…노려본다" then "온다…!".
- **Lunge:** the head drives 0.6 m forward, the jaw opens 40° in 0.12 s and **Skull tilts up 8°** (the coelacanth's hinged skull). Marine snow is sucked in.
- **Bite:** the jaw closes in 0.08 s over the lure, and the lure billboard is hidden (it is in the mouth). A white flash of 2 frames at 60 %, `view.Shake(0.15, 0.15)` and a 0.12 s hit-stop on the fish only (no `Time.timeScale`). `Sfx.Chomp` plays, with the caption "덥석!".
- **Hooked** (0.8 s, full screen): the head shakes ±25° at 5 Hz, bubbles and silt burst, and the line snaps taut.

### 2.8 Minigame rules

- The gauge starts at `20 + pity`, where pity is 0, 10, 20 or 30. It loses 3 per second throughout Tease.
- **Moods follow the gauge band:** below 40 is 경계, 40–74 is 호기심, 75 and up is 흥분. With hysteresis 5, the fish drops a mood only below 35 or 70.

| Mood | Prompt | Gain | Losses (each with a caption) |
|---|---|---|---|
| 경계 | 살살… 천천히 감아요 | Winding at 0.15–0.7 rev/s: +9/s ("좋아요"). 0.7–1.2 rev/s is neutral. | Faster than 1.2 rev/s: −12/s ("너무 빨라요!"). Any flick: −15 ("놀랐어요!"). |
| 호기심 | 톡! 당기고 기다려요 | +14 when the pause after a flick of strength ≤ 0.6 reaches 0.6 s ("관심을 보여요!"). Only one credit per flick. | Flick strength > 0.8: −10 ("너무 세요!"). A second flick < 0.6 s after the last: −6 ("너무 서둘러요"). Circling for more than 1.5 s straight: −6/s ("감지 말고 톡!"). No flick for more than 3 s: −5/s ("지루해해요…"). |
| 흥분 | 멈춰요! 움직이지 마요 | Still for ≥ 0.4 s: +12/s ("입을 벌린다…!") | Any flick: −20 ("놀랐어요!"). Circling after a 0.25 s grace: −15/s ("움직이면 안 돼요!"). |

- **Fairness.** A penalty of the same kind cannot hit again within 0.8 s, so one clumsy gesture costs only once. Every penalty gets a flinch, a red gauge flash and one caption.
- **Success:** the gauge reaches 100, which starts NoseIn.
- **Fail:** the gauge reaches 0.
- **Timeout:** 28 s of Tease. With a gauge ≥ 60 the fish goes to NoseIn anyway; below 60 it leaves with "흥미를 잃고 돌아갔다…".
- **Pacing check:**

| Stretch | Net rate | Time |
|---|---|---|
| 경계, 20 → 40 | +6/s | ≈ 3.3 s |
| 호기심, 40 → 75 | ≈ +9.5 per 1.5 s cycle | ≈ 5.5 s |
| 흥분, 75 → 100 | +9/s, after 0.4 s | ≈ 3.2 s |

Perfect play takes about 12 s of Tease, plus 7.3 s of intro and about 1.5 s of bite, so roughly **21 s**. Typical play is 25–35 s, and the hard cap is about 40 s.

### 2.9 The bite and the hook set

- **Early tap.** A tap, flick or circle during NoseIn before the buffer (that is, before `close − 0.15 s`) makes the fish flinch. The gauge drops to 65 (호기심) with "너무 일찍 챘어요!" and play returns to Tease. The Tease timer does not reset.
- **Buffered tap.** An input within 0.15 s before the jaw closes is buffered and counts at the close.
- **Hook window.** It opens at the close and lasts `0.6 + rod.hookBonus` s: 0.6 s with bamboo, 1.0 s with the dragon rod. Tap, flick or circle all set the hook, the same inputs as `Biting`.
  - Within 0.25 s of the close (or buffered): "완벽한 챔질!", with a gold spark. The fight starts at `Stamina = 0.85`.
  - Otherwise: "걸었다!".
  - When the window runs out: "뱉어버렸다…". The lure pops out and the fish turns away; this counts as a fail.
- **완벽한 유혹.** A perfect hook with zero penalties during Tease rolls the size twice and keeps the larger. The caption adds "완벽한 유혹!".

### 2.10 Transition to the fight

1. **Hooked** (0.8 s, full screen), then the **surfacing wipe** (0.45 s, 2.4).
2. `Spawner.SpawnHooked(sp, cm, Tackle.HookPos)` spawns the legend agent: State Hooked, heading +z, added to the list.
3. `BeginFight(agent, perfect)`, refactored out of `SetHook()`, builds the `FightModel` exactly as `SetHook` does today. If the hook was perfect it then sets `Fight.Stamina = 0.85f` and calls `Fight.Hold(0.5f)`: for 0.5 s, `Step` keeps tension at 0 and the line fixed, so the player can get a finger ready to circle.
4. A surface boil at the lure spot (`JumpSplash(pos, 1.2)`, `view.Shake(0.2, 0.3)`) and the flash "실러캔스가 걸렸다! 원을 그려 감아요!".
5. The coelacanth has jump 0, so the fight stays deep. The fight, landing, catch card and aquarium flow are unchanged.
6. The spawner is unpaused and the cooldown is set to `coolFight` (180 s), whatever the outcome.

### 2.11 Fail, cooldown, pity, rewards, save

**Fail** covers a gauge of 0, a timeout below 60 and a missed hook. On a fail:
- cooldown `coolFail` (60 s);
- pity rises by 10, up to 30;
- the caption shows "실러캔스가 어둠 속으로 사라졌다…" plus a tip for the single worst mistake (see 2.12);
- no bait or lure is lost.

**Pity** resets only when the legend is **landed**.

**First encounter** (the first time an encounter reaches Approach): +100 XP (`Game.AddXp`) and the toast "첫 조우! · 도감에 목격 기록 +100 XP". The collection's silhouette for the legend gets "목격 n회 · 아직 낚지 못함".

**Catch:** the normal legendary rewards through `RegisterCatch`, including its existing ×2 XP for a new species.

**Save** (`SaveData`):

```csharp
[Serializable] public class LegendRecord { public string id; public int seen, fails, pity; public long coolUntil; public int coolLen; }
public List<LegendRecord> legends = new List<LegendRecord>();   // Sanitize: ??= new
```

Cooldowns are saved in the legend's `LegendRecord`: `coolUntil` is the wall-clock end (UTC unix seconds, `SaveSystem.Now`; 0 = none) and `coolLen` the length it was given. `LegendWatch.Cool` sets both and saves; `LegendWatch.Remaining(id)` / `AwayOf` read them. Because the end is wall clock, a cooldown keeps running while the app is closed and a relaunch does not reset it. If the device clock is set back, a remaining time longer than `coolLen` is capped at `coolLen` (the end moved to now + `coolLen`). Saves from before this have no cooldown. The `-fkencounter` tests clear the saved cooldowns at start and set none.

### 2.12 Korean texts

**Surface**
- 깊은 곳에서 무언가 눈을 떴다…
- …깊은 곳에서 무언가 움직인다
- 물고기들이 흩어졌다…!
- 줄이 미세하게 떨린다…
- 기척이 멀어졌다…
- 이 녀석을 상대하려면 더 튼튼한 줄이 필요할 것 같다… (20kg 이상)
- 어둠 속에서 빛나며 가라앉는 먹이에 끌리는 것 같다…

**Encounter start**
- …! (Omen flash)
- 어둠 속에서 무언가 다가온다…

**Name card**
- ??? · 전설의 물고기
- 실러캔스 · 4억 년을 살아남은 고대어

**Moods:** 경계 / 호기심 / 흥분

**Prompts**
- 살살… 천천히 감아요
- 톡! 당기고 기다려요
- 멈춰요! 움직이지 마요
- 돌아서려 해요! + the legend's 경계 prompt

**Positive:** 좋아요 · 관심을 보여요! · 입을 벌린다…!

**Negative:** 너무 빨라요! · 놀랐어요! · 너무 세요! · 너무 서둘러요 · 감지 말고 톡! · 지루해해요… · 움직이면 안 돼요!

**Bite:** …노려본다 · 온다…! · 덥석! · 지금! 챔질! · 완벽한 챔질! · 걸었다! · 완벽한 유혹! · 너무 일찍 챘어요! · 뱉어버렸다…

**Result:** 실러캔스가 걸렸다! 원을 그려 감아요! · 실러캔스가 어둠 속으로 사라졌다… · 흥미를 잃고 돌아갔다…

**Fail tips**, keyed by the worst mistake:
- 다음엔: 경계할 땐 아주 천천히!
- 다음엔: 너무 세게 당기지 않기
- 다음엔: 흥분하면 멈추기!
- 다음엔: 입을 벌리기 전엔 챔질 참기

**Reel label during the encounter:** 원 = 감기 · ↓톡 = 당기기

**Other:** 첫 조우! · 도감에 목격 기록 +100 XP · 목격 {n}회 · 아직 낚지 못함 · 얼음 구멍에서는 쓸 수 없어요 · 따라오다 돌아섰어요

---

## 3. Art list (all made in Blender)

### 3.1 `Tools/Blender/fk_items.py`

These outputs are written straight into `Assets/Resources/Sprites/`, as the script does today.

- **`BAITS`** gains `bait_spinner`, `bait_crank`, `bait_popper`, `bait_softworm`, `bait_egi` and `bait_kona`. Each gets a `build_bait` branch:
  - spinner: a willow blade on a clevis, a body and a treble;
  - crank: a fat body with a lip;
  - popper: a cupped face;
  - softworm: a curly tail on a jig head;
  - egi: a shrimp-shaped body with cloth, glow fins and a cyan emissive belly;
  - kona: a skirt with a resin head.
- **Outputs** for the new lures:
  - `Items/bait_<id>.png`, 32×32 (`render_icon`);
  - `World/bait_<id>_w.png`, 11×11 (size 11, margin 0).
- **Existing lure icons are unchanged.** `bait_frog_w` and `bait_minnow_w` are fine.
- **`UI_ICONS`** additions, output to `Sprites/UI/<name>.png`:
  - action chips, 12×12: `act_steady`, `act_twitch`, `act_top`, `act_bottom`, `act_vertical`;
  - mood icons, 12×12: `mood_wary`, `mood_curious`, `mood_excited`;
  - verb icons, 16×16: `verb_wind`, `verb_flick` (톡: a down arrow — the finger's pull — beside a rod tip flicking up), `verb_hold`;
  - `icon_eye`, 16×16 (seen / legend key marker).

### 3.2 `Tools/Blender/variants/hybrid/hyb_encounter.py` (new)

- It uses the cave preset palette (`use_preset("cave")`): crystals `#6ad8ff` / `#c89cff`, rock dark violet, water deep `#04101a`.
- It writes to `_tmp/variants/hybrid/encounter/`. `build_hybrid.ps1` gets the script, and `-Install` copies the output into `Assets/Resources/Sprites/Encounter/`.

| File | Size | Content |
|---|---|---|
| `uw_cave_bg.png` | 640×400, opaque | Abyss gradient (`#04101a` at the bottom to `#0b2230` at the top, banded) and far rock-arch silhouettes `#0e2a38` |
| `uw_cave_mid.png` | 768×200, alpha | Pillars and crystal clusters `#1f5a66`, with 1 px `#6affea` twinkle pixels |
| `uw_cave_floor.png` | 768×120, alpha | Silt floor band `#10303a` in pseudo-perspective, stones smaller towards the top |
| `uw_cave_fore.png` | 256×128, alpha | Out-of-focus rock edge for the bottom-left |
| `uw_ray.png` | 64×256, alpha | White god-ray, tinted in code |
| `lure_egi_0/1.png` | 16×8 | Side view, 2 flutter frames, emissive belly |
| `lure_softworm_0/1.png` | 16×8 | Tail wiggle |
| `lure_jig_0/1.png` | 16×8 | Flash frames |
| `lure_halo.png` | 24×24 | White dithered radial halo |
| `eyeshine.png` | 7×7 | White 2×2 core plus a 40 % ring, tinted in code |
| `silt_0.png` … `silt_3.png` | 8×8 | Puff frames |
| `bubble_s.png` / `bubble_m.png` | 3×3 / 5×5 | Bubbles |
| `waterline.png` | 32×4 | A 1 px `#bfffff` line with a 2 px foam strip, tileable |
| `enc_frame_cave.png` | 24×24 | 9-slice with a 6 px border: 3 px stone, a 1 px `#6affea` inner line and crystal studs at the corners |

`Assets/Editor/PixelArtImporter.cs`: add `{ "enc_frame_cave", 6 }` to `Borders` and bump `GetVersion()` to 3.

### 3.3 `Tools/Blender/variants/hybrid/hyb_legend3d.py <fish_id>` (new, generic)

- **Contract:** the same as `hyb_actors3d.py`. Reuse its `FBX_OPTS`: metres, +Y up, the model faces **+Z** in Unity, the root with an identity transform, rigid bone-parented parts `geo_<Bone>`, no skin and no animation. The Blender armature carries Rx(+90).
- **Source geometry:** reuse `fk_fish.F["coelacanth"]` (the Body loft profile, the "coel" tail and the lobed fins). Pre-cut the loft into segments at t = 0.82 / 0.64 / 0.46 / 0.30 / 0.16; each segment overlaps the next by 0.015 m and has end caps, so bends up to 15° per joint show no gap.
- **Size:** modelled **1.0 m** long (nose tip at z +0.50, tail tip at z −0.50). At runtime the scale is `cm / 100`.
- **Budget:** ≤ 3000 triangles, flat shaded (the importer writes smoothed hull normals into UV3).
- **Outputs:**
  - `Tools/Blender/_tmp/actors3d/legend_coelacanth.fbx`
  - `legend_coelacanth_palette.json`
  - `legend_coelacanth_check.png` (rest pose, jaw open, maximum bend)
  - `legend_coelacanth_report.json`
- **Install:** copy into `Assets/Resources/Models/legend_coelacanth.fbx` and `Assets/Resources/Models/legend_coelacanth_palette.json`. They are loaded with `ActorArt.Model("legend_coelacanth")` and `ActorArt.Palette("legend_coelacanth_palette")`.

**Hierarchy.** Bone heads are in Unity metres for the 1.0 m model. Every bone except Root has one child mesh `geo_<Bone>`.

```
Coelacanth (root node)
└ Root (0,0,0)
  ├ Spine.F (0,0,0.04) ─ Head (0,0,0.24) ─┬ Skull (0,0.035,0.34)   hinge about X, 0..+10° (roof of the head)
  │                                        ├ Jaw   (0,-0.025,0.33)  about X, 0..40° down
  │                                        ├ Eye.L (-0.032,0.02,0.41)  EMPTY, +Z = the eye's facing (out and forward)
  │                                        ├ Eye.R ( 0.032,0.02,0.41)  EMPTY
  │                                        ├ Mouth (0,-0.01,0.49)      EMPTY (lure attach point)
  │                                        └ geo_Eye.L / geo_Eye.R meshes under Head (disc 0.045 m)
  ├ Pec.L (-0.045,-0.035,0.26) ─ Pec.L.Fan (-0.075,-0.05,0.20)     lobed: fleshy stalk + ray fan
  ├ Pec.R ( 0.045,-0.035,0.26) ─ Pec.R.Fan
  ├ Dorsal1 (0,0.085,0.10)                                          fold about X, 0..60°
  └ Spine.B1 (0,0,-0.04) ─ Spine.B2 (0,0,-0.16) ─ Spine.B3 (0,0,-0.27) ─ Tail (0,0,-0.36) ─┬ Tail.Upper
       ├ Pel.L (-0.03,-0.075,-0.02) ─ Pel.L.Fan     (children of Spine.B1)                  ├ Tail.Mid  (small middle lobe)
       ├ Pel.R ( 0.03,-0.075,-0.02) ─ Pel.R.Fan                                             └ Tail.Lower
       ├ Dorsal2 (0,0.06,-0.18) ─ Dorsal2.Fan       (children of Spine.B2)
       └ Anal    (0,-0.06,-0.18) ─ Anal.Fan
```

**Materials.** Every name is prefixed `coel_`, because ActorArt caches materials globally by name. The body segments are split into back, side and belly faces by height. The outline for all materials is `#0b1322`.

| Material | Dark | Mid | Light | Note |
|---|---|---|---|---|
| `coel_back` | `#1c2744` | `#27375a` | `#35466b` | |
| `coel_side` | `#27375a` | `#35466b` | `#4d628a` | |
| `coel_belly` | `#35466b` | `#465a7c` | `#5d7396` | |
| `coel_fin` | `#18223c` | `#27375a` | `#3a4d74` | |
| `coel_spots` | `#8e9ab4` | `#c2cadb` | `#e4eaf4` | About 30 flat decal patches per side, 1 mm off the skin |
| `coel_mouth` | `#3a1a2a` | `#6a2f44` | `#8e4a5e` | Inside the jaw |
| `coel_teeth` | `#a8a090` | `#d0c8b4` | `#e8e0cc` | |
| `coel_eye_ring` | `#05080c` | `#0a0e14` | `#1a2230` | |
| `coel_eye_glow` | `#6ea060` | `#b8ff8a` | `#f0ffd8` | The `_glow` suffix makes it unlit with no outline |

---

## 4. Code plan

### 4.1 Files

**New files**, in `Assets/Scripts/Fishing/`:

| File | Kind | Responsibility |
|---|---|---|
| `LureInput.cs` | Pure | `LureInput`: flick, tap and pause detection (1.1); `event Action<float> Flicked`; `PauseT`, `WindRunT`, `Winding`; keyboard T / Shift+T. `LureAction`: Q, `StrikeOpen`, `Feedback` (1.4). |
| `Legend/LegendWatch.cs` | Plain class | Lurk point, cues, meter, tells, gear gate, roll, saved cooldowns (read from `LegendRecord`); `Debug` hooks for `-fkencounter`. |
| `Legend/LegendEncounter.cs` | Pure | The phase machine and gauge (2.3, 2.8, 2.9). Inputs each tick: `LureInput` and `Gesture`. Outputs: `Phase`, `Gauge`, `Mood`, `PhaseT`, and events `Penalty(kind, text)`, `Credit(text)`, `MoodChanged`, `Tell`, `Close`, `Hooked(perfect, lure)`, `Failed(reason, tip)`. |
| `Legend/EncounterView.cs` | MonoBehaviour | RTs, cameras, quads, crop rect animation, dim, frame, timer, backdrop parallax, particles, line, eyeshine, flash, wipe, choreography targets, per-frame `_LurePos`. |
| `Legend/Legend3D.cs` | Poser (like `Reel3D`) | `TryCreate(EncounterDef, cm)`; `Pose(dt, swimHz, mood, yawToTarget, jaw, skull, dorsal1, finFlare)`; `EyeL`, `EyeR`, `Mouth`; `SetBodyVisible(bool)`. |
| `Legend/EncounterHUD.cs` | UI | Prompt plate, gauge, mood label, captions, name card; the flash reroute. |

Unity generates the `.meta` files for these on import.

**Modified files:**

| File | Changes |
|---|---|
| `Data/Models.cs` | The enums and `BaitDef` fields (1.6); `FishSpecies.actionPrefs`, `Appeal()` and `encounter`; `EncounterDef`, `MoodDef`, `Verb`. |
| `Data/GameDatabase.cs` | 6 new lure rows and updates to the existing 4; `bait_glow.glow = true`; hints; the `@` parsing in `F()`; the preference strings (1.7); the coelacanth `EncounterDef` row. |
| `Fishing/Tackle.cs` | Buoyancy modes, the per-lure `Wind`, `Twitch()`, `Resume()`, `OnBottom`, `BottomTouch`, `Falling`, `FallT`. |
| `Fishing/FishingController.cs` | `S.Encounter`; `gestureOn` includes it; `LureInput` and `LureAction` properties updated in Waiting / Encounter; flick → `Tackle.Twitch` plus the `RodJerk01` rod jerk; the new `WantsToApproach` formula; the tap-only scare rule; `LegendWatch` tick in Waiting; `UpdateEncounter`; `BeginFight(FishAgent, bool perfect)` split out of `SetHook`; `CanLeave`; `FaceTarget` (Encounter → `Tackle.Surface`). |
| `Fishing/FishAgent.cs` | Approach strike gating by `StrikeOpen` and Q; interest loss at Q < 0.25; nibble only for `needBottom` lures. |
| `Fishing/FishSpawner.cs` | `Pick` skips encounter species; `Paused`; `SpawnHooked(sp, cm, pos)`. |
| `Fishing/FightModel.cs` | `Hold(float s)`: a grace period in which `Step` keeps tension at 0 and the line fixed. |
| `Fishing/FishingHUD.cs` | `OnState(Encounter)`: hide the top bar, stage name, back, settings, tackle panel and retrieve; reel label. `Flash` rerouted during an encounter. Tackle-panel lure slot, feedback label, picker sections, ice greying. |
| `UI/ShopUI.cs` | 미끼 / 루어 sub-tabs, action groups, chips; the fan list uses `Appeal`. |
| `UI/CollectionUI.cs` | "목격 n회 · 아직 낚지 못함" for uncaught legends with `seen > 0`. |
| `Core/SaveData.cs` | `LegendRecord` and `legends`, plus `Sanitize`. |
| `Core/Game.cs` | `-fkencounter`, `-fklure`; a `LegendRecord` helper. |
| `Audio/Sfx.cs` | New clips (4.3). |
| `Shaders/ActorToon.shader` | The lure light (4.2). |
| `Editor/PixelArtImporter.cs` | Frame border; version 3. |
| `Debug/AutoPilot.cs` | Scenarios `encounter` and `lure` (4.5). |

`Tackle.MoveSpeed` stays for the float-bait rules.

### 4.2 Shader change (`ActorToon.shader`, no keyword)

**New properties:**
- `_LurePos ("Lure light: xyz world, w radius m (0 = off)", Vector) = (0,0,0,0)`
- `_Abyss ("Colour outside the lure light", Color) = (0.012,0.031,0.047,1)`
- `_FogOutline ("Outline outside the lure light", Color) = (0.122,0.373,0.439,1)`

**TOON pass.** Pass the world position from the vertex shader. In the fragment shader, when `_LurePos.w > 0`:

```
float d = distance(wpos, _LurePos.xyz);
float lit = 1 - smoothstep(0.35 * _LurePos.w, _LurePos.w, d);
float f = (0.5 + 0.5 * dot(N, normalize(_LurePos.xyz - wpos))) * lit;
c = f < 0.12 ? _Abyss : (f < _Bands.x ? _Dark : (f < _Bands.y ? _Mid : _Light));
```

**OUTLINE pass.** Pass the world position too. When `_LurePos.w > 0`, the colour is `lit > 0.3 ? _OutlineColor : _FogOutline`, and the alpha stays 254/255 for the rim pass.

With `w = 0` the output is bit-identical to today's, so the angler and the reels are unaffected. `_Unlit` materials (the eyes) return early, as they already do.

### 4.3 Sfx additions (`Sfx.cs`, synthesized like the rest)

| Clip | Sound |
|---|---|
| `Drone` | 1.5 s, 55→50 Hz triangle plus low noise |
| `Heartbeat` | Two thumps, 60 / 50 Hz, 0.35 s |
| `Glint` | "ting", 2640 Hz sine, 0.25 s decay |
| `Chomp` | Noise burst plus a 90 Hz thud, 0.3 s |

`Plop`, `Bubble`, `Hook`, `Splash` and `Error` are reused.

### 4.4 Test switches (`Game.DebugBoot` and `LegendWatch`)

| Switch | Effect |
|---|---|
| `-fkencounter [now\|natural]` | Owns and equips `bait_egi`. If the equipped line is under 20 kg, owns and equips `line_pe3`. Cooldowns are 0 and pity is 0. **`now`** (default): the encounter starts 1.0 s after the lure lands, skipping the conditions. **`natural`**: the lurk point is placed at (Angler.X, −, 16) right away, the soak is 2 s, the meter fills ×8 and the roll always succeeds. Use it with `-fkscene Fishing -fkstage cave`. |
| `-fkencplay perfect\|bad\|early` | The AutoPilot `encounter` play style (default `perfect`). `coolsave`: a failed encounter whose cooldown is set and saved despite `-fkencounter`; relaunch with the same `-fksave`, no `-fkencounter`, and `-fkauto legcool` to check it survived. |
| `-fklure <id>` | Owns and equips that lure, like `-fkbait`. With `-fkauto lure`, runs the lure-action scenario. |

Existing switches still apply: `-fkfresh`, `-fkrich`, `-fksave`, `-fkscene`, `-fkstage`, `-fkrod`, `-fkshots`, `-fkflick`.

### 4.5 AutoPilot scenarios

**`-fkauto encounter`** (for example `-fkfresh -fkrich -fksave enc -fkscene Fishing -fkstage cave -fkencounter -fkauto encounter -fkshots <dir>`):

1. Cast straight with the default flick (3.4 H/s, z ≈ 14), then wait for `S.Encounter`. In `natural` mode it first waits for `OnBottom` and works the egi: a 톡 (a downward pull) at 0.9 H/s, then a 2 s pause, repeated.
2. Log `[ENC] phase=<p> t=<s> gauge=<g> mood=<m>` on every phase or mood change.
3. Play:
   - **경계:** simulated circles at 0.4 rev/s (radius 0.12 H).
   - **호기심:** a 톡 (a downward pull) with 0.10 H of travel at 1.0 H/s (strength ≈ 0.4), then a 1.2 s pause, repeated.
   - **흥분:** no input.
   - **NoseIn:** wait. At `Close`, tap 0.1 s later.
   - `bad`: circles at 2.0 rev/s in 경계, which should fail.
   - `early`: a tap during NoseIn, which should drop the gauge to 65 and then succeed.
4. **One capture set** into `-fkshots`:

| Shot | When |
|---|---|
| `enc_1_eyes.png` | Eyes + 1.5 s |
| `enc_2_approach.png` | Approach + 2.0 s |
| `enc_3_pass.png` | The frame where the fish is nearest the camera in 호기심 |
| `enc_4_excited.png` | 흥분 + 1.0 s |
| `enc_5_bite_full.png` | The Close frame, full screen |
| `enc_6_wipe.png` | Mid-wipe |
| `enc_7_fight.png` | The first frame of Fighting |

5. Checks:
   - `[AUTO] CHECK encounter triggered`
   - `reached NoseIn`
   - `hook perfect=<b>`
   - `fight species=coelacanth`
   - for `bad`: `failed → Waiting, lure kept`

   After the checks, the existing `Fish()` fight loop lands the fish.

**`-fkauto lure`** (for example `-fklure bait_minnow -fkstage lake`):
- First (with the minnow) checks the 톡's direction in Waiting: an upward swipe must not count (`CHECK upward swipe is no 톡`, reject "up"), a downward pull must, and it must jerk the rod (`CHECK downward pull is a 톡 and jerks the rod`).
- Casts, then plays the equipped lure's ideal input for 20 s: its reel band midpoint, or its flick count, gap and rest midpoints (each 톡 a downward pull, 0.10 H at 1.0 H/s).
- Then plays a wrong input for 10 s: the opposite verb.
- Logs `[LURE] t q strike depth onBottom` every 0.5 s, plus every bite.
- Checks: `[AUTO] CHECK q_good>=0.7`, `q_bad<=0.3`.
- No screenshots.

### 4.6 Build order

Each step is playable on its own. Nothing is polished beyond this list.

1. **Lures.** Models, GameDatabase, LureInput and LureAction, Tackle, the controller's bite rules, FishAgent, HUD, picker and shop, and the fk_items art.
2. **Encounter logic.** LegendWatch, LegendEncounter, `S.Encounter`, `BeginFight` / `SpawnHooked` / `FightModel.Hold`, save, Sfx and EncounterHUD. Placeholders at this step: a grey capsule stands in for the fish, and a flat `#04101a` backdrop inside the window.
3. **Visuals.** The ActorToon lure light, EncounterView, Legend3D, `hyb_legend3d.py` and `hyb_encounter.py`, and the importer border.
4. **Test switches and AutoPilot scenarios,** then **one** `-fkauto encounter` capture run.

---

## 5. Prototype scope

**Built now:**
- all 10 lures and the action system, on every stage;
- the complete encounter framework;
- one `EncounterDef` (the coelacanth), one model (`legend_coelacanth`) and one backdrop set (`uw_cave_*`).

**Not built now:** the other five legends keep `encounter = null`. Their current path (spawned agent, `golden:` preferences, 황금 떡밥 `rareBoost`) stays exactly as it is.

**Adding a legend later** takes three things:
- one `EncounterDef` row;
- `hyb_legend3d.py <fish_id>` (a model plus a palette);
- a `uw_<backdrop>_*` set from `hyb_encounter.py`.

The planned data for those rows (not implemented):

| Legend | Stage / backdrop | Key lures | 경계 / 호기심 / 흥분 verbs (suggested) |
|---|---|---|---|
| 황금잉어 golden_carp | lake / `lake` | spinner 1.0, softworm 0.5 | Slow wind / short wind runs with pauses / hold |
| 피라루쿠 arapaima | swamp / `swamp` | frog 1.0, popper 0.8 | Slow wind / flick–pause (surface) / hold |
| 철갑상어 sturgeon | ice / `ice` | softworm 1.0, jig 0.5 | Slow drag / light flick–pause on the bottom / hold |
| 청새치 blue_marlin | ocean / `ocean` | kona 1.0, jig 0.5 | Fast wind in band / wind bursts / hold |
| 백상아리 great_white | ocean / `ocean` (shared) | kona 1.0, jig 0.6 | Fast wind / flick–pause / hold; `minLine` 100 (titanium) |

**Defaults chosen here, so implementation does not have to wait on answers:**
1. For converted legends, 황금 떡밥 does nothing. It still summons the unconverted ones.
2. The lurk cue (eye glint and bubbles) exists only on stages whose legend is converted. For now that is the cave only.

**Out of scope:**
- colour variants of lures;
- circular iris masks;
- new underwater sets for other stages;
- any change to the fight model beyond `Hold()`;
- extra QA or polish passes beyond the single capture set in 4.5.
