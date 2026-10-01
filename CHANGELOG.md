# 변경 기록 (CHANGELOG)

- **이 문서가 다루는 것**: 지금까지 들어간 기능을 기능 단위로 묶어 "무엇이 추가/변경되었는지" 정리
- **관련 코드**: `Assets/Scripts/` 전체, `Assets/Shaders/`, `Assets/Editor/`, `Tools/Blender/`, `Tools/Music/`
- **관련 문서**: [README](README.md) · [구조](Docs/architecture.md) · [낚시 플레이](Docs/fishing_gameplay.md) · [수족관](Docs/aquarium.md) · [아트 파이프라인](Docs/art_pipeline.md) · [데이터 표](Docs/data_reference.md) · [배경음](Docs/music.md)

커밋 히스토리는 영역별(아트 파이프라인 → 렌더 결과물 → 에디터 도구 → 코어 → 낚시 → 전설어 → 수족관 → 테스트 → 사양서 → README)로
한 번에 들어가 있어 기능별 세부 순서는 남아 있지 않습니다. 그래서 아래는 **날짜·버전 순이 아니라 기능별**로 묶었습니다.
수치는 해당 설계 문서에 출처(파일·클래스·상수)와 함께 있으니 여기서는 요약만 적습니다.

---

## 이후 변경

### 배경음 (MIDI 작곡 → OGG 스템)
- 추가: 배경음 파이프라인 `Tools/Music/`(Python으로 음표를 적어 MIDI → FluidSynth 렌더 → 루프·음량을 맞춘 OGG 스템, `Resources/Data/music.json` 매니페스트)과
  임포터 `MusicImporter`(Vorbis, Compressed In Memory, 백그라운드 로드).
- 추가: `Music`(`[Game]`): 큐마다 스템별 `AudioSource`를 한 DSP 시각에 `PlayScheduled`해 샘플 단위로 맞물린 **덱**, 스템별 페이드,
  큐 사이 크로스페이드, sting과 그 덕킹, 곡이 없으면 예전 칩튠으로 대체, 출력 장치가 바뀌면 다시 시작.
- 추가: 타이틀·지도·수족관 곡, 낚시 씬의 감독(`FishingController.Music.cs`): 스테이지 곡의 `day`/`night` 스템(시간대 전환 8초 크로스페이드),
  입질 덕킹, 장력을 따르는 `fight` 스템, 잡음·놓침 sting, 전설어 조우 단계별 스템과 `sting_hook` → `legend_<id>` → `sting_legend` / `sting_escape`.
- 추가: 설정 → **배경음** 켜짐/꺼짐(`SettingsUI`), `SaveData.musicOn`(옛 세이브는 켬, `Game.SetMusic`).
- 추가: 테스트 스위치 `-fkmusic off|chiptune`, `-fkmusiclog`, `-fkauto music`(`AutoPilot.Music.cs`).
- 자세히: [music.md](Docs/music.md), [Tools/Music/README.md](Tools/Music/README.md)

### 수족관 `받기` 금액 한 줄 표시 (`f114b92`)
- 변경: `받기` 버튼의 적립 금액이 길어지면 줄바꿈·자동 축소(best fit) 대신 **선명한 작은 픽셀 글꼴로 바꿔** 한 줄에 표시합니다.
  6자 이상 16, 7자 이상 12 크기 (`Assets/Scripts/Scenes/AquariumScene.cs` `AquariumScene.SetCollectText`).

---

## 기반 (최초 도입분)

### 하이브리드 아트
- 추가: 16비트 픽셀아트(retro16)의 기법 — 한정 팔레트, 팔레트 램프 툰 재질, 그라데이션에만 쓰는 4×4 Bayer 디더, 팔레트 안 색조의 1px 외곽선 —
  에 시네마틱(cinematic) 시안의 분위기(프리셋별 하늘·빛·안개·반사·림)를 섞은 **하이브리드 스타일**을 현재 스타일로 채택 (`Tools/Blender/variants/hybrid/hyb_core.py`).
- 추가: 스테이지 7곳·지도·수족관·물고기·캐릭터 렌더, 스테이지 프리셋(`hyb_core.PRESETS`), 설치 스크립트 `build_hybrid.ps1 -Install`.
- 추가: Unity 임포터 `PixelArtImporter`(Point 필터, 무압축, 스프라이트 규칙)와 `ActorModelImporter`(FBX 규칙, 외곽선용 스무딩 노멀을 UV3에 기록).
- 참고: `build_all.ps1`(1세대)은 hybrid 아트를 1세대 아트로 덮어쓰므로 `build_hybrid.ps1 -Install`을 그 뒤에 돌려야 합니다.
- 자세히: [art_pipeline.md](Docs/art_pipeline.md)

### 부분 3D 캐릭터
- 추가: 낚시꾼(`Resources/Models/angler`, 20본 리그)과 릴 6종(`reel_<id>`)을 실시간 3D로 그려 **픽셀 레이어**로 2D 정렬 순서에 끼워 넣음
  (`ActorLayer`: 픽셀 뷰와 같은 크기의 RT, `Persp`와 같은 카메라의 오프센터 투영).
- 추가: `Angler3D` 포즈 블렌드·다리/손 IK(왼손은 낚싯대, 오른손은 릴 노브), `Reel3D` 크랭크 회전을 감은 바퀴 수에 연동.
- 추가: `ActorToon`(3단 툰 램프 + 뒤집힌 헐 외곽선 `_OutlinePx`) / `ActorRim`(햇빛 쪽 실루엣에만 1px 림) 셰이더, 스테이지·시간대별 림·키 라이트(`ActorArt`).
- 추가: `-fkactors2d`로 예전 2D 스프라이트로 되돌리기.
- 자세히: [architecture.md](Docs/architecture.md) 5절

### 플릭 캐스팅
- 추가: 아래로 당겨 장전(`FlickCast.WindUpMin`) → 위로 튕기며 놓기. **튕긴 속도 = 거리, 놓는 순간의 방향 = 방향**.
  터치는 cm/s(`TouchSpeedMin`/`TouchSpeedFull`), 마우스는 모니터 높이/초(`MouseSpeedMin`/`MouseSpeedFull`)로 기기와 무관하게 같은 힘.
- 추가: 천천히 올려 놓기·짧은 떨림 등 취소 조건, 화면 선을 월드로 역투영해 손가락이 그은 선 위에 떨어지게 하는 방향 계산(±`FlickCast.YawMax`).
- 추가: 얼음 구멍 변형(당긴 길이 = 내릴 수심), 머리 위 가이드 화살표(`CastArrow`)와 물 위 부채꼴.
- 추가: 놓을 때마다 `Player.log`에 `[Flick]` 로그.
- 자세히: [fishing_gameplay.md](Docs/fishing_gameplay.md) 2–3절, [controls.md](Docs/controls.md)

### 루어 10종·액션
- 추가: 루어 10종 — 스피너·스푼·크랭크베이트·트롤링 루어(감기), 미노우(저킹), 포퍼·개구리 루어(수면), 소프트 웜(바닥), 메탈 지그·야광 에기(수직).
  자연 미끼 8종(떡밥·지렁이·옥수수·새우·갯지렁이·오징어·야광 웜·황금 떡밥)과 함께 `GameDatabase`에 있음.
- 추가: 루어 입력 세 동사 `LureInput` — 감기(원 그리기), **톡**(아래로 짧고 빠르게 당기기), 멈춤 — 과 루어별 액션 품질 Q·덮침 창
  (`restStrike`, `fallStrike`, `landStrike`, `burstStrike`, `pauseStrike`).
- 추가: 루어별 한 줄 힌트, 어종별 선호 문자열(`prefers`)에 루어 id.
- 자세히: [lures_legend_spec.md](Docs/lures_legend_spec.md) 1절, [fishing_gameplay.md](Docs/fishing_gameplay.md) 7절, [data_reference.md](Docs/data_reference.md) 3절

### 전설어 조우 6종 (피라루쿠 탑뷰 포함)
- 추가: 조건이 맞으면 어둠 속에서 눈이 빛나며 다가오는 **물속 장면**이 창(오버레이)으로 열렸다가 전체 화면으로 커지고,
  루어를 조작해 기분(Mood)을 맞춰 유인한 뒤 챔질하는 미니게임 (`Legend/LegendWatch`, `LegendEncounter`, `EncounterView`, `EncounterHUD`).
- 추가: 전설어 6종과 무대 — 실러캔스(수정 동굴), 황금잉어(호수), 피라루쿠(늪지), 철갑상어(얼음 호수), 청새치·백상아리(먼바다: 한 은신처를 공유).
- 추가: 피라루쿠는 수면 루어용 **위에서 본 시점**(`EncounterView.Top.cs`, `EncShadow.shader`: 깊이에 따라 흐려지는 그림자, 수면 물결·물보라 스프라이트).
- 추가: 전설어 3D 모델(`Legend3D`, `Tools/Blender/variants/hybrid/legends/`)과 조우 배경 세트(`encounter_sets/`: cave·lake·swamp·swamp_top·ice·ocean).
- 추가: 실패 쿨다운·보정(pity)·세이브 기록(`LegendRecord`), 조우로 건 전설어는 파이트 시작을 잠시 붙잡음(`Fight.Hold`).
- 자세히: [lures_legend_spec.md](Docs/lures_legend_spec.md) 2절, [legends_rollout.md](Docs/legends_rollout.md)

### 좌우 스윕·사이드 프레셔
- 추가: 물 위를 좌우로 곧게 밀면(`SideSlide`) 낚싯대 끝이 그쪽으로 기울고 손을 떼도 유지. 대기·회수 중에는 감는 동안 채비가 그쪽으로 휘어 들어오고,
  찌는 감지 않아도 조금 끌려옴 (`Tackle.Wind`, `Tackle.Drag`).
- 추가: 파이트 중 **사이드 프레셔** — 물고기가 달리는 반대쪽으로 기울여 버티면 질주 방향을 꺾고(체력 소모 증가), 같은 쪽이면 더 멀리 달림
  (`FishingController.SidePressure`). 파이트 바 가운데 칸의 방향 표시.
- 추가: 떠 있는 찌 또는 줄의 입수점 위에 밀어야 할 쪽을 가리키는 **금색 화살표**(`SideArrow`: 깜빡임/빛남/흔들림).
- 추가: 파이트 중에도 **찌가 보임** — 줄에 고정되어 끌려가고, 줄이 팽팽하면 물속으로 잠겼다가 다시 떠오름 (`Tackle.RenderFightFloat`).
- 추가: 밀기와 원 그리기·톡·탭 구분, 키보드 A/D.
- 자세히: [fishing_gameplay.md](Docs/fishing_gameplay.md) 8절, [controls.md](Docs/controls.md)

### 시간대·물때·물살
- 추가: 게임 시계 — 실제 1초 = 게임 1분(`GameClock.Scale` 기본 1, 하루 `GameClock.DayMin` 1440분 = 실제 24분), 낚시 스테이지가 열려 있을 때만 흐름.
  05:00 새벽 · 08:00 낮 · 17:00 저녁 · 20:00 밤(`GameClock.Bounds`), 경계에서 디졸브 전환(`PeriodDissolve.shader`).
- 추가: 스테이지마다 시간대 4종 렌더(`periods/`, `Assets/Resources/Data/Periods/<stage>_<period>.json`)와 낚시꾼 룩, 시간대별 어종 활동도(`TimeActivity`).
- 추가: 바다 물때(반일주조: 간조 03:30/15:30, 만조 09:30/21:30 — `GameClock` 주석), 물살 `CurrentField`(계곡 한쪽 흐름, 바다 조류, 먼바다 보트 표류, 호수·늪 바람) —
  찌·루어가 흘러가고 줄이 휘며(멘딩), 파이트에도 영향.
- 추가: HUD 시계 패널과 지도 표시, 한 번만 뜨는 힌트.
- 자세히: [time_currents_spec.md](Docs/time_currents_spec.md)

### 장애물
- 추가: 물 위 바위·말뚝·보트·통나무·테트라포드·수정·나뭇가지에 캐스팅이 **튕기거나** 얹힘, 바위 옆 착수 `뱅크샷!`, 가지 아래 `가지 아래로 쏙!`.
- 추가: 연잎 — 개구리 루어·포퍼는 위에 앉고, 다른 채비는 미끄러지거나 걸림.
- 추가: 물속 바위·가라앉은 통나무·수초의 **밑걸림**(톡·빠지는 쪽으로 밀기로 풀기, 감으면 끊김, `끊기` 버튼).
- 추가: 커버로 파고드는 어종과 줄 **쓸림** 게이지(줄 종류별 내마모), 사이드 프레셔로 끌어내기, 얼음 구멍 가장자리 쓸림.
- 추가: 위치는 Blender 장면에서 내보냄(`hyb_obstacles.py`, `obstacles/<stage>.py` → `Assets/Resources/Data/obstacles_<stage>.json`), 조준 윤곽 표시(`ObstacleOverlay`).
- 자세히: [obstacles_spec.md](Docs/obstacles_spec.md), [controls.md](Docs/controls.md)

### 발판 가림과 깜빡임 수정
- 추가: 방파제·바위·배 뒤로 내려가는 줄, 물 밖 물고기, 물보라, 공중의 찌·미끼가 앞 레이어 위에 그려지던 문제를 **픽셀 단위 깊이 비교**로 가림
  (`FrontOcclusion` + `FrontOcclude.shader`, 깊이 지도 `Sprites/Stages/<stage>_front_depth.png` + `Data/frontdepth_<stage>.json`, 허용 오차 `FrontOcclusion.Bias`).
  줄은 꼭짓점마다 깊이를 실어 발판 모서리에서 정확히 잘림(`FrontOcclusion.ZPerMetre`). 연잎 등 수면의 평평한 것은 가리지 않음.
- 추가: 깊이 지도 생성 `hyb_frontdepth.py`(스테이지 앞 장면을 같은 시드·같은 카메라로 다시 만들어 View Z를 인코딩).
- 수정(깜빡임):
  - 가림 재질을 한 번 붙이면 떼지 않고 깊이만 바꿈 — 상태가 바뀌는 프레임에 테스트 없이 그려지는 일이 없음 (`FrontOcclusion.Use`).
  - 앞 레이어 사각형을 `StageView.Update`와 `LateUpdate`에서 두 번 배치 — Update 순서와 무관하게 실제로 그려지는 위치로 테스트 (`StageView.PlaceOcclusion`).
  - 한 프레임 안의 순서 고정: 물고기(0) → 낚시꾼(`Angler` 50) → 찌(`Tackle` 90) → 화살표(100) → 줌(950) → 3D 레이어(1000) (`DefaultExecutionOrder`).
  - 던진 직후 코루틴에서 바로 `DrawFlying`으로 위치·깊이를 잡아 한 프레임 이전 위치에 보이던 문제 수정 (`Tackle.Launch`).
  - 원 그리기 중 감기/파이트/대기 포즈가 프레임마다 바뀌던 문제: 새 포즈가 `Angler.PoseHold`(0.12초) 유지될 때만 전환.
  - 파이트 위치 계산에 그린 초릿대 대신 `Angler.RodTipPlan`을 써서 한 프레임씩 자리가 뒤바뀌는 피드백 제거.
- 추가: 검사 도구 `-fkauto occlusion`, `-fkoccwatch`(`Debug/OcclusionWatch.cs`: 프레임 단위 노출·순간 튐 검출), `-fkocclusion off`.
- 자세히: [architecture.md](Docs/architecture.md) 6–7절

### 캐스팅 후 줌
- 추가: 설정 "캐스팅 후 줌인" 4모드 — **끔 / 1.25배(기본) / 1.5배 / 액티브**(입질 순간 줌인) (`SaveData.zoomMode`, `ZoomMode`).
  배율은 항상 **정수 픽셀 배율**로 맞춤(1080p: 게임 1px = 4 → 5 / 6 화면 px).
- 추가: 초릿대와 채비(파이트 중엔 물고기)를 한 화면에 두는 프레이밍, 위쪽 파이트 바 영역 제외(`ViewZoom.TopInsetPx`), 전설어 신호 쪽으로 미리 돌아보기.
  캐스팅·랜딩·회수·조우 중엔 1배.
- 자세히: [fishing_gameplay.md](Docs/fishing_gameplay.md) 9절, [architecture.md](Docs/architecture.md) 2.2절

### 수족관 (먹이·생먹이·청소·장식·수조 단계)
- 추가(먹이): 기본·고급 **사료 봉투** — 절취선을 따라 잘라 뜯고(가위 힌트) 끌어서 뿌리기. **생새우·정어리**는 한 마리씩 집어 물고기에게 줌.
  어종마다 먹는 먹이가 정해져 있음(`GameDatabase.Diets`).
- 추가(성장·가치): 배부름(12시간 / 고급 18시간에 100%→0%), 배부른 시간에 따라 길이가 최대 +8%까지 점근 성장
  (`AquaCare.GrowthOf`: `0.08 × (1 − e^(−fedDays/2.3))`), 무게·판매가 재계산.
- 추가(관람 수입): 희귀도 가중치 × 가치 비례, 배부름 ×1.2 / 배고픔 ×0.7, 장식 보너스 최대 +15%, 오염 감소 최대 −25%, 마지막 받기 후 12시간까지만 적립.
- 추가(청소): 이끼·부유물·바닥 오물이 시간과 물고기 수에 따라 쌓이고 수입이 줄어듦. 스펀지(무료)·뜰채·사이펀으로 손으로 청소.
- 추가(장식): 조명·뒤쪽·가운데·앞쪽 **자리(slot)** 방식, 장식 17종(조명 5, 뒤쪽 4, 가운데 5, 앞쪽 3), 조명별 선호 물고기 +5%, 수초는 오염 감소, 기포기는 부유물 감소.
  꾸미기 모드에서 끌어 놓기·교체·보관함.
- 추가(수조 5단계): 작은 어항 → 중형 수조 → 대형 수조 → 아쿠아리움 → 황금 대수족관. 물고기 길이로 등급(소형 1 / 중형 2 / 대형 4 / 초대형 8칸)이 정해지고,
  단계마다 칸 수·들어가는 최대 등급·장식 자리 수가 늘어남. 순서대로만 구매.
- 추가(시간 경과): 실시간 경과와 오프라인 보정(최대 7일 따라잡기 `AquaCare.MaxCatchUp` 등), 구 세이브 이전(`aquaVer`, `capVer`, `TankCare.ver`) —
  칸 도입 전 세이브의 초과 물고기는 빼지 않음.
- 추가: 수조·장식·청소 아트(`hyb_aquarium.py`, `hyb_aquatanks.py`, `hyb_aquadecor.py`, `fk_aquaclean.py`), 테스트 `-fkaqua feed|live|clean|decor|tanks|migrate`.
- 자세히: [aquarium.md](Docs/aquarium.md), [data_reference.md](Docs/data_reference.md) 4절

### 그 밖의 기반
- 추가: 씬 흐름(Title → Map → Fishing / Aquarium), `Game` 싱글턴 자동 생성, 자동 저장(`SaveData`), 저해상도 픽셀 뷰(`PixelView`)와 줌(`ViewZoom`),
  마우스·터치 통합 입력(`PointerInput`), Blender 카메라와 같은 수식의 원근 투영(`Persp`).
- 추가: 스테이지 7곳, 어종 36종(일반~전설 5단계), 낚싯대·릴·줄 각 6종, 상점·도감·레벨업, 코드로 합성한 사운드(`Sfx`), 픽셀 폰트 Galmuri.
- 추가: AutoPilot 자동 플레이 시나리오와 스크린샷([testing.md](Docs/testing.md)).
- 추가: 설계 문서 4종과 README 개편(조작표는 [controls.md](Docs/controls.md), 테스트 스위치는 [testing.md](Docs/testing.md)로 분리).
