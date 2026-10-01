# 데이터 표 (스테이지 · 어종 · 장비 · 수족관 · 세이브)

- **이 문서가 다루는 것**: 코드에 들어 있는 정적 게임 데이터 전체(스테이지 7, 어종 36, 낚싯대·릴·줄 각 6, 미끼 8·루어 10, 수조 5, 사료 4, 장식 17, 청소 도구 3, 장식 슬롯 10)와 각 열의 단위·의미, `SaveData`의 모든 필드와 버전·마이그레이션 동작
- **관련 코드**: `Assets/Scripts/Data/GameDatabase.cs`, `Assets/Scripts/Data/Models.cs`, `Assets/Scripts/Data/TimeActivity.cs`, `Assets/Scripts/Core/SaveData.cs`, `Assets/Scripts/Core/Game.cs`, `Assets/Scripts/Core/AquaCare.cs`, `Assets/Scripts/Core/AquaTank.cs` (보조: `Assets/Resources/Data/stage_*.json`, `Assets/Resources/Data/aquarium_tank_*.json`, `Assets/Scripts/Fishing/CurrentField.cs`, `Assets/Scripts/Fishing/FishSpawner.cs`, `Assets/Scripts/Core/GameClock.cs`, `Assets/Scripts/Core/ViewZoom.cs`)
- **관련 문서**: [README](../README.md) · [구조](architecture.md) · [낚시 규칙과 상수](fishing_gameplay.md) · [수족관 규칙과 공식](aquarium.md) · [아트 파이프라인](art_pipeline.md) · [테스트 스위치](testing.md) · [루어·전설어 사양](lures_legend_spec.md) · [전설어 확장](legends_rollout.md) · [시간대·물살](time_currents_spec.md) · [장애물](obstacles_spec.md) · [변경 기록](../CHANGELOG.md)

이 문서는 **값의 목록**입니다. 값이 게임에서 어떻게 쓰이는지(파이트 공식, 입질 확률, 수족관 수입 계산 등)는
[fishing_gameplay.md](fishing_gameplay.md)와 [aquarium.md](aquarium.md)를 보세요. 모든 값은 코드에서 그대로 옮겼고,
"(계산)"이라고 적은 열만 코드의 공식으로 계산한 값입니다.

## 0. 공통 규칙

- 데이터는 모두 `GameDatabase`의 정적 생성자에서 한 번 만들어집니다: `BuildFish()` → `BuildItems()` → `BuildStages()`
  순서이고, 이어서 id 사전(`fishById`, `itemById`, `stageById`)이 채워집니다. 조회는 `GameDatabase.GetFish`, `GetItem<T>`,
  `GetStage`입니다.
- 게임 공간 단위는 미터입니다(`StageLayout` 주석: "Game space is metres"). 모델 주석의 "units"도 미터와 같습니다
  (`ReelDef.lineCap` 주석: "units / m").
- 힘은 kgf, 시간은 초(s), 감기 속도는 초당 핸들 회전수(rev/s)입니다.
- 처음 시작할 때의 장비는 `GameDatabase.StarterRod` = `rod_bamboo`, `StarterReel` = `reel_basic`,
  `StarterLine` = `line_nylon2`, `StarterBait` = `bait_paste`입니다.

### 0.1 희귀도 (`Models.cs` `Rarity`, `RarityInfo`)

| enum | 이름 (`RarityInfo.Names`) | 별 (`Stars` = 순번 + 1) | 기본 XP (`RarityInfo.Xp`) | 수족관 가중치 (`AquaCare.RarityW`) |
|---|---|---|---|---|
| `Common` | 일반 | 1 | 10 | 1 |
| `Uncommon` | 고급 | 2 | 25 | 1.3 |
| `Rare` | 희귀 | 3 | 60 | 1.7 |
| `Epic` | 영웅 | 4 | 150 | 2.2 |
| `Legendary` | 전설 | 5 | 400 | 3 |

잡았을 때 받는 XP는 `Game.RegisterCatch`에서 `round(BaseXp × (0.8 + 0.6 × SizeT(cm)) × (처음 잡은 종이면 2))`입니다.
레벨업에 필요한 XP는 `Game.XpToNext(level) = 60 + level² × 35`이고, 레벨이 오를 때마다 `새 레벨 × 100` 코인을 줍니다
(`Game.AddXp`).

## 1. 스테이지

### 1.1 기본 데이터 (`GameDatabase.BuildStages`, 필드는 `Models.cs` `StageDef`)

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
| `lake` | 고요한 호수 | 낚시를 처음 배우기 좋은 잔잔한 호수 | 1 | 1 | 0 | 1.0 | 1.1 | 8 |
| `stream` | 산골 계곡 | 차갑고 맑은 물이 흐르는 계곡 | 2 | 2 | 1200 | 1.0 | 1.0 | 8 |
| `sea` | 바다 방파제 | 파도가 부서지는 테트라포드 방파제 | 3 | 4 | 6000 | 1.05 | 1.0 | 9 |
| `swamp` | 안개 늪지 | 안개 속에 괴어가 숨어 있는 늪 | 3 | 6 | 15000 | 1.1 | 0.95 | 8 |
| `ice` | 얼음 호수 | 얼음 구멍 아래로 미끼를 내리는 빙어낚시 | 4 | 8 | 35000 | 1.15 | 0.95 | 8 |
| `ocean` | 먼바다 | 배를 타고 나가는 대물 트롤링 | 5 | 11 | 80000 | 1.2 | 0.9 | 7 |
| `cave` | 수정 동굴 | 빛나는 수정 아래 고대어가 잠든 지하 호수 | 5 | 14 | 200000 | 1.25 | 0.9 | 7 |

`lake`는 새 세이브에서 이미 해금되어 있고(`SaveData.unlockedStages` 기본값), `SaveSystem.Sanitize`도 항상 넣어 둡니다.

### 1.2 출현 어종과 가중치 (`StageDef.spawns`)

가중치는 상대값입니다. `FishSpawner.Pick`은 가중치에 시간대 활동도(`TimeActivity.A`, 2.4절)를 곱하고, 희귀 이상은
낚싯대 `luck`, 영웅 이상은 미끼 `rareBoost`를 한 번 더(전설은 한 번 더) 곱합니다. **조우(`encounter`)가 있는 전설 6종은
가중치와 상관없이 일반 물고기로 나오지 않습니다**(`FishSpawner.Pick`: `if (sp.encounter != null) continue;`).

| 스테이지 | 어종 id (가중치) |
|---|---|
| `lake` | `crucian_carp` 40 · `bluegill` 35 · `carp` 16 · `largemouth_bass` 14 · `golden_carp` 1.2 (조우) |
| `stream` | `pale_chub` 40 · `cherry_salmon` 30 · `rainbow_trout` 18 · `mandarin_fish` 8 · `lenok` 3 |
| `sea` | `horse_mackerel` 35 · `mackerel` 30 · `rockfish` 20 · `flounder` 14 · `black_porgy` 8 · `red_seabream` 3 |
| `swamp` | `piranha` 35 · `catfish` 28 · `snakehead` 20 · `arowana` 7 · `arapaima` 1.5 (조우) |
| `ice` | `smelt` 45 · `burbot` 22 · `northern_pike` 18 · `arctic_char` 9 · `sturgeon` 1.5 (조우) |
| `ocean` | `yellowtail` 34 · `mahi_mahi` 26 · `bluefin_tuna` 14 · `ocean_sunfish` 7 · `blue_marlin` 2.5 (조우) · `great_white` 1.2 (조우) |
| `cave` | `cave_tetra` 40 · `crystal_koi` 12 · `anglerfish` 10 · `coelacanth` 2 (조우) |

합계 5 + 5 + 6 + 5 + 5 + 6 + 4 = **36종** (README의 "어종 36종"과 같음). 한 어종은 한 스테이지에만 나옵니다
(`GameDatabase.StageOfFish`는 처음 찾은 스테이지를 돌려줌).

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
- **야광**: 야광 미끼(`glow`)는 동굴이나 얼음에서 감지 거리가 늘어납니다(`FishingController`:
  `if (bait.glow && (Stage.Def.id == "cave" || L.IsIce)) sense += 2f;`).
- **장애물**: 7개 스테이지 모두 `Assets/Resources/Data/obstacles_<id>.json`이 있습니다. 형식은
  [obstacles_spec.md](obstacles_spec.md) 2절.
- **전설어 조우 배경**(`EncounterDef.backdrop` → `GameDatabase.GetSet`): 호수 `lake`, 늪 `swamp`, 얼음 `ice`, 먼바다 `ocean`
  (청새치·백상아리 공유), 동굴 `cave`. 계곡과 방파제에는 전설어가 없습니다.

## 2. 어종 (`GameDatabase.BuildFish`, 필드는 `Models.cs` `FishSpecies`)

### 2.1 열 설명

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
| 점프 스타일 | `jumpStyle` | `Hopper` 짧게 도약 · `Shaker` 공중에서 머리 털기 · `TailWalker` 꼬리로 수면 걷기 (`JumpStyle` 주석). 지정 안 된 종은 기본값 `Hopper` |
| 수심 | `depthMin`, `depthMax` | 선호 수심, 수면 아래 m |
| 커버 | `coverSeek` / `coverReach` / `coverDig` · `coverFor` | 달릴 때 커버로 향하는 빈도 (0 = 안 감) / 커버를 찾는 거리 m (0 = 얼음 구멍 가장자리) / 파고드는 세기 / 쓰는 커버 종류. 표에 없는 종은 `coverSeek` 0 ([obstacles_spec.md](obstacles_spec.md) 7절) |
| 조우 | `encounter` | 전설어 물속 조우 데이터 (null = 일반 출현·입질) |
| 선호 | `baitPrefs`, `actionPrefs` | 문자열 `"worm:1,@twitch:0.8"`: `bait_` 접두사를 뺀 미끼 id와 선호도(0..1), `@` 뒤는 루어 액션 선호도 (`GameDatabase.F`) |
| 먹이 | `diet` | 수족관에서 먹는 먹이 (`Diet` 플래그: 사료 · 생새우 · 정어리), 기본값 `Pellet` |
| 받아먹기 | `feedStyle` | 떨어뜨린 새우·정어리를 먹는 방식: `Grab` 물 중간에서 · `Bottom` 자갈 위에서 · `Surge` 수면으로 솟구쳐 한입에 |

미끼 선호의 실제 매력도는 `FishSpecies.Appeal(b)`입니다: 자연 미끼는 `Pref(id)`, 루어는
`max(Pref(id), 0.7 × actionPrefs[b.action])`.

### 2.2 크기 · 무게 · 가격

| id | 이름 | 스테이지 | 희귀도 | 크기 (cm) | k | 무게 kg (계산) | 기준 가격 | 가격 (계산) | 수조 등급 (계산) |
|---|---|---|---|---|---|---|---|---|---|
| `crucian_carp` | 붕어 | lake | 일반 | 12–35 | 1.6 | 0.03–0.69 | 35 | 21–53 | 소형~소형 |
| `bluegill` | 블루길 | lake | 일반 | 10–25 | 2.0 | 0.02–0.31 | 26 | 16–39 | 소형~소형 |
| `carp` | 잉어 | lake | 고급 | 35–90 | 1.5 | 0.64–10.94 | 150 | 90–225 | 소형~중형 |
| `largemouth_bass` | 큰입배스 | lake | 고급 | 25–60 | 1.4 | 0.22–3.02 | 180 | 108–270 | 소형~중형 |
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

커버 열은 `coverSeek / coverReach / coverDig · coverFor` 순서입니다(`GameDatabase.BuildFish`의 커버 표).
점프 스타일: `Shaker`는 `largemouth_bass`, `rainbow_trout`, `cherry_salmon`, `lenok`, `snakehead`, `arowana`,
`northern_pike`, `arctic_char`, `arapaima`, `TailWalker`는 `blue_marlin`, `mahi_mahi`, 나머지는 `Hopper`입니다.

| id | 힘 (kgf) | 체력 (s) | 속도 (m/s) | 공격성 | 점프 | 점프 스타일 | 수심 (m) | 커버 | 조우 |
|---|---|---|---|---|---|---|---|---|---|
| `crucian_carp` | 1.6 | 6 | 2.0 | 0.3 | 0 | Hopper | 2–7 | — | — |
| `bluegill` | 1.2 | 5 | 2.4 | 0.5 | 0 | Hopper | 1–5 | — | — |
| `carp` | 5.0 | 14 | 2.6 | 0.45 | 0.05 | Hopper | 3–7 | 0.30 / 8 / 1.0 · weed, reed, pad | — |
| `largemouth_bass` | 3.8 | 10 | 3.4 | 0.7 | 0.35 | Shaker | 1–6 | 0.55 / 10 / 1.2 · post, pad, reed, weed, boat, log | — |
| `golden_carp` | 7.0 | 20 | 3.0 | 0.5 | 0.1 | Hopper | 4–7 | 0.45 / 10 / 1.3 · weed, pad, reed | 조우 |
| `pale_chub` | 0.8 | 4 | 3.0 | 0.5 | 0.1 | Hopper | 0.5–3 | — | — |
| `cherry_salmon` | 1.8 | 7 | 3.2 | 0.55 | 0.2 | Shaker | 1–4 | — | — |
| `rainbow_trout` | 3.5 | 11 | 3.6 | 0.6 | 0.4 | Shaker | 1–5 | 0.20 / 8 / 0.8 · rock | — |
| `mandarin_fish` | 3.2 | 12 | 3.0 | 0.65 | 0.05 | Hopper | 2–5 | 0.70 / 8 / 1.4 · rock | — |
| `lenok` | 4.5 | 16 | 3.8 | 0.6 | 0.3 | Shaker | 2–5 | 0.25 / 8 / 1.0 · rock | — |
| `horse_mackerel` | 1.8 | 7 | 3.5 | 0.6 | 0 | Hopper | 1–5 | — | — |
| `mackerel` | 2.4 | 8 | 4.0 | 0.7 | 0 | Hopper | 1–4 | — | — |
| `rockfish` | 3.0 | 9 | 2.2 | 0.4 | 0 | Hopper | 4–7 | 0.75 / 6 / 1.5 · tet, rock | — |
| `flounder` | 3.8 | 12 | 2.4 | 0.45 | 0 | Hopper | 5.5–7.5 | — | — |
| `black_porgy` | 4.5 | 14 | 3.0 | 0.55 | 0 | Hopper | 3–7 | 0.55 / 8 / 1.2 · tet, rock | — |
| `red_seabream` | 8.0 | 20 | 3.6 | 0.6 | 0 | Hopper | 4–7 | 0.20 / 10 / 1.0 · tet | — |
| `piranha` | 2.2 | 6 | 3.8 | 0.8 | 0.05 | Hopper | 1–5 | — | — |
| `catfish` | 5.0 | 13 | 2.4 | 0.4 | 0 | Hopper | 3.5–6 | 0.45 / 8 / 1.1 · root, log | — |
| `snakehead` | 7.0 | 15 | 3.2 | 0.7 | 0.2 | Shaker | 1–5 | 0.65 / 10 / 1.4 · pad, reed, root, log, weed | — |
| `arowana` | 7.5 | 16 | 4.0 | 0.6 | 0.6 | Shaker | 0.5–3 | 0.20 / 8 / 0.8 · root | — |
| `arapaima` | 24 | 35 | 3.2 | 0.6 | 0.3 | Shaker | 2–5.5 | 0.60 / 14 / 1.6 · root, log, weed | 조우 |
| `smelt` | 0.6 | 3 | 3.0 | 0.5 | 0 | Hopper | 1–6 | — | — |
| `burbot` | 4.0 | 12 | 2.2 | 0.4 | 0 | Hopper | 5–7.5 | 0.30 / 6 / 1.0 · rock | — |
| `northern_pike` | 8.0 | 14 | 4.2 | 0.75 | 0.1 | Shaker | 1–5 | 0.55 / 0 / 1.2 · rim | — |
| `arctic_char` | 7.0 | 16 | 3.8 | 0.6 | 0.1 | Shaker | 2–6 | 0.20 / 0 / 1.0 · rim | — |
| `sturgeon` | 30 | 40 | 2.6 | 0.5 | 0.05 | Hopper | 5.5–7.5 | 0.40 / 0 / 1.5 · rim | 조우 |
| `yellowtail` | 9.0 | 16 | 4.5 | 0.7 | 0 | Hopper | 1–6 | 0.40 / 12 / 1.1 · hull | — |
| `mahi_mahi` | 12 | 18 | 5.0 | 0.7 | 0.6 | TailWalker | 0.5–3 | — | — |
| `bluefin_tuna` | 28 | 30 | 5.5 | 0.8 | 0.05 | Hopper | 3–8 | 0.35 / 14 / 1.3 · hull | — |
| `ocean_sunfish` | 16 | 25 | 1.6 | 0.2 | 0 | Hopper | 2–7 | — | — |
| `blue_marlin` | 45 | 45 | 6.0 | 0.8 | 0.7 | TailWalker | 1–6 | — | 조우 |
| `great_white` | 80 | 60 | 5.0 | 0.85 | 0.15 | Hopper | 3–8 | 0.45 / 16 / 1.6 · hull | 조우 |
| `cave_tetra` | 0.8 | 4 | 3.0 | 0.5 | 0 | Hopper | 1–6 | — | — |
| `crystal_koi` | 9 | 18 | 3.2 | 0.5 | 0.1 | Hopper | 2–6 | 0.35 / 10 / 1.0 · crystal | — |
| `anglerfish` | 12 | 20 | 2.4 | 0.6 | 0 | Hopper | 5–7.5 | 0.40 / 8 / 1.1 · rock | — |
| `coelacanth` | 35 | 40 | 2.8 | 0.55 | 0 | Hopper | 5–7.5 | 0.60 / 14 / 1.5 · crystal, rock | 조우 |

### 2.4 선호 미끼 · 활동 시간대 · 수족관 먹이

- **선호 문자열**은 `GameDatabase.F`에 넘기는 원문 그대로입니다(`bait_` 접두사 생략, `@` = 루어 액션:
  `steady` 감기 · `twitch` 저킹 · `topwater` 수면 · `bottom` 바닥 · `vertical` 수직).
- **활동도**는 `TimeActivity` 표의 새벽 / 낮 / 저녁 / 밤 값입니다. 출현 가중치에 곱하고, 입질에는 `sqrt(a)`로 쓰며,
  전설어는 조우 게이지 채우는 속도에 곱합니다(`TimeActivity` 클래스 주석). 표에 없는 id는 모든 시간대 1입니다.
  시간대 경계는 `GameClock.PeriodAt`: 새벽 05:00–08:00, 낮 08:00–17:00, 저녁 17:00–20:00, 밤 20:00–05:00
  (게임 분 300 / 480 / 1020 / 1200).
- **도감 표기 (계산)**은 `TimeActivity.Describe`의 규칙을 적용한 결과입니다: 밤에만 나오면 "밤에만", a ≥ 1.3인 시간대가
  있으면 그 시간대, 모두 0.8~1.2면 "하루 종일", 아니면 가장 높은 시간대.
- **먹이·받아먹기**는 `GameDatabase.Diets` 표입니다. 36종 모두 들어 있습니다(빠진 종은 경고 로그를 남기고 사료로 봅니다).

| id | 이름 | 선호 문자열 | 활동도 (새벽 / 낮 / 저녁 / 밤) | 도감 표기 (계산) | 먹이 | 받아먹기 |
|---|---|---|---|---|---|---|
| `crucian_carp` | 붕어 | `paste:1,corn:0.8,worm:0.7,golden:0.3` | 1.3 / 0.8 / 1.3 / 0.9 | 활동: 새벽 · 저녁 | 사료 | Grab |
| `bluegill` | 블루길 | `worm:1,paste:0.5,shrimp:0.6,spinner:0.6,spoon:0.3,golden:0.3` | 1.0 / 1.3 / 1.0 / 0.4 | 활동: 낮 | 사료 + 생새우 | Grab |
| `carp` | 잉어 | `corn:1,paste:0.9,worm:0.4,golden:0.4` | 1.2 / 0.7 / 1.2 / 1.4 | 활동: 밤 | 사료 + 생새우 | Bottom |
| `largemouth_bass` | 큰입배스 | `minnow:1,crank:1,softworm:1,frog:0.9,popper:0.9,spinner:0.6,spoon:0.6,worm:0.4,golden:0.4,@twitch:0.8,@topwater:0.8,@bottom:0.8,@steady:0.7` | 1.5 / 0.6 / 1.5 / 0.7 | 활동: 새벽 · 저녁 | 생새우 | Grab |
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
개복치 (밤 a = 0).

### 2.5 전설어 조우 요약 (`EncounterDef`)

조우가 있는 종은 6종이고 모두 `GameDatabase.BuildFish` 끝에서 붙습니다(`Coelacanth()`, `GoldenCarp()`, `Arapaima()`,
`Sturgeon()`, `BlueMarlin()`, `GreatWhite()`). 기분(mood)별 수치, 연출(`Choreo`), 문구 전체는 코드와
[legends_rollout.md](legends_rollout.md) 3절을 보세요. 아래는 조건과 장비에 관련된 값만 옮겼습니다.

| 필드 | 뜻 | `coelacanth` | `golden_carp` | `arapaima` | `sturgeon` | `blue_marlin` | `great_white` |
|---|---|---|---|---|---|---|---|
| `backdrop` | 조우 배경 세트 | cave | lake | swamp | ice | ocean | ocean |
| `keyLures` | 유인 미끼와 가중치 | egi 1.0 · softworm 0.6 · jig 0.4 | golden 1.0 · corn 0.5 · softworm 0.4 | frog 1.0 · popper 0.8 | softworm 1.0 · jig 0.5 | kona 1.0 · jig 0.5 | kona 1.0 · jig 0.6 |
| `meterQ` | 게이지를 채우는 품질 | `Lure` | `Still` (softworm만 `Lure`: `keyRules`) | `Lure` | `Lure` | `Lure` | `Crawl` (jig는 `Lure`, `depthMin` 6: `keyRules`) |
| `depthMin` / `depthMax` | 루어 수심 조건 (m, `depthMax` 0 = 끔) | 5.0 / 0 | 4.0 / 0 | 0 / 0.4 | 3.5 / 0 | 0 / 3.0 | 0 / 0 |
| `bottomBand` | 바닥에서 이 거리 안 (m, 99 = 끔) | 1.5 | 0.8 | 99 | 0.8 | 99 | 99 |
| `lurkZMin`–`lurkZMax` / `nearLurk` | 은신처 거리 / 가까움 판정 (m) | 14–30 / 8 | 14–30 / 7 | 10–30 / 7 | 6–14 / 12 | 20–45 / 10 | 20–45 / 10 |
| `lurkDepth` | 은신 수심 (m, 0 = 바닥 근처) | 0 | 0 | 1.2 | 0 | 6 | 6 |
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

공통값(`GameDatabase.Row`, 실러캔스는 같은 값을 직접 지정): `gaugeStart` 20, `pityStep` 10, `pityMax` 30, `decay` 3,
`timeoutStrike` 60, `tell` 0.35, `perfectT` 0.25, `buffer` 0.15. `Row`를 쓰는 5종은 `moodHold` 2.5 (실러캔스는 기본값 0).
`EncounterDef` 기본값: `curiousAt` 40, `excitedAt` 75, `hysteresis` 5, `turnAwayBelow` 15, `earlyGauge` 65.
조우 판정에는 낚싯대 `luck`이 곱해집니다(`LegendWatch`: `Mathf.Min(0.95f, chance × Rod.luck)`).

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

루어는 한 번 사면 계속 쓰고, 줄이 끊어지면 잃습니다(`Game.LoseTackle`). 모두 `packSize` 1, `rareBoost` 1입니다.
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

`Game.cs`에는 마이그레이션 코드가 없습니다. `Game.Boot`가 `SaveSystem.Load`를 부르고, `Game.ResetProgress`와
`-fkfresh`가 `SaveData.NewGame()`으로 새로 시작합니다. 옛 세이브 변환 테스트는 `-fkaqua migrate` 등
([testing.md](testing.md))입니다.

## 사양서와 달라진 점

코드가 기준입니다. 아래는 사양서가 코드와 다른 곳입니다.

- **[lures_legend_spec.md](lures_legend_spec.md) 1.2 / 1.6** — 트롤링 루어 `bait_kona`의 좋은 감기 범위: 사양서 1.6–2.6 rev/s,
  코드 `reelBand = new Vector2(1.6f, 5f)` (`GameDatabase.BuildItems`).
- **[lures_legend_spec.md](lures_legend_spec.md) 1.6** — 필드 목록에 덮침 창 필드(`restStrike`, `fallStrike`, `landStrike`,
  `burstStrike`, `pauseStrike`)가 없지만 코드 `BaitDef`에는 있고, 루어 행에도 값이 들어 있습니다(3.5절).
- **[lures_legend_spec.md](lures_legend_spec.md) 1.7** — `blue_marlin` 선호: 사양서 `kona:0.8,jig:0.6,squid:0.6,golden:1`,
  코드 `kona:1,jig:0.5` (`GameDatabase.BuildFish`). 코드 값은 [legends_rollout.md](legends_rollout.md) 1.6과 같습니다.
- **[lures_legend_spec.md](lures_legend_spec.md) 2.1** — `Verb` enum: 사양서 `{ Wind, FlickPause, Hold }`, 코드는 `RunPause`가 더 있음
  (`Models.cs`). "`encounter`가 null인 나머지 전설 5종"이라는 설명과 달리 코드에서는 전설 6종 모두 `encounter`가 있습니다.
- **[lures_legend_spec.md](lures_legend_spec.md) 2.1** — 실러캔스 루어 빛 반경: 사양서 야광 1.6 m / 일반 1.0 m, 코드 `lightGlow = 3.0f`,
  `lightPlain = 2.0f` (`GameDatabase.Coelacanth`, 주석에 이유가 적혀 있음).
- **[legends_rollout.md](legends_rollout.md) 3.2** — 피라루쿠 `noseDist`: 사양서 1.0 m, 코드 `0.8f` (`GameDatabase.Arapaima`).
  위에서 본 시점(`TopViewDef` 기본값): 사양서 그림자 불투명도 0.75→0.2 (1.8 m), 흐림 0.6→3.2 px, 경계 타원 1.9×0.62 m·1.1 m 아래,
  호기심 1.25×0.42 m·0.6 m 아래, 흥분 −35°, 코 들이밀기 0.25 m (−50°) / 코드 `shadowAlpha` (0.85, 0.25), `shadowDeep` 2.0,
  `shadowSoft` (0.6, 3.0), `waryPath` (2.6, 0.45, 1.3), `curiousPath` (1.5, 0.35, 0.6), `hover` (0.45, −18), `noseIn` (0.22, −30)
  (`Models.cs` `TopViewDef`).
- **[legends_rollout.md](legends_rollout.md) 3.4** — 청새치 기분 `경계` 감기 범위: 사양서 1.6–2.6 rev/s, 코드 `lo = 1.6f, hi = 9f`.
  `호기심` 달리기 범위: 사양서 1.8–3.0 rev/s, 코드 `lo = 1.8f, hi = 9f`. 눈 시작점: 사양서 (2.5, −4.5, 9.0), 코드
  `eyes0 = (2.5, -3.6, 9.0)` (`GameDatabase.BlueMarlin`).
- **[legends_rollout.md](legends_rollout.md) 3.5** — 백상아리 눈 이동: 사양서 (1.5, −4.0, 8.0) → (1.0, −2.8, 6.5), 코드
  `eyes0 = (1.6, -2.9, 9.5)`, `eyes1 = (1.4, -2.1, 5.6)` (`GameDatabase.GreatWhite`).
- **[legends_rollout.md](legends_rollout.md) 2** — "공통" 목록에 없는 `moodHold` 2.5가 `GameDatabase.Row`로 5종에 들어가 있습니다
  (실러캔스는 0).
- **[time_currents_spec.md](time_currents_spec.md) 6.2** — 활동도 표 36행은 코드 `TimeActivity`와 모두 같습니다(다른 점 없음).
- **[time_currents_spec.md](time_currents_spec.md) 13** — 세이브 필드 6개는 코드와 같습니다. 다만 13절은 시계 필드만 다루고,
  `SaveData`에는 그 뒤로 수족관(`aquaVer`, `feed`, `feedTornOnce`, `tank`, `capVer`, `aquaCarry`), 줌(`zoomMode`),
  힌트(`sweepHint`, `sideHint`) 필드가 더 생겼습니다(5.1절).
- **[obstacles_spec.md](obstacles_spec.md) 7.1 / 7.5** — 커버 표 23행과 줄 `tough` 6개는 코드와 같습니다. 사양서의 얼음 종
  `reach` "-"는 코드에서 `0f` (주석: "0 = the ice hole's rim")입니다.
- **[README.md](../README.md)** — "스테이지 7곳", "어종 36종", "루어 10종 · 액션 5가지", "전설어 조우 6종", "5단계 수조"는 코드와
  같습니다.
