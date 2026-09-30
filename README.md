# 낚시왕 (Fishing King)

Unity 6 (6000.3.22f1) 2D 픽셀 낚시 게임. **모든 그래픽은 Blender 스크립트로 직접 생성**했습니다
(`Tools/Blender`). 캐릭터 뒷모습 시점에서 앞으로 캐스팅하고, 화면에 원을 그려 릴을 감습니다.

## 실행

- **에디터**: Unity Hub에서 이 폴더를 열고 `Assets/Scenes/Title.unity` → Play
  (어느 씬에서 Play해도 게임 매니저가 자동 생성됩니다)
- **바로 플레이**: `Builds/Windows/FishingKing.exe` (빌드: 메뉴 `FishingKing > Build Windows`)
- 씬/빌드 설정이 꼬이면 메뉴 `FishingKing > Setup Project` 로 다시 생성

## 조작 (마우스 / 터치 동일)

| 동작 | 방법 |
|---|---|
| 캐스팅 | 화면을 **아래로 당겼다가 던질 쪽으로 위로 튕기며 놓기** — 튕기는 속도 = 거리, 놓는 순간 튕긴 방향 = 방향 (천천히 제자리로 올려 놓으면 취소) |
| 얼음낚시 | 아래로 당긴 길이 = 얼음 구멍에 내릴 수심 |
| 챔질 | 찌가 **쑥 들어가면(!)** 탭. 톡톡 입질 중에 누르면 물고기가 도망감 |
| 루어 톡 | 루어가 물에 있을 때 화면을 **아래로 짧고 빠르게 당겼다 놓기**(0.35초 이내, 화면 높이 5% 이상) — 낚싯대를 톡 채어 올리는 느낌으로 루어가 움직임. 세게 당길수록 강하게. 위로 쓸어 올리는 건 톡이 아님(캐스팅 동작). 키보드 T(약) / Shift+T(강) |
| 릴 감기 | 화면 아무 곳에 **시계방향으로 원 그리기** (빠를수록 빨리 감김) |
| 줄 풀기 | **반시계방향** 원 — 장력이 빨간색일 때 줄이 끊어지지 않게 |
| 채비 회수 | `회수` 버튼 또는 원 그리기 |
| 낚싯대 스윕 | 채비가 물에 있을 때(대기·회수 중) 물 위를 **좌우로 곧게 밀기**(화면 폭 6% 이상, 수평에서 30° 이내) — 낚싯대 끝이 그쪽으로 기울고(최대 약 30°, 낚싯대 회전 한계 안에서) **손을 떼도 그대로**(반대로 밀면 돌아옴), 그대로 한 손가락으로 원을 그려 감으면 루어·채비가 그쪽으로 **휘어 들어옴**(감는 거리 × sin(각도) × 0.6만큼 옆으로) — 물고기·수초·전설어 은신처를 피하거나 지나가게. 찌는 감지 않아도 그쪽으로 천천히 조금 끌려감(한 번 밀 때 최대 1.5m, 줄 길이는 그대로, 물가 쪽으로는 물가 앞 0.4m에서 멈춤). 원·톡·탭과는 구분됨(원을 그리기 시작하면 기울임 취소). 얼음 구멍·전설어 조우 중에는 없음 |
| 사이드 프레셔 | 파이트 중 같은 동작(또는 A/D)으로 낚싯대를 기울임 — 물고기가 **달리는 반대쪽**으로 기울이면 머리를 돌려 질주 방향을 꺾고(0.7초쯤 버티면 "방향을 꺾었다!" — 그쪽으로 돌아 달리다 남은 질주가 절반으로 짧아짐, 체력 소모 ×1.4, "사이드 프레셔!"; 물고기가 낚싯대 회전 한계 밖에 있어도 기울인 만큼 인정), **같은 쪽**이면 더 멀리·오래 달림(체력 소모 ×0.8, "반대쪽으로!"). 어느 쪽이든 장력 +10%, 가운데면 그대로. 파이트 바 가운데 칸: 물고기가 달리는 쪽 화살표 + 낚싯대 기울기 막대. 수면에 뜬 찌(찌 채비) 또는 물속으로 줄이 들어가는 지점(루어, 찌가 잠겼을 때) 바로 위에는 **밀어야 할 쪽을 가리키는 금색 화살표**(안 밀면 반짝이며 깜빡, 맞게 밀면 환하게 빛남, 반대로 밀면 흔들림; 휴식·점프 중·얼음에선 숨김). 점프는 그대로, 전설어 파이트도 동일 |
| 장애물·던지기 | 물 위로 나온 바위·말뚝·보트·통나무·테트라포드·수정·나뭇가지에 채비가 맞으면 **튕겨 나감** — 바위 옆 2m 안에 떨어지면 `뱅크샷!`(자연스러운 착수: 8초간 근처 입질 ×1.3), 늘어진 가지 아래로 떨어지면 `가지 아래로 쏙!`. 바위 위·물가에 걸쳐지면 **톡** 당기거나 감아서 떨어뜨림(감아서 떨어뜨리면 가끔 걸림). 개구리 루어·포퍼는 **연잎 위에 앉음**(개구리: 감으면 연잎 위를 기어 오다 가장자리에서 `퐁` — 1.2초간 입질 기회 ×1.5, 가만히 두면 연잎을 뚫고 덮치기도), 다른 루어는 미끄러져 떨어지고(20% 연잎에 걸림), 찌 채비는 연잎에 걸림. 방파제·배 앞머리에 가려 안 보이는 발밑 물로 약하게 던지면 보이는 물까지 조금 더 날아가 떨어짐 |
| 밑걸림 풀기 | 물속 바위·가라앉은 통나무·수초에 바늘이 걸리면 `밑걸림!`(수초/갈대/연잎) — 파이트 바가 걸림 모드로 바뀌고 장력 = 걸림 장력. **톡** 당기거나(잠깐 감지 않고 줄을 느슨하게 둔 뒤면 더 잘 빠짐), 낚싯대를 **빠지는 쪽**(물고기처럼 줄의 반대 방향)으로 0.6초 밀어 두면 `빠졌다!`; 반대로 밀면 금색 화살표가 빠지는 쪽을 알려줌(수초·갈대·연잎은 어느 쪽이든 0.5초). **감으면** 장력이 올라 줄이 끊어지고 루어를 잃음. 개구리 루어는 수초·연잎에 걸리지 않고, 소프트 웜은 거의 안 걸림. 얼음 구멍에선 밀기가 없어 톡(과 줄 늦추기)으로만 풂 |
| 끊기 | 밑걸림 중 `회수` 버튼이 `끊기`로 바뀜 — 누르면 줄을 끊음(루어 분실) |
| 커버로 도망 | 큰입배스·가물치·쏘가리·우럭 등은 파이트 중 바위·말뚝·보트·통나무·수초로 달아나 박힘(`커버로 파고든다!`) — 줄이 구조물에 쓸리면 파이트 바의 **쓸림** 게이지가 차고(차면 줄이 끊어짐, 쓸린 줄은 약해짐; 플로로카본은 강하고 PE 합사는 약함), **사이드 프레셔**(화살표 쪽으로 밀기)로 방향을 꺾으면 `커버에서 끌어냈다!`. 얼음 구멍에선 강꼬치고기·북극곤들매기·철갑상어가 얼음 밑으로 멀리 파고들 때만 줄이 구멍 가장자리에 쓸림(`얼음 밑으로 파고든다!` — 박혀 있을 때 꾸준히 감으면 끌어냄) |
| 키보드(PC) | Space/R 누르고 있기 = 감기, B = 풀기, T / Shift+T = 톡, A/D(←/→) = 준비 중엔 걷기 · 채비가 물에 있을 때/파이트 중엔 누르는 동안 낚싯대 좌우 (한쪽 용도로 누르고 있던 키는 떼었다 다시 눌러야 다른 용도로 동작) |

파이트 요령: 물고기가 **도망칠 때(질주/점프)** 는 감기를 멈추거나 풀어주고, **쉴 때·지쳤을 때** 힘껏 감습니다.
옆으로 달리면 반대쪽으로 낚싯대를 밀어(사이드 프레셔) 방향을 꺾으세요.
찌 채비는 파이트 중에도 **찌가 보입니다**: 찌는 바늘에서 찌 수심만큼 위 줄에 고정 — 줄이 물에 들어가는 지점에서 수면에 떠서
끌려가고(기울고, 옆으로 끌리면 물살 V자), 물고기가 깊이 들어가 줄이 팽팽하면 줄을 따라 **물속으로 끌려 들어갔다가**(어둡게·작게)
줄이 느슨해지거나 물고기가 올라오면 파문과 함께 **다시 떠오름**. 세게 당기면 잠깐 쑥 잠김, 점프 땐 줄을 따라 튐, 랜딩 땐 물고기 위 줄에
매달림(찌 수심이 더 길면 초릿대 끝). 사이드 프레셔 화살표는 떠 있는 찌 위에 뜸. 물고기가 빠지거나 줄이 끊기면 찌는 그 자리에 남았다가 회수됨.
장력 바의 흰 선은 릴 드랙(이 이상이면 줄이 미끄러져 풀림), 빨간 구간은 줄 강도 한계입니다.

## 게임 구성

- **스테이지 7곳**: 고요한 호수 · 산골 계곡 · 바다 방파제 · 안개 늪지 · 얼음 호수(구멍낚시) · 먼바다(보트) · 수정 동굴
  — 레벨 조건 + 코인으로 해금, 난이도(물고기 힘/입질률) 상승
- **어종 36종** (일반~전설 5단계), 크기에 따라 무게·가격·경험치 변화, 종마다 선호 미끼·수심·습성(질주/점프/체력)
- **상점**: 낚싯대 6 (비거리·탄력·챔질 여유·행운) / 릴 6 (감기 속도·드랙·줄 용량·전동 자동감기) /
  낚싯줄 6 (강도·은신) / 미끼 12 (떡밥 무한, 소모성 미끼, 재사용 루어 — 줄이 끊기면 분실) / 수조 확장 4단계
- **잡은 물고기**: 즉시 판매 / 수조 보관 / 놓아주기. 수조의 물고기는 헤엄치며 **관람 수입**(최대 12시간 누적)을 벌고, 나중에 판매 가능
- **도감**: 발견한 어종, 최대 크기, 서식지, 좋아하는 미끼
- 레벨업 보상, 자동 저장 (`Application.persistentDataPath/fishingking_save.json`),
  사운드·음악은 코드로 합성 (오디오 파일 없음)

밸런스 수치는 모두 `Assets/Scripts/Data/GameDatabase.cs` 한 곳에 있습니다.

## 프로젝트 구조

```
Assets/
  Resources/Sprites/   Blender가 렌더한 스프라이트 (Fish, Items, UI, Character, Stages, World)
  Resources/Data/      Blender가 내보낸 레이아웃 JSON (스테이지 수심/영역, 캐릭터 손 위치, 지도 마커, 장애물 obstacles_<stage>.json 등)
  Scripts/Core/        Game(상태·경제), SaveData, Art(리소스), PixelView(저해상도 픽셀 카메라), PointerInput, SceneFlow
  Scripts/Data/        Models, GameDatabase
  Scripts/Fishing/     Persp(원근 투영), StageView, Angler, Tackle, FishAgent, FishSpawner,
                       FightModel(장력 시뮬레이션), CircleGesture(원 그리기 인식), FishingController, FishingHUD, CatchPopup,
                       Obstacles / ObstacleOverlay / FishingController.Obstacles (장애물: Docs/obstacles_spec.md)
  Scripts/Scenes/      Title / Map / Fishing / Aquarium 부트스트랩
  Scripts/UI/          UIKit(코드 UI), Widgets(TopBar·Toast·Dialog), ShopUI, CollectionUI
  Scripts/Debug/       AutoPilot (-fkauto 테스트 자동조종)
  Editor/              PixelArtImporter(포인트 필터·9-slice·피벗), FishingKingSetup(씬 생성·빌드)
Tools/Blender/         에셋 생성 스크립트
```

## 에셋 파이프라인 (Blender 5.2)

```powershell
.\Tools\Blender\build_all.ps1
```

| 스크립트 | 생성물 |
|---|---|
| `fk_fish.py` | 36종 파라미터 모델 → 옆모습 2프레임(획득·수조·도감) + 위에서 본 그림자 2프레임(물속) |
| `fk_items.py` | 낚싯대·릴·줄·미끼·수조 아이콘, UI 아이콘, 9-slice 패널/버튼, 릴 위젯, 찌 |
| `fk_character.py` | 뒷모습 낚시꾼 7포즈 + 손/낚싯대 방향 JSON |
| `fk_stages.py` | 7개 스테이지 3D 디오라마 → 뒤/앞 레이어 + 게임 레이아웃 JSON |
| `fk_misc.py` | 섬 지도(+마커 좌표), 수족관 방, 타이틀 로고(Galmuri11 Bold, 픽셀 격자 정렬) |

로우폴리 모델 → 법선 기반 툰 셰이딩(에미션) → 안티앨리어싱 없는 EEVEE 렌더 → numpy로 알파 컷 + 1px 외곽선.
스테이지는 `fk_persp.py`의 원근 카메라로 렌더하고, Unity의 `Persp.cs`가 **같은 카메라 수식**으로
물고기·찌·낚싯줄을 투영하기 때문에 배경과 정확히 맞물립니다.

## 테스트 스위치 (빌드 실행 인자)

`-fksave <이름>` 별도 세이브 · `-fkfresh` 새 게임 · `-fkrich` 코인/레벨/전 스테이지 해금 ·
`-fkgear` 최고 장비 · `-fkbait <id>` 미끼 지급 · `-fkscene Fishing -fkstage lake` 바로 이동 ·
`-fkauto fish|tour|walk|flick|windup -fkshots <폴더> [-fkcount N]` 자동 플레이 + 스크린샷 (windup: 캐스팅 준비 가이드 화살표 전체 화면 샷) ·
`-fkflick <속도>[:<각도>]` fish/walk 자동 캐스팅의 튕김 (창 높이/초, 화면 기준 각도, 기본 3.4:0) ·
`-fkaim <-1..1>` fish 자동 캐스팅 방향 (±1 = 최대 각도) ·
`-fkencounter [now|natural]` 전설어 조우 테스트 (야광 에기 지급, 쿨다운/보정 없음; now = 착수 1초 후 바로 조우, natural = 발밑 앞 16m에 은신처·조건 완화) ·
`-fkauto lure -fklure <id>|all` 루어 액션 테스트 (톡 = 아래로 당기기; 먼저 미노우로 위로 쓸기는 톡이 아닌지·아래로 당기면 낚싯대가 들리는지 확인, 루어별 q_good≥0.7 / q_bad≤0.3) ·
`-fkauto steer [-fksteer clear|fights|arrow]` 낚싯대 스윕·사이드 프레셔 테스트 (루어 캐스팅 후 왼쪽→오른쪽 스윕하며 감기 궤적, 찌 옆으로 끌기, 같은 물고기로 반대/같은 쪽/중앙 사이드 프레셔 파이트 비교(체력 소모·꺾인 시간·장력), 밀기와 원·톡·탭 구분, 극단 기울기와 파이트 중 낚싯대-모자 간격; 얼음에선 스윕이 없는지 확인. clear = 간격만(`-fkactors2d`와 함께 2D 스프라이트 확인), fights = 파이트 비교만(+화살표), arrow = 사이드 프레셔 화살표만: 찌·루어로 안 밀기/맞게/반대로 캡처, `arrowshot` 로그에 화면 좌표) ·
`-fkfloatshots` (fish / steer에 추가) 찌 채비 파이트의 찌 순간 캡처(수면 끌림·잠김·떠오름·점프·화살표·랜딩·놓침), `floatshot` 로그에 화면 좌표; 얼음 steer에선 구멍 파이트를 따로 한 번 더(`-fkicecm <cm> -fkicedepth <m>`, 기본 45cm·3m) ·
`-fkobstacles show|off` 장애물 전부 표시 / 장애물 없이(예전처럼) · `-fkobstlog` 장애물 이벤트마다 `[OBST]` 로그 · `-fksnag <배율>` 밑걸림 확률 배율(0 = 없음, 99 = 즉시) · `-fkobstseed <n>` 장애물 난수 시드 ·
`-fkauto obstacles` 장애물 테스트 + 캡처 (예: `-fkfresh -fkrich -fkgear -fksave obst -fkscene Fishing -fkstage stream -fkauto obstacles -fkobstlog -fkshots <폴더>`: 조준 윤곽, 바위에 튕기는 캐스팅·뱅크샷, 스푼 밑걸림 톡/크랭크 스윕/억지로 감아 끊김, 호수 연잎 개구리·지렁이, 보트로 도망치는 배스(방치 → 쓸려 끊김 / 사이드 프레셔 → 끌어냄), 커버 근처 입질 비교, 늪 조준 윤곽, 스테이지별 장애물 표시; 캡처 obst_aim·obst_aim_swamp·obst_bounce·obst_bankshot·obst_snag·obst_snag_free·obst_snag_arrow·obst_pad·obst_pad_snag·obst_rub·obst_break·obst_pullout·obst_show_<스테이지>; `[OBST] CHECK` 줄) ·
`-fkauto encounter [-fkencplay perfect|bad|early|both]` 조우 자동 플레이 + 캡처 (예: `-fkfresh -fkrich -fkgear -fkrod rod_bamboo -fksave enc -fkscene Fishing -fkstage cave -fkencounter now -fkauto encounter -fkshots <폴더>`)

캐스팅 세기 조정: `Assets/Scripts/Fishing/FlickCast.cs` 맨 위 상수 (터치는 cm/s, 마우스는 모니터 높이/초).
놓을 때마다 `Player.log`에 `[Flick]` 줄이 남으니 실제 기기에서 튕긴 속도를 보고 `TouchSpeedFull` 등을 맞추세요.

## 글꼴 (Galmuri)

UI와 타이틀 로고는 픽셀 폰트 **Galmuri** v2.40.4 (© Lee Minseo, [SIL Open Font License 1.1](Assets/Resources/Fonts/Galmuri-LICENSE.txt), https://github.com/quiple/galmuri)를 사용합니다.

- `Assets/Resources/Fonts/` — Galmuri7/9/11/14 원본 TTF(수정 없음) + 라이선스. 임포트 설정은 동적 글리프 + `Hinted Raster`(안티에일리어싱 없음)
- `Tools/Fonts/Galmuri11-Bold.ttf` — Blender 로고 렌더 전용 (빌드에 포함되지 않음)
- 픽셀 폰트는 **격자 크기의 정수배**일 때만 선명합니다 (Galmuri7=8px, 9=10px, 11=12px, 14=15px).
  `UIKit.Label(…, size)`에 준 크기는 가장 가까운 선명한 크기로 자동 스냅됩니다
  (16·20·24·30 캔버스 단위 등, `PixelFonts.For`). UI 캔버스는 화면 높이 270px당 정수 배율로 스케일되어
  스프라이트와 글자가 모든 해상도에서 픽셀 단위로 맞습니다.
- 리치 텍스트의 `<b>/<i>`는 제거되고 `<size=N>`은 격자 배수로 보정됩니다(`PixelText`).
- 새 문자열을 추가했다면 글리프 누락 검사: `python Tools/font_coverage.py Assets/Resources/Fonts/Galmuri11.ttf …`
- 다른 글꼴을 쓰고 싶으면 `Assets/Resources/Fonts/UIFont.ttf`를 넣으면 그 글꼴이 우선합니다.

## 참고

- 렌더 파이프라인은 Built-in, 입력은 Input System 패키지입니다.
