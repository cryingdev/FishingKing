# 낚시왕 (Fishing King)

Unity 6 (6000.3.22f1) 픽셀아트 모바일 낚시 게임입니다. 낚시꾼 뒷모습 시점에서 던지고, 화면에 원을 그려 릴을 감습니다.
**모든 그래픽은 Blender 스크립트로 직접 만들었습니다** (`Tools/Blender`). 16비트 픽셀아트에 시네마틱한 분위기를 섞은 하이브리드 스타일이고,
캐릭터·릴·전설어는 부분 3D 모델을 픽셀 화면에 합성합니다.

## 실행

- **에디터**: Unity Hub에서 이 폴더를 열고 `Assets/Scenes/Title.unity` → Play (어느 씬에서 시작해도 게임 매니저가 자동 생성됩니다)
- **빌드**: 메뉴 `FishingKing > Build Windows` → `Builds/Windows/FishingKing.exe`
- 씬·빌드 설정이 꼬이면 메뉴 `FishingKing > Setup Project`로 다시 생성합니다.

## 조작 요약 (마우스 / 터치 동일)

| 동작 | 방법 |
|---|---|
| 캐스팅 | 아래로 당겼다가 **던질 쪽으로 위로 튕기며 놓기** — 튕기는 속도 = 거리, 놓는 순간의 방향 = 방향 |
| 챔질 | 찌가 쑥 들어가면 탭 |
| 릴 감기 / 풀기 | 시계방향 / 반시계방향으로 원 그리기 (설정에서 반대로 바꿀 수 있음) |
| 루어 톡 | 아래로 짧고 빠르게 당기기 — 낚싯대를 채어 올림 |
| 낚싯대 스윕 | 좌우로 곧게 밀기 — 대기 중엔 채비가 그쪽으로 휘어 오고, 파이트 중엔 사이드 프레셔 |
| 걷기 | 준비 상태에서 ◀ ▶ 버튼 또는 A/D |
| 키보드 | Space/R 감기 · B 풀기 · T / Shift+T 톡 · A/D 걷기·스윕 |
| 손잡이·낚싯대 위치 | 설정 → 조작: 오른손/왼손(왼손이면 낚싯대를 오른손에 들고 왼손으로 감음), 옆/가운데(배 앞에서 두 손으로) — 낚시 중에도 바로 바뀜 |

전체 조작과 수치는 [Docs/controls.md](Docs/controls.md)에 있습니다.

## 주요 기능

- **스테이지 7곳**: 고요한 호수 · 산골 계곡 · 바다 방파제 · 안개 늪지 · 얼음 호수(구멍낚시) · 먼바다(보트) · 수정 동굴
- **시간대와 물**: 게임 시계(실제 24분 = 하루)에 따라 새벽·낮·저녁·밤으로 맵이 바뀌고, 물고기 활동 시간이 다릅니다.
  계곡의 물살, 바다의 들물·날물, 먼바다 해류, 호수·늪의 바람에 찌와 루어가 흘러갑니다.
- **어종 42종** (일반~전설 5단계): 크기에 따라 무게·가격이 바뀌고, 종마다 선호 미끼·수심·습성이 다릅니다.
  호수에는 떡붕어·끄리·누치·동자개(빠가사리)·뱀장어·강준치를 포함해 10종이 그 호수 바닥의 어디에 사는지 저마다 다릅니다.
- **루어 10종 · 액션 5가지** (감기·저킹·수면·바닥·수직): 루어마다 알맞은 조작을 해야 입질이 옵니다.
- **파이트**: 장력·드랙·점프(호핑·머리 털기·꼬리 걷기), 사이드 프레셔, 커버로 파고드는 물고기와 줄 쓸림
- **장애물**: 바위·말뚝·연잎에 캐스팅이 튕기거나 얹히고, 물속 구조물에 밑걸림이 생깁니다. 위치는 Blender 장면에서 내보냅니다.
- **전설어 조우 6종**: 조건이 맞으면 어둠 속에서 눈이 빛나며 다가오는 물속 장면이 열리고, 루어를 조작해 유인한 뒤 챔질합니다.
- **캐스팅 후 줌**: 끔 / 1.25배 / 1.5배 / 액티브(입질 순간 줌인), 픽셀 정수배 유지.
  스테이지 그림은 화면(480 px)보다 넓게 렌더되어(오버스캔: 640 px, 바다는 800 px) 물고기가 1x 화면 밖으로 달아나면
  카메라가 정수 픽셀 단위로 부드럽게 따라가고(끔 모드도, 필요한 만큼만) 파이트가 끝나면 원래 자리로 돌아옵니다
- **수족관**: 사료 봉투를 뜯어 뿌리고 새우·정어리를 집어 주기, 미세한 성장과 가격 변화, 이끼·부유물 청소, 자리별 장식,
  5단계 수조와 크기별 칸 수. 관람 수입은 희귀도 × 가치에 비례합니다.
- **상점·도감·레벨업**, 자동 저장, 코드로 합성한 효과음·스테이지별 환경음(새벽·밤 층), MIDI로 작곡해 렌더한 배경음(시간대·파이트·조우에 따라 스템을 섞음), 설정 → 음량(전체·배경음악·효과음·환경음), 설정 → 조작(왼손잡이, 낚싯대를 몸 가운데로)

어종과 스테이지는 데이터 파일입니다: 어종마다 `Assets/Resources/Data/Fish/<id>.json` 하나, 스테이지와 거기 나오는 어종은
`Assets/Resources/Data/stages.json`. **어종을 더하려면** 파일 하나 + 그림 4장 + 스테이지 목록 한 줄이면 되고, 검사기가 빠진 것을
알려 줍니다([data_reference.md](Docs/data_reference.md) 2.7절). 장비·미끼·수조 수치는 `Assets/Scripts/Data/GameDatabase.cs`에 있습니다.

## 프로젝트 구조

```
Assets/
  Resources/            Blender가 렌더한 스프라이트·모델(FBX)·팔레트·레이아웃/장애물/깊이 데이터, 어종 파일(Data/Fish)·stages.json, 폰트, 배경음 스템(OGG)·music.json
  Shaders/              툰·림 라이트·시간대 디졸브·그림자·발판 가림 셰이더
  Scripts/Core/         Game(상태·경제), SaveData, PixelView(저해상도 픽셀 뷰)·ViewZoom, 입력, 게임 시계, 수족관 모델
  Scripts/Data/         Models, GameDatabase, 어종 데이터 로더(SpeciesData)·검사기(SpeciesCheck), 전설어 조우 행(LegendEncounters)
  Scripts/Fishing/      원근 투영(Persp), 스테이지, 낚시꾼·릴 3D, 채비, 물고기 AI, 파이트, 물살, 장애물, 가림, 줌, HUD
  Scripts/Fishing/Legend/ 전설어 조우
  Scripts/Scenes/       타이틀·지도·낚시·수족관(먹이·청소·장식)
  Scripts/UI/           코드 UI, 상점, 도감, 설정
  Scripts/Audio/        Sfx(합성 효과음·스테이지 환경음·칩튠), Music(배경음 덱·스템·크로스페이드·sting), AudioMix(음량 슬라이더)
  Scripts/Debug/        AutoPilot 자동 테스트 시나리오
  Editor/               픽셀아트·액터 모델·배경음 임포터, 프로젝트 설정·빌드 메뉴
Tools/Blender/          에셋 생성 스크립트 (variants/hybrid 가 현재 스타일)
Tools/Music/            배경음 작곡·렌더 스크립트 (MIDI → FluidSynth → OGG)
Docs/                   설계 문서
```

## 에셋 파이프라인 (Blender 5.2)

```powershell
.\Tools\Blender\build_all.ps1
.\Tools\Blender\variants\hybrid\build_hybrid.ps1 -Install
```

로우폴리 모델을 툰 셰이딩으로 안티앨리어싱 없이 렌더한 뒤 알파 컷과 1px 외곽선을 입힙니다.
스테이지는 Blender 원근 카메라로 렌더하고, Unity의 `Persp.cs`가 **같은 카메라 수식**으로 물고기·찌·줄을 투영하므로 배경과 정확히 맞물립니다.
시간대(`periods/`), 전설어 모델(`legends/`), 조우 배경(`encounter_sets/`), 장애물 내보내기, 발판 깊이 지도도 같은 파이프라인에서 만듭니다.
스테이지 그림을 640 px보다 넓게(오버스캔, `Data/stage_<id>.json`의 `widthPx`) 렌더하려면 `build_overscan.ps1 -Stage <id>`:
같은 카메라로 넓게 렌더하고 홈 화면 부분은 예전 640 레이아웃 그대로 붙인 뒤 시간대·깊이 지도·장애물까지 다시 만듭니다.

## 테스트

빌드 실행 인자로 자동 플레이 시나리오를 돌리고 스크린샷을 남깁니다. 예:

```powershell
Builds\Windows\FishingKing.exe -fkfresh -fkrich -fkgear -fksave test -fkscene Fishing -fkstage cave -fkencounter now -fkauto encounter -fkshots <폴더>
```

스위치 목록은 [Docs/testing.md](Docs/testing.md)에 있습니다.

## 설계 문서

구현 정리 (현재 코드 기준):

- [전체 구조](Docs/architecture.md) — 씬 흐름, 픽셀 뷰·줌·입력, 낚시 상태 머신, 그리기 순서, 부분 3D, 발판 가림, 실행 순서
- [낚시 플레이 시스템](Docs/fishing_gameplay.md) — 플릭 캐스팅, 입질·챔질, 파이트 모델, 스윕·사이드 프레셔, 원 그리기, 루어 입력, 캐스팅 후 줌, 걷기
- [수족관](Docs/aquarium.md) — 먹이·생먹이, 배부름·성장·관람 수입, 청소, 장식, 수조 5단계, 오프라인 보정
- [Blender 에셋 파이프라인](Docs/art_pipeline.md) — 스크립트별 역할, 하이브리드 스타일 규칙, 빌드·설치, Unity 임포터
- [데이터 표](Docs/data_reference.md) — 스테이지·어종·장비·루어·수조·사료·장식, SaveData 필드와 마이그레이션
- [배경음](Docs/music.md) — 덱·스템·크로스페이드·sting·칩튠 대체, 음량과 오디오 설정(음량 슬라이더), 낚시 씬의 곡 규칙 (작곡·빌드: [Tools/Music/README.md](Tools/Music/README.md))
- [변경 기록](CHANGELOG.md)

사양서:

- [루어와 전설어 조우](Docs/lures_legend_spec.md) · [전설어 확장](Docs/legends_rollout.md)
- [시간대·물때·물살](Docs/time_currents_spec.md) · [장애물](Docs/obstacles_spec.md)

## 글꼴

UI와 로고는 픽셀 폰트 **Galmuri** (© Lee Minseo, [SIL Open Font License 1.1](Assets/Resources/Fonts/Galmuri-LICENSE.txt), https://github.com/quiple/galmuri)를 사용합니다.
픽셀 폰트는 격자 크기의 정수배에서만 선명하므로 `UIKit.Label`의 크기는 가장 가까운 선명한 크기로 자동 보정됩니다.
새 문자열을 추가했다면 `python Tools/font_coverage.py Assets/Resources/Fonts/Galmuri11.ttf …`로 글리프 누락을 확인하세요.

## 사운드폰트

배경음은 GM 사운드폰트 **FluidR3_GM** (© Frank Wen, MIT License; Debian/Ubuntu `fluid-soundfont-gm` 패키지)으로 렌더했습니다.
렌더한 OGG만 저장소에 들어 있고 사운드폰트 파일은 들어 있지 않습니다. 다시 렌더하는 방법은 [Tools/Music/README.md](Tools/Music/README.md)에 있습니다.

## 효과음

효과음과 환경음은 모두 코드로 합성합니다(`Sfx.cs`·`Sfx.Foley.cs`). 녹음 효과음을 넣을 때는 출처와 라이선스를 여기에 적고 `Assets/Resources/Audio/Sfx`(또는 `Ambience`)에 둡니다.

## 참고

렌더 파이프라인은 Built-in, 입력은 Input System 패키지입니다.
