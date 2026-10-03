# 데이터 표 (스테이지 · 어종 · 장비 · 수족관 · 세이브)

- **이 문서가 다루는 것**: 정적 게임 데이터 전체(어종·스테이지는 데이터 파일, 나머지는 코드; 스테이지 7, 어종 42, 낚싯대·릴·줄 각 6, 미끼 8·루어 10, 수조 5, 사료 4, 장식 17, 청소 도구 3, 장식 슬롯 10)와 각 열의 단위·의미, `SaveData`의 모든 필드와 버전·마이그레이션 동작
- **관련 코드**: `Assets/Resources/Data/Fish/<id>.json`, `Assets/Resources/Data/stages.json`, `Assets/Scripts/Data/SpeciesData.cs`, `Assets/Scripts/Data/SpeciesCheck.cs`, `Assets/Scripts/Data/LegendEncounters.cs`, `Assets/Scripts/Data/GameDatabase.cs`, `Assets/Scripts/Data/Models.cs`, `Assets/Scripts/Data/TimeActivity.cs`, `Assets/Scripts/Core/SaveData.cs`, `Assets/Scripts/Core/Game.cs`, `Assets/Scripts/Core/AquaCare.cs`, `Assets/Scripts/Core/AquaTank.cs` (보조: `Assets/Resources/Data/stage_*.json`, `Assets/Resources/Data/aquarium_tank_*.json`, `Assets/Scripts/Fishing/CurrentField.cs`, `Assets/Scripts/Fishing/FishSpawner.cs`, `Assets/Scripts/Core/GameClock.cs`, `Assets/Scripts/Core/ViewZoom.cs`)
- **관련 문서**: [README](../README.md) · [구조](architecture.md) · [낚시 규칙과 상수](fishing_gameplay.md) · [수족관 규칙과 공식](aquarium.md) · [아트 파이프라인](art_pipeline.md) · [테스트 스위치](testing.md) · [루어·전설어 사양](lures_legend_spec.md) · [전설어 확장](legends_rollout.md) · [시간대·물살](time_currents_spec.md) · [장애물](obstacles_spec.md) · [변경 기록](../CHANGELOG.md)

이 문서는 **값의 목록**입니다. 값이 게임에서 어떻게 쓰이는지(파이트 공식, 입질 확률, 수족관 수입 계산 등)는
[fishing_gameplay.md](fishing_gameplay.md)와 [aquarium.md](aquarium.md)를 보세요. 모든 값은 데이터 파일과 코드에서 그대로
옮겼고, "(계산)"이라고 적은 열만 코드의 공식으로 계산한 값입니다. **어종을 더하는 방법은 2.7절**입니다.

## 0. 공통 규칙

- **어종과 스테이지는 데이터 파일입니다.** 어종마다 파일 하나 `Assets/Resources/Data/Fish/<id>.json`(2절), 스테이지 목록과
  스테이지마다 나오는 어종·가중치는 `Assets/Resources/Data/stages.json`(1절)입니다. 장비·미끼·수조(`BuildItems`)와 조우
  배경(`BuildSets`), 전설어의 조우 행(`LegendEncounters.cs`, 2.5절)은 코드에 남아 있습니다.
- 데이터는 모두 `GameDatabase`의 정적 생성자에서 한 번 만들어집니다: `SpeciesData.Build`가 `stages.json`과 `Data/Fish`의
  파일을 읽고 → `BuildItems()` → `BuildSets()` → `Install`(어종·스테이지 목록과 id 사전 `fishById`, `stageById`) → `itemById`
  순서입니다. 조회는 `GameDatabase.GetFish`, `GetItem<T>`, `GetStage`입니다. 읽지 못한 것은 `GameDatabase.LoadErrors`에 남고
  `[DATA]` 오류 로그로 한 번씩 찍힙니다(파일이 망가진 어종은 빠지고, 그때 `SaveSystem.Sanitize`는 그 어종의 수족관 물고기를
  지우지 않고 둡니다: `[DATA] aquarium kept: N fish of unloaded species`). 에디터에서 JSON을 고치면 다음 Play부터 반영됩니다
  (도메인 리로드가 켜져 있음).
- 데이터 파일 형식: UTF-8(BOM 없음), JSON(주석 없음 — 설명은 `note` 키). 숫자는 쓴 그대로 읽습니다(`1.6`은 예전 C# `1.6f`와
  비트 단위로 같은 float). `JsonUtility`로 다시 저장하지 마세요(숫자 표기가 바뀜).
- 게임 공간 단위는 미터입니다(`StageLayout` 주석: "Game space is metres"). 모델 주석의 "units"도 미터와 같습니다
  (`ReelDef.lineCap` 주석: "units / m").
- 힘은 kgf, 시간은 초(s), 감기 속도는 초당 핸들 회전수(rev/s)입니다.
- 처음 시작할 때의 장비는 `GameDatabase.StarterRod` = `rod_bamboo`, `StarterReel` = `reel_basic`,
  `StarterLine` = `line_nylon2`, `StarterBait` = `bait_paste`입니다.

### 0.1 희귀도 (`Models.cs` `Rarity`, `RarityInfo`)

| enum | 이름 (`RarityInfo.Names`) | 별 (`Stars` = 순번 + 1) | 기본 XP (`RarityInfo.Xp`) | 수족관 가중치 (`AquaCare.RarityW`) | 출현 기본 가중치 (`RarityInfo.SpawnBase`) |
|---|---|---|---|---|---|
| `Common` | 일반 | 1 | 10 | 1 | 40 |
| `Uncommon` | 고급 | 2 | 25 | 1.3 | 16 |
| `Rare` | 희귀 | 3 | 60 | 1.7 | 3.5 |
| `Epic` | 영웅 | 4 | 150 | 2.2 | 1.5 |
| `Legendary` | 전설 | 5 | 400 | 3 | 1.2 |

출현 기본 가중치는 생성 지형이 있는 스테이지(지금은 호수)에서 `stages.json`에 `weight`를 적지 않은 어종의 가중치입니다
(1.2절: 스테이지가 이것에서 실제 가중치를 끌어냅니다).

잡았을 때 받는 XP는 `Game.RegisterCatch`에서 `round(BaseXp × (0.8 + 0.6 × SizeT(cm)) × (처음 잡은 종이면 2))`입니다.
레벨업에 필요한 XP는 `Game.XpToNext(level) = 60 + level² × 35`이고, 레벨이 오를 때마다 `새 레벨 × 100` 코인을 줍니다
(`Game.AddXp`).

## 1. 스테이지

### 1.1 기본 데이터 (`Assets/Resources/Data/stages.json`, 필드는 `Models.cs` `StageDef`)

`stages.json`은 `{"stages": [...]}`이고 배열 순서가 지도·도감의 스테이지 순서입니다. 스테이지마다 키는 `id`, `name`,
`subtitle`, 아래 표의 필드 이름 그대로의 키, 그리고 `fish`(1.2절)입니다. 모두 필수이고, 모르는 키는 검사기 오류(2.7절)입니다.
스테이지 그림·레이아웃 `stage_<id>.json`은 Blender가 통째로 다시 쓰는 파일이라 여기에 어종을 적지 않습니다.

| 열 | 필드 | 의미 |
|---|---|---|
| 난이도 | `difficulty` | 별 1~5 (지도·HUD의 별 개수) |
| 요구 레벨 | `reqLevel` | 해금하려면 플레이어 레벨이 이 값 이상 (`Game.Unlock`) |
| 해금 가격 | `unlockCost` | 해금할 때 내는 코인 (`Game.Unlock`) |
| 힘 배율 | `powerMult` | 파이트의 물고기 힘에 곱함 (`FightModel` 생성자에 전달) |
| 입질 배율 | `biteMult` | 입질 확률에 곱함 (`FishingController`) |
| 개체 수 | `population` | 동시에 떠 있는 물고기 수 (`FishSpawner`) |

| id | 이름 | 부제 (`subtitle`) | 난이도 | 요구 레벨 | 해금 가격 | 힘 배율 | 입질 배율 | 개체 수 |
|---|---|---|---|---|---|---|---|---|
| `lake` | 고요한 호수 | 낚시를 처음 배우기 좋은 잔잔한 호수 | 1 | 1 | 0 | 1.0 | 1.1 | 15 |
| `stream` | 산골 계곡 | 차갑고 맑은 물이 흐르는 계곡 | 2 | 2 | 1200 | 1.0 | 1.0 | 8 |
| `sea` | 바다 방파제 | 파도가 부서지는 테트라포드 방파제 | 3 | 4 | 6000 | 1.05 | 1.0 | 9 |
| `swamp` | 안개 늪지 | 안개 속에 괴어가 숨어 있는 늪 | 3 | 6 | 15000 | 1.1 | 0.95 | 8 |
| `ice` | 얼음 호수 | 얼음 구멍 아래로 미끼를 내리는 빙어낚시 | 4 | 8 | 35000 | 1.15 | 0.95 | 8 |
| `ocean` | 먼바다 | 배를 타고 나가는 대물 트롤링 | 5 | 11 | 80000 | 1.2 | 0.9 | 7 |
| `cave` | 수정 동굴 | 빛나는 수정 아래 고대어가 잠든 지하 호수 | 5 | 14 | 200000 | 1.25 | 0.9 | 7 |

`lake`는 새 세이브에서 이미 해금되어 있고(`SaveData.unlockedStages` 기본값), `SaveSystem.Sanitize`도 항상 넣어 둡니다.
호수의 개체 수 15는 예전 8보다 많지만, 생성 지형에서는 물고기가 미끼 근처에 올 때마다 "먹이를 먹는 중인지"를 한 번 굴립니다
(먹이 확률 F, [fishing_gameplay.md](fishing_gameplay.md) 11.1). F는 호수의 분당 입질·수입이 예전 호수와 같은 범위에
들도록 지형마다 계산됩니다([lake_phase2_spec.md](lake_phase2_spec.md) A7).

### 1.2 출현 어종과 가중치 (`stages.json`의 `fish` → `StageDef.spawns`)

스테이지의 `fish`가 **그 스테이지에 나오는 어종 목록**입니다: `[{"id": "pale_chub", "weight": 40}, ...]`. 순서가
`StageDef.spawns`의 순서이고(`FishSpawner.Pick`의 뽑기·도감·지도 칸 순서), 스테이지를 차례로 훑어 처음 나오는 순서가
`GameDatabase.Fish`의 순서입니다. 생성 지형이 없는 스테이지는 모든 항목에 `weight`가 필수입니다(전설어의 값도 그대로 두지만
`Pick`은 읽지 않음).

가중치는 상대값입니다. `FishSpawner.Pick`은 가중치에 시간대 활동도(`TimeActivity.A`, 2.4절)를 곱하고, 희귀 이상은
낚싯대 `luck`, 영웅 이상은 미끼 `rareBoost`를 한 번 더(전설은 한 번 더) 곱합니다. **조우(`encounter`)가 있는 전설 6종은
가중치와 상관없이 일반 물고기로 나오지 않습니다**(`FishSpawner.Pick`: `if (sp.encounter != null) continue;`).

**생성 지형이 있는 스테이지(호수)는 가중치를 지형에서 끌어냅니다**([lake_phase2_spec.md](lake_phase2_spec.md) A4,
`HabitatModel.DerivedWeights`, `StageDef.Derived`). `weight`는 적지 않아도 되고, 적지 않으면 희귀도의 기본 가중치
(0.1절: 일반 40, 고급 16, 희귀 3.5, 영웅 1.5, 전설 1.2)가 바탕(`base`)입니다. 적으면 그 값이 희귀도의 기본값 대신 바탕이
됩니다(덮어쓰기, `StageDef.GivenWeight`). 실제 가중치는 지형마다 시간대마다

- 그 호수에 그 어종이 좋아하는 바닥이 얼마나 있는지: R_ref(z 2.7..50, 화면 폭 안, 줄마다 같은 무게) 위에서 서식지 계수
  `h`의 평균을 그 어종이 받을 수 있는 최대 `h*`로 나눈 `q`(몸 규칙 포함),
- 스톡 전체의 평균 `q̄ = Σ base·a·q / Σ base·a`,
- 가용도 `A = clamp(q / q̄, 0.6, 1.4)`,
- `W = base × A × a`(a는 그 시간대의 활동도)

이고, `FishSpawner.Pick`이 `base × lerp(A(전), A(후), 시계의 섞임) × 활동도`로 뽑습니다(`FishHabitat.SpawnWeight`). 덮어쓴
가중치도 A와 활동도는 그대로 곱해집니다. 지형이 없는 스테이지와 `-fkbathy off`, `-fkecomode legacy`는 예전처럼 `weight × 활동도`입니다.

| 스테이지 | 어종 id (가중치) |
|---|---|
| `lake` | `crucian_carp` (40) · `bluegill` (40) · `white_crucian` (40) · `three_lips` (40) · `carp` (16) · `largemouth_bass` (16) · `barbel_steed` (16) · `yellow_catfish` (16) · `freshwater_eel` (3.5) · `redfin_culter` (1.5) · `golden_carp` (1.2, 조우) — 괄호는 희귀도의 기본값, 실제는 지형에서 (위) |
| `stream` | `pale_chub` 40 · `cherry_salmon` 30 · `rainbow_trout` 18 · `mandarin_fish` 8 · `lenok` 3 |
| `sea` | `horse_mackerel` 35 · `mackerel` 30 · `rockfish` 20 · `flounder` 14 · `black_porgy` 8 · `red_seabream` 3 |
| `swamp` | `piranha` 35 · `catfish` 28 · `snakehead` 20 · `arowana` 7 · `arapaima` 1.5 (조우) |
| `ice` | `smelt` 45 · `burbot` 22 · `northern_pike` 18 · `arctic_char` 9 · `sturgeon` 1.5 (조우) |
| `ocean` | `yellowtail` 34 · `mahi_mahi` 26 · `bluefin_tuna` 14 · `ocean_sunfish` 7 · `blue_marlin` 2.5 (조우) · `great_white` 1.2 (조우) |
| `cave` | `cave_tetra` 40 · `crystal_koi` 12 · `anglerfish` 10 · `coelacanth` 2 (조우) |

합계 11 + 5 + 6 + 5 + 5 + 6 + 4 = **42종** (README의 "어종 42종"과 같음; 호수 11 = 일반 어종 10 + 전설 1). 한 어종은 한 스테이지에만 나옵니다
(`GameDatabase.StageOfFish`는 처음 찾은 스테이지를 돌려줌; 검사기가 두 스테이지에 적힌 어종을 오류로 봅니다).

### 1.3 스테이지 특성

`StageDef`에는 얼음·보트·물살 같은 특성 필드가 없습니다. 특성은 Blender가 내보내는 레이아웃
`Assets/Resources/Data/stage_<id>.json`(`StageLayout`: `mode`, `ambient`, `standH`, `zNear`/`zFar`/`xLim`, `depthZ`/`depthV`,
`holeX`/`holeZ`/`holeR`)과 스테이지 id로 분기하는 코드에서 정해집니다.

| id | `mode` | `ambient` | 발판 높이 `standH` (m) | 낚시 가능 z (m) | `xLim` (m) | 수심 `depthZ`→`depthV` (m) | 물 (`CurrentField` `Kind` / `Ref` m/s) |
|---|---|---|---|---|---|---|---|
| `lake` | shore | day | 1.0 | 1.2–176 | 90 | 0→1.2, 8→3, 20→6, 40→8, 140→6, 176→1 | `Lake` / 0.14 (잔잔, 가끔 돌풍) |
| `stream` | shore | day | 1.4 | 1.8–118 | 7.5 | 0→1.0, 10→2.5, 30→4.2, 60→4.8, 118→3 | `Stream` / 0.55 (한 방향 물살) |
| `sea` | shore | day | 3.0 | 3.6–600 | 90 | 0→3, 10→6, 30→10, 60→12 | `Sea` / 0.8 (물때에 따른 조류) |
| `swamp` | shore | mist | 0.9 | 1.0–58 | 40 | 0→1.0, 8→2.5, 20→4.5, 40→5.5, 58→2 | `Swamp` / 0.06 (잔잔, 가끔 돌풍) |
| `ice` | **ice** | snow | 0.25 | 0.8–85 | 35 | 0→4, 10→6, 30→8, 85→4 | `None` (물살 없음) |
| `ocean` | **boat** | day | 1.7 | 1.6–900 | 120 | 0→18, 100→20 | `Ocean` / 0.25 (보트가 흘러감) |
| `cave` | shore | cave | 1.2 | 1.6–100 | 14 | 0→2, 10→5, 30→8, 100→9 | `None` (물살 없음) |

- **얼음(`ice`)**: `StageLayout.IsIce`(`mode == "ice"`). 구멍 위치 `holeX` 1.9, `holeZ` 3.0, 반지름 `holeR` 0.62
  (`stage_ice.json`). 얼음 구멍에서는 자연 미끼와 바닥·수직 루어만 쓸 수 있습니다(`LureInfo.IceOk`, 문구
  `LureInfo.IceBanned` = "얼음 구멍에서는 쓸 수 없어요"). 스윕·사이드 프레셔도 꺼집니다(`FishingController`의 `L.IsIce` 분기).
- **보트(`ocean`)**: `mode` = boat. 너울에 갑판이 흔들립니다(`StageView.DeckBob`, `WaterFx`).
- **물때(`sea`)**: 바다만 물때로 수심이 바뀝니다(`StageLayout.TideOffset` 주석: 물때 수위 × 0.4 m, 다른 스테이지는 0).
  물때 일정은 [time_currents_spec.md](time_currents_spec.md) 7절.
- **호수 바닥(`lake`)**: 호수의 수심은 세이브의 세계 시드로 실행 중에 만드는 **생성 지형**입니다
  ([terrain_depth_spec.md](terrain_depth_spec.md), [lake_phase2_spec.md](lake_phase2_spec.md) A1). 시드마다 호수의 성격
  (얕은·깊은, 수초가 많은·돌이 많은)을 뽑아 바탕을 자유롭게 만들고(물가 1.2 m에서 턱, 경사를 지나 주 수심 3.1–6.7 m), 그 위에
  얕은 턱·연잎과 갈대 밑 수초 평지(수초가 많은 호수는 0–3개 더)·수초 둔덕·잔교 앞 깊은 골·옛 물골·깊은 웅덩이 1–2·수중 둔덕
  1–4가 얹힙니다. 0.5 m 격자(x −48..48, z 0..64, 그림의 오버스캔까지), 칸마다 수심(cm)·바닥 종류(`BedKind`)·바닥 재질
  (`BedMat`: 진흙·모래·자갈·수초)·구역 이름. 고정된 그림과 맞추는 핀: 연잎 1.0–2.0 m, 연잎 줄기 0.8–2.2, 갈대 0.35–1.2,
  보트 1.2–2.5, 수초밭 2.4–3.0, 가라앉은 통나무 6.225 ± 0.1, 잔교 앞(x −3.5..3.5, z 0..3.5)은 위 표의 프로필 그대로,
  깊은 골 ≥ 4.5. 전체 0.35–9.0 m, 경사 1.5 m/m 이하. 호수의 거리 프로필(`ProfileDepth`)은 위 표가 아니라 만든 바닥의 줄마다
  가운데값(P\*)이고, 격자 밖은 P\*입니다. 위 표의 프로필은 `AuthoredDepth`로 남아(잔교 핀, 경제의 "예전 호수") 쓰입니다. 다른
  스테이지는 위 표의 프로필 그대로이고(이전과 비트 단위로 같음), 물때 보정(`TideOffset`)은 계속 `DepthAt(x, z)` 안에서
  더해집니다. `-fkbathy off`면 호수도 예전 프로필입니다.
- **야광**: 야광 미끼(`glow`)는 동굴이나 얼음에서 감지 거리가 늘어납니다(`FishingController`:
  `if (bait.glow && (Stage.Def.id == "cave" || L.IsIce)) sense += 2f;`).
- **장애물**: 7개 스테이지 모두 `Assets/Resources/Data/obstacles_<id>.json`이 있습니다. 형식은
  [obstacles_spec.md](obstacles_spec.md) 2절.
- **전설어 조우 배경**(`EncounterDef.backdrop` → `GameDatabase.GetSet`): 호수 `lake`, 늪 `swamp`, 얼음 `ice`, 먼바다 `ocean`
  (청새치·백상아리 공유), 동굴 `cave`. 계곡과 방파제에는 전설어가 없습니다.

## 2. 어종 (`Assets/Resources/Data/Fish/<id>.json`, 필드는 `Models.cs` `FishSpecies`)

### 2.1 열 설명과 파일 키

어종 파일 하나가 그 어종의 모든 것입니다. 파일 이름 = `id`(소문자·숫자·`_`, 세이브 키와 서식지 시드에 쓰이므로 바꾸지 않음).
키는 아래 표의 필드 이름과 같고, 묶음 키 넷만 다릅니다:

| 파일 키 | 필수 | 들어가는 필드 |
|---|---|---|
| `id`, `name`, `desc`, `rarity` | 필수 | `rarity`는 `"common"` · `"uncommon"` · `"rare"` · `"epic"` · `"legendary"` |
| `minCm`, `maxCm`, `weightK`, `basePrice`(정수), `power`, `stamina`, `speed`, `aggression`, `jump`, `depthMin`, `depthMax` | 필수 | 같은 이름의 필드 |
| `jumpStyle` | `jump` > 0이면 필수 | `"hopper"` · `"shaker"` · `"tailWalker"` (안 적으면 `Hopper`) |
| `baits` | 필수 | 문자열 `"worm:1,minnow:0.8,@twitch:0.8"` → `baitPrefs` / `actionPrefs` (문자열 순서대로) |
| `activity` | 필수 | `{ "dawn", "day", "evening", "night" }` 넷 다 → `activity[4]` (`TimeActivity`, 2.4절) |
| `cover` | 필수 | `{ "types": [] }` = 커버를 안 찾음. 종류가 있으면 `seek`, `reach`, `dig`도 필수 → `coverSeek` / `coverReach` / `coverDig` / `coverFor` |
| `diet` | 필수 | `{ "foods": ["pellet" \| "shrimp" \| "sardine", …1~2개], "style": "grab" \| "bottom" \| "surge" }` → `diet` / `feedStyle` |
| `habitat` | 생성 지형 스테이지(호수)의 일반 어종은 필수 | 문자열 → `HabitatDef` (2.6절); 없으면 null |
| `pocketHold` | 선택 (기본 0) | 계곡 물살 뒤 웅덩이에 머무는 확률 0..1 ([time_currents_spec.md](time_currents_spec.md) 9.6) |
| `encounter` | 전설어만 | `id`와 같은 값: `LegendEncounters`의 조우 행 (2.5절) |
| `note` | 선택 | 설명 (`cover`, `diet` 안에도 둘 수 있음). 게임은 읽지 않음 |
| `art` | 선택 (예약) | 나중에 Blender 모델 값을 옮길 자리. 게임과 검사기 모두 무시 |

| 열 | 필드 | 단위·의미 (출처: `FishSpecies` 주석과 메서드) |
|---|---|---|
| 크기 | `minCm`, `maxCm` | 잡히는 길이 범위 (cm) |
| k | `weightK` | 무게 계수: `WeightKg(cm) = k × cm³ / 100000` (kg) |
| 무게 (계산) | — | 최소·최대 길이에서의 `WeightKg`, `Game.MakeCatch`처럼 소수 둘째 자리 반올림 |
| 기준 가격 | `basePrice` | "price at average size". 실제 가격은 `Price(cm) = max(1, round(basePrice × (0.6 + 0.8t² + 0.1t)))`, `t = SizeT(cm) = InverseLerp(minCm, maxCm, cm)` → 최소 크기 0.6배, 최대 크기 1.5배 |
| 가격 (계산) | — | 최소·최대 길이에서의 `Price` (float 연산 기준, `Mathf.RoundToInt`) |
| 수조 등급 (계산) | — | 최소·최대 길이의 `AquaTank.ClassOf`: 소형 <40cm, 중형 40–100, 대형 100–200, 초대형 200+ (4.2절) |
| 힘 | `power` | 최대 크기에서 당기는 힘 (kgf) |
| 체력 | `stamina` | 세게 싸우는 시간 (s) |
| 속도 | `speed` | 달릴 때 헤엄 속도 (m/s) |
| 공격성 | `aggression` | 0..1, 달리려는 경향 |
| 점프 | `jump` | 0..1, 수면 가까이 달릴 때 점프할 확률 |
| 점프 스타일 | `jumpStyle` | `Hopper` 짧게 도약 · `Shaker` 공중에서 머리 털기 · `TailWalker` 꼬리로 수면 걷기 (`JumpStyle` 주석). 점프하지 않는 종(`jump` 0)은 적지 않고 기본값 `Hopper` |
| 수심 | `depthMin`, `depthMax` | 선호 수심, 수면 아래 m |
| 커버 | `coverSeek` / `coverReach` / `coverDig` · `coverFor` | 달릴 때 커버로 향하는 빈도 (0 = 안 감) / 커버를 찾는 거리 m (0 = 얼음 구멍 가장자리) / 파고드는 세기 / 쓰는 커버 종류. `types`가 빈 종은 `coverSeek` 0, `coverFor` null ([obstacles_spec.md](obstacles_spec.md) 7절). 종류는 그 스테이지의 커버 구역(`obstacles_<id>.json`의 `kind` "cover"의 `coverFor`, 얼음은 `rim`)에 있는 것만 씀 |
| 조우 | `encounter` | 전설어 물속 조우 데이터 (null = 일반 출현·입질) |
| 선호 | `baitPrefs`, `actionPrefs` | 문자열 `"worm:1,@twitch:0.8"`: `bait_` 접두사를 뺀 미끼 id와 선호도(0..1), `@` 뒤는 루어 액션 선호도 (`SpeciesData.ParsePrefs`, 예전 `GameDatabase.F`의 반복문 그대로) |
| 먹이 | `diet` | 수족관에서 먹는 먹이 (`Diet` 플래그: 사료 · 생새우 · 정어리), 기본값 `Pellet` |
| 받아먹기 | `feedStyle` | 떨어뜨린 새우·정어리를 먹는 방식: `Grab` 물 중간에서 · `Bottom` 자갈 위에서 · `Surge` 수면으로 솟구쳐 한입에 |
| 활동도 | `activity` | 새벽 · 낮 · 저녁 · 밤의 활동도 (`Period` 순서, 2.4절) |
| 웅덩이 머물기 | `pocketHold` | 계곡에서 떠돌 목표가 바위 뒤 웅덩이 안에 잡힐 확률 (0 = 안 머묾; 쏘가리 0.8, 열목어·무지개송어 0.5, 산천어 0.4, 피라미 0.1) |

미끼 선호의 실제 매력도는 `FishSpecies.Appeal(b)`입니다: 자연 미끼는 `Pref(id)`, 루어는
`max(Pref(id), 0.7 × actionPrefs[b.action])`.

### 2.2 크기 · 무게 · 가격

| id | 이름 | 스테이지 | 희귀도 | 크기 (cm) | k | 무게 kg (계산) | 기준 가격 | 가격 (계산) | 수조 등급 (계산) |
|---|---|---|---|---|---|---|---|---|---|
| `crucian_carp` | 붕어 | lake | 일반 | 12–35 | 1.6 | 0.03–0.69 | 35 | 21–53 | 소형~소형 |
| `bluegill` | 블루길 | lake | 일반 | 10–25 | 2.0 | 0.02–0.31 | 26 | 16–39 | 소형~소형 |
| `white_crucian` | 떡붕어 | lake | 일반 | 15–45 | 1.8 | 0.06–1.64 | 40 | 24–60 | 소형~중형 |
| `three_lips` | 끄리 | lake | 일반 | 15–38 | 1.0 | 0.03–0.55 | 30 | 18–45 | 소형~소형 |
| `carp` | 잉어 | lake | 고급 | 35–90 | 1.5 | 0.64–10.94 | 150 | 90–225 | 소형~중형 |
| `largemouth_bass` | 큰입배스 | lake | 고급 | 25–60 | 1.4 | 0.22–3.02 | 180 | 108–270 | 소형~중형 |
| `barbel_steed` | 누치 | lake | 고급 | 20–60 | 1.1 | 0.09–2.38 | 130 | 78–195 | 소형~중형 |
| `yellow_catfish` | 동자개 | lake | 고급 | 10–32 | 1.3 | 0.01–0.43 | 120 | 72–180 | 소형~소형 |
| `freshwater_eel` | 뱀장어 | lake | 희귀 | 40–100 | 0.2 | 0.13–2.00 | 400 | 240–600 | 중형~대형 |
| `redfin_culter` | 강준치 | lake | 영웅 | 45–100 | 0.7 | 0.64–7.00 | 900 | 540–1350 | 중형~대형 |
| `golden_carp` | 황금잉어 | lake | 전설 | 50–100 | 1.5 | 1.88–15.00 | 3000 | 1800–4500 | 중형~대형 |
| `pale_chub` | 피라미 | stream | 일반 | 8–18 | 1.0 | 0.01–0.06 | 15 | 9–23 | 소형~소형 |
| `cherry_salmon` | 산천어 | stream | 일반 | 15–35 | 1.1 | 0.04–0.47 | 45 | 27–68 | 소형~소형 |
| `rainbow_trout` | 무지개송어 | stream | 고급 | 25–65 | 1.2 | 0.19–3.30 | 160 | 96–240 | 소형~중형 |
| `mandarin_fish` | 쏘가리 | stream | 희귀 | 20–50 | 1.4 | 0.11–1.75 | 420 | 252–630 | 소형~중형 |
| `lenok` | 열목어 | stream | 영웅 | 30–70 | 1.1 | 0.30–3.77 | 950 | 570–1425 | 소형~중형 |
| `horse_mackerel` | 전갱이 | sea | 일반 | 15–40 | 1.0 | 0.03–0.64 | 35 | 21–53 | 소형~중형 |
| `mackerel` | 고등어 | sea | 일반 | 20–45 | 0.9 | 0.07–0.82 | 50 | 30–75 | 소형~중형 |
| `rockfish` | 우럭 | sea | 고급 | 20–55 | 1.7 | 0.14–2.83 | 140 | 84–210 | 소형~중형 |
| `flounder` | 광어 | sea | 고급 | 30–80 | 1.2 | 0.32–6.14 | 200 | 120–300 | 소형~중형 |
| `black_porgy` | 감성돔 | sea | 희귀 | 25–60 | 1.8 | 0.28–3.89 | 480 | 288–720 | 소형~중형 |
| `red_seabream` | 참돔 | sea | 영웅 | 35–100 | 1.5 | 0.64–15.00 | 1300 | 780–1950 | 소형~대형 |
| `piranha` | 피라냐 | swamp | 일반 | 15–35 | 2.2 | 0.07–0.94 | 60 | 36–90 | 소형~소형 |
| `catfish` | 메기 | swamp | 일반 | 30–100 | 1.0 | 0.27–10.00 | 110 | 66–165 | 소형~대형 |
| `snakehead` | 가물치 | swamp | 고급 | 40–90 | 1.1 | 0.70–8.02 | 300 | 180–450 | 중형~중형 |
| `arowana` | 아로와나 | swamp | 희귀 | 50–110 | 0.8 | 1.00–10.65 | 900 | 540–1350 | 중형~대형 |
| `arapaima` | 피라루쿠 | swamp | 전설 | 150–300 | 1.0 | 33.75–270.00 | 6000 | 3600–9000 | 대형~초대형 |
| `smelt` | 빙어 | ice | 일반 | 8–15 | 0.8 | 0.00–0.03 | 20 | 12–30 | 소형~소형 |
| `burbot` | 모캐 | ice | 고급 | 30–80 | 1.0 | 0.27–5.12 | 220 | 132–330 | 소형~중형 |
| `northern_pike` | 강꼬치고기 | ice | 고급 | 50–120 | 0.7 | 0.88–12.10 | 350 | 210–525 | 중형~대형 |
| `arctic_char` | 북극곤들매기 | ice | 희귀 | 40–80 | 1.1 | 0.70–5.63 | 800 | 480–1200 | 중형~중형 |
| `sturgeon` | 철갑상어 | ice | 전설 | 120–280 | 0.6 | 10.37–131.71 | 9000 | 5400–13500 | 대형~초대형 |
| `yellowtail` | 방어 | ocean | 일반 | 50–100 | 1.1 | 1.38–11.00 | 280 | 168–420 | 중형~대형 |
| `mahi_mahi` | 만새기 | ocean | 고급 | 60–150 | 0.7 | 1.51–23.62 | 520 | 312–780 | 중형~대형 |
| `bluefin_tuna` | 참다랑어 | ocean | 희귀 | 100–250 | 1.6 | 16.00–250.00 | 2600 | 1560–3900 | 대형~초대형 |
| `ocean_sunfish` | 개복치 | ocean | 영웅 | 100–250 | 3.0 | 30.00–468.75 | 4000 | 2400–6000 | 대형~초대형 |
| `blue_marlin` | 청새치 | ocean | 전설 | 200–400 | 0.8 | 64.00–512.00 | 15000 | 9000–22500 | 초대형~초대형 |
| `great_white` | 백상아리 | ocean | 전설 | 300–500 | 1.0 | 270.00–1250.00 | 30000 | 18000–45000 | 초대형~초대형 |
| `cave_tetra` | 장님동굴어 | cave | 일반 | 6–12 | 1.5 | 0.00–0.03 | 90 | 54–135 | 소형~소형 |
| `crystal_koi` | 수정비단잉어 | cave | 희귀 | 40–80 | 1.5 | 0.96–7.68 | 3200 | 1920–4800 | 중형~중형 |
| `anglerfish` | 초롱아귀 | cave | 영웅 | 40–100 | 2.5 | 1.60–25.00 | 4500 | 2700–6750 | 중형~대형 |
| `coelacanth` | 실러캔스 | cave | 전설 | 120–200 | 1.3 | 22.46–104.00 | 25000 | 15000–37500 | 대형~초대형 |

### 2.3 파이트 · 점프 · 수심 · 커버

커버 열은 각 어종 파일 `cover`의 `seek / reach / dig · types` 순서입니다. 우럭·감성돔의 `rock`과 가물치·피라루쿠의 `weed`는
데이터 파일로 옮길 때 뺐습니다: 방파제에는 rock 커버가, 늪에는 weed 커버 구역이 없어 한 번도 맞은 적이 없는 종류였습니다
(커버 짝짓기 `Obstacles.CoverMatch`는 그 스테이지의 커버 구역만 봄 — 게임 동작은 그대로).
점프 스타일: `Shaker`는 `largemouth_bass`, `redfin_culter`, `rainbow_trout`, `cherry_salmon`, `lenok`, `snakehead`, `arowana`,
`northern_pike`, `arctic_char`, `arapaima`, `TailWalker`는 `blue_marlin`, `mahi_mahi`, 나머지는 `Hopper`입니다.

| id | 힘 (kgf) | 체력 (s) | 속도 (m/s) | 공격성 | 점프 | 점프 스타일 | 수심 (m) | 커버 | 조우 |
|---|---|---|---|---|---|---|---|---|---|
| `crucian_carp` | 1.6 | 6 | 2.0 | 0.3 | 0 | Hopper | 2–7 | — | — |
| `bluegill` | 1.2 | 5 | 2.4 | 0.5 | 0 | Hopper | 1–5 | — | — |
| `white_crucian` | 2.0 | 7 | 2.2 | 0.3 | 0 | Hopper | 0.8–3 | 0.15 / 6 / 0.8 · weed, pad | — |
| `three_lips` | 1.6 | 6 | 3.6 | 0.65 | 0.3 | Hopper | 0.3–2 | — | — |
| `carp` | 5.0 | 14 | 2.6 | 0.45 | 0.05 | Hopper | 3–7 | 0.30 / 8 / 1.0 · weed, reed, pad | — |
| `largemouth_bass` | 3.8 | 10 | 3.4 | 0.7 | 0.35 | Shaker | 1–6 | 0.55 / 10 / 1.2 · post, pad, reed, weed, boat, log | — |
| `barbel_steed` | 3.6 | 12 | 3.0 | 0.5 | 0 | Hopper | 2–6 | — | — |
| `yellow_catfish` | 1.8 | 7 | 2.0 | 0.45 | 0 | Hopper | 3–8 | 0.35 / 6 / 1.1 · log, post, boat | — |
| `freshwater_eel` | 4.0 | 14 | 2.4 | 0.55 | 0 | Hopper | 4–9 | 0.70 / 8 / 1.6 · log, weed, boat | — |
| `redfin_culter` | 6.0 | 16 | 3.8 | 0.65 | 0.2 | Shaker | 1.5–5 | — | — |
| `golden_carp` | 7.0 | 20 | 3.0 | 0.5 | 0.1 | Hopper | 4–7 | 0.45 / 10 / 1.3 · weed, pad, reed | 조우 |
| `pale_chub` | 0.8 | 4 | 3.0 | 0.5 | 0.1 | Hopper | 0.5–3 | — | — |
| `cherry_salmon` | 1.8 | 7 | 3.2 | 0.55 | 0.2 | Shaker | 1–4 | — | — |
| `rainbow_trout` | 3.5 | 11 | 3.6 | 0.6 | 0.4 | Shaker | 1–5 | 0.20 / 8 / 0.8 · rock | — |
| `mandarin_fish` | 3.2 | 12 | 3.0 | 0.65 | 0.05 | Hopper | 2–5 | 0.70 / 8 / 1.4 · rock | — |
| `lenok` | 4.5 | 16 | 3.8 | 0.6 | 0.3 | Shaker | 2–5 | 0.25 / 8 / 1.0 · rock | — |
| `horse_mackerel` | 1.8 | 7 | 3.5 | 0.6 | 0 | Hopper | 1–5 | — | — |
| `mackerel` | 2.4 | 8 | 4.0 | 0.7 | 0 | Hopper | 1–4 | — | — |
| `rockfish` | 3.0 | 9 | 2.2 | 0.4 | 0 | Hopper | 4–7 | 0.75 / 6 / 1.5 · tet | — |
| `flounder` | 3.8 | 12 | 2.4 | 0.45 | 0 | Hopper | 5.5–7.5 | — | — |
| `black_porgy` | 4.5 | 14 | 3.0 | 0.55 | 0 | Hopper | 3–7 | 0.55 / 8 / 1.2 · tet | — |
| `red_seabream` | 8.0 | 20 | 3.6 | 0.6 | 0 | Hopper | 4–7 | 0.20 / 10 / 1.0 · tet | — |
| `piranha` | 2.2 | 6 | 3.8 | 0.8 | 0.05 | Hopper | 1–5 | — | — |
| `catfish` | 5.0 | 13 | 2.4 | 0.4 | 0 | Hopper | 3.5–6 | 0.45 / 8 / 1.1 · root, log | — |
| `snakehead` | 7.0 | 15 | 3.2 | 0.7 | 0.2 | Shaker | 1–5 | 0.65 / 10 / 1.4 · pad, reed, root, log | — |
| `arowana` | 7.5 | 16 | 4.0 | 0.6 | 0.6 | Shaker | 0.5–3 | 0.20 / 8 / 0.8 · root | — |
| `arapaima` | 24 | 35 | 3.2 | 0.6 | 0.3 | Shaker | 2–5.5 | 0.60 / 14 / 1.6 · root, log | 조우 |
| `smelt` | 0.6 | 3 | 3.0 | 0.5 | 0 | Hopper | 1–6 | — | — |
| `burbot` | 4.0 | 12 | 2.2 | 0.4 | 0 | Hopper | 5–7.5 | 0.30 / 6 / 1.0 · rock | — |
| `northern_pike` | 8.0 | 14 | 4.2 | 0.75 | 0.1 | Shaker | 1–5 | 0.55 / 0 / 1.2 · rim | — |
| `arctic_char` | 7.0 | 16 | 3.8 | 0.6 | 0.1 | Shaker | 2–6 | 0.20 / 0 / 1.0 · rim | — |
| `sturgeon` | 30 | 40 | 2.6 | 0.5 | 0.05 | Hopper | 5.5–7.5 | 0.40 / 0 / 1.5 · rim | 조우 |
| `yellowtail` | 9.0 | 16 | 4.5 | 0.7 | 0 | Hopper | 1–6 | 0.40 / 12 / 1.1 · hull, rock | — |
| `mahi_mahi` | 12 | 18 | 5.0 | 0.7 | 0.6 | TailWalker | 0.5–3 | 0.45 / 12 / 1.0 · weed | — |
| `bluefin_tuna` | 28 | 30 | 5.5 | 0.8 | 0.05 | Hopper | 3–8 | 0.35 / 14 / 1.3 · hull, weed | — |
| `ocean_sunfish` | 16 | 25 | 1.6 | 0.2 | 0 | Hopper | 2–7 | — | — |
| `blue_marlin` | 45 | 45 | 6.0 | 0.8 | 0.7 | TailWalker | 1–6 | — | 조우 |
| `great_white` | 80 | 60 | 5.0 | 0.85 | 0.15 | Hopper | 3–8 | 0.45 / 16 / 1.6 · hull | 조우 |
| `cave_tetra` | 0.8 | 4 | 3.0 | 0.5 | 0 | Hopper | 1–6 | — | — |
| `crystal_koi` | 9 | 18 | 3.2 | 0.5 | 0.1 | Hopper | 2–6 | 0.35 / 10 / 1.0 · crystal | — |
| `anglerfish` | 12 | 20 | 2.4 | 0.6 | 0 | Hopper | 5–7.5 | 0.40 / 8 / 1.1 · rock | — |
| `coelacanth` | 35 | 40 | 2.8 | 0.55 | 0 | Hopper | 5–7.5 | 0.60 / 14 / 1.5 · crystal, rock | 조우 |

### 2.4 선호 미끼 · 활동 시간대 · 수족관 먹이

- **선호 문자열**은 어종 파일의 `baits` 원문 그대로입니다(`bait_` 접두사 생략, `@` = 루어 액션:
  `steady` 감기 · `twitch` 저킹 · `topwater` 수면 · `bottom` 바닥 · `vertical` 수직).
- **활동도**는 어종 파일 `activity`의 새벽 / 낮 / 저녁 / 밤 값입니다(`TimeActivity.A`가 `FishSpecies.activity`를 읽음). 출현
  가중치에 곱하고, 입질에는 `sqrt(a)`로 쓰며, 전설어는 조우 게이지 채우는 속도에 곱합니다(`TimeActivity` 클래스 주석). 모르는
  id는 모든 시간대 1입니다.
  시간대 경계는 `GameClock.PeriodAt`: 새벽 05:00–08:00, 낮 08:00–17:00, 저녁 17:00–20:00, 밤 20:00–05:00
  (게임 분 300 / 480 / 1020 / 1200).
- **도감 표기 (계산)**은 `TimeActivity.Describe`의 규칙을 적용한 결과입니다: 밤에만 나오면 "밤에만", a ≥ 1.3인 시간대가
  있으면 그 시간대, 모두 0.8~1.2면 "하루 종일", 아니면 가장 높은 시간대.
- **먹이·받아먹기**는 어종 파일의 `diet`(`foods`, `style`)입니다. 필수라 빠진 종은 검사기 오류이고 게임에 들어가지 않습니다.

| id | 이름 | 선호 문자열 | 활동도 (새벽 / 낮 / 저녁 / 밤) | 도감 표기 (계산) | 먹이 | 받아먹기 |
|---|---|---|---|---|---|---|
| `crucian_carp` | 붕어 | `paste:1,corn:0.8,worm:0.7,golden:0.3` | 1.3 / 0.8 / 1.3 / 0.9 | 활동: 새벽 · 저녁 | 사료 | Grab |
| `bluegill` | 블루길 | `worm:1,paste:0.5,shrimp:0.6,spinner:0.6,spoon:0.3,golden:0.3` | 1.0 / 1.3 / 1.0 / 0.4 | 활동: 낮 | 사료 + 생새우 | Grab |
| `white_crucian` | 떡붕어 | `paste:1,corn:0.6,worm:0.4,golden:0.3` | 1.2 / 1.2 / 1.3 / 0.5 | 활동: 저녁 | 사료 | Grab |
| `three_lips` | 끄리 | `spinner:1,minnow:0.9,spoon:0.8,popper:0.5,crank:0.5,worm:0.5,golden:0.3,@steady:0.8,@twitch:0.7,@topwater:0.5` | 1.5 / 1.0 / 1.4 / 0.3 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Grab |
| `carp` | 잉어 | `corn:1,paste:0.9,worm:0.4,golden:0.4` | 1.2 / 0.7 / 1.2 / 1.4 | 활동: 밤 | 사료 + 생새우 | Bottom |
| `largemouth_bass` | 큰입배스 | `minnow:1,crank:1,softworm:1,frog:0.9,popper:0.9,spinner:0.6,spoon:0.6,worm:0.4,golden:0.4,@twitch:0.8,@topwater:0.8,@bottom:0.8,@steady:0.7` | 1.5 / 0.6 / 1.5 / 0.7 | 활동: 새벽 · 저녁 | 생새우 | Grab |
| `barbel_steed` | 누치 | `worm:1,corn:0.5,shrimp:0.5,paste:0.4,softworm:0.4,golden:0.4,@bottom:0.5` | 1.2 / 1.1 / 1.2 / 0.8 | 하루 종일 | 사료 + 생새우 | Bottom |
| `yellow_catfish` | 동자개 | `worm:1,shrimp:0.9,glow:0.5,softworm:0.3,golden:0.4,@bottom:0.4` | 0.2 / 0 / 0.6 / 1.9 | 활동: 밤 | 생새우 | Bottom |
| `freshwater_eel` | 뱀장어 | `worm:1,shrimp:0.8,glow:0.6,golden:0.5` | 0.2 / 0 / 0.4 / 2.0 | 활동: 밤 | 생새우 | Bottom |
| `redfin_culter` | 강준치 | `minnow:1,spoon:0.9,spinner:0.7,crank:0.7,popper:0.5,jig:0.5,shrimp:0.4,golden:0.6,@twitch:0.8,@steady:0.7,@topwater:0.5,@vertical:0.5` | 1.5 / 0.6 / 1.5 / 0.6 | 활동: 새벽 · 저녁 | 생새우 + 정어리 | Surge |
| `golden_carp` | 황금잉어 | `golden:1,corn:0.5,softworm:0.4` | 1.4 / 0.7 / 1.4 / 1.0 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Bottom |
| `pale_chub` | 피라미 | `worm:1,paste:0.6,spinner:0.4,golden:0.3` | 1.0 / 1.3 / 1.0 / 0.3 | 활동: 낮 | 사료 | Grab |
| `cherry_salmon` | 산천어 | `worm:1,spinner:1,spoon:0.7,minnow:0.5,shrimp:0.4,golden:0.3` | 1.5 / 0.8 / 1.4 / 0.4 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Grab |
| `rainbow_trout` | 무지개송어 | `spoon:1,spinner:0.9,minnow:0.8,worm:0.6,corn:0.3,golden:0.4,@steady:0.7` | 1.4 / 0.8 / 1.4 / 0.6 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Grab |
| `mandarin_fish` | 쏘가리 | `minnow:1,softworm:0.8,shrimp:0.6,spoon:0.5,golden:0.5` | 0 / 0 / 0 / 1.8 | 밤에만 | 생새우 | Grab |
| `lenok` | 열목어 | `spoon:1,spinner:0.8,minnow:0.8,worm:0.3,golden:0.7` | 1.6 / 0.5 / 1.6 / 0.8 | 활동: 새벽 · 저녁 | 생새우 | Grab |
| `horse_mackerel` | 전갱이 | `shrimp:1,sandworm:0.7,jig:0.6,spoon:0.4,golden:0.3` | 1.3 / 1.0 / 1.3 / 1.1 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Grab |
| `mackerel` | 고등어 | `jig:1,shrimp:0.9,spoon:0.6,squid:0.5,sandworm:0.5,golden:0.3,@vertical:0.7` | 1.5 / 1.1 / 1.4 / 0.4 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Grab |
| `rockfish` | 우럭 | `sandworm:1,softworm:0.9,squid:0.8,shrimp:0.7,jig:0.5,golden:0.4,@bottom:0.7` | 1.0 / 0.6 / 1.2 / 1.6 | 활동: 밤 | 생새우 | Bottom |
| `flounder` | 광어 | `squid:1,minnow:0.8,softworm:0.8,sandworm:0.6,golden:0.4` | 1.3 / 0.9 / 1.2 / 0.8 | 활동: 새벽 | 생새우 | Bottom |
| `black_porgy` | 감성돔 | `shrimp:1,sandworm:0.8,corn:0.4,golden:0.5` | 1.4 / 0.6 / 1.3 / 1.3 | 활동: 새벽 · 저녁 · 밤 | 사료 + 생새우 | Grab |
| `red_seabream` | 참돔 | `squid:1,jig:0.8,egi:0.7,shrimp:0.7,golden:0.7` | 1.6 / 0.9 / 1.3 / 0.5 | 활동: 새벽 · 저녁 | 생새우 + 정어리 | Grab |
| `piranha` | 피라냐 | `worm:0.8,squid:0.6,shrimp:0.7,minnow:0.6,spinner:0.5,golden:0.3` | 0.9 / 1.4 / 1.0 / 0.5 | 활동: 낮 | 사료 + 생새우 | Grab |
| `catfish` | 메기 | `worm:1,sandworm:0.5,squid:0.6,softworm:0.5,glow:0.4,golden:0.3` | 0 / 0 / 0 / 2.0 | 밤에만 | 사료 + 생새우 | Bottom |
| `snakehead` | 가물치 | `frog:1,popper:0.8,minnow:0.8,crank:0.6,worm:0.3,golden:0.4,@topwater:0.8` | 1.5 / 0.8 / 1.5 / 0.6 | 활동: 새벽 · 저녁 | 생새우 | Grab |
| `arowana` | 아로와나 | `frog:1,popper:1,minnow:0.7,shrimp:0.4,golden:0.5` | 1.2 / 1.3 / 1.0 / 0.3 | 활동: 낮 | 생새우 + 정어리 | Surge |
| `arapaima` | 피라루쿠 | `frog:1,popper:0.8` | 1.3 / 1.1 / 1.0 / 0.6 | 활동: 새벽 | 정어리 | Surge |
| `smelt` | 빙어 | `worm:1,glow:0.8,golden:0.3` | 1.3 / 1.2 / 1.0 / 0.5 | 활동: 새벽 | 사료 | Grab |
| `burbot` | 모캐 | `worm:0.8,sandworm:0.6,glow:1,squid:0.5,softworm:0.5,golden:0.4` | 0 / 0 / 0 / 2.0 | 밤에만 | 생새우 | Bottom |
| `northern_pike` | 강꼬치고기 | `jig:1,softworm:0.7,egi:0.5,golden:0.4,@vertical:0.7` | 1.4 / 1.0 / 1.3 / 0.3 | 활동: 새벽 · 저녁 | 생새우 | Grab |
| `arctic_char` | 북극곤들매기 | `jig:1,egi:0.7,glow:0.6,golden:0.5` | 1.3 / 0.7 / 1.3 / 0.9 | 활동: 새벽 · 저녁 | 사료 + 생새우 | Grab |
| `sturgeon` | 철갑상어 | `softworm:1,jig:0.5` | 0.8 / 0.6 / 1.0 / 1.6 | 활동: 밤 | 생새우 | Bottom |
| `yellowtail` | 방어 | `jig:1,squid:0.8,kona:0.7,minnow:0.6,golden:0.3` | 1.5 / 1.0 / 1.2 / 0.4 | 활동: 새벽 | 생새우 + 정어리 | Surge |
| `mahi_mahi` | 만새기 | `popper:1,minnow:1,kona:0.9,squid:0.8,jig:0.6,golden:0.4` | 1.1 / 1.4 / 1.0 / 0.3 | 활동: 낮 | 생새우 + 정어리 | Surge |
| `bluefin_tuna` | 참다랑어 | `jig:1,kona:0.9,squid:0.9,golden:0.6` | 1.6 / 0.8 / 1.3 / 0.5 | 활동: 새벽 · 저녁 | 정어리 | Surge |
| `ocean_sunfish` | 개복치 | `squid:1,shrimp:0.7,golden:0.7` | 0.6 / 1.8 / 0.6 / 0 | 활동: 낮 | 생새우 | Grab |
| `blue_marlin` | 청새치 | `kona:1,jig:0.5` | 1.3 / 1.2 / 0.9 / 0.4 | 활동: 새벽 | 정어리 | Surge |
| `great_white` | 백상아리 | `kona:1,jig:0.6` | 1.1 / 0.6 / 1.4 / 1.5 | 활동: 저녁 · 밤 | 정어리 | Surge |
| `cave_tetra` | 장님동굴어 | `glow:1,worm:0.5,golden:0.3` | 1.0 / 1.0 / 1.0 / 1.0 | 하루 종일 | 사료 | Grab |
| `crystal_koi` | 수정비단잉어 | `glow:1,corn:0.6,golden:0.8` | 1.0 / 1.3 / 1.0 / 0.8 | 활동: 낮 | 사료 | Grab |
| `anglerfish` | 초롱아귀 | `glow:0.8,egi:0.8,squid:0.6,jig:0.5,golden:0.7` | 1.0 / 0.8 / 1.0 / 1.4 | 활동: 밤 | 정어리 | Bottom |
| `coelacanth` | 실러캔스 | `egi:1,softworm:0.6,jig:0.4` | 1.0 / 0.8 / 1.0 / 1.4 | 활동: 밤 | 정어리 | Surge |

밤에만 나오는 종(모든 낮 시간대 a = 0): `mandarin_fish` 쏘가리, `catfish` 메기, `burbot` 모캐. 밤에 안 나오는 종: `ocean_sunfish`
개복치 (밤 a = 0). 낮(08–17시)에만 안 나오는 종(낮 a = 0, 새벽·저녁은 조금): `yellow_catfish` 동자개, `freshwater_eel` 뱀장어.

### 2.5 전설어 조우 요약 (`EncounterDef`)

조우가 있는 종은 6종입니다. 어종 파일의 `"encounter": "<id>"`가 `Assets/Scripts/Data/LegendEncounters.cs`의 팩토리
(`GoldenCarp()`, `Arapaima()`, `Sturgeon()`, `BlueMarlin()`, `GreatWhite()`, `Coelacanth()`)를 가리키고, 로더가 새 행을 만들어
**`keyLures`를 그 어종의 `baits`에서 같은 순서로 채웁니다**(전설어의 선호 = 유인 미끼; 팩토리는 `keyLures`를 비워 둠). 그래서
전설어의 `baits`에는 `@액션`을 쓰지 않고, `keyRules`의 미끼는 `baits`에 있어야 합니다(검사기 E10). 조우 행의 수치는
`EncounterView`의 연출 분기와 묶인 손맛 값이라 코드에 남겼습니다. 기분(mood)별 수치, 연출(`Choreo`), 문구 전체는 코드와
[legends_rollout.md](legends_rollout.md) 3절을 보세요. 아래는 조건과 장비에 관련된 값만 옮겼습니다.

| 필드 | 뜻 | `coelacanth` | `golden_carp` | `arapaima` | `sturgeon` | `blue_marlin` | `great_white` |
|---|---|---|---|---|---|---|---|
| `backdrop` | 조우 배경 세트 | cave | lake | swamp | ice | ocean | ocean |
| `keyLures` | 유인 미끼와 가중치 (= 어종 파일의 `baits`) | egi 1.0 · softworm 0.6 · jig 0.4 | golden 1.0 · corn 0.5 · softworm 0.4 | frog 1.0 · popper 0.8 | softworm 1.0 · jig 0.5 | kona 1.0 · jig 0.5 | kona 1.0 · jig 0.6 |
| `meterQ` | 게이지를 채우는 품질 | `Lure` | `Still` (softworm만 `Lure`: `keyRules`) | `Lure` | `Lure` | `Lure` | `Crawl` (jig는 `Lure`, `depthMin` 6: `keyRules`) |
| `depthMin` / `depthMax` | 루어 수심 조건 (m, `depthMax` 0 = 끔) | 5.0 / 0 | 4.0 / 0 | 0 / 0.4 | 3.5 / 0 | 0 / 3.0 | 0 / 0 |
| `bottomBand` | 바닥에서 이 거리 안 (m, 99 = 끔) | 1.5 | 0.8 | 99 | 0.8 | 99 | 99 |
| `lurkZMin`–`lurkZMax` / `nearLurk` | 은신처 거리 / 가까움 판정 (m) | 14–30 / 8 | 14–30 / 7 | 10–30 / 7 | 6–14 / 12 | 20–45 / 10 | 20–45 / 10 |
| `lurkDepth` | 은신 수심 (m, 0 = 바닥 근처: 은신처 밑 바닥 `DepthAt(x, z)` − 0.5) | 0 | 0 | 1.2 | 0 | 6 | 6 |
| `lurkWeedEdge` | 생성 지형에서 은신처를 수초 옆 브레이크라인(WeedEdge 칸, ≥ `depthMin` + 0.5)에 둠 | – | **true** | – | – | – | – |
| `minQ` / `minSoak` / `fillTime` / `chance` | 최소 품질 / 최소 담금 s / 게이지 채움 s / 확률 | 0.55 / 8 / 16 / 0.6 | 0.6 / 10 / 18 / 0.6 | 0.55 / 6 / 16 / 0.6 | 0.5 / 8 / 18 / 0.6 | 0.6 / 5 / 14 / 0.6 | 0.55 / 6 / 20 / 0.55 |
| `minLine` | 필요한 줄 강도 (kgf) | 20 | 11 | 22 | 22 | 45 | 100 |
| `coolFail` / `coolFight` | 재등장 대기 s (실패 / 파이트 후) | 60 / 180 | 60 / 180 | 60 / 180 | 60 / 180 | 60 / 180 | 90 / 240 |
| `appear` / `relocate` | 등장 / 자리 이동 간격 s | 20–40 / 90–150 | 20–40 / 90–150 | 20–40 / 90–150 | 20–40 / 120–180 | 20–40 / 90–150 | 20–40 / 90–150 |
| `teaseLimit` | 유인 제한 시간 s | 28 | 30 | 28 | 30 | 26 | 30 |
| `noseIn` / `hookWindow` | 입질 직전 s / 챔질 창 s (+ `RodDef.hookBonus`) | 0.6–1.4 / 0.6 | 1.0–1.6 / 0.7 | 0.8–1.4 / 0.5 | 1.2–1.8 / 0.7 | 0.6–1.0 / 0.5 | 1.0–1.6 / 0.6 |
| `fakeOuts` | 속임 입질 횟수 | 0 | 1 | 0 | 1 | 1 | 1 |
| `lightGlow` / `lightPlain` | 루어 빛 반경 m (야광 / 일반) | 3.0 / 2.0 | 3.6 / 3.6 | 2.2 / 2.2 | 3.0 / 3.0 | 6.0 / 6.0 | 6.0 / 6.0 |
| `camScale` | 카메라 거리 배율 | 1 | 1 | 1.15 | 1.1 | 1.2 | 1.4 |
| `teaseView` | 유인 장면 시점 | Side | Side | **Top** (`swamp_top`) | Side | Side | Side |

공통값(`LegendEncounters.Row`, 실러캔스는 같은 값을 직접 지정): `gaugeStart` 20, `pityStep` 10, `pityMax` 30, `decay` 3,
`timeoutStrike` 60, `tell` 0.35, `perfectT` 0.25, `buffer` 0.15. `Row`를 쓰는 5종은 `moodHold` 2.5 (실러캔스는 기본값 0).
`EncounterDef` 기본값: `curiousAt` 40, `excitedAt` 75, `hysteresis` 5, `turnAwayBelow` 15, `earlyGauge` 65.
조우 판정에는 낚싯대 `luck`이 곱해집니다(`LegendWatch`: `Mathf.Min(0.95f, chance × Rod.luck)`).

### 2.6 호수 바닥 위 서식지 (어종 파일의 `habitat`, `HabitatDef`)

호수의 생성 지형([terrain_depth_spec.md](terrain_depth_spec.md) 7절, [lake_phase2_spec.md](lake_phase2_spec.md) A3)에서
물고기가 어디로 헤엄치고 어디서 나타나는지, 그리고 그 호수의 출현 가중치(1.2절)를 정합니다. 활동량(`TimeActivity`)은
그대로 몇 마리가 나와 있는지를, 서식지는 그 물고기들이 어디에 있는지를 정합니다. 문자열 `"key:value,…"`:

- `depth:pA-pB` 선호 물 깊이를 **그 호수의 수심 백분위**로(0 = 가장 얕은 곳, 100 = 가장 깊은 곳; R_ref 위의 분포, 줄마다 같은
  무게). 같은 `p55-p100`이 얕은 호수에서는 3 m 남짓, 깊은 호수에서는 5 m 넘는 곳입니다. 0 ≤ A < B ≤ 100, B − A ≥ 10.
- `@<시간대>:<N>p` 그 시간대의 이동(백분위 포인트, − 얕게, |N| ≤ 50, 양수는 부호 생략 가능). 띠는 0..100 밖으로 나가면 폭을
  지킨 채 안으로 밀립니다. 헤엄 깊이의 이동은 그 호수에서 `Quantile(이동한 가운데) − Quantile(가운데)` m의 절반입니다.
- 바닥 종류 키(`open shelf flat shoal dropoff hump hole channel basin`)와 재질 키(`mud sand gravel weed`)는 배율, `edge`
  브레이크라인 근처 가산, `col:mid|bottom` 헤엄 층(중층 / 바닥 2 m 안), `beta` 서식지가 위치를 끄는 세기(0 = 고르게),
  `@<시간대>.<종류>:<배율>`, `runDeep` 40 cm 이상이면 걸린 뒤 질주의 30%를 더 깊은 쪽으로.

칸의 깊이 맞음은 백분위 P로 봅니다: 띠 [A', B'] 안이면 1, 얕으면 `max(0.2, 1 − (A' − P)/25)`, 깊으면
`max(0.2, 1 − (P − B')/50)`. 예전처럼 미터로 적는 `depth:a-b`, `@<시간대>:<m>`도 읽히지만, 생성 지형 스테이지의 일반 어종은
반드시 백분위로 적어야 합니다(미터와 섞이면 E9). 모르는 키는 로드 오류(`[DATA]`)이고 검사기 E9입니다. 생성 지형이 있는
스테이지(지금은 `lake`, `TerrainRecipes.For`)의 일반 어종은 `habitat`이 필수이고 `depth`와 `col`이 들어 있어야 합니다. 다른
스테이지 어종에 적으면 경고(읽히지 않음)입니다. 도감의 "수심 a~b m"는 서식지가 아니라 어종의 헤엄 층(`depthMin`–`depthMax`)
입니다.

| 어종 | 선호 깊이 (백분위) | 층 | beta | 시간대 이동 (새벽 / 낮 / 저녁 / 밤, 포인트) | 좋아하는 바닥 |
|---|---|---|---|---|---|
| `crucian_carp` 붕어 | p25–p70 | 바닥 | 0.65 | −15 / +15 / −15 / −10 | 수초 평지·수초 둔덕 1.5(낮 둔덕 ×1.5), 브레이크라인 1.3(낮 ×1.6), 수초 재질 1.4, 진흙 1.2; 밤 평지 ×1.2 |
| `bluegill` 블루길 | p5–p50 | 중층 | 0.75 | 0 / +10 / 0 / +20 | 얕은 턱 1.8(낮 ×1.2), 평지 1.6, 둔덕 1.5, 모래 1.3·자갈 1.2·수초 1.3; 밤 브레이크라인 ×1.5 |
| `carp` 잉어 | p55–p100 | 바닥 | 0.65 | −20 / +10 / −20 / −40 | 낮 웅덩이·물골 ×1.5, 깊은 바닥 1.3, 진흙 1.4; 밤 평지 ×3.0·둔덕 ×2.6 (밤엔 얕은 평지로); `runDeep` |
| `largemouth_bass` 배스 | p20–p75 | 중층 | 0.8 | −8 / +25 / −8 / −5 | 브레이크라인 1.8(낮 ×1.5), 수중 둔덕 1.6(낮 ×1.8), 둔덕 1.5(낮 ×1.3), 자갈 1.3; 새벽 평지 ×1.8·얕은 턱 ×1.6; `runDeep` |
| `white_crucian` 떡붕어 | p10–p50 | 중층 | 0.7 | −8 / +12 / −8 / −5 | 수초 평지 1.6, 얕은 턱 1.5, 둔덕 1.4, 수초 재질 1.7; 낮 브레이크라인 ×1.4·둔덕 ×1.3, 저녁 평지 ×1.3 |
| `three_lips` 끄리 | p20–p70 | 중층 | 0.7 | −20 / +12 / −20 / +5 | 수중 둔덕 1.6(낮 ×1.5), 브레이크라인 1.5, 둔덕·열린 바닥 1.3, 자갈 1.5·모래 1.3; 새벽 평지 ×1.5, 저녁 얕은 턱 ×1.5 |
| `barbel_steed` 누치 | p35–p80 | 바닥 | 0.75 | −10 / +5 / −10 / −15 | 물골 1.8(낮 ×1.4), 수중 둔덕 1.4, 열린 바닥 1.3, 자갈 1.7·모래 1.6; 밤 둔덕 ×1.4; `runDeep` |
| `yellow_catfish` 동자개 | p60–p95 | 바닥 | 0.7 | 0 / 0 / −5 / −10 | 깊은 웅덩이 1.6, 깊은 바닥 1.5, 물골 1.4, 진흙 1.5; 밤 브레이크라인 ×1.5·열린 바닥 ×1.3 |
| `freshwater_eel` 뱀장어 | p75–p100 | 바닥 | 0.85 | 0 / 0 / 0 / −10 | 깊은 웅덩이 2.2, 깊은 바닥 1.4, 물골 1.3, 진흙 1.6; 밤 물골 ×1.4·브레이크라인 ×1.3; `runDeep` |
| `redfin_culter` 강준치 | p65–p100 | 중층 | 0.85 | −20 / +10 / −20 / −5 | 물골 2.0, 깊은 바닥 1.5, 웅덩이·브레이크라인 1.3, 자갈 1.2; 새벽 브레이크라인 ×1.5, 저녁 수중 둔덕 ×1.5; `runDeep` |
| `golden_carp` 황금잉어 | – | – | – | – | `runDeep`만 (걸린 뒤 질주) |

정확한 문자열은 각 어종 파일의 `habitat`. 예전 미터 띠는 50개 호수를 합친 분포에서 붕어 1.2–4.0 m ≈ p5–p63, 블루길
0.8–3.0 ≈ p0–p46, 잉어 3.0–7.0 ≈ p46–p99, 배스 1.0–4.5 ≈ p1–p70입니다(`-fkauto depth` D2의 정보 줄). 서식지는 거리(z) 한
줄의 몫을 절반 지킨 채(`HabitatModel.RowKeep` 0.5) 물고기를 그 줄 안, 그리고 좋아하는 깊이 쪽으로 옮깁니다. 어종마다·채비마다·
자리마다 차이는 일부러 남기고(어디에 어떤 물고기가 사는지), 경제만 묶습니다: 기준 플레이어가 부채꼴 전체·모든 채비·시간대를
평균하면 호수의 분당 입질·수입이 예전 호수의 0.8–1.25배([lake_phase2_spec.md](lake_phase2_spec.md) A7).

### 2.7 어종 추가하기 (검사기 `SpeciesCheck`)

어종 하나를 더하는 일은 **어종 파일 하나 + 그림 4장 + 스테이지 목록 한 줄**입니다. 도감 순서, 출현, 시간대, 입질 미끼,
수족관 먹이, 커버, 호수 바닥 위 서식지는 모두 그 파일에서 정해지고, 다른 코드는 고치지 않습니다.

1. **어종 파일** `Assets/Resources/Data/Fish/<id>.json`을 만듭니다(아래 틀, 키는 2.1절). 이 폴더에는 어종 파일 말고 아무것도
   두지 마세요: Resources가 폴더 안의 텍스트를 모두 어종 파일로 읽습니다(README 하나도 오류).
2. **그림**: `Tools/Blender/fk_fish.py`에 `fish("<id>", ...)` 모델을 더하고 `Tools/Blender/variants/hybrid/hyb_fish.py`로
   렌더해 `Assets/Resources/Sprites/Fish/<id>_0.png`, `<id>_1.png`(옆), `<id>_t0.png`, `<id>_t1.png`(위)를 만듭니다
   ([art_pipeline.md](art_pipeline.md)).
3. **노출**: `Assets/Resources/Data/stages.json`의 한 스테이지 `fish`에 `{ "id": "<id>", "weight": N }`을 더합니다. 이 목록이
   "그 스테이지에서 게임에 나오는 어종"이고, 넣은 자리가 도감·지도·출현 순서입니다. 가중치는 같은 희귀도의 다른 종을
   참고합니다(일반 28–45, 고급 14–26, 희귀 7–14, 영웅 3–10). **생성 지형이 있는 스테이지(호수)는 `{ "id": "<id>" }`만 적으면**
   희귀도의 기본 가중치(0.1절)에서 지형마다 가중치를 끌어냅니다(1.2절). `weight`를 적으면 그 값이 기본값 대신 바탕이 됩니다.
   호수 어종의 `habitat`은 백분위 띠로 적습니다(2.6절).
4. **검사**: 에디터 메뉴 **FishingKing/Validate Species Data**(대화 상자 + Console 목록). 빌드
   (`FishingKingSetup.BuildWindows`)도 먼저 검사하고, 오류가 있으면 아무것도 빌드하지 않습니다(batch면 종료 코드 1).
   게임 안 확인까지는 아래 **확인 절차**의 명령 하나(`Tools/Test/new_species.ps1`).
5. **전설어**라면 더: `LegendEncounters.cs`에 팩토리(키 = id, `keyLures`는 비워 둠 — 어종의 `baits`가 유인 미끼)와
   어종 파일의 `"encounter": "<id>"`, 리그 `Tools/Blender/variants/hybrid/legends/<id>.py`(`CM` = `minCm`, `maxCm`) →
   `Assets/Resources/Models/legend_<id>.fbx`·`legend_<id>_palette.json`, 조우 배경은 있는 세트(`lake` · `swamp` · `ice` ·
   `ocean` · `cave`) 중 하나. 음악 `legend_<id>`는 선택입니다(없으면 공통 조우 음악, 경고).

일반 어종의 틀(값은 붕어):

```json
{
  "id": "new_fish",
  "name": "새 물고기",
  "desc": "도감 설명 한두 문장.",
  "rarity": "common",
  "minCm": 12,
  "maxCm": 35,
  "weightK": 1.6,
  "basePrice": 35,
  "power": 1.6,
  "stamina": 6,
  "speed": 2.0,
  "aggression": 0.3,
  "jump": 0,
  "depthMin": 2,
  "depthMax": 7,
  "baits": "paste:1,corn:0.8,worm:0.7,golden:0.3",
  "activity": { "dawn": 1.3, "day": 0.8, "evening": 1.3, "night": 0.9 },
  "cover": { "types": [] },
  "diet": { "foods": ["pellet"], "style": "grab", "note": "왜 이 먹이인지" }
}
```

점프하는 종(`jump` > 0)은 `"jumpStyle"`, 커버를 찾는 종은 `"cover": { "seek": 0.45, "reach": 8, "dig": 1.1, "types": ["rock"] }`
(종류는 그 스테이지의 커버 구역에 있는 것), 호수 어종은 `"habitat"`(2.6절), 계곡의 웅덩이 어종은 `"pocketHold"`를 더합니다.

**검사 규칙** (오류는 메뉴·batch·빌드·`-fkauto species`를 실패시키고, 경고는 로그만):

| 규칙 | 오류가 되는 경우 |
|---|---|
| E1 파일 | `stages.json`이나 어종 파일이 없거나 JSON이 아님, BOM, 파일 이름 ≠ `id`, `id` 형식(`^[a-z][a-z0-9_]*$`), 같은 id 두 번 |
| E2 모르는 키 | 어느 객체에서든 정해진 키가 아닌 것(오타 `coverSeak` 등), 같은 키 두 번 (`art` 안은 안 봄) |
| E3 필수 값 | 필수 숫자·문자열이 없거나 형식이 틀림, `activity` 네 시간대 중 하나라도 없음, `diet`의 `foods`/`style`, `cover` 키, 점프하는 종의 `jumpStyle`, 모르는 enum 값(숫자도 안 됨) |
| E4 범위 | 0 < `minCm` < `maxCm`, `weightK`·`power`·`stamina`·`speed`·`basePrice` > 0, `aggression`·`jump` 0..1, 0 ≤ `depthMin` ≤ `depthMax`, 활동도 ≥ 0이고 하나는 > 0, 커버가 있으면 `seek` (0, 1]·`reach` ≥ 0·`dig` > 0, `pocketHold` 0..1, 먹이 1–2가지, `surge`는 사료를 안 먹음 |
| E5 미끼 | `key:숫자` 형식, 상점의 미끼·루어 id(`bait_<key>`), `@` 뒤가 루어 액션(`steady` `twitch` `topwater` `bottom` `vertical`), 같은 키 두 번, 가중치 > 0인 미끼가 하나는 있음 |
| E6 커버 | 커버 종류마다 그 어종의 스테이지 커버 구역(`obstacles_<stage>.json`의 `kind` "cover" `coverFor`, `rim`은 `kind` "rim")에 있어야 함; 커버를 찾는 어종의 스테이지에는 장애물 파일이 있어야 함 |
| E7 그림 | `Sprites/Fish/<id>_0`, `_1`, `_t0`, `_t1` 네 장 |
| E8 노출·스테이지 | 어느 스테이지에도 없는 어종, 두 스테이지에 있는 어종, 목록의 모르는 어종·같은 어종 두 번, 생성 지형이 없는 스테이지에서 가중치 없음, 가중치 ≤ 0, 스테이지 값의 범위(난이도 1–5, 요구 레벨 ≥ 1, 해금 가격 ≥ 0, 배율 > 0, 개체 수 ≥ 1), `stage_<id>.json` 없음, 일반 어종이 없는 스테이지, `lake` 없음 |
| E9 서식지 | 생성 지형 스테이지의 일반 어종에 `habitat`이 없거나 `depth`/`col`이 없음, `depth`가 백분위가 아니거나(0 ≤ A < B ≤ 100, B − A ≥ 10이 아님) 미터 깊이·미터 이동과 섞임("mixes units"), 시간대 이동이 50 포인트를 넘음, 서식지 문자열의 모르는 키 |
| E10 전설어 | `encounter` ≠ `id`이거나 `LegendEncounters`에 없음, `LegendEncounters`에 팩토리가 있는데 어종 파일에 `encounter`가 없음(일반 물고기로 헤엄침), 팩토리가 `keyLures`를 채움, `keyRules`의 미끼가 `baits`에 없음, `baits`에 `@액션`, 모델·팔레트 없음, 모르는 조우 배경 |
| E11 Blender | `fk_fish.py`에 `fish("<id>"` 줄이 없음, `hyb_fish.py`가 모든 모델을 렌더하지 않음(`FF.F.keys()`), 전설어 리그 `legends/<id>.py` 없음 (에디터는 항상, 플레이어는 `-fkrepo <저장소>`일 때) |
| E12 로드 | 실행 중인 게임의 `GameDatabase.LoadErrors` (플레이어만) |
| 경고 | 읽히지 않는 `habitat`, 전설어 음악 `legend_<id>` 없음, 리그의 `CM`이 크기와 다름, 어종 파일이 없는 `fk_fish.py` 모델, `reach` 0인데 `rim`이 아닌 커버, 얼음 구멍에서 쓸 미끼를 하나도 안 좋아하는 얼음 어종, 어종이 없는 `LegendEncounters` 팩토리, `encounter` 없는 `legendary` 어종 |

실행:

- 에디터 batch: `Unity.exe -batchmode -nographics -projectPath <프로젝트> -executeMethod FishingKing.EditorTools.SpeciesValidator.Batch -logFile <로그> [-fkspeciesfixtures]`
  — 오류가 있으면 종료 코드 1. `-fkspeciesfixtures`면 일부러 망가뜨린 데이터 56가지(`SpeciesFixtures`)를 모두 잡는지도 확인합니다.
- 플레이어: `FishingKing.exe -fkfresh -fkrich -fksave <이름> -fkauto species -fkrepo <저장소> -fkshots <폴더>` — 검사기·로드 오류·
  망가뜨린 데이터 56가지, 그리고 `species_dump.txt`(모든 어종·스테이지 값, float는 비트 패턴까지)와 `spawn_baseline.txt`
  (스테이지 × 시각 × 장비마다 `FishSpawner.Pick`의 몫, 스테이지 × 시간대 × 미끼마다 재고 전체의 입질 질량 Σ 가중치·a·√a·매력도)를
  `-fkshots`에 씁니다. 데이터를 바꾼 뒤 두 파일을 비교하면 무엇이 달라졌는지 보입니다. [testing.md](testing.md)

#### 확인 절차 (명령 하나)

코드는 그대로 두고 어종만 더했다면(위 1–5) 이것 하나만 돌립니다. 빌드 포함 약 4분(빌드 ~1분, 확인 ~3분; 뱀장어로 잰 3:15):

```
powershell -ExecutionPolicy Bypass -File Tools\Test\new_species.ps1 -Id <id> [-NoBuild] [-Out <폴더>]
```

1. **검사기** — 빌드의 첫 단계(오류면 빌드하지 않고 멈춤). `-NoBuild`면 에디터 batch `SpeciesValidator.Batch`만 돌리고 있는 빌드를 씁니다.
2. **빌드** 한 번 (`-NoBuild`면 건너뜀).
3. **`-fkauto newspecies -fkspecies <id>`** 한 프로세스(포인터 제스처 없이 테스트 훅만, 시작 화면에서 그 스테이지로 스스로 감):
   - N1 로드됐고 스테이지 목록에 있음.
   - N2 (생성 지형 스테이지 = 호수) `-fkauto depth`의 경제 게이트를 그 어종을 넣은 채로: 50 시드 × 대나무·카본·용의 추정기
     (실제 낚시·라이브 소크 없음) — F가 클램프에 안 걸리고, 스테이지의 분당 입질·수입이 예전 호수의 0.8–1.25; 그 어종의 가장
     좋은 채비에서 상위 10% 캐스팅 ≥ 부채꼴 평균 × 1.5(대나무·카본); **자리** — 손으로 쓴 게이트(2.6절 2단계 5종)가 있으면
     그것, 없으면 서식지에서 가장 강한 선호(가장 활발한 시간대의 바닥 종류 또는 재질, 가중치 ≥ 1.3)의 목표 몫 ≥ 고른 목표
     × 1.3(시드 1, 용 낚싯대 물); 끌어낸 출현 가중치 보고(시드 1의 A·W·스톡 중 몫, 50 시드의 A 범위, 0.6 / 1.4에 걸린 횟수 —
     게이트 아님, 걸리면 `CLAMP HIT`). 입질·수입이 밴드 가장자리에서 **0.03 안**이면 `NOTE` 줄: 라이브 소크(`-fkauto economy`)가
     필요하다는 뜻이지만 자동으로 돌리지 않습니다 — 사용자에게 물어봅니다.
   - N3 결과 카드 `card_<id>`와 도감 상세 `collection_detail_<id>` 캡처.
   - N4 크기 등급이 들어가는 수조가 있으면: 그 수조 중 가장 작은 것에 배고픈 한 마리, 먹이를 훅으로 위에서 떨어뜨려 먹는지
     (`aqua_<id>`, `aqua_<id>_eat`; 같은 검사를 따로: `-fkaqua species -fkspecies <id>`). 없으면 SKIP.
   - 전설어는 N1 뒤 멈춤(조우는 아래 회귀 스위트).
4. **요약 표**(단계·결과·실패 수·시간·로그)와 `[NEWSP]` 보고 줄(밴드 거리, 가중치, NOTE, SKIP). 실패가 있으면 종료 코드 1.
   로그·캡처: `Logs/new_species/<id>_<시각>/`.

**회귀 테스트는 어종 추가가 코드도 바꿀 때만** 돌리고, **돌리기 전에 사용자에게 무엇을·왜·얼마나 돌릴지 묻습니다**:
`-Regress <목록> -DryRun`으로 계획(스위트, 병렬 / 직렬 묶음, 제한 시간, 예상 시간)을 보여 주고, 동의를 받은 뒤 같은 명령에
`-Yes`를 붙입니다. `-Yes`가 없으면 계획만 출력하고 아무것도 띄우지 않습니다(종료 코드 2). 목록은 `이름` 또는 `이름=값`:

| 바뀐 것 | `-Regress` | 짧은 기본값 (약) |
|---|---|---|
| 새 미끼·루어 | `lure=<루어 id>` (자연 미끼면 `fish`) | 루어 하나 (2분; `lure=all`은 다섯 개 4분) |
| 행동: 점프 방식·파이트 | `fish`, `breaks` | 그 어종만 2마리 실제 캐스팅·파이트 (3분), 줄 끊김 (2분) |
| 수족관 먹는 방식 | `aqua` | `-fkaqua live` (2분) |
| 전설어 (조우 + 3D 모델) | `encounter`, `legendspot` | 그 전설어 완벽 플레이 한 번 (3분), 물보라 자리 (2분) |
| 스테이지 UI 수용량 초과 (지도 창 12종 넘음) | `tour` | 화면 순회, 도감 상세 = 그 어종 (2분) |
| 새 스테이지·지형 | `depth`, `obstacles=<stage>`, `cards=<stage>`, `tour` | 호수 지형 전체 (10분), 그 스테이지 (5분, 2분) |
| 검사기 자체 | `species` | 망가뜨린 데이터 56가지 (1분) |

포인터 제스처(캐스팅·드래그·탭)를 쓰는 스위트(`tour` `fish` `lure` `encounter` `legendspot` `obstacles` `breaks` `aqua`)는
한 줄로 하나씩, 포인터가 없는 것(`newspecies` `species` `cards` `depth`)은 그 옆에서 동시에 돕니다. 동시에 최대 논리 CPU의
절반(`-MaxParallel`), 스위트마다 제한 시간이 넘으면 그 프로세스를 끝내고 TIMEOUT. 예: `-Id my_legend -Regress
"encounter,legendspot" -DryRun` → 묻고 → `... -Yes`.

## 3. 낚시 장비 (`GameDatabase.BuildItems`)

모든 장비는 `ItemDef`(`id`, `name`, `price`, `desc`)를 상속합니다. 가격은 코인입니다. 가격이 0인 항목은 시작 장비입니다.
상점(`ShopUI`)은 이 목록을 그대로 씁니다(루어는 액션별로 묶어 보여 줌).

### 3.1 낚싯대 (`RodDef`)

| 열 | 필드 | 단위·의미 |
|---|---|---|
| 비거리 | `castDist` | 최대 캐스팅 거리 (m) |
| 탄력 | `flex` | 0..1, 장력 순간 상승을 흡수하고 끊어지기 전 여유를 늘림 |
| 챔질 여유 | `hookBonus` | 챔질 창에 더하는 시간 (s) |
| 행운 | `luck` | 희귀어 배율 (기본 1). 출현 가중치(희귀 이상)와 전설 조우 확률에 곱함 |
| 길이 | `length` | 그려지는 낚싯대 길이 (m) |
| 그림 | `nodes` / `gimbal` / `thick` / `glow` | 대나무 마디 / 빅게임 김벌 손잡이 / 굵은 블랭크 / 끝이 빛남 (`Color.clear` = 없음) |

| id | 이름 | 가격 | 비거리 | 탄력 | 챔질 여유 | 행운 | 길이 | 그림 특징 |
|---|---|---|---|---|---|---|---|---|
| `rod_bamboo` | 대나무 낚싯대 | 0 | 16 | 0.15 | 0 | 1 | 3.0 | `nodes` |
| `rod_glass` | 글라스 로드 | 900 | 20 | 0.25 | 0.1 | 1 | 3.0 | — |
| `rod_carbon` | 카본 루어 로드 | 4000 | 24 | 0.2 | 0.25 | 1 | 2.9 | — |
| `rod_surf` | 원투 서프 로드 | 14000 | 34 | 0.3 | 0.1 | 1 | 3.7 | — |
| `rod_biggame` | 빅게임 로드 | 45000 | 27 | 0.5 | 0.2 | 1 | 2.7 | `gimbal`, `thick` |
| `rod_dragon` | 용왕의 낚싯대 | 180000 | 36 | 0.6 | 0.4 | 1.6 | 3.3 | `thick`, `glow` `#ffb040` |

색(`blank`, `grip`, `blank2`, `seat`, `wrap`)은 코드의 `Art.Hex` 값을 보세요. 상점 아이콘(`Tools/Blender/fk_items.py` RODS)과
맞춰져 있습니다(`RodDef` 주석).

### 3.2 릴 (`ReelDef`)

| 열 | 필드 | 단위·의미 |
|---|---|---|
| 감기 | `retrieve` | 핸들 한 바퀴에 감기는 줄 (m) |
| 최대 드랙 | `dragMax` | 드랙이 미끄러지기 시작하는 힘 (kgf) |
| 드랙 부드러움 | `dragSmooth` | 0..1, 초과 장력 중 드랙이 흡수하는 비율 |
| 줄 용량 | `lineCap` | 풀 수 있는 최대 줄 길이 (m). 넘으면 `Outcome.Spooled` (`FightModel`) |
| 자동 감기 | `autoReel` | 전동 릴의 수동 감기 속도 (m/s), 감는 중에 더해짐 (`FightModel`) |

| id | 이름 | 가격 | 감기 | 최대 드랙 | 드랙 부드러움 | 줄 용량 | 자동 감기 |
|---|---|---|---|---|---|---|---|
| `reel_basic` | 기본 스피닝 릴 | 0 | 0.8 | 2.5 | 0.3 | 60 | 0 |
| `reel_light` | 라이트 스피닝 릴 | 1200 | 0.9 | 5 | 0.45 | 80 | 0 |
| `reel_highgear` | 하이기어 릴 | 5000 | 1.2 | 9 | 0.5 | 100 | 0 |
| `reel_baitcast` | 베이트캐스팅 릴 | 16000 | 1.05 | 18 | 0.65 | 120 | 0 |
| `reel_electric` | 전동 빅게임 릴 | 50000 | 1.3 | 40 | 0.75 | 200 | 0.9 |
| `reel_poseidon` | 포세이돈 릴 | 200000 | 1.5 | 90 | 0.9 | 300 | 0.6 |

### 3.3 줄 (`LineDef`)

| 열 | 필드 | 단위·의미 |
|---|---|---|
| 강도 | `strength` | 끊어지는 힘 (kgf) |
| 은밀함 | `stealth` | 입질 확률 배율 (기본 1) |
| 내마모 | `tough` | 구조물에 쓸릴 때 마모량을 이 값으로 나눔 (기본 1, [obstacles_spec.md](obstacles_spec.md) 7.5) |
| (상점 표시) | | 강도 옆에 내마모 등급: `tough` ≥ 3 쓸림 매우 강함 · ≥ 1.4 쓸림 강함(초록) · ≥ 0.95 쓸림 보통 · 그 아래 쓸림 약함(빨강) (`ShopUI.Abrasion`) |

| id | 이름 | 가격 | 강도 | 은밀함 | 내마모 | 조우 가능 전설 (`minLine` ≤ 강도) |
|---|---|---|---|---|---|---|
| `line_nylon2` | 나일론 2호 | 0 | 3 | 1 | 1.0 | — |
| `line_nylon4` | 나일론 4호 | 600 | 6 | 1 | 1.15 | — |
| `line_fluoro6` | 플로로카본 6호 | 3000 | 11 | 1.15 | 1.7 | 황금잉어 |
| `line_pe3` | PE 합사 3호 | 12000 | 22 | 1 | 0.6 | + 실러캔스, 피라루쿠, 철갑상어 |
| `line_pe8` | PE 합사 8호 | 40000 | 45 | 1 | 0.8 | + 청새치 |
| `line_titan` | 티타늄 와이어 | 150000 | 100 | 1 | 5.0 | + 백상아리 |

### 3.4 자연 미끼 (`BaitDef`, `isLure` = false)

자연 미끼는 입질 한 번에 1개가 줄고(`Game.ConsumeBait`), 다 쓰면 떡밥으로 바뀝니다. 상점에서 사면 `packSize`개가 들어옵니다
(`Game.Buy`). 액션은 `None`이라 찌 채비로 씁니다.

| id | 이름 | 가격 (1팩) | 팩 개수 `packSize` | 가라앉는 속도 `sinkSpeed` (m/s) | 특징 |
|---|---|---|---|---|---|
| `bait_paste` | 떡밥 | 0 | 1 | 1.0 | `infinite` (무한, 살 수 없음) |
| `bait_worm` | 지렁이 | 120 | 20 | 1.1 | 새 게임 시작 때 10개 지급 (`SaveData.NewGame`) |
| `bait_corn` | 옥수수 | 150 | 20 | 1.2 | — |
| `bait_shrimp` | 새우 | 350 | 15 | 1.2 | — |
| `bait_sandworm` | 갯지렁이 | 450 | 15 | 1.4 | — |
| `bait_squid` | 오징어 | 900 | 10 | 1.5 | — |
| `bait_glow` | 야광 웜 | 1500 | 10 | 1.1 | `glow` |
| `bait_golden` | 황금 떡밥 | 5000 | 3 | 1.0 | `rareBoost` 3 (영웅 이상 출현 가중치 ×3, 전설은 한 번 더) |

### 3.5 루어 (`BaitDef`, `isLure` = true)

루어는 한 번 사면 계속 쓰고, 줄이 끊어지면 잃습니다(`Game.LoseLure`; 물고기가 털고 간 루어는 감아서 회수). 모두 `packSize` 1, `rareBoost` 1입니다.
액션 이름·아이콘·색은 `LureInfo`(`감기` `act_steady` `#6ab8ff` · `저킹` `act_twitch` `#6ad06a` · `수면` `act_top` `#ffd24a` ·
`바닥` `act_bottom` `#b8865a` · `수직` `act_vertical` `#b58aff`)에 있습니다. 필드 의미(`Models.cs` `BaitDef` 주석):

| 필드 | 기본값 | 의미 |
|---|---|---|
| `action` | `None` | 루어 액션 (`LureAction`: `Steady` 감기, `Twitch` 저킹, `Topwater` 수면, `Bottom` 바닥, `Vertical` 수직) |
| `work` | `Wind` | 조작 입력: `Wind` 릴 원 그리기, `Flick` 톡 (화면 아래로 짧고 빠르게 당기기) |
| `buoyancy` | `Sink` | 쉴 때: `Sink` 가라앉음, `Suspend` 헤엄 수심에 머묾, `Float` 떠오름 |
| `sinkSpeed` | 1.2 | 가라앉는 속도 (m/s) |
| `fallSpeed` | 0 | 톡이나 멈춤 뒤 떨어지는 속도 (m/s, 0 = `sinkSpeed`) |
| `riseSpeed` | 0.35 | `Float`: 쉴 때 0.05 m까지 떠오르는 속도 (m/s) |
| `swimDepth` | 0 | `Suspend`: 머무는 수심 (m) |
| `maxDepth` | 0 | 가장 깊이 가는 수심 (m, 0 = 바닥까지) |
| `windLift` | 0.45 | 1 m 감을 때 올라오는 깊이 (m, 음수 = 잠수) |
| `hopM`, `hopBase` | 0, 1 | 톡 한 번에 올라가는 높이 = `hopM × (hopBase + (1 − hopBase) × 세기)` (m, 음수 = 잠수) |
| `pullM`, `dartM` | 0.25, 0 | 톡 한 번에 물가 쪽으로 끌려오는 거리 / 옆으로 튀는 거리 (m) |
| `reelBand` | (0, 0) | 좋은 감기 속도 범위 (rev/s). `Flick` 루어에 `runMax` > 0이면 느린 끌기 범위 |
| `flicks` | (1, 1) | 한 사이클의 톡 횟수 범위 |
| `gap`, `rest` | (0, 0) | 톡 사이 간격 / 조작 뒤 쉬는 시간 (s) |
| `runMax` | 0 | 쉬기가 있는 `Wind` 조작의 최대 감기 시간 (s) |
| `maxStrength` | 1 | 이보다 센 톡은 "너무 세요" |
| `needBottom` | false | 바닥에 닿아 있어야 함 |
| `glow` | false | 야광 |
| `restStrike` | −1 | 쉬기 시작 후 이 시간(s)부터 끝까지 덮침 창 (−1 = 없음) |
| `fallStrike` | (−1, −1) | 톡 뒤 떨어지는 동안, 떨어진 시간 [x, y] s에 덮침 창 (x < 0 = 없음) |
| `landStrike` | 0 | 바닥에 닿은 뒤 이 시간(s) 동안 덮침 창 |
| `burstStrike` | false | 톡 연타 중 덮침 창 |
| `pauseStrike` | 0 | 감기 루어: 멈춘 뒤 이 시간(s) 동안 덮침 창 (스푼의 팔랑임) |

**움직임** (빈 칸 = 기본값):

| id | 이름 | 가격 | 액션 | `work` | `buoyancy` | `sinkSpeed` | `fallSpeed` | `riseSpeed` | `swimDepth` | `maxDepth` | `windLift` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `bait_spinner` | 스피너 | 1200 | 감기 | Wind | Sink | 0.9 | | | | | 0.35 |
| `bait_spoon` | 스푼 | 1500 | 감기 | Wind | Sink | 1.8 | 1.2 | | | | 0.30 |
| `bait_crank` | 크랭크베이트 | 2200 | 감기 | Wind | **Float** | | | 0.3 | | 2.5 | −0.6 |
| `bait_kona` | 트롤링 루어 | 12000 | 감기 | Wind | Sink | 0.3 | | | | | 0.45 |
| `bait_minnow` | 미노우 | 2500 | 저킹 | **Flick** | **Suspend** | 0.9 | | | 1.2 | 1.5 | 0.10 |
| `bait_popper` | 포퍼 | 3000 | 수면 | **Flick** | **Float** | | | 0.4 | | | 0 |
| `bait_frog` | 개구리 루어 | 4000 | 수면 | Wind | **Float** | | | 0.4 | | | 0 |
| `bait_softworm` | 소프트 웜 | 1800 | 바닥 | **Flick** | Sink | 1.2 | 1.0 | | | | 0.15 |
| `bait_jig` | 메탈 지그 | 8000 | 수직 | **Flick** | Sink | 2.6 | 1.2 | | | | 0.45 |
| `bait_egi` | 야광 에기 | 6000 | 수직 | **Flick** | Sink | 0.6 | 0.6 | | | | 0.30 |

**조작** (빈 칸 = 기본값):

| id | `hopM` | `hopBase` | `pullM` | `dartM` | `reelBand` (rev/s) | `flicks` | `gap` (s) | `rest` (s) | `runMax` | `maxStrength` | 기타 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `bait_spinner` | | | | | 0.5–1.1 | | | | | | |
| `bait_spoon` | | | | | 0.8–1.6 | | | | | | |
| `bait_crank` | | | | | 1.2–2.2 | | | | | | |
| `bait_kona` | | | | | 1.6–5 | | | | | | |
| `bait_minnow` | −0.15 | | | 0.3 | | 1–3 | 0.2–0.6 | 0.5–1.5 | | | |
| `bait_popper` | | | 0.2 | | | 1–1 | | 1.0–2.5 | | | |
| `bait_frog` | 0 | | 0.2 | | 0.3–0.8 | | | 0.5–2.0 | 2 | | |
| `bait_softworm` | 0.4 | | 0.3 | | 0.1–0.5 | 1–1 | | 1.0–3.0 | 2 | 0.6 | `needBottom` |
| `bait_jig` | 1.5 | 0.4 | 0.15 | | | 2–4 | 0.25–0.6 | 0.8–2.0 | | | |
| `bait_egi` | 1.2 | 0.42 | 0.2 | | | 1–2 | 0.2–0.8 | 1.5–4.0 | | | `glow` |

**덮침 창 · 분류** (수층은 `LureInfo.Zone`, 얼음 가능은 `LureInfo.IceOk`, `IsSteady` = 액션 있음 · `Wind` · `rest.y` ≤ 0):

| id | `restStrike` | `fallStrike` | `landStrike` | `burstStrike` | `pauseStrike` | 수층 | 얼음 | `IsSteady` | `hint` |
|---|---|---|---|---|---|---|---|---|---|
| `bait_spinner` | −1 | — | 0 | — | 0 | 중층 | 불가 | 예 | 천천히 일정하게 감기 — 날개가 반짝여요 |
| `bait_spoon` | −1 | — | 0 | — | 1.0 | 중층 | 불가 | 예 | 보통 속도로 꾸준히, 멈추면 팔랑팔랑 가라앉아요 |
| `bait_crank` | −1 | — | 0 | — | 0 | 중층 | 불가 | 예 | 빨리 감을수록 깊이 잠수, 멈추면 떠올라요 |
| `bait_kona` | −1 | — | 0 | — | 0 | 수면 | 불가 | 예 | 빠르게 쉬지 않고 감기 |
| `bait_minnow` | 0.3 | — | 0 | — | 0 | 중층 | 불가 | 아니오 | 아래로 톡톡 당기고(1~3번) 잠깐 멈추기 |
| `bait_popper` | 0.3 | — | 0 | — | 0 | 수면 | 불가 | 아니오 | 톡 당겨 '퐁'! — 물결이 잦아들 때까지 기다리기 |
| `bait_frog` | 0.3 | — | 0 | — | 0 | 수면 | 불가 | 아니오 | 아주 천천히 감다가 멈추기를 반복 |
| `bait_softworm` | −1 | 0–99 | 1.5 | — | 0 | 바닥 | 가능 | 아니오 | 바닥까지 가라앉힌 뒤 살짝 톡 당기고, 바닥에서 기다리기 |
| `bait_jig` | −1 | 0–0.6 | 0 | 예 | 0 | 바닥 | 가능 | 아니오 | 바닥까지 내린 뒤 빠르게 연속으로 톡톡 당겨 올리기 |
| `bait_egi` | −1 | 0.5–99 | 1.0 | — | 0 | 바닥 | 가능 | 아니오 | 톡 당겨 올린 뒤 손을 떼고 가라앉히기 — 입질은 떨어질 때! |

(수층 "수면"인 트롤링 루어는 `LureInfo.Zone`의 규칙 `Sink`이면서 `sinkSpeed` < 0.5, `windLift` ≥ 0.4에 해당합니다.
자연 미끼 8종은 모두 얼음에서 쓸 수 있습니다.) 옛 루어 id 4개(`spoon`, `minnow`, `frog`, `jig`)는 그대로 두어 옛 세이브가
유효합니다(`BuildItems` 주석).

## 4. 수족관 데이터

규칙과 공식(배고픔, 성장, 관람 수입, 오염)은 [aquarium.md](aquarium.md)에 있습니다. 여기서는 표만 둡니다.

### 4.1 수조 (`GameDatabase.BuildItems` `Tanks`, `Models.cs` `TankDef`; 화면 치수는 `Assets/Resources/Data/aquarium_tank_<n>.json`)

| 열 | 필드 | 의미 |
|---|---|---|
| 단계 | `level` | `SaveData.tankLevel`과 같은 번호. 한 단계씩만 살 수 있음 (`Game.Buy`: `NeedPreviousTank`) |
| 칸 | `capacity` | 물고기가 차지하는 칸의 합 한도 |
| 최대 등급 | `maxClass` | 넣을 수 있는 가장 큰 크기 등급 (0 소형 … 3 초대형) |
| 초대형 수 | `maxHuge` | 초대형을 이 수까지만 (0 = 칸 제한만) |
| 장식 슬롯 | — | 이 단계에서 열리는 슬롯 수 (`AquaTank.SlotCount`, 4.5절) |
| 유리 / 바닥 맵 | `glassW`×`glassH` / `glassW`×`dirtH` | 이끼·바닥 때 맵의 픽셀 크기 (`aquarium_tank_<n>.json`) |

| id | 이름 | 단계 | 가격 | 칸 | 최대 등급 | 초대형 수 | 장식 슬롯 | 유리 맵 | 바닥 맵 |
|---|---|---|---|---|---|---|---|---|---|
| `tank_0` | 작은 어항 | 0 | 0 | 6 | 0 소형 | 0 | 6 | 288×132 | 288×22 |
| `tank_1` | 중형 수조 | 1 | 2500 | 12 | 1 중형 | 0 | 7 | 436×164 | 436×28 |
| `tank_2` | 대형 수조 | 2 | 12000 | 24 | 2 대형 | 0 | 8 | 584×224 | 584×38 |
| `tank_3` | 아쿠아리움 | 3 | 45000 | 40 | 3 초대형 | 2 | 9 | 704×264 | 704×46 |
| `tank_4` | 황금 대수족관 | 4 | 150000 | 64 | 3 초대형 | 0 | 10 | 880×332 | 880×56 |

`AquaTank.LimitText`는 상점에 "12칸 · 중형(40~100cm)까지" 형식으로 보여 줍니다(최대 등급이 초대형이면 `maxHuge` > 0일 때
"2마리", 아니면 "여러 마리"가 붙음).

### 4.2 크기 등급 (`AquaTank.ClassNames`, `ClassSpace`, `ClassRange`, `ClassOf`)

| 등급 | 이름 | 길이 (지금 크기 `sizeCm` 기준) | 차지하는 칸 |
|---|---|---|---|
| 0 | 소형 | 40cm 미만 | 1 |
| 1 | 중형 | 40~100cm | 2 |
| 2 | 대형 | 100~200cm | 4 |
| 3 | 초대형 | 200cm 이상 | 8 |

### 4.3 먹이 (`AquaCare.Feeds`, `AquaCare.Live`, 필드는 `FeedDef`)

| 열 | 필드 | 의미 |
|---|---|---|
| 가격 | `price` | 한 번 살 때 코인 |
| 봉투 / 조각 | `bagsPerBuy` / `piecesPerBuy` | 한 번 살 때 봉투 수(사료) / 조각 수(생먹이) |
| 분량 | `portions` | 봉투 하나의 분량. 1분량 = 포만감 0→100 %, 알갱이 `AquaCare.PelletsPerPortion` = 5개 |
| 채움 | `fill` | 생먹이 한 조각의 포만감 (사료 알갱이는 `AquaCare.PelletFill` 0.2) |
| 지속 | `fullHours` | 가득 찬 배가 0 %가 되는 시간 (h) |
| 성장 | `growth` | 이 먹이를 먹는 동안 성장 속도 배율 |
| 먹는 종류 | `kind` | 이 `Diet`가 있는 물고기만 먹음 |
| 그릇 | `box` | 생먹이 그릇 스프라이트 키 |

| id | 이름 | 종류 `kind` | 가격 | 봉투 / 조각 | 분량 | 채움 | 지속 (h) | 성장 | `premium` | 그릇 | 상점 설명 `who` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `feed_basic` | 기본 사료 | Pellet | 200 | 봉투 2 | 20 | 0.2/알 | 12 | 1 | — | — | 작은 물고기와 잡식성 (붕어·잉어·송어) |
| `feed_premium` | 고급 사료 | Pellet | 450 | 봉투 1 | 20 | 0.2/알 | 18 | 1.25 | 예 | — | 작은 물고기와 잡식성 (붕어·잉어·송어) |
| `feed_shrimp` | 생새우 | Shrimp | 300 | 조각 10 | — | 0.4 | 12 | 1 | — | `tub` | 중형 육식어와 바닥 물고기 (배스·쏘가리·메기) |
| `feed_sardine` | 정어리 | Sardine | 1500 | 조각 6 | — | 1 | 12 | 1 | — | `cooler` | 대형·바다 포식자 (피라루쿠·청새치·상어) |

먹는 종 수 (2.4절 표에서 셈): 사료 16종, 생새우 25종, 정어리 10종 (잡식 종은 둘 다에 셈) (`AquaCare.LogDiets`가 같은 값을 로그로 남김).

### 4.4 청소 도구 (`AquaTank.Tools`, `ToolDef`)

| id | 이름 | 가격 | 설명 |
|---|---|---|---|
| `tool_sponge` | 스펀지 | 0 | 유리에 낀 이끼를 문질러 닦아요. (무료, `AquaTank.Ensure`가 항상 지급) |
| `tool_net` | 뜰채 | 600 | 물에 떠다니는 찌꺼기를 건져요. |
| `tool_siphon` | 사이펀 | 1500 | 바닥 자갈 위를 훑어 쌓인 때를 빨아들여요. |

### 4.5 장식 (`AquaTank.Decors`, `DecorDef`) 과 슬롯 (`AquaTank.Slots`, `DecorSlot`)

| 열 | 필드 | 의미 |
|---|---|---|
| 종류 | `kind` | `Light` 조명 · `Back` 뒤쪽 · `Mid` 가운데 · `Front` 앞쪽 (`AquaTank.KindName`). 같은 종류 슬롯에만 놓임 |
| 보너스 | `bonus` | 관람 수입 + (0.05 = +5 %). 장식과 조명 우대의 합은 `AquaTank.BonusCap` 0.15까지 |
| 크기 | `w`×`h` | 스프라이트 px |
| 프레임 | `frames` / `fps` | 애니메이션 프레임 수 (0 = 정지) / 초당 프레임 |
| 효과 | `plant` / `bubbler` / `chest` / `wheel` | 수초(이끼 −12 %, 바닥 때 −8 %: `PlantAlgae`, `PlantDirt`) / 기포기(부유물 ×0.7: `BubblerDebris`) / 보물상자 / 물레방아 |
| 조명 우대 | `favour` / `favourRarity` | 이 스테이지에서 잡은 물고기, 또는 이 희귀도 이상에 +5 % (`AquaTank.FavourBonus`) |

조명은 모두 `bonus` 0.03, 크기 48×24입니다(`AquaTank.L`).

| id | 이름 | 종류 | 가격 | 보너스 | 크기 | 프레임 / fps | 효과 · 우대 |
|---|---|---|---|---|---|---|---|
| `decor_light_day` | 주광 조명 | Light | 2000 | 0.03 | 48×24 | — | 색 `#ffffff`, 우대 `lake`, `stream` (호수·계곡 물고기) |
| `decor_light_sunset` | 노을 조명 | Light | 4000 | 0.03 | 48×24 | — | 색 `#ffe0c0`, 우대 `sea`, `swamp` (방파제·늪지 물고기) |
| `decor_light_moon` | 달빛 조명 | Light | 5000 | 0.03 | 48×24 | — | 색 `#b4c4ee`, 우대 `ice`, `cave` (얼음 호수·동굴 물고기) |
| `decor_light_neon_pink` | 네온 핑크 | Light | 7000 | 0.03 | 48×24 | — | 색 `#ffd0ee`, 우대 희귀도 `Epic` 이상 (영웅·전설 물고기) |
| `decor_light_neon_cyan` | 네온 시안 | Light | 7000 | 0.03 | 48×24 | — | 색 `#d0f6ff`, 우대 `ocean` (먼바다 물고기) |
| `decor_ship` | 침몰선 | Back | 12000 | 0.05 | 128×72 | — | — |
| `decor_driftwood` | 유목 | Back | 2500 | 0.03 | 108×72 | — | — |
| `decor_coral_branch` | 가지 산호 | Back | 4000 | 0.04 | 82×68 | — | — |
| `decor_cabomba` | 카봄바 | Back | 1800 | 0.03 | 54×100 | 4 / 3 | `plant` |
| `decor_wheel` | 물레방아 | Mid | 9000 | 0.05 | 56×52 | 6 / 8 | `wheel` |
| `decor_chest` | 보물상자 | Mid | 7000 | 0.05 | 40×44 | — | `chest` |
| `decor_coral_brain` | 뇌 산호 | Mid | 1500 | 0.02 | 44×28 | — | — |
| `decor_rocks` | 돌탑 | Mid | 800 | 0.02 | 52×36 | — | — |
| `decor_bubbler` | 기포기 | Mid | 3500 | 0.03 | 36×32 | — | `bubbler` |
| `decor_fern` | 자바 펀 | Front | 1000 | 0.02 | 40×36 | 4 / 3 | `plant` |
| `decor_rotala` | 로탈라 | Front | 1500 | 0.03 | 40×40 | 4 / 3 | `plant` |
| `decor_coral_fan` | 부채 산호 | Front | 2500 | 0.03 | 36×40 | — | — |

슬롯 (`col`, `row` = `aquarium_back.png` 캔버스 px, 실제 위치는 사용 중인 수조의 `AquaLayout`이 우선; 최대 크기는
`AquaTank.S`의 종류별 값: 조명 48×24, 뒤쪽 128×100, 가운데 56×52, 앞쪽 40×40):

| 슬롯 id | 종류 | `col` | `row` | 열리는 수조 단계 `level` | 정렬 `order` |
|---|---|---|---|---|---|
| `light` | Light | 320 | 86 | 0 | 42 |
| `back_c` | Back | 338 | 261 | 0 | 3 |
| `mid_l` | Mid | 314 | 266 | 0 | 5 |
| `mid_r` | Mid | 412 | 268 | 0 | 6 |
| `front_l` | Front | 124 | 274 | 0 | 31 |
| `front_c` | Front | 364 | 274 | 0 | 31 |
| `back_l` | Back | 196 | 260 | 1 | 2 |
| `back_r` | Back | 472 | 263 | 2 | 4 |
| `mid_rr` | Mid | 488 | 268 | 3 | 7 |
| `front_r` | Front | 516 | 274 | 4 | 31 |

## 5. 세이브 데이터 (`Assets/Scripts/Core/SaveData.cs`)

세이브는 `JsonUtility`로 `Application.persistentDataPath/<이름>.json`에 씁니다. 이름은 기본 `fishingking_save`이고
`-fksave <이름>`으로 바꿀 수 있습니다(`SaveSystem.PathFile`). 저장은 `.tmp`에 쓴 뒤 바꿔치기합니다(`SaveSystem.Save`).
읽기에 실패하면 새 게임으로 시작합니다(`SaveSystem.Load`). `JsonUtility`는 JSON에 없는 필드에 필드 초기값을 남기므로,
옛 세이브에 없는 새 필드는 아래 "기본값"으로 읽힙니다.

### 5.1 `SaveData`

| 필드 | 형식 | 기본값 | 의미 |
|---|---|---|---|
| `version` | int | 1 | 세이브 형식 번호. **게임 코드 어디에서도 읽거나 올리지 않습니다** (`Assets/Scripts` 검색 결과: 테스트용 JSON 문자열 `AutoPilot.ZoomModes`, `AutoPilot.Music` 에만 나옴). 실제 마이그레이션은 `aquaVer`, `capVer`, `tank.ver`로 함 |
| `coins` | int | 300 | 코인 |
| `level` | int | 1 | 플레이어 레벨 (`Sanitize`: 최소 1) |
| `xp` | int | 0 | 현재 레벨에서 쌓인 XP |
| `ownedItems` | List&lt;string&gt; | 비어 있음 (`NewGame`: 시작 장비 4개 + `tank_0`) | 가진 장비·루어·수조 id. 자연 미끼도 처음 얻을 때 추가됨 (`Game.AddBait`) |
| `baits` | List&lt;BaitCount&gt; | 비어 있음 (`NewGame`: `bait_worm` 10) | 자연 미끼 개수 |
| `rod` / `reel` / `line` / `bait` | string | 시작 장비 id | 장착 중인 장비 (`Sanitize`: 없는 id면 시작 장비로) |
| `tankLevel` | int | 0 | 수조 단계 (`TankDef.level`) |
| `aquarium` | List&lt;CaughtFish&gt; | 비어 있음 | 수조에 있는 물고기 (넣은 순서) |
| `aquariumCollectedAt` | long | 0 (`NewGame`: 지금) | 관람 수입을 마지막으로 거둔 시각 (unix s). 수입은 이후 12 h까지 쌓임 (`AquaCare.IncomeWindow`) |
| `aquariumBank` | int | 0 | 쌓였지만 아직 안 거둔 관람 수입 (코인) |
| `aquaCarry` | double | 0 | 수입의 1코인 미만 나머지 (`AquaCare.Bank`) |
| `aquaVer` | int | 0 | 0 = 먹이 기능 이전 세이브. `AquaCare.Ensure`가 옮기고 1로 올림 (5.4절) |
| `feed` | List&lt;FeedStock&gt; | 비어 있음 | 가진 먹이 |
| `feedTornOnce` | bool | false | 사료 봉투를 한 번 뜯어 봄 (가위 힌트 반복 중지) |
| `tank` | TankCare | 새 `TankCare` | 수조 오염도·도구·장식 (5.3절) |
| `capVer` | int | 0 | 0 = 칸 제도 이전 세이브. `AquaCare.Ensure`가 1로 올림 (5.4절) |
| `unlockedStages` | List&lt;string&gt; | `["lake"]` | 해금한 스테이지 |
| `records` | List&lt;SpeciesRecord&gt; | 비어 있음 | 종별 도감 기록 |
| `legends` | List&lt;LegendRecord&gt; | 비어 있음 | 전설어 조우 기록 |
| `lastStage` | string | `"lake"` | 마지막으로 간 스테이지 (`SceneFlow`가 기록; 지도의 핀 강조와 낚시 씬 기본 스테이지에 씀: `MapScene`, `FishingScene`) |
| `soundOn` | bool | true | 소리 켬: 전체 음소거 (`AudioListener.volume` = 꺼짐 0, 켜짐 전체 볼륨) |
| `musicOn` | bool | true | 배경음 켬 (설정 → 음량 → 배경음, `Music`이 매 프레임 읽음; 이 필드가 없는 옛 세이브도 켬으로 읽힘) |
| `masterVol` · `musicVol` · `sfxVol` · `ambVol` | int | 100 | 설정 → 음량의 전체 볼륨·배경음악·효과음·환경음 (0~100, 10 단위; 설계 믹스에 곱하는 값, 곡선 (p/100)², `AudioMix`; 옛 세이브는 100; `SaveSystem.Sanitize`가 범위를 맞춤) |
| `reelRing` | bool | true | 릴 원을 그릴 때 원과 방향 화살표 표시 |
| `reelReverse` | bool | false | 반시계방향이 감기 (기본: 시계방향) |
| `zoomMode` | int | 0 | 캐스팅 후 줌: `ZoomMode` `X125` = 0 (1.25배, 기본이자 옛 세이브 값), `Off` = 1, `X150` = 2, `Active` = 3 (`Sanitize`: 0..3 밖이면 0) |
| `totalCaught` | int | 0 | 지금까지 잡은 수 |
| `totalEarned` | int | 0 | 지금까지 번 코인 (판매, 관람 수입) |
| `tutorialDone` | bool | false | 첫 낚시 튜토리얼을 보여 줌 (`FishingScene`) |
| `sweepHint` | bool | false | 낚싯대 스윕 1회 힌트를 보여 줌 |
| `sideHint` | bool | false | 사이드 프레셔 1회 힌트를 보여 줌 |
| `clockMin` | float | 600 (10:00) | 게임 시계, 00:00부터 게임 분 (`Sanitize`: NaN/무한대면 600, 0..1439.99로 자름) |
| `clockDay` | int | 1 | 게임 날짜 (`Sanitize`: 최소 1) |
| `timeHint` | bool | false | 첫 시간대 변경 힌트를 보여 줌 |
| `tideHint` | bool | false | 바다 물때 힌트를 보여 줌 |
| `driftHint` | bool | false | 계곡 흘림 힌트를 보여 줌 |
| `mendHint` | bool | false | 멘딩 힌트를 보여 줌 |
| `pinHint` | bool | false | 찌가 물살에 밀려 화면 가장자리에 멈췄다는 1회 힌트를 보여 줌 |
| `worldSeed` | int | 0 (`NewGame`: 새로 뽑음) | 호수 생성 지형의 세계 시드 ([terrain_depth_spec.md](terrain_depth_spec.md) 12절). 새 게임 = 새 호수. 0은 지형 이전 세이브라는 뜻이고 `Game.Boot`가 한 번 뽑아 저장 |
| `lieHint` | bool | false | 찌가 누웠다는 1회 힌트("찌가 누웠어요! 미끼가 바닥에 닿았다는 뜻이에요 — 찌 수심을 줄이면 다시 서요")를 보여 줌 |

### 5.2 목록 원소

**`CaughtFish`** (잡은 물고기 한 마리; 판매 대기와 수조 양쪽에 씀)

| 필드 | 형식 | 의미 |
|---|---|---|
| `uid` | string | 12자 임의 id (`Game.MakeCatch`: `Guid` 앞 12자) |
| `speciesId` | string | 어종 id (`Sanitize`: 없는 종은 수조에서 뺌) |
| `sizeCm` | float | 지금 길이 (cm, 소수 첫째 자리). 수조에서 자라면 바뀜 |
| `weightKg` | float | 지금 무게 (kg, 소수 둘째 자리) |
| `value` | int | 지금 가격 (코인) |
| `stageId` | string | 잡은 스테이지 (조명 우대에 씀) |
| `caughtAt` | long | 잡은 시각 (unix s) |
| `baseCm` / `baseKg` / `baseValue` | float / float / int | 수조에 넣을 때의 길이·무게·가격 (성장 기준) |
| `addedAt` | long | 수조에 넣은 시각 (unix s) |
| `careAt` | long | 마지막 실시간 갱신 시각 (unix s) |
| `fedDays` | float | 배부른 상태로 쌓은 날 수 (성장에 씀) |
| `fullness` | float | 포만감 0..1 |
| `premiumShare` | float | 배 속 먹이 중 고급 사료 비율 0..1 |

**`BaitCount`**: `id` (자연 미끼 id), `count` (개수).

**`SpeciesRecord`**: `id` (어종 id), `caught` (잡은 수), `bestCm` (최대 길이).

**`LegendRecord`**: `id` (전설어 id), `seen` (접근 단계까지 본 횟수), `fails` (실패 횟수), `pity` (다음 조우 시작 관심도
보너스: 실패마다 `pityStep`만큼 오르고, 낚으면 0).

**`FeedStock`** (`AquaCare.cs`): `id` (먹이 id), `bags` (뜯지 않은 봉투; 열린 봉투가 없을 때 선반의 봉투 포함),
`portions` (열린 봉투에 남은 분량, float), `pieces` (생먹이 남은 조각), `open` (생먹이 그릇 뚜껑이 열림).

### 5.3 `TankCare` (`AquaTank.cs`)

| 필드 | 형식 | 기본값 | 의미 |
|---|---|---|---|
| `ver` | int | 0 | 0 = 아직 설정 안 됨. `AquaTank.Ensure`가 1로 올림 |
| `at` | long | 0 | 오염도 마지막 실시간 갱신 (unix s) |
| `algae` / `debris` / `dirt` | float | 0 | 유리 이끼 / 떠다니는 찌꺼기 / 바닥 때, 0 (깨끗) .. 1 (더러움) |
| `algaeMap` / `dirtMap` | string | `""` | 픽셀별 이끼·때 맵. 6비트 값 run-length + base64 (`AquaTank.Encode`), `""` = 맵 전체가 평균값과 같음 |
| `owned` | List&lt;string&gt; | 비어 있음 | 산 도구와 장식 id (`tool_sponge`는 무료로 항상 들어감) |
| `places` | List&lt;DecorPlace&gt; | 비어 있음 | 슬롯 → 장식 (`DecorPlace`: `slot`, `item`) |
| `cleans` | int | 0 | 수조 전체 청소 횟수 (반짝반짝!) |

맵은 수조 크기마다 픽셀 수가 달라서(4.1절 유리·바닥 맵), 다른 크기로 저장된 맵은 읽을 때 길이로 원래 수조를 찾아
최근접 보간으로 다시 맞춥니다(`AquaTank.DecodeAny`, `Resample`).

### 5.4 버전 · 마이그레이션

세이브를 읽을 때와 수조를 쓸 때 다음 순서로 고칩니다. 옛 세이브를 버리는 경우는 없습니다.

1. **`SaveSystem.Sanitize`** (읽을 때마다)
   - null 목록(`ownedItems`, `baits`, `aquarium`, `records`, `legends`, `unlockedStages`)을 빈 목록으로.
   - 시작 장비 4개와 `tank_0`을 `ownedItems`에, `lake`를 `unlockedStages`에 없으면 추가.
   - 장착 id가 없는 장비면 시작 장비로 되돌림.
   - 수조에서 null이거나 없는 어종인 물고기를 뺌.
   - `aquariumCollectedAt` ≤ 0이면 지금으로, `level` 최소 1, `clockMin`·`clockDay`·`zoomMode` 범위 보정(5.1절).
2. **`AquaTank.Ensure`** (`AquaCare.Ensure`에서 먼저 호출)
   - `tank`가 없으면 새로 만듦. `tank.ver` < 1이면 깨끗한 상태(모든 오염도 0, 맵 `""`, `at` = 지금)로 두고 `ver` = 1.
   - 스펀지를 항상 `owned`에 넣고, 오염도를 0..1로 자르고(NaN → 0), `places`에서 없는 장식·없는 슬롯·종류가 안 맞음·
     안 가진 장식·같은 장식 두 번·같은 슬롯 두 번을 뺌.
3. **`AquaCare.Ensure`** (`AquaCare.Advance`, `Pending`, `DebugStart`에서 호출)
   - `aquaVer` < 1 (먹이 기능 이전): 옛 공식으로 쌓였던 수입(물고기마다 `max(1, round(value × 0.006))` 코인/분,
     `AquaCare.OldIncome`, 마지막 수거 뒤 최대 12 h)을 `aquariumBank`에 넣고, 모든 물고기의 지금 크기·무게·가격을 기준값으로
     삼아 포만감 0.6(`AquaCare.StartFullness`)에서 시작시킵니다(`addedAt` = `caughtAt`, 없거나 미래면 지금). 그 뒤 `aquaVer` = 1.
   - 기준값이 없는 물고기(`baseCm` ≤ 0)는 지금 기준으로 초기화.
   - `capVer` < 1 (칸 제도 이전): `capVer` = 1로 올리고 한도를 넘는 물고기를 로그로만 남깁니다. **물고기는 빼지 않습니다.**
     넘는 물고기는 `AquaTank.Over`로 표시되고, 자리가 생길 때까지 새 물고기를 넣을 수 없습니다.
4. **`zoomMode`**: 설정이 생기기 전 세이브는 필드가 없어 0 = `X125`로 읽힙니다(`ViewZoom.cs` 주석). 별도 변환 코드는 없습니다.
5. **루어 id**: 루어 세분화 전의 id(`bait_spoon`, `bait_minnow`, `bait_frog`, `bait_jig`)를 그대로 써서 옛 세이브의
   `ownedItems`가 유효합니다.

6. **`worldSeed`** (호수 생성 지형): 지형 이전 세이브는 필드가 없어 0으로 읽힙니다. `Game.Boot`가 `SaveSystem.Load` 바로 뒤에
   `SaveSystem.EnsureWorldSeed`로 한 번 뽑아(`SaveSystem.NewSeed`: GUID 해시, 0 아님, `UnityEngine.Random` 아님) 곧바로 저장하므로
   그 세이브의 호수는 그때부터 같습니다(로그 `[BATHY] world seed minted for an old save`). 버전 번호는 올리지 않습니다
   (`aquaVer`/`capVer`와 같은 방식). 테스트용 `-fkbathyseed <n>`(그리고 `-fkauto`면 1)은 세이브에 쓰지 않습니다.

`Game.Boot`가 `SaveSystem.Load`를 부르고(위 6번의 세계 시드만 고침), `Game.ResetProgress`와
`-fkfresh`가 `SaveData.NewGame()`으로 새로 시작합니다(새 세계 시드 = 새 호수). 옛 세이브 변환 테스트는 `-fkaqua migrate` 등
([testing.md](testing.md))입니다.

## 사양서와 달라진 점

코드가 기준입니다. 아래는 사양서가 코드와 다른 곳입니다.

- **[lures_legend_spec.md](lures_legend_spec.md) 1.2 / 1.6** — 트롤링 루어 `bait_kona`의 좋은 감기 범위: 사양서 1.6–2.6 rev/s,
  코드 `reelBand = new Vector2(1.6f, 5f)` (`GameDatabase.BuildItems`).
- **[lures_legend_spec.md](lures_legend_spec.md) 1.6** — 필드 목록에 덮침 창 필드(`restStrike`, `fallStrike`, `landStrike`,
  `burstStrike`, `pauseStrike`)가 없지만 코드 `BaitDef`에는 있고, 루어 행에도 값이 들어 있습니다(3.5절).
- **[lures_legend_spec.md](lures_legend_spec.md) 1.7** — `blue_marlin` 선호: 사양서 `kona:0.8,jig:0.6,squid:0.6,golden:1`,
  코드 `kona:1,jig:0.5` (`Data/Fish/blue_marlin.json`). 코드 값은 [legends_rollout.md](legends_rollout.md) 1.6과 같습니다.
- **[lures_legend_spec.md](lures_legend_spec.md) 2.1** — `Verb` enum: 사양서 `{ Wind, FlickPause, Hold }`, 코드는 `RunPause`가 더 있음
  (`Models.cs`). "`encounter`가 null인 나머지 전설 5종"이라는 설명과 달리 코드에서는 전설 6종 모두 `encounter`가 있습니다.
- **[lures_legend_spec.md](lures_legend_spec.md) 2.1** — 실러캔스 루어 빛 반경: 사양서 야광 1.6 m / 일반 1.0 m, 코드 `lightGlow = 3.0f`,
  `lightPlain = 2.0f` (`LegendEncounters.Coelacanth`, 주석에 이유가 적혀 있음).
- **[legends_rollout.md](legends_rollout.md) 3.2** — 피라루쿠 `noseDist`: 사양서 1.0 m, 코드 `0.8f` (`LegendEncounters.Arapaima`).
  위에서 본 시점(`TopViewDef` 기본값): 사양서 그림자 불투명도 0.75→0.2 (1.8 m), 흐림 0.6→3.2 px, 경계 타원 1.9×0.62 m·1.1 m 아래,
  호기심 1.25×0.42 m·0.6 m 아래, 흥분 −35°, 코 들이밀기 0.25 m (−50°) / 코드 `shadowAlpha` (0.85, 0.25), `shadowDeep` 2.0,
  `shadowSoft` (0.6, 3.0), `waryPath` (2.6, 0.45, 1.3), `curiousPath` (1.5, 0.35, 0.6), `hover` (0.45, −18), `noseIn` (0.22, −30)
  (`Models.cs` `TopViewDef`).
- **[legends_rollout.md](legends_rollout.md) 3.4** — 청새치 기분 `경계` 감기 범위: 사양서 1.6–2.6 rev/s, 코드 `lo = 1.6f, hi = 9f`.
  `호기심` 달리기 범위: 사양서 1.8–3.0 rev/s, 코드 `lo = 1.8f, hi = 9f`. 눈 시작점: 사양서 (2.5, −4.5, 9.0), 코드
  `eyes0 = (2.5, -3.6, 9.0)` (`LegendEncounters.BlueMarlin`).
- **[legends_rollout.md](legends_rollout.md) 3.5** — 백상아리 눈 이동: 사양서 (1.5, −4.0, 8.0) → (1.0, −2.8, 6.5), 코드
  `eyes0 = (1.6, -2.9, 9.5)`, `eyes1 = (1.4, -2.1, 5.6)` (`LegendEncounters.GreatWhite`).
- **[legends_rollout.md](legends_rollout.md) 2** — "공통" 목록에 없는 `moodHold` 2.5가 `LegendEncounters.Row`로 5종에 들어가 있습니다
  (실러캔스는 0).
- **[time_currents_spec.md](time_currents_spec.md) 6.2** — 활동도 표 36행은 어종 파일의 `activity`와 모두 같습니다(다른 점 없음). 그 뒤에 더한 호수 6종(떡붕어·끄리·누치·동자개·뱀장어·강준치)은 표에 없고 어종 파일에만 있습니다(2.4절).
- **[time_currents_spec.md](time_currents_spec.md) 13** — 세이브 필드 6개는 코드와 같습니다. 다만 13절은 시계 필드만 다루고,
  `SaveData`에는 그 뒤로 수족관(`aquaVer`, `feed`, `feedTornOnce`, `tank`, `capVer`, `aquaCarry`), 줌(`zoomMode`),
  힌트(`sweepHint`, `sideHint`) 필드가 더 생겼습니다(5.1절).
- **[obstacles_spec.md](obstacles_spec.md) 7.1 / 7.5** — 줄 `tough` 6개는 코드와 같습니다. 커버는 어종 파일의 `cover` 27종이고(호수 신종 떡붕어·동자개·뱀장어 포함), 사양서와 달리 우럭·감성돔에 `rock`, 가물치·피라루쿠에 `weed`가 없습니다(그 스테이지에 그 커버 구역이 없어 한 번도 맞지 않던 종류라 데이터 파일로 옮길 때 뺌; 2.3절). 방어 `hull, rock`, 만새기 `weed`, 참다랑어 `hull, weed`도 데이터 그대로입니다. 사양서의 얼음 종
  `reach` "-"는 코드에서 `0f` (주석: "0 = the ice hole's rim")입니다.
- **[README.md](../README.md)** — "스테이지 7곳", "어종 42종", "루어 10종 · 액션 5가지", "전설어 조우 6종", "5단계 수조"는 코드와
  같습니다.
