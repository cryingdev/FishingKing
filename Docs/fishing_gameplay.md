# 낚시 플레이 시스템

- **이 문서가 다루는 것**: 캐스팅(플릭·얼음 구멍), 입질과 챔질, 파이트 모델(장력·드랙·체력·점프·바늘 털림·줄 끊김·랜딩), 낚싯대 스윕과 사이드 프레셔, 원 그리기 인식, 루어 입력(감기/톡/멈춤), 캐스팅 후 줌, 캐스팅 가이드 화살표, 걷기가 어떻게 구현돼 있는지와 그 상수
- **관련 코드**: `Assets/Scripts/Fishing/FlickCast.cs`, `FightModel.cs`, `SideSlide.cs`, `SideArrow.cs`, `CircleGesture.cs`, `LureInput.cs`, `CastArrow.cs`, `Tackle.cs`, `FishAgent.cs`, `Angler.cs`, `FishingController.cs`, `FishingController.Zoom.cs`, `FishingHUD.cs`, `Assets/Scripts/Core/ViewZoom.cs`, `Assets/Scripts/Core/PointerInput.cs`, `Assets/Scripts/UI/SettingsUI.cs`
- **관련 문서**: [조작 상세](controls.md) · [테스트 스위치](testing.md) · [구조](architecture.md) · [데이터 표](data_reference.md) · [루어와 전설어 조우](lures_legend_spec.md) · [전설어 확장](legends_rollout.md) · [시간대·물때·물살](time_currents_spec.md) · [장애물](obstacles_spec.md) · [README](../README.md) · [변경 이력](../CHANGELOG.md)

플레이어가 보는 조작 설명은 [controls.md](controls.md)에 있습니다. 이 문서는 그 조작이 코드에서 **어떻게 판정되고 어떤 수치로 움직이는지**를 정리합니다.
상태 기계(`FishingController.S`) 전체 흐름과 그리기 순서는 [architecture.md](architecture.md), 낚싯대·릴·줄·미끼의 개별 수치(`castDist`, `hookBonus`, `retrieve`, `dragMax` 등)는 [data_reference.md](data_reference.md)를 보세요.
루어별 리듬 점수(`LureRhythm`, Q), 전설어 조우, 물살·물때, 장애물·밑걸림·커버는 각 사양서가 자세히 다루므로 여기서는 연결 지점만 적습니다.

---

## 1. 입력의 공통 바탕

모든 제스처는 `PointerInput.Samples`(포인터 위치·시각 표본, `Assets/Scripts/Core/PointerInput.cs` `PointerInput.MaxSamples` = 240개, 가득 차면 앞의 1/4을 버림)를 읽습니다.
UI 위에서 시작한 누름(`PointerInput.StartedOverUI`)은 월드 입력이 아니며, `PointerInput.WorldPressed` = `Pressed && !StartedOverUI`입니다.

한 프레임에서 `FishingController.Update`가 입력을 읽는 순서 (`Assets/Scripts/Fishing/FishingController.cs` `FishingController.Update`):

1. `CircleGesture.Update` — 원 그리기. 켜지는 상태: `Waiting`, `Fighting`, `Biting`, `Encounter`, `Snagged` (`gestureOn`)
2. `LureInput.Update` — 톡·탭·멈춤. 켜지는 조건: `Waiting`이면서 채비가 물에 있거나(`Tackle.Mode.Water`) 얹혀 있을 때(`Tackle.Mode.Perched`), 또는 `Encounter`, `Snagged`
3. `SideSlide.Update` — 좌우 밀기. 켜지는 조건(`sweepIn`): 얼음이 아니고, `Waiting`+물속 채비, `Retrieving`, `Fighting`, `Snagged`. `Biting` 중에는 `SideSlide.Freeze()`로 기울기를 그대로 유지
4. 상태별 `Update*` 함수

설정 창 같은 다이얼로그가 열리면(`Dialog.Open`, 파이트·조우 제외) 입력은 무시되고, 캐스팅 준비(`Aiming`) 중이었다면 조용히 `Ready`로 돌아갑니다.

---

## 2. 플릭 캐스팅 (`FlickCast`)

얼음 호수를 제외한 모든 스테이지의 캐스팅입니다. 아래로 당겨 준비(wind-up)한 뒤 위로 튕기며 놓으면, **놓는 순간**의 표본을 분석해 힘(거리)과 방향을 정합니다.

### 2.1 흐름

| 단계 | 코드 | 내용 |
|---|---|---|
| 누름 | `FishingController.UpdateReady` | `Ready`에서 `PointerInput.WorldPressed` → 누른 점(`pressPos`) 기록, `S.Aiming` |
| 당기기 | `FishingController.UpdateAiming` | `pull` = (누른 점 y − 현재 y) / 화면 높이. 최대값 `windMax`가 `FlickCast.WindUpMin` 이상이면 **장전**(`WindUpArmed`, 딸깍 소리 `Sfx.Click`) |
| 자세 | 〃 | 장전됐거나 `pull > 0.012`이면 `aim` 자세. 낚싯대는 손가락 오프셋을 따라감(시각 효과일 뿐, 방향은 놓는 순간 결정). 스무딩 `FishingController.WindTau` = 0.05 s |
| 놓기 | `FishingController.Throw` | `FlickCast.Analyze`로 표본 분석 → 취소 또는 던지기 |
| 던지기 | `FishingController.CastRoutine` | `cast` 자세, 0.08 s 뒤 `Tackle.Launch` (그 사이 미끼는 초릿대에 매달린 채) |

낚싯대가 손가락을 따라가는 각도 (`Assets/Scripts/Fishing/Angler.cs`):

| 상수 | 값 | 뜻 |
|---|---|---|
| `Angler.WindBack` / `WindBackAt` | 62° / 0.25 H | 누른 점 아래 0.25 H에서 수직 뒤로 62° |
| `Angler.WindFwd` / `WindFwdAt` | 25° / 0.10 H | 누른 점 위 0.10 H에서 수직 앞으로 25° |
| `Angler.WindLean` / `WindLeanAt` | 30° / 0.25 W | 옆으로 0.25 W에서 끝이 30° 기울어짐 |
| `Angler.WindLeanOut` | 10° | 손가락이 바로 아래일 때 왼쪽으로 기운 기본값 |

### 2.2 속도 단위

같은 손동작이 어떤 화면에서도 같은 힘이 되도록 단위를 나눕니다 (`FlickCast.UnitsNow`).

| 입력 | 단위 | 픽셀 → 단위 환산 | 최소 속도 | 최대 힘 속도 |
|---|---|---|---|---|
| 터치 | cm/s (화면 위) | `Screen.dpi / 2.54` (dpi를 모르면 화면 높이 = `FlickCast.TouchFallbackHeightCm` 7 cm) | `FlickCast.TouchSpeedMin` 5 | `FlickCast.TouchSpeedFull` 60 |
| 마우스 | DH/s (모니터 높이/초) | `Screen.currentResolution.height` | `FlickCast.MouseSpeedMin` 0.35 | `FlickCast.MouseSpeedFull` 4 |
| 자동 테스트의 가상 포인터 | H/s (창 높이/초) | 창 높이 | 마우스와 같음 | 마우스와 같음 |

가상 터치는 `PointerInput.SimDpi`를 dpi로 씁니다. 놓을 때마다 `Player.log`에 `[Flick]` 줄이 남으므로 실제 기기에서 값을 맞출 수 있습니다([testing.md](testing.md) 참고).

### 2.3 분석 알고리즘 (`FlickCast.Analyze`)

길이는 화면 높이(H), 시간은 초입니다.

1. **현재 위쪽 스트로크 찾기**: 가장 낮은 점(`low`)부터 시작. 꼭대기에서 `FlickCast.StrokeReset`(0.03 H) 이상 다시 내려오면 그 지점부터 새 스트로크.
2. **정지 꼬리 제거**: 놓기 직전 손가락이 멈춰 있던 표본(한 걸음 < `StillStep` 0.002 H)은 끝에서 건너뜀.
3. **속도**: 각 표본에서 최소 `FlickCast.SpeedWindow`(0.05 s) 구간의 변위로 속도를 재고, 수직에서 `FlickCast.MaxUpAngle`(75°)보다 누운 움직임과 아래 방향은 무시. 놓기 전 `FlickCast.PeakMaxAge`(0.3 s) 안의 **최고값**이 `speed`, 스트로크 전체의 최고값은 `speedAny`(취소 판정용).
4. **방향**: 끝에서 `FlickCast.DirWindow`(0.07 s) 동안, 최소 `FlickCast.DirMinSamples`(3)개 표본의 **최소제곱 속도**. 그 구간 이동이 `FlickCast.DirMinTravel`(0.015 H)보다 짧거나 위쪽이 아니면 스트로크 전체(가장 낮은 점 → 끝) 방향으로 대체(`dirFallback`).
5. **판정** (`why`):

| 결과 | 조건 |
|---|---|
| `tap` | 표본 2개 미만 |
| `slow` / `stopped` / `no-upstroke` | `speed < min` (속도가 있으면 slow, 없는데 스트로크가 `StrokeMin` 이상이면 stopped, 아니면 no-upstroke) |
| `twitch` | 스트로크 길이 `travel` < `FlickCast.StrokeMin` (0.015 H) |
| 플릭 | 그 외 |

6. **힘**: `x = InverseLerp(min, full, speed)`, `power = x ^ FlickCast.PowerCurve(1) × clamp01(travel / FlickCast.StrokeFull(0.10 H))` — 짧은 스트로크는 비례해 약해집니다.
7. **조준선**: `FlickCast.AimAngle` — 수직에서 `FlickCast.DirDeadzone`(3°) 이내면 정면, 아니면 `각도 × FlickCast.DirGain(1)`.

`Analyze(..., down: true)`는 y를 뒤집어 같은 분석을 **아래 방향**으로 합니다. 루어 톡(6장)이 이 모드를 씁니다.

### 2.4 취소 조건 (`FishingController.Throw`)

장전(`WindUpArmed`)되지 않았거나 플릭이 아니면 던지지 않고 `Ready`로 돌아갑니다.

| 경우 | 판정 | 반응 |
|---|---|---|
| 조용한 취소 (`quiet`) | 장전됨 ∧ 플릭 아님 ∧ 놓은 위치가 누른 점 아래 `WindUpMin` 이내 ∧ `speedAny < min` — 천천히 제자리로 올려 놓음 | 메시지 없음 |
| 튕김 실패 | 장전됐지만 조용한 취소가 아님 | "위로 튕기듯 올리며 놓아야 던져져요!" + `Sfx.Error` |
| 준비 없이 튕김 | 장전 안 됨 ∧ 플릭 | "먼저 아래로 당겼다가 위로 튕겨요!" |
| 탭 등 | 장전 안 됨 ∧ 플릭 아님 | 메시지 없음 |

`FlickCast.WindUpMin` = 0.07 H.

### 2.5 방향과 거리

- **월드 방향**: `FishingController.YawOnScreenLine` — 화면에서 튕긴 선을 카메라로 역투영해 발밑 물에서 그 선 위에 떨어지는 요(yaw)를 구합니다. 뒤·위에 있는 카메라 때문에 옆으로 던진 것은 화면에서 약 두 배 넓어 보이므로, 이렇게 하면 채비가 손가락이 그은 선 위에 떨어집니다. ±`FlickCast.YawMax`(38°, 화면에선 약 57°)로 제한.
- **거리**: `dist = Lerp(L.zNear + 2.5, rod.castDist, power)` (`FishingController.Throw`)
- **착수 지점 제한**: z는 `[zNear + 1.2, zFar − 1]`, x는 `±(xLim − 0.6)`. 방파제·뱃머리에 가려 안 보이는 발밑 물로 약하게 던지면 `Tackle.PastStand`가 보이는 물까지 밀어냅니다.
- **피드백**: `FishingHUD.ShowCastPower` — "힘 N%", `power ≥ 0.95`면 "최고! 힘 N%"(금색). `FishingHUD.CastFbTime` 1.3 s 동안(`CastFbFade` 0.3 s 페이드). 소리는 `power`에 따라 커지고, `≥ 0.6`이면 `Sfx.Whoosh`, `≥ 0.95`면 초릿대에서 금색 불꽃 (`FishingController.CastRoutine`).
- 바위·말뚝에 맞아 튕기기, 연잎·물가에 얹히기 등 착수 처리는 [obstacles_spec.md](obstacles_spec.md) 4~5장.

### 2.6 얼음 구멍 변형 (`FishingController.UpdateAimingIce`)

얼음 호수에서는 플릭이 아니라 **당긴 길이 = 수심**입니다.

| 항목 | 식 / 값 |
|---|---|
| 당김 비율 `AimPower` | `clamp01(−dy / (Screen.height × 0.3))` — 화면 높이 30%를 당기면 최대 |
| 수심 `AimDepth` | `Lerp(0.6, L.DepthAt(holeZ) − 0.3, AimPower)` m |
| `aim` 자세·조준 점선 | `AimPower > 0.04` |
| 취소 | 놓을 때 `AimPower < 0.08` |
| HUD | 막대 + "수심 N.Nm" (`FishingHUD.Tick`) |

착수 지점은 항상 구멍(`L.holeX`, `L.holeZ`)이고, 착수 후 `Tackle.FloatDepth = AimDepth`. 조준은 14개 점(`dots`)과 목표 링으로 그립니다(`FishingController.DrawAim`). 얼음에서는 바닥·수직 루어만 쓸 수 있고, 다른 루어를 낀 채 들어오면 떡밥으로 바뀝니다(`FishingController.Init`, `LureInfo.IceOk`).

---

## 3. 캐스팅 가이드 (`CastArrow`, 부채꼴)

### 3.1 머리 위 화살표 (`Assets/Scripts/Fishing/CastArrow.cs`)

준비 중(얼음 제외) 모자 위에 뜨는 금색 화살표입니다. 스프라이트는 `Tools/Blender/fk_items.py`의 `castarrow`(`Sprites/UI/cast_arrow_f0..f7`, 26×38 px).

| 상수 | 값 | 뜻 |
|---|---|---|
| `CastArrow.Order` | `Fx.OrderSparkle − 2` | 낚시꾼(50)·앞쪽 낚싯대(53)·매달린 미끼(55) 위, 반짝이(60) 아래 |
| `OffPx` | (1.1, 26.9) px | 모자 중심 → 스프라이트 중심 |
| `LeanPx` | 0.1 px/° | 낚싯대 기울기에 따라 옆으로 이동 (약 ±3 px) |
| `DimFrom` → `DimTo` | 0.3 → 0.6 | 당기는 중 알파 (장전에 가까울수록 밝게) |
| `CastArrow.GlintStep` | 0.09 s | 장전 후 반짝임 프레임(f1..f7) 하나의 시간 |
| `CastArrow.Hold` | 0.27 s | 반짝임 뒤 기본 프레임(f0) 휴식 |
| `CastArrow.Loop` | `GlintStep × 7 + Hold` | 한 루프 |

`FishingController.UpdateAiming`이 매 프레임 `Set(pulled, armed, windMax / WindUpMin)`을 부르고, `Aiming`이 아닌 상태로 바뀌면 즉시 `Hide()`합니다. 위치는 `Angler.HatPos2D`에 붙어 보트 흔들림도 따라가며 항상 정수 픽셀에 놓입니다.

### 3.2 물 위 부채꼴 (`FishingController.DrawFan`)

던질 수 있는 범위를 흐린 점선으로 보여 줍니다: 양쪽 가장자리 ±`YawMax` 선에 각 `FanRay`(8)개, 먼 호에 `FanArc`(13)개. 거리 범위는 `zNear + 2.5` ~ `rod.castDist`. 알파는 기본 0.22, 장전 후 0.32에 바깥으로 흐르는 물결(+0.2 / +0.14). 물속 장애물 윤곽도 같이 켜집니다([obstacles_spec.md](obstacles_spec.md) 9장).

---

## 4. 입질과 챔질

### 4.1 물고기의 접근 (`FishingController.WantsToApproach`, `FishAgent`)

떠도는 물고기(`FishAgent.St.Wander`)는 약 0.8~1.3 s마다(`thinkT`) 접근을 굴립니다. 감지 거리는 루어 `5 + 4Q` m, 미끼 5 m(야광 미끼는 동굴·얼음에서 +2 m), 수심 차 2.5 m 이내.
확률 `p = min(0.95, appeal × line.stealth × stage.biteMult × activity × 0.45 × BiteMult)`이며, `activity`는 루어 `0.15 + 1.25Q`, 미끼는 `Tackle.RelSpeed < 0.4`면 1 아니면 0.35입니다.
`BiteMult`(시간대·물때·물살 위치·구조물, 0.25~2)는 [time_currents_spec.md](time_currents_spec.md) 9.5와 [obstacles_spec.md](obstacles_spec.md) 8장, Q는 [lures_legend_spec.md](lures_legend_spec.md) 1.4~1.5를 보세요.
전설어 게이지 `Watch.Meter ≥ 0.5`이면 일반 물고기는 붙지 않고, 한 번에 한 마리만 채비에 붙습니다(`FishingController.CanFishEngage`).

### 4.2 입질 종류

| 종류 | 대상 | 흐름 (`Assets/Scripts/Fishing/FishAgent.cs`) |
|---|---|---|
| **톡톡 입질**(`St.Nibble`) → 본입질 | 찌 채비(`Tackle.UsesFloat`) 전부, 바닥 루어 중 `needBottom`(소프트 웜; `LureRhythm.StrikeOpen`일 때) | 바늘 0.3 m 안에 오면 시작. 톡톡 횟수 `Random.Range(1, 4)`(1~3) − (공격성 > 0.6이면 1). 한 번 쪼는 시간 0.25 s, 사이 간격 0.45~0.9 s. 매번 `OnNibble`(찌 살짝 잠김 `Tackle.Nibble` dip −2.5, "톡톡... 입질이다! 기다려요"). 다 쪼고 나면 85%로 본입질, 15%는 흥미를 잃음 |
| **스트라이크** → 본입질 | 그 밖의 루어 | 루어 바로 뒤(0.3 m)에서 스트라이크 창(`LureRhythm.WindowId`)이 올 때마다 한 번 굴림: `(0.35 + 0.5 × aggression) × (0.5 + 0.7Q) × StrikeMult` (`FishAgent.UpdateApproach`) |

따라오는 동안 루어 Q < 0.25가 2 s 이어지면 돌아섭니다("따라오다 돌아섰어요", 6 s에 한 번만). 찌가 물을 가르며 `RelSpeed > 1.3`으로 움직이면 초당 0.8 확률로 흥미를 잃습니다. 접근은 14 s가 지나면 포기합니다.

### 4.3 본입질과 챔질 창 (`FishingController.OnBite`, `UpdateBiting`)

본입질(`St.Bite`)이 오면 찌가 쑥 잠기고(`Tackle.BiteDown` dip −7), "!" 표시와 "쑥! 지금 탭해서 챔질!"이 뜨며 `S.Biting`이 됩니다. 물고기는 미끼를 물고 헤엄쳐 가며 찌를 끌고 갑니다.

| 항목 | 값 | 출처 |
|---|---|---|
| 챔질 창 | `0.75 + rod.hookBonus + (찌 채비면 0.1)` s | `FishingController.OnBite` |
| 줄 휨 벌칙 | `bowLate = 0.12 × max(0, |Tackle.Bow| − 1)`; 창 = `max(min(창, 0.45), 창 − bowLate)` (0.45 s 밑으로는 줄이지 않음) | 〃, [time_currents_spec.md](time_currents_spec.md) 9.2 |
| 챔질 입력 | `PointerInput.WorldPressed`(월드를 **누르는 순간**) 또는 `Gesture.Circling`(원 그리기) | `FishingController.UpdateBiting` |
| 줄 파문 | 0.35 s마다 | 〃 |

- **성공** (`FishingController.SetHook`): `Sfx.Hook`, 화면 흔들림(0.12, 0.15), 찌 채비면 미끼 1개 소모(`Game.ConsumeBait`), "걸었다! 원을 그려 릴을 감아요!", 파이트 시작.
- **시간 초과**: 물고기는 도망(`Flee`). 찌 채비는 미끼 소모 + "미끼만 떼먹고 도망갔어요..." → `Retrieving`(자동 회수). 루어는 "놓쳤다! 계속 움직여서 유혹해요" → `Waiting`으로 계속.
- 입질 동안 낚싯대 기울기는 `SideSlide.Freeze()`로 그대로 유지됩니다.

### 4.4 너무 이른 챔질 (`FishingController.UpdateWaiting`)

`Waiting`에서 `LureInput.TapNow`(짧고 거의 안 움직인 **탭을 뗀 순간**, 6장)가 오고, 미끼가 찌 채비이거나 `needBottom` 루어일 때 톡톡 입질 중(`St.Nibble`)인 물고기가 있으면 그 물고기가 도망갑니다.
메시지는 찌 채비 "너무 일찍 챘어요! 찌가 쑥 들어갈 때 탭!", 소프트 웜 "너무 일찍 챘어요! 쑥 끌고 갈 때 탭!" + `Sfx.Error`.

전설어 조우의 챔질(버퍼·완벽한 챔질·전용 창)은 [lures_legend_spec.md](lures_legend_spec.md) 2.9와 [legends_rollout.md](legends_rollout.md)의 전설어별 `hookWindow`를 보세요. 조우에서 걸린 전설어도 같은 `FishingController.BeginFight`로 파이트에 들어갑니다(5.8 참고).

---

## 5. 파이트 모델 (`FightModel`)

`Assets/Scripts/Fishing/FightModel.cs`는 순수 시뮬레이션입니다. 매 프레임 `FishingController.UpdateFighting`이 `Step(dt, revs, nearSurface)`를 부릅니다. `revs` = `CircleGesture.Speed`(부드럽게 한 초당 회전수, 음수 = 풀기), `nearSurface` = 물고기 수심 < 1.6 m.

### 5.1 시작값 (`FishingController.BeginFight`, `FightModel` 생성자)

| 항목 | 값 |
|---|---|
| 물고기 힘 `Power` (kgf) | `sp.power × (0.55 + 0.45 × sizeT) × stage.powerMult` (`sizeT` = 크기 0~1) |
| 처음 줄 길이 `Line` | 초릿대(`Angler.RodTipPlan`)에서 물고기까지 거리 |
| 랜딩 거리 `landDist` | 초릿대에서 랜딩 점(물가: `(Angler.X, 0, zNear + 0.4)`, 얼음: 구멍)까지 거리 + 0.3 m |
| 처음 장력 | `Power × 0.3` |
| 첫 단계 | `Run`, 1.0 s |
| 체력 `Stamina` | 1 (조우에서 완벽한 챔질이면 0.85) |
| 난수 | `seed`로 고정 (같은 시드 = 같은 물고기의 같은 행동, 질주 방향도 `runRnd = seed + 1`) |

낚싯대 스윕은 파이트 시작 때 가운데로 돌아갑니다(`Slide.Reset()`).

### 5.2 단계 전환 (`FightModel.NextPhase`)

단계는 `Run`(질주), `Burst`(돌진), `Rest`(휴식), `Jump`(점프) 네 가지입니다. `phaseTime`이 0이 되면 다음 단계를 정합니다 (`R(a, b)` = a~b 균등 난수, `aggression` = 어종 공격성).

| 이전 단계 | 다음 단계 |
|---|---|
| (지침 `Exhausted`, 체력 ≤ 0.001) | `Rest` 1 s |
| `Rest` | 확률 `0.55 + 0.4 × aggression`로 움직임: 수면 근처이고 `sp.jump × 0.6` 확률이면 **점프**, 아니면 `aggression × 0.35` 확률로 **돌진** 0.5 s, 아니면 **질주** `R(1.0, 2.3) × (0.8 + 0.6 × aggression)` s. 움직이지 않으면 **휴식** `R(0.6, 1.2)` s |
| `Burst` | **질주** `R(0.8, 1.8)` s (같은 질주의 연장으로 취급, 5.6) |
| `Run`, `Jump` | **휴식** `R(0.9, 2.2) × (1.4 − 0.6 × aggression)` s |

테스트 스위치 `-fkjump`(`FightModel.ForceJump`)를 주면 휴식 뒤마다 그 종류로 점프합니다(수심 무관).

### 5.3 힘과 줄 (`FightModel.Step`)

| 단계 | `force` | `swim` |
|---|---|---|
| `Run` | 1 | 1 |
| `Burst` | 1.4 | 1.5 |
| `Jump` | 머리 털 때(`Shaking`) 0.95, 아니면 0.55 | 꼬리 걷기 0.7, 그 외 0.4 |
| `Rest` | 0.22 | −0.12 |
| 지침 | 0.12 | −0.25 |
| 커버에 박힘(`CoverHold`) | 0.6 | 0 |

- 당기는 힘 `F = Power × force × (0.35 + 0.65 × Stamina) × clamp01(0.35 + elapsed / 0.8)` — 걸린 직후 0.8 s 동안 서서히 힘을 씀
- 물고기가 줄을 가져가려는 속도 `u = sp.speed × swim × (0.4 + 0.6 × Stamina)`
- 감는 속도 `v = revs × reel.retrieve` (+ 감을 때 `reel.autoReel`)
- 질주 중 물살·커버 보정: 물살 정렬 `al`에 따라 `F × (1 + 0.5 max(al,0) + 0.3 min(al,0))`, `u × (1 + 0.6 max(al,0) + 0.4 min(al,0))`, 하류 질주면 `u = −0.25 × CurrentSpeed`; 커버로 도망 중(`CoverRun`) `F × 1.2`, `u × 1.15` ([time_currents_spec.md](time_currents_spec.md) 9.7, [obstacles_spec.md](obstacles_spec.md) 7.3)

**감는 중 (`v ≥ 0`)** — 스풀이 잠겨 물고기가 낚싯대에 맞서 당김:
- 목표 장력 `targetT = F + clamp(v / (retrieve × 2), 0, 1.6) × Power × 0.5`
- 줄 변화 `dL = max(0,u) × 0.18 − v × eff + (u < 0 ? u × 0.3 : 0)`, `eff` = 질주 중 `0.3 + 0.5 × clamp01(1 − Power / line.strength)`, 아니면 1 → **도망칠 때 감으면 잘 안 감기고 장력만 오름**

**푸는 중 (`v < 0`)** — 장력이 무너지고 물고기는 자유롭게 헤엄:
- `targetT = F × 0.15`, `dL = −v + max(0,u) × 0.5`

그 뒤 물살 하중 `CurrentLoad + LineDrag`(풀 때는 절반)를 더하고, 사이드 프레셔 배율 `SideTensionMult`(5.6)를 곱합니다.

### 5.4 드랙과 장력

| 항목 | 식 | 출처 |
|---|---|---|
| 드랙 미끄러짐 | `targetT > reel.dragMax`이면 초과분 `excess`만큼 `dL += excess / max(0.5, Power) × sp.speed × 0.6` (줄이 풀려 나감), `targetT = dragMax + excess × (1 − reel.dragSmooth)` | `FightModel.Step` |
| 장력 응답 | `Tension`이 `targetT`로 `1 − exp(−resp × dt)`씩 따라감, `resp = Lerp(10, 4, rod.flex)` (유연한 낚싯대일수록 급변을 누그러뜨림) | 〃 |
| 줄 강도 한계 `LineLimit` | `line.strength × (1 − 0.45 × Abrasion)` (쓸림으로 약해짐) | `FightModel.LineLimit` |
| `TensionRatio` | `Tension / LineLimit` | 〃 |
| 장비 부족 `Outclassed` | `Power > line.strength × 1.25` ∧ `Power > reel.dragMax × 1.25` → 줄이 끊기면 "이 녀석에겐 장비가 약해요..." 추가 | `FightModel.Outclassed`, `FishingController.LineBroke` |

HUD 장력 막대(`FishingHUD.UpdateFight`)는 비율 0.55 미만 초록, 0.85 미만 노랑, 그 이상 빨강 깜빡임입니다. 흰 선 `dragMark`는 `dragMax / LineLimit` 위치(0.99 이상이면 숨김)입니다. 비율 > 0.85면 경고음(`Sfx.Warn`, > 0.97이면 더 빠르고 높게), 장력이 `dragMax × 0.97` 이상이면 드랙 소리(0.05 s 간격)가 납니다.

### 5.5 체력

- 소모: `dt × (0.25 + 0.75 × clamp(work, 0, 1.5)) / sp.stamina × SideDrainMult × (1 − 0.25 × al) × (커버에 박힘 ? 0.5 : 1)`
  - `work = plainTension / max(0.1, Power)` — `plainTension`은 사이드 프레셔 하중과 물살 하중을 **뺀** 장력이라, 사이드 프레셔의 효과는 `SideDrainMult`로만 들어갑니다.
- 회복: `Rest`이고 장력 < `Power × 0.3`이면 초당 +0.02
- 체력 0(`Exhausted`)이면 힘 0.12로 약해지고 물고기 목표 수심은 0.25 m(`FishingController.UpdateFighting`). HUD "지쳤다! 지금 힘껏 감아요!"

### 5.6 점프 (`JumpKind`: Hop / Shake / TailWalk)

점프 종류는 어종의 `jumpStyle`로 정합니다 (`FightModel.StartJump`).

| `jumpStyle` | Hop | Shake | TailWalk |
|---|---|---|---|
| `Shaker` | 30% | 70% | — |
| `TailWalker` | 15% | 25% | 60% |
| 그 외 | 100% | — | — |

| 종류 | 점프 시간 | 머리 털기 횟수 | 바늘 털림 판정 |
|---|---|---|---|
| **Hop**(호핑) | `R(0.7, 0.9)` s | 0 | 장력 > `LineLimit × 0.7`이면 초당 1.2 확률(`dt × 1.2`)로 언제든 |
| **Shake**(머리 털기) | `0.6 + 0.24 × 횟수` s (오름 0.3 s, 털기당 0.24 s, 내려옴 0.3 s) | `2 + rnd.Next(3)` = 2~4 | 털기 정점마다 한 번: 장력 > `LineLimit × 0.6`이면 45% |
| **TailWalk**(꼬리 걷기) | `R(1.8, 2.6)` s | `max(2, floor(시간 × 0.76 / 0.45))` | 털기 정점마다 한 번: 장력 > `LineLimit × 0.6`이면 30% |

- 털기 시점 `ShakeAt`은 공중 구간(Shake: `0.3 / 시간` ~ 끝에서 같은 만큼, TailWalk: 0.12 ~ 0.88)에 고르게 퍼집니다. `Shaking`은 각 시점 ±0.07 s 동안 참이며, 이때 HUD가 "머리를 턴다! 릴 멈춰요!"를 띄웁니다. → **머리 털 때 감기를 멈추면 장력이 내려가 바늘이 안 털립니다.**
- 연출(`FishingController.UpdateJump`): 높이 `1.4 × clamp(몸길이, 0.7, 2.2)`. Hop은 한 번의 호(−40°~40° 기울기), Shake는 공중에 매달려 털 때마다 좌우로 뒤집히며 C자로 휨, TailWalk는 약 72°로 서서 옆으로 0.45 rad 미끄러지며(공간이 넓은 쪽) 물보라 자국(0.06 s 간격)을 남기고 쓰러짐.
- 점프 중에는 사이드 프레셔가 없고, 줄은 공중의 입(`AirMouth`)으로 이어집니다.

### 5.7 끝나는 조건

| 결과 (`FightModel.Outcome`) | 조건 | 처리 (`FishingController`) |
|---|---|---|
| `Landed` | `Line ≤ landDist` | `LandRoutine` |
| `Snapped`(장력) | 장력 > `LineLimit`가 `BreakGrace = 0.3 + rod.flex × 0.6` s 넘게 이어짐 (한계 아래로 내려가면 타이머가 두 배 속도로 줄어듦) | `LineBroke`: "줄이 끊어졌다!", 루어면 루어 분실(`Game.LoseLure`), 미끼는 챔질 때 이미 소모(추가 차감 없음), 찌채비는 빈 바늘로 회수 (FishingController.Breaks.cs) |
| `Snapped`(쓸림) | `Abrasion ≥ 1`, 또는 쓸리는 중(`Rubbing`) 끊김 ∧ `Abrasion > 0.02` | 〃, "줄이 {이름}에 쓸려 끊어졌다!" ([obstacles_spec.md](obstacles_spec.md) 7.4~7.5) |
| `Spooled` | `Line > reel.lineCap` (줄이 다 풀림) | 〃, "줄이 다 풀려서 끊어졌다!" |
| `Escaped`(느슨함) | 장력 < `Power × 0.05`(지치지 않았을 때)가 누적 `SlackLimit = 2.4 + rod.hookBonus × 2` s 초과 (아니면 초당 1씩 줄어듦) | `FishEscaped`: "물고기가 바늘을 털고 도망갔다..." |
| `Escaped`(점프) | 5.6의 바늘 털림 | 〃 |

HUD 경고 우선순위: 끊어지려 함(`BreakRatio > 0.3`, "줄이 끊어지려 한다!!") > 느슨함(`SlackRatio > 0.5`, "줄이 느슨해! 감아요!") > 쓸림 > 커버 > 사이드 프레셔 > 단계 문구 (`FishingHUD.UpdateFight`).

물고기가 빠지거나 줄이 끊겼을 때(`FishingController.FishOff`): 루어는 바로 사라지고 `Ready`. 찌 채비는 찌가 그 자리 물 위에 남았다가(`Tackle.LetGo`) `RetrieveWait` 0.7 s 뒤 자동 회수됩니다. 공중에서 빠지면 물에 떨어지는 물보라(`DropFromAir`).

**랜딩** (`FishingController.LandRoutine`): 0.8 s 동안 낚싯대를 거의 수직으로 세우고(`RodLift01`, 1.6배 빨리) 물고기를 초릿대 아래(`HangPoint` = 초릿대 − (0.35 + 몸길이 × 0.45) m)로 끌어올려 매답니다. 찌 채비는 찌도 줄에 같이 매달립니다(`Tackle.HangFromRod`). 잡은 물고기가 흔들리며 매달린 채 `CardDelay` 1 s 뒤 잡은 물고기 카드(`CatchPopup`: 팔기 / 수조 / 놓아주기)가 뜹니다.

### 5.8 파이트 중 물고기 위치 (`FishingController.UpdateFighting`, `OnPhase`)

- 물고기는 항상 초릿대에서 **줄 길이만큼** 떨어진 점에 놓입니다 (방향 `fightYaw`, 수심).
- 방향 목표 `fightYawTarget`: 질주/돌진이면 현재에서 `0.3 + 0.4 × 난수` rad 한쪽으로(공간이 0.2 rad 미만이면 반대쪽), 얼음에선 ±0.7 rad 무작위. 휴식이면 ±0.15 rad. 좌우 한계 `MaxFightYaw` = `asin((xLim − 0.5) / 거리)`, 최대 0.8 rad (얼음 π).
- 계곡에서는 새 질주의 35%가 하류 질주(`DownstreamRun`) — [time_currents_spec.md](time_currents_spec.md) 9.7.
- 방향 이동 속도: 질주 0.45, 꼬리 걷기 0.35, 그 외 0.15 rad/s. 수심 이동: 질주 1.2, 그 외 0.5 m/s. 휴식 목표 수심 `max(0.4, 현재 − 0.8)`.
- 커버를 찾는 어종은 질주를 커버 쪽으로 틉니다(`TryCoverRun`, [obstacles_spec.md](obstacles_spec.md) 7장).
- 전설어는 조우에서 걸리면 `FightModel.Hold(0.5)`로 0.5 s 동안 장력 0·줄 고정으로 멈춰 있다가 시작합니다(손가락 준비 시간).

---

## 6. 원 그리기 인식 (`CircleGesture`)

화면 어디서나 원을 그리면 릴 회전으로 바뀝니다 (`Assets/Scripts/Fishing/CircleGesture.cs`). 시계방향 = 감기(+)이고, 설정 "원을 그려 감는 방향"(`SaveData.reelReverse`, `CircleGesture.Reversed`)으로 반대로 바꿀 수 있습니다.

### 6.1 알고리즘

1. 누르고 있는 동안 표본(위치, `Time.unscaledTime`)을 쌓고, `SlowWindow`(1.5 s)보다 오래된 표본은 버립니다(최소 4개 유지).
2. **짧은 창**: 최근 `Window`(0.4 s) 표본이 6개 이상이면 **Kasa 대수적 원 맞춤**(최소제곱, 수치 안정을 위해 평균을 뺀 뒤 3×3 연립방정식을 크래머 공식으로 풂)으로 중심·반지름을 구합니다.
3. 합격 조건: 반지름이 `Screen.height × 0.025` ~ `× 0.55`, 표본들이 중심을 둘러싼 각도 합(`Coverage`)이 한 바퀴의 0.35 이상 (직선 끌기 거부).
4. **느린 원 대체**: 짧은 창이 실패하면 전체 `SlowWindow`(1.5 s) 표본으로 다시 맞추고 `SlowCoverage`(0.3) 이상이면 합격 — 루어를 약 0.2 rev/s로 천천히 감는 경우용.
5. 합격하면 중심을 부드럽게 옮기고(원 그리는 중이면 0.35씩, 처음이면 바로), 중심 기준 각도 변화 `d`로 `FrameRevs = −d / 360` (화면 y가 위쪽이므로 시계방향 = 음의 각도 변화).
6. **이어 그리기(sticky)**: 손가락을 떼지 않고 멈췄다가 다시 돌리면 창이 정지 표본으로 가득해 맞춤이 한참 실패합니다. 그래서 마지막으로 돌던 원(중심·반지름)을 `StickyT`(3 s) 동안 기억하고, 정지(1 px 미만 이동, `StillPx`) 후 `SlowWindow` 안에 그 원 위(반지름 ±35%, `StickyBand`)를 움직이면 바로 그 원 기준으로 회전을 셉니다. 0.5°(`StickyMinDeg`) 넘게 움직여야 원 그리기로 인정.
7. 한 프레임 최대 회전: `MaxRevsPerSec`(4.5) × `max(dt, 1/120)`.
8. `Speed`(초당 회전)는 시간상수 0.12 s로 부드럽게, 손을 떼면 0.08 s로 0에 수렴합니다. 파이트는 이 `Speed`를 씁니다.

### 6.2 키보드

| 키 | 값 |
|---|---|
| Space / R 누르고 있기 | +2.6 rev/s (감기) |
| B 누르고 있기 | −1.5 rev/s (풀기) |

키 입력은 `Circling` 없이 `FrameRevs`만 만듭니다.

### 6.3 감기가 채비에 미치는 영향

- 대기 중: `revs × reel.retrieve` m만큼 `Tackle.Wind` (얼음은 `Tackle.Raise`로 들어 올리기만). 루어는 `Bait.windLift × 감은 거리`만큼 수심이 변함. 채비가 발밑(`z ≤ zNear + 0.3`, 얼음은 수심 ≤ 0.05)에 오면 회수 완료 (`FishingController.WindTackle`).
- 자동 회수(`회수` 버튼, `S.Retrieving`): 초당 `reel.retrieve × 3.2 × 2` m (얼음은 `× 1.5`로 들어 올림) (`FishingController.UpdateRetrieving`).
- 릴 째깍 소리는 0.25회전마다 (`FishingController.ReelTicks`).

---

## 7. 루어 입력 (`LureInput`: 감기 / 톡 / 멈춤)

루어와 전설어 조우의 세 동사입니다(`Assets/Scripts/Fishing/LureInput.cs`). 사양은 [lures_legend_spec.md](lures_legend_spec.md) 1.1, 루어별 리듬 점수(`LureRhythm`)는 같은 문서 1.4를 보세요.

| 상수 | 값 | 뜻 |
|---|---|---|
| `LureInput.WindMin` | 0.1 rev/s | `CircleGesture.Speed`가 이 이상이면 **감기** |
| `LureInput.FlickMaxTime` | 0.35 s | 톡·탭의 최대 누름 시간 |
| `LureInput.FlickMinTravel` | 0.05 H | 톡의 최소 순 **아래** 이동 |
| `LureInput.FlickMaxAngle` | 60° | 수직 아래에서 벗어날 수 있는 각도 |
| `LureInput.TapMaxTravel` | 0.02 H | 탭: 시작점에서 가장 멀리 간 거리의 상한 |
| `LureInput.KeyWeak` / `KeyStrong` | 0.45 / 0.9 | 키보드 T / Shift+T 톡 세기 |

**떼는 순간 분류** (`LureInput.Classify`, 순서대로):

| 판정 | 조건 |
|---|---|
| `tap` | 시간 ≤ 0.35 s ∧ 최대 이동 < 0.02 H → `TapNow` |
| `circled` | 누르는 동안 한 번이라도 `Gesture.Circling` |
| `long` | 시간 > 0.35 s |
| `up` | 아래 이동 부족 ∧ 위로 0.05 H 이상, 수직 위 60° 이내 — 위로 쓸기는 톡이 아님(캐스팅 동작). 대기 중 루어면 "톡은 아래로 당겨요!"(6 s에 한 번) |
| `short` | 아래 이동 < 0.05 H |
| `angle` | 수직 아래에서 60° 초과 |
| `slow` | `FlickCast.Analyze(down: true)`의 최고 속도 < 최소 속도(터치 5 cm/s) |
| 톡 | 그 외. 세기 = `InverseLerp(min, 0.5 × full, speed)` — 터치 5~30 cm/s → 0~1 |

- **멈춤**: `PauseT` = 마지막 감기·톡 이후 시간 (손가락을 화면에 가만히 올려 둔 것도 멈춤). `WindRunT` = 이번 감기가 이어진 시간.
- **톡의 효과** (`Tackle.Twitch`): 루어가 `hopM × (hopBase + (1 − hopBase) × 세기)`만큼 뛰고, `pullM`만큼 끌려오고, `dartM`만큼 좌우로 번갈아 튐. `Tackle.TwitchTime` 0.14 s에 걸쳐 움직인 뒤 가라앉음. 물살 배율 `clamp(1 + c_along, 0.5, 1.5)` ([time_currents_spec.md](time_currents_spec.md) 9.4). 수면 루어는 물보라+"퐁", 저킹·수직 루어는 반짝임.
- **낚싯대 채기** (`FishingController.StartRodJerk` / `RodJerk`): 진폭 `JerkMin + JerkGain × 세기` = 0.35~0.85. 올라감 `JerkRise` 0.06 s(ease-out) → 유지 `JerkHold` 0.05 s → 내려옴 `JerkFall` 0.28 s(smoothstep). 채는 도중 새 톡은 현재 위치에서 다시 올라감. 랜딩·결과 중에는 없음.
- 톡은 얹힌 채비를 떨어뜨리고 밑걸림을 푸는 데도 쓰입니다([obstacles_spec.md](obstacles_spec.md) 5.4, 6.5).

---

## 8. 낚싯대 스윕과 사이드 프레셔 (`SideSlide`)

### 8.1 좌우 밀기 인식 (`Assets/Scripts/Fishing/SideSlide.cs`)

화면에서 **곧게 옆으로 민** 스트로크가 낚싯대를 옆으로 기울입니다. 기울기는 **붙어 있어서**(sticky) 손을 떼도 유지되고, 같은 손가락으로 곧바로 원을 그려 감을 수 있습니다. 반대로 밀어야 돌아옵니다.
W = 화면 폭입니다.

| 상수 | 값 | 뜻 |
|---|---|---|
| `SideSlide.MinTravel` | 0.06 W | 확정에 필요한 순 옆 이동 |
| `SideSlide.LiveTravel` | 0.035 W | 이 이상이면 미는 도중에도 낚싯대가 실시간으로 기울어짐 |
| `SideSlide.MaxAngle` | 30° | 스트로크 전체(현)가 수평에서 벗어날 수 있는 각도 |
| `SideSlide.LateAngle` / `LateWindow` | 40° / 0.05 s | 최근 0.05 s 방향의 허용 각도 |
| `SideSlide.Bow` | 0.12 | 현에서 가장 먼 표본이 현 길이의 이 비율을 넘으면 "휨"(곧지 않음) |
| `SideSlide.FullTravel` | 0.2 W | 가운데 → 한쪽 끝까지 가는 밀기 길이 |
| `SideSlide.Snap` | 0.2 | 확정 후 |값|이 이보다 작으면 가운데로 |
| `SideSlide.HoldCommit` | 0.2 s | 실시간으로 밀다가 이만큼 멈춰 있으면 그 자리에서 확정하고 새 스트로크 시작(밀고 → 멈추고 → 떼지 않고 원 그리기) |
| `SideSlide.StillMm` | 1.2 mm | 멈춤으로 보는 흔들림 (dpi를 모르면 0.006 W, 최소 3 px) |

- **다른 제스처와 구분**: 원 그리기는 닫힌 고리라 현이 돌아서거나(시작점 뒤로 현 길이의 1/4 넘게 돌아오면 고리로 판정) 제스처가 원을 인식하고, 톡은 30°보다 가파른 **아래** 당김, 탭은 거의 안 움직입니다. 실시간으로 기울던 스트로크가 원으로 바뀌면 그 기울기는 취소됩니다(`Cancels`).
- 거부 사유 `LastReject`: `circled`, `tap`, `short`, `angle`, `bent`.
- 값: `Value`(확정된 기울기, −1~1), `Shown`(낚싯대가 보여 주는 값: 실시간 기울기, 키를 누르는 동안 ±1, 아니면 `Value`).
- **키보드**: A/D(←/→)를 누르는 동안 끝까지(±1). 스윕이 켜질 때 이미 눌려 있던 키는 한 번 뗄 때까지 무시(`keyLatch`).
- UI 위에서 시작한 누름(예: `회수` 버튼)은 스트로크가 아닙니다.
- 얼음 구멍과 전설어 조우 중에는 없습니다.

### 8.2 낚싯대가 기우는 방식 (`Angler`)

| 상수 | 값 | 출처 |
|---|---|---|
| 최대 스윕 | 30° | `Angler.SweepMax` (`Angler.Sweep = Slide.Shown × SweepMax`) |
| 스윕 스무딩 | 0.07 s (약 0.2 s에 95%) | `Angler.SweepTau` |
| 낚싯대 회전 한계 | 왼쪽 40°, 오른쪽 30° (바라보는 방향 + 스윕 합; 오른손·옆 기준 — 왼손이면 왼쪽 30°·오른쪽 40°, 가운데면 양쪽 35°) | `Angler.RodMax`(낚싯대 든 손 쪽), `Angler.RodMaxAcross`(몸을 가로지르는 쪽), `Angler.RodMaxCentre`; 그 프레임의 한계 `Angler.RodYawMin` / `RodYawMax` |
| 바깥쪽(낚싯대 든 손 쪽, 오른손이면 왼쪽; 가운데면 양쪽) 끝까지 기울 때 끝이 내려가는 각도 | 10°, 파이트 중(`SideLow`) 25° | `Angler.SweepDrop`, `Angler.SweepDropLow` |

- `FishingController.Lean` = `Angler.SweepReq / SweepMax` — 회전 한계 **전**의 요청값이라, 물고기가 한계 밖에 있어 그려진 낚싯대가 끝에 붙어 있어도 사이드 프레셔는 기울인 만큼 인정됩니다.
- `FishingController.SweepSin` = `sin(Angler.SweepLine)` — 채비로 가는 줄에서 초릿대가 벗어난 각도(스윕 방향). 채비가 한쪽 멀리 있어 낚싯대가 한계에 붙은 상태에서 바깥쪽으로 더 미는 건 0으로 칩니다.

### 8.3 대기·회수 중: 채비 휘어 오기 (`Tackle.Wind`, `Tackle.Drag`)

| 상수 | 값 | 효과 |
|---|---|---|
| `Tackle.SweepLateral` | 0.6 | 감을 때(또는 톡의 끌림) 1 m 들어올 때마다 `0.6 × SweepSin` m 옆으로 → 경로가 초릿대 쪽으로 휨 |
| `Tackle.SweepDragMax` | 1.5 m | 감지 않는 찌 채비가 한 번 밀 때 끌려가는 최대 거리 (반대쪽이나 가운데로 가면 새로 셈) |
| `Tackle.SweepDragSpeed` | 0.3 m/s | 끝까지 기울였을 때(sin 30°) 끌리는 속도, 작게 기울이면 비례해 느림. 줄 길이는 그대로(낚시꾼 중심 원 위로 끌림) |
| `Tackle.SweepNearZ` | 0.4 m | 옆 이동만으로는 물가 앞 0.4 m보다 가까이 오지 않음 |

- 찌 끌기는 `|SweepSin| > 0.05`일 때만, 감지 않을 때만(`revs ≤ 0.0001`), 얼음이 아닐 때 (`FishingController.UpdateWaiting`).
- 옆으로 가다 물 위 구조물에 막히면 그 가장자리를 따라 미끄러집니다.
- **멘딩**: 휜 줄 반대쪽으로 밀면 줄 휨이 풀립니다(`FishingController.TryMend`) — [time_currents_spec.md](time_currents_spec.md) 9.2.
- 처음 채비를 물에 넣었을 때 한 번 "좌우로 밀면 낚시대가 기울어요 — 감으면 그쪽으로 휘어 와요"(`SaveData.sweepHint`).

### 8.4 파이트 중: 사이드 프레셔 (`FishingController.SidePressure`)

물고기가 한쪽으로 질주(`FishRun` = −1 왼쪽 / +1 오른쪽)할 때 낚싯대를 기울이는 방향이 파이트에 영향을 줍니다. 조건(`SideActive`): 질주/돌진 중 ∧ 지치지 않음 ∧ 점프 아님 ∧ 얼음 아님. 전설어 파이트도 같습니다.

| 상수 | 값 | 뜻 | 출처 |
|---|---|---|---|
| `SideDead` | 0.15 | |Lean|이 이 미만이면 가운데로 봄 | `FishingController` |
| `TurnRate` | 0.6 rad/s | 반대로 기울이면 물고기 방향을 낚싯대 쪽으로 끌어당기는 속도(끝까지 기울였을 때) | 〃 |
| `PushRate` | 0.25 rad/s | 같은 쪽으로 기울이면 바깥으로 더 밀어내는 속도 | 〃 |
| `TurnTime` | 0.7 s | 끝까지 반대로 기울인 누적 시간이 이만큼이면 "방향을 꺾었다!" (덜 기울이면 더 오래; 커버로 도망 중이면 × `coverDig`) | 〃 |
| `LongRun` | 1.6 s | 사이드 프레셔 없이 이만큼 달리면 한 번 힌트(`SaveData.sideHint`) | 〃 |
| `FightModel.TurnCut` | 0.5 | 꺾인 질주의 남은 시간 × 0.5 | `FightModel.Turn` |
| `FightModel.SideDrainGood` | 1.4 | 반대로 끝까지: 체력 소모 × 1.4 | `FightModel` |
| `FightModel.SideDrainBad` | 0.8 | 같은 쪽으로 끝까지: 체력 소모 × 0.8 | 〃 |
| `FightModel.SideTension` | 0.1 | 어느 쪽이든 기울이면 장력 × (1 + 0.1 × 기울기) | `FightModel.SideTensionMult` |
| `FightModel.SideRunLonger` | 0.35 | 같은 쪽이면 질주 시계가 최대 35% 느리게 감 (더 오래 달림) | `FightModel.Step` |

- `good = clamp01(−Lean × FishRun)`, `bad = clamp01(Lean × FishRun)`, `SideNow = good − bad`.
- 꺾이면 물고기 머리가 낚싯대 쪽으로 돌아 그쪽으로 0.2 rad 더 간 방향으로 계속 달립니다. 물살을 탄 질주(`RunAlign ≥ 0.3` 또는 하류 질주)를 꺾으면 "물살에서 빼냈다!"와 함께 4 s 동안 물살 도움이 없어지고, 커버로 도망 중이면 커버에서 끌어냅니다(`PullOut`, [obstacles_spec.md](obstacles_spec.md) 7.6).
- 돌진은 항상 같은 방향의 질주로 이어지며, 그 질주는 새로 굴리지 않고 사이드 프레셔 진행(꺾임 포함)을 이어받습니다(돌진에서 꺾였으면 이어지는 질주도 `CutRun`으로 절반).
- 질주가 끝날 때마다 `RunEnded(방향, 시간, 꺾였는지)` 이벤트(테스트용).
- **파이트 바의 가운데 칸** (`FishingHUD.UpdateSide`, 108×24): 물고기가 달리는 쪽 ◀/▶ 깜빡임과 물고기 아이콘, 낚싯대 막대(`Lean × 30` 단위 이동, `× 35°` 기울기), 맞게 밀면(`SideNow > 0.2`) 금색, 반대면 주황. 문구 "사이드 프레셔!" / "◀ 반대쪽으로!" / "반대쪽으로! ▶". 물살 정렬 |al| ≥ 0.3이면 `>>` 표시. 얼음에서는 숨김.

### 8.5 사이드 프레셔 화살표 (`SideArrow`)

"이쪽으로 밀어요" 화살표를 플레이어가 보는 지점 바로 위에 띄웁니다 (`Assets/Scripts/Fishing/SideArrow.cs`). 스프라이트 `fk_items.py`의 `sidearrow`(`side_arrow_f0..f7` + `side_arrow_on`, 26×20 px 캔버스, 오른쪽을 가리키고 왼쪽이면 좌우 반전).

- **보이는 조건**: `S.Fighting` ∧ `SideActive`. 가리키는 방향 = `−FishRun`(달리는 반대쪽). 새 질주가 반대로 가면 먼저 사라졌다가 새 방향으로 다시 나타납니다. 밑걸림(`S.Snagged`) 중에는 빠지는 쪽을 가리킵니다([obstacles_spec.md](obstacles_spec.md) 6.5).
- **기준점**(`AnchorKind`): 찌가 수면에 떠서 줄에 끌려가면(`Tackle.FloatRiding`) 찌 꼭대기(`float`), 아니면 줄이 물에 들어가는 지점(`entry`, 루어·잠긴 찌), 줄이 물 밖으로 곧장 입에 가면 물고기 입 바로 위 수면(`mouth`).

| 상수 | 값 | 뜻 |
|---|---|---|
| `SideArrow.Order` | `Angler.OrderRodFront + 4` | 물 효과·물고기·줄·낚시꾼 위, 반짝이(60)·HUD 아래 |
| `GapPx` / `BottomPx` | 4 / 9 px | 기준점과 화살표 바닥 사이 틈 / 스프라이트 중심에서 바닥까지 |
| `SideArrow.MinScale`, `ScalePow` | 0.7, 0.35 | 원근 크기 = `clamp(ScaleAt^0.35, 0.7, 1)`, 폭은 짝수 픽셀로 맞춤 |
| `SideArrow.Deadband` | 0.2 | |SideNow|가 이보다 크면 밀었다고 봄 (HUD 문구와 같음) |
| `SideArrow.GlintStep`, `Hold`, `Loop` | 0.07 s, 0.34 s, `0.07 × 7 + 0.34` | 안 밀 때: 꼬리→끝 반짝임 후 휴식 루프 |
| `PulseLo` | 0.6 | 안 밀 때 휴식 중 알파 |
| `SideArrow.NudgePx` | 2 px | 안 밀 때 반짝이는 동안 미는 쪽으로 살짝 밀림 |
| `SideArrow.FadeIn`, `FadeOut` | 0.15 s, 0.12 s | 나타남 / 사라짐 |
| `SideArrow.PopTime`, `PopScale` | 0.2 s, 0.22 | 맞게 밀었을 때 밝은 프레임(`side_arrow_on`)이 커졌다 돌아옴 |
| `SideArrow.ShakePx`, `ShakeHz` | 2 px, 11 Hz | 반대로 밀었을 때 좌우 흔들림 |

파이트가 끝나면(랜딩·끊김·놓침) 즉시 사라집니다. 실행 순서는 `DefaultExecutionOrder(100)` — 낚시꾼(줄의 물 진입점)과 채비(90, 찌 위치)의 LateUpdate 뒤.

### 8.6 파이트 중 찌 표시 (`Tackle.RenderFightFloat`)

찌 채비는 파이트 중에도 찌가 줄 위(바늘에서 `FloatDepth`만큼 위)에 고정되어 보입니다(`Tackle.FightFloat.Line`).

| 상태 | 조건 | 그리기 |
|---|---|---|
| 수면에 떠서 끌림 | 줄이 물속이고 물 진입점 → 입 거리 ≤ `FloatDepth`, 또는 줄이 느슨함 | 진입점에 떠서 당기는 쪽으로 기움(최대 28° × (0.35 + 0.65 × 팽팽함)), 팽팽할수록 빠르게 까딱임, 끌리면 물살 V자 |
| 물속으로 끌려 들어감 | 거리 > `FloatDepth` ∧ 팽팽함 | 줄 위 그 지점으로 잠김, 깊을수록(3 m까지) 어둡고(`WaterDeep` 쪽으로 0.4~0.75) 작고(× 0.8까지) 흐림(알파 0.85 → 0.4, 얼음 × 0.8) |
| 공중 | 점프·랜딩으로 줄이 물 밖 | 그려진 줄 끝에서 `FloatDepth`만큼 뒤, 초릿대를 넘지 않음. 물에 닿으면 물 위에서 튐 |
| 매달림 | 랜딩 후 (`FightFloat.Hang`) | 물고기 위 줄에 매달림 |

| 상수 / 식 | 값 |
|---|---|
| 팽팽함 `Tackle.FightTaut` | `clamp01(max(TensionRatio × 2.5, Tension / max(0.2, Power × 0.6)))` (`FishingController.UpdateFighting`) — 강한 줄의 작은 물고기도 잠기게 |
| `Tackle.UnderFrom` / `UnderFull` | 0.3 / 0.6 — 팽팽함이 이 사이에서 잠기는 깊이가 0 → 끝까지 |
| `SinkTau` / `RiseSpeed` | 0.08 s로 빠르게 잠기고, 1.6 m/s로 떠오름 |
| `Sunk` / `Up` | 0.18 m 넘게 잠기면 파문, 0.03 m 안으로 올라오면 "퐁" 떠오름 파문 |
| 세게 당김 `Tackle.FightHard` | 돌진 중 또는 `TensionRatio > 0.8` → `TugTime` 0.3 s 동안 쑥 잠김(dip −7), 다음까지 1.1~2.2 s 쿨다운 |
| 최소 크기 | `FloatMinPx` 8 px |

물고기가 빠지거나 줄이 끊기면 찌는 그 자리에 떠올라(`Tackle.LetGo`) 0.7 s 뒤 회수됩니다(5.7). 사이드 프레셔 화살표는 떠 있는 찌 위에 뜹니다(8.5). 밤에는 찌 끝 케미 불빛이 켜집니다.

---

## 9. 캐스팅 후 줌 (`FishingController.Zoom.cs`, `ViewZoom`)

설정 → "캐스팅 후 줌인"(`Assets/Scripts/UI/SettingsUI.cs`, 선택지 순서 끔 / 1.25배 / 1.5배 / 액티브)으로 고릅니다. 저장값 `SaveData.zoomMode`는 `ZoomMode` 열거형(`X125 = 0`, `Off = 1`, `X150 = 2`, `Active = 3`)이며, 0이 기본이라 설정이 없던 옛 세이브도 1.25배가 됩니다. 범위 밖 값은 0으로 고칩니다(`SaveData`). 설정은 매 프레임 읽으므로 낚시 도중 바꿔도 바로 반영됩니다.

### 9.1 모드별 동작

| 모드 | 줌인하는 상태 (`FishingController.ZoomWanted`) | 동작 |
|---|---|---|
| **1.25배** (기본) | `Waiting`, `Biting`, `Fighting`, `Snagged` | 착수하면 0.6 s에 걸쳐 1.25배에 가장 가까운 정수 픽셀 배율로 줌인(1080p: 게임 1 px = 화면 4 → 5 px) |
| **1.5배** | 〃 | 1.5배에 가장 가까운 배율(1080p: 4 → 6). 초릿대와 채비/물고기가 한 화면에 안 들어가면 1.25배까지 한 단계씩 내려감 |
| **액티브** | `Biting`, `Fighting` (밑걸림은 이미 줌인돼 있으면 유지) | 기다리는 동안 1배, 본입질 순간 `ZoomBiteIn` 0.35 s에 1.25배로 찌/루어에 줌인, 챔질한 파이트 동안 유지. 헛챔질이면 1배로 |
| **끔** | 없음 | 줌 없음 (파이트 따라가기도 없음) |

모든 모드에서 랜딩 시작, 회수, 물고기 놓침(→ 준비 또는 회수), 전설어 조우(창·전체 화면이 1배 화면 기준으로 배치; 걸리면 파이트에서 다시 줌인), 준비 상태로 돌아오면 1배로 돌아갑니다. **캐스팅은 항상 1배**이며, 준비·던지기(`Aiming`, `Casting`) 중에는 아직 진행 중인 줌아웃을 `ZoomAimOut` 0.15 s 안에 끝냅니다. HUD는 줌되지 않습니다.

### 9.2 프레이밍 규칙 (`FishingController.DirectZoom`)

매 프레임 `ViewZoom.Director`가 `Want`(줌인 여부), `Focus`(어디를 볼지), `Keep`(화면 안에 반드시 둘 점과 여백)을 호출합니다.

| 상황 | 초점 (`Focus`) | 꼭 보일 점 (`Keep`, 게임 px 여백) |
|---|---|---|
| 대기·입질·밑걸림 | 초릿대와 채비의 중간, `ZoomWaitTau` 0.5 s | 초릿대 `ZoomTipMargin` 10, 채비 `ZoomRigMargin` 14 (얹혀 있으면 얹힌 곳, 가라앉은 루어는 실제 위치도 14) |
| 파이트 | 초릿대와 물고기의 중간, `ZoomFightTau` 1.2 s (부드럽게 따라감) | 초릿대 10, 물고기 `ZoomFishMargin` 20 |
| 액티브의 입질 | 채비, `ZoomBiteTau` 0.25 s | 초릿대 10, 채비 14 |
| 전설어 신호가 곧 나올 때(대기) | `ZoomCueLead` 0.7 s 전부터 `ZoomCueTau` 0.3 s로 돌아봄 | 신호의 파문·눈빛 `ZoomCueMargin` 12 (soft). 한 화면에 안 들어가면 신호 동안 1배로 |

- 위쪽 파이트 바 영역(`FightStripUnits` 76 캔버스 단위 × `UIKit.ScaleFactor`)은 `ViewZoom.TopInsetPx`로 빼서, 꼭 보일 점이 그 아래에 오게 합니다.
- 낚시꾼은 화면 아래에서 잘릴 수 있습니다.

### 9.3 `ViewZoom`의 픽셀 규칙 (`Assets/Scripts/Core/ViewZoom.cs`)

월드는 항상 저해상도 렌더 타깃 전체에 그려지고, 줌은 그 타깃을 화면에 보여 주는 **UV 잘라내기**만 바꿉니다. 그래서 스테이지·액터·물·화살표·조우 창이 함께 줌되고 HUD 캔버스는 줌되지 않습니다.

| 상수 | 값 | 뜻 |
|---|---|---|
| `ViewZoom.Aim` | 1.25 | 기본 배율 |
| `ViewZoom.AimWide` | 1.5 | 1.5배 모드 |
| `ViewZoom.EaseTime` | 0.6 s | 전체 줌인/아웃 (smoothstep) |
| `ViewZoom.KeepTau` | 0.08 s | 여백 밖으로 나간 필수 점이 화면을 끌어당기는 속도 |
| `ViewZoom.StepDownTime` | 0.35 s | 필수 점이 안 들어가 한 단계 내려갈 때 |
| `ViewZoom.StepUpHold` | 1 s | 큰 단계가 여유 있게 담아야 다시 올라감 |
| `FitSlack` / `FitSlackUp` | 3 / 12 게임 px | 단계 유지 / 다시 올라가기에 필요한 여유 |

- **정수 배율**: 단계 = 요청 배율 × 기본 배율에 가장 가까운 정수 화면 px(`StepOf`, 항상 1배보다 큼). 1.5배 요청이면 1.25배 단계보다 최소 1 크게. 정지 상태에서는 잘라내기 원점도 정수 화면 픽셀에 맞춰 모든 게임 픽셀이 정확히 n×n이 됩니다(`PixelExact`). 줌이 움직이는 동안만 소수 배율을 지납니다.
- 팬은 전체 단계 기준으로 잡고 렌더 타깃 밖으로 나가지 않게 제한합니다(빈 테두리 없음). 줌은 팬을 축으로 돌아가므로 중간 단계의 잘라내기는 항상 최종 잘라내기를 포함합니다.
- 1배에서 줌인할 때는 이번 프레임 초점으로 바로 팬합니다(`jumpPan`).
- 실행 순서 `DefaultExecutionOrder(950)`: 액터 LateUpdate(초릿대·물고기·찌) 뒤, 조우 HUD(1100) 전.

테스트: `-fkauto zoom [-fkzoommode off|125|150|active]` ([testing.md](testing.md)).

---

## 10. 걷기

준비 상태(`S.Ready`)에서만 옆으로 걸을 수 있습니다 (`FishingController.UpdateReady` → `Angler.WalkInput`).

| 입력 | 출처 |
|---|---|
| 화면 오른쪽 아래 ◀ ▶ 버튼 (누르고 있는 동안) | `FishingHUD.WalkButton`, `FishingHUD.WalkHeld` |
| A / D, ← / → | `PointerInput.WalkKeys` |

- 버튼 크기 `WalkBtn` 64, 간격 `WalkGap` 12, 오른쪽 여백 `WalkInset` 20, 아래 `WalkBottom` 26 (`FishingHUD`). 준비 상태이고 걸을 범위가 0.01 m보다 넓을 때만 보이며, 끝에 닿은 쪽 버튼은 비활성(회색)입니다(`FishingHUD.RefreshWalk`). 누르는 순간 클릭 소리.
- 버튼과 키는 합쳐서 −1~1로 제한합니다. 스윕·사이드 프레셔에 쓰던 A/D를 누른 채 준비 상태로 돌아오면 한 번 뗄 때까지 걷지 않습니다(`walkLatch`).
- 같은 A/D 키가 채비가 물에 있거나 파이트 중엔 낚싯대 스윕이 됩니다(8.1).

| 상수 | 값 | 출처 |
|---|---|---|
| 걷는 속도 | 0.8 m/s (약 2.7걸음/s) | `Angler.WalkSpeed` |
| 가속·감속 | 시간상수 0.07 s (0.2 s에 약 95%) | `Angler.WalkTau` |
| 화면 여백 | 발이 픽셀 뷰 양옆에서 56 px 안쪽 | `Angler.ViewMarginPx` |
| 걷는 쪽으로 몸 돌림 | 25° | `Angler.WalkFace` |

스테이지별 걸을 수 있는 발 x 범위 (m, `Angler.WalkRange`, 앞 레이어 그림에서 발판·소품을 피해 잰 값):

| 스테이지 | 최소 | 최대 |
|---|---|---|
| `lake` | −0.25 | 1.0 |
| `stream` | −0.8 | 0.8 |
| `sea` | −0.5 | 0.8 |
| `swamp` | −0.85 | 0.7 |
| `ice` | −0.5 | 2.6 |
| `ocean` | −0.65 | 1.5 |
| `cave` | −0.85 | 1.5 |

마지막으로 선 위치는 스테이지별로 이번 실행 동안만 기억합니다(`Angler.LastX`, 저장 안 함). 테스트 스위치 `-fkangx <m>`로 시작 위치를 정할 수 있습니다(`Angler.StartX`). 얼음에서는 항상 구멍(x 1.9)에서 낚시하며, 걷는 것은 서 있는 위치만 바꿉니다. 던지기 방향·부채꼴·찌 끌기의 기준점은 모두 현재 발 위치(`Angler.X`)입니다.

---

## 사양서와 달라진 점

- **[lures_legend_spec.md](lures_legend_spec.md) 1.1 마지막 문단** ("`LureInput` is updated ... in the states `Waiting` and `Encounter`"): 코드는 `Waiting` 중에도 채비가 물에 있거나(`Tackle.Mode.Water`) 얹혀 있을 때(`Tackle.Mode.Perched`)만 켜고, **`Snagged`에서도 켭니다**(톡으로 얹힌 채비를 떨어뜨리고 밑걸림을 풀기 위해). 또 `gestureOn`에는 `Biting`, `Fighting`, `Snagged`도 들어 있습니다. (`Assets/Scripts/Fishing/FishingController.cs` `FishingController.Update`)
- **[lures_legend_spec.md](lures_legend_spec.md) 1.1 표 tap 행** ("in Biting ... it sets the hook"): 일반 입질(`Biting`)의 챔질은 탭 분류(떼는 순간)를 기다리지 않고 **월드를 누르는 순간**(`PointerInput.WorldPressed`) 또는 원 그리기(`Gesture.Circling`)로 바로 걸립니다. 톡 동작도 누르는 순간 챔질이 됩니다. (`FishingController.UpdateBiting`)
- **[lures_legend_spec.md](lures_legend_spec.md) 2.10 3번** ("If the hook was perfect it then sets `Fight.Stamina = 0.85f` and calls `Fight.Hold(0.5f)`"): 코드는 체력 0.85는 완벽한 챔질일 때만이지만, **`Hold(0.5)`는 조우에서 걸린 전설어라면 완벽 여부와 상관없이** 항상 적용합니다. (`FishingController.BeginFight`: `if (fish.Sp.encounter != null) { if (perfect) Fight.Stamina = 0.85f; Fight.Hold(0.5f); }`)
