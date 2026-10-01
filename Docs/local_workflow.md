# 로컬 작업 안내 (머지 전 확인과 이후 작업)

- **이 문서가 다루는 것**: 클라우드 세션에서 만든 배경음·효과음 작업을 로컬(Windows + Unity)에서 받아 확인하고, 고치고, 머지하는 순서. 로컬 Claude Code 세션에 맡길 때의 지침
- **관련 코드**: `Assets/Scripts/Audio/Music.cs`, `Assets/Scripts/Audio/Sfx.cs`, `Assets/Scripts/Audio/Sfx.Foley.cs`, `Assets/Scripts/Fishing/FishingController.Music.cs`, `Assets/Editor/MusicImporter.cs`, `Assets/Scripts/Debug/AutoPilot.Music.cs`, `Tools/Music/`
- **관련 문서**: [README](../README.md) · [배경음 런타임](music.md) · [배경음 파이프라인](../Tools/Music/README.md) · [테스트 스위치](testing.md) · [구조](architecture.md) · [변경 기록](../CHANGELOG.md)

클라우드 세션에서는 **Unity를 실행할 수 없었고, 소리를 귀로 들을 수도 없었습니다.** C# 코드는 Unity 2021.3 참조 어셈블리와 대체 코드(stub)로 컴파일 검사만 했습니다(오류 0개).
그래서 머지 전에 로컬에서 꼭 해야 할 일은 **Unity에서 열어 보기, 실제로 플레이하며 듣기, 자동 검사 시나리오 돌리기** 세 가지입니다.

---

## 1. 브랜치 상황

| 브랜치 | 내용 | PR |
|---|---|---|
| `docs/overview` | 구현 문서 5종, CHANGELOG, README 링크 | [cryingdev/FishingKing#1](https://github.com/cryingdev/FishingKing/pull/1) (열림) |
| `claude/jolly-cannon-bpb1gt` | `docs/overview` 위에 쌓은 배경음·효과음 작업 전체 | 아직 없음 |

`claude/jolly-cannon-bpb1gt`에는 작업 중간 스냅샷 커밋("WIP: …")이 여러 개 섞여 있습니다. 머지할 때 **Squash and merge**로 하나로 합치는 것을 권합니다(7절).

```powershell
git fetch origin
git switch claude/jolly-cannon-bpb1gt      # 처음이면: git switch -c claude/jolly-cannon-bpb1gt origin/claude/jolly-cannon-bpb1gt
git log --oneline -5
```

---

## 2. Unity에서 처음 열 때

에디터 버전은 **6000.3.22f1**입니다(`ProjectSettings/ProjectVersion.txt`).

1. Unity Hub에서 프로젝트를 열고 임포트가 끝날 때까지 기다립니다. 새 오디오 40개 + 효과음·환경음 파일이 처음 임포트됩니다.
2. **Console에 컴파일 오류가 없는지** 확인합니다. 클라우드의 컴파일 검사는 uGUI·Input System·UnityEditor를 대체 코드로 검사했기 때문에, 그쪽 API를 잘못 쓴 곳은 여기서 처음 드러날 수 있습니다.
3. 임포트 설정을 확인합니다.

| 폴더 | 기대하는 설정 | 담당 |
|---|---|---|
| `Assets/Resources/Audio/Music/` (OGG 40개) | Vorbis, **Compressed In Memory**, Load In Background 켬, Preload Audio Data 끔, 품질 0.6 | `MusicImporter` |
| `Assets/Resources/Audio/Ambience/` (`amb_stream.ogg`) | 위와 같되 Load In Background 끔, Preload 켬 | `MusicImporter` |
| `Assets/Resources/Audio/Sfx/` (WAV) | Unity 기본값(짧은 소리라 Decompress On Load로 충분) | — |

   설정이 다르게 보이면 폴더를 선택하고 **Reimport**하세요(`MusicImporter.GetVersion()`이 2라서 보통은 자동으로 다시 임포트됩니다).
4. 새 파일들의 `.meta`는 클라우드에서 guid만 넣은 최소 형태로 만들었습니다. Unity가 열면서 설정을 채워 `.meta`가 바뀌는데, **바뀐 `.meta`는 그대로 커밋**하면 됩니다.

---

## 3. 플레이하며 확인할 것 (귀로)

세이브를 건드리지 않으려면 테스트 세이브로 실행하세요: `-fksave listen -fkfresh -fkrich -fkgear` (빌드 실행 인자, [testing.md](testing.md)).

### 3.1 배경음

| 장면 | 들어 볼 것 | 바꿀 곳 |
|---|---|---|
| 전체 | 배경음과 효과음의 균형. 배경음 음량은 측정으로만 정함 | `Music.Volume` (0.4, `Music.cs`) |
| 타이틀 → 지도 → 수족관 | 곡 전환이 부드러운지(1.5초 크로스페이드), 루프 이음매가 들리는지 | `Tools/Music/cues/*.py` |
| 낚시 대기 | 시간대가 바뀔 때 낮↔밤 스템이 8초에 걸쳐 바뀌는지 (`-fktime 16:50 -fktimescale 20`으로 빨리 확인) | `FishingController.Music.cs` |
| 파이트 | 파이트 스템이 장력에 따라 올라가는지, 너무 작지 않은지(파이트 스템은 낮 스템보다 2–4 dB 작게 만들어져 있음) | 큐 스크립트의 `fight` 트랙 `vol` |
| 잡음 / 놓침 | 희귀도별 팡파르, 놓침 sting. **기존 `Sfx.Catch`와 팡파르가 겹쳐 남** | 겹침 정리는 아직 미결(8절) |
| 전설어 조우 (`-fkencounter now`) | 레이어가 쌓이는지, 덮칠 때 무음, 걸리면 `sting_hook` → 전설어 곡, 실패하면 `sting_fail` | `FishingController.Music.cs` |
| 전설어 6곡 | 서로 비슷하게 들리지 않는지(같은 틀로 짜여 있음) | `Tools/Music/cues/legend_*.py` |
| 설정 → 배경음 끔/켬 | 즉시 꺼지고 켜지는지, 옛 세이브에서 켜짐으로 시작하는지 | `SettingsUI`, `SaveData.musicOn` |

### 3.2 효과음

| 소리 | 들어 볼 것 | 바꿀 곳 |
|---|---|---|
| 캐스팅 `cast_swing.wav` | 손을 놓는 순간과 맞는지, 세기에 따라 크기가 자연스러운지 | `FishingController.CastRoutine` |
| 찌 착수 `float_land.wav` | 찌 채비만. 루어는 여전히 합성 `Plop` | 같은 파일의 착수 부분 |
| 릴 클릭 `reel_click_1..9` | 감기 시작 때 1번, 이어서 2→9 순서. 빠르게 감을 때 박자 | `Sfx.ReelClick`, `ReelRestart` (0.35초) |
| 참방거림 `fish_thrash.wav` | 챔질 순간 1.2초, 수면 가까이 몸부림칠 때 계속 | `Sfx.Thrash` 호출부 |
| 줄 튕김 `LineTing` (합성) | 장력 60%부터, 끝으로 갈수록 급하게 높아지는지 | `Sfx.LineStrain`, `StrainFrom` (0.6), `StrainPitchCurve` (2.5) |
| 드랙 풀림 (합성) | 장력이 드랙을 넘을 때만 나는지 | `Sfx.Drag` |
| 발소리 (합성) | 걸음 박자(0.3 m마다)가 애니메이션과 맞는지 | `Angler.StepEvery` |
| 계곡 환경음 `amb_stream.ogg` | 크기(합성 급류에 맞춰 1.4배로 틈) | `Sfx.RecordedAmbGain` |
| 다른 스테이지 환경음 (합성) | 녹음으로 바꿀 후보 | 4.2절 규칙으로 파일 추가 |

---

## 4. 자동 검사와 빌드

### 4.1 배경음 자동 검사

```powershell
Builds\Windows\FishingKing.exe -fksave music -fkfresh -fkrich -fkgear -fkscene Title -fkauto music -fkencounter now -fkmusiclog -fkshots C:\tmp\music
```

- `Player.log`의 `[MUSIC] CHECK` 줄과 마지막 요약을 봅니다. 로그 위치: `%USERPROFILE%\AppData\LocalLow\FishingKing\낚시왕 Fishing King\Player.log`
- 이 시나리오의 대기 시간과 허용 오차(스템 동기 5 ms, 덕킹 0.06 등)는 **한 번도 실제로 돌려 보지 않은 값**입니다. 실패하면 먼저 실제 문제인지, 기준값이 너무 빡빡한지 가려야 합니다.
- 기존 시나리오(`-fkauto fish`, `steer`, `encounter`, `occlusion`, `zoom` 등)도 한 번씩 돌려 회귀가 없는지 봅니다. 경고음(`Sfx.Warn`)을 뺐으니 로그나 캡처에서 그 소리를 기대하던 곳이 없는지도 확인합니다.

### 4.2 빌드

- 에디터 메뉴 `FishingKing > Build Windows` → `Builds/Windows/FishingKing.exe`
- 명령줄: `Unity.exe -batchmode -quit -projectPath <프로젝트> -executeMethod FishingKing.EditorTools.FishingKingSetup.BuildWindows`

---

## 5. 배경음을 고칠 때 (Windows)

### 5.1 준비 (한 번만)

| 도구 | 설치 |
|---|---|
| Python 3.11 이상 | `pip install mido numpy scipy` |
| FluidSynth 2.x | GitHub 릴리스의 Windows 빌드를 받아 `fluidsynth.exe`가 있는 폴더를 `PATH`에 추가 |
| oggenc (vorbis-tools) | Windows용 `oggenc.exe`를 받아 `PATH`에 추가 |
| 사운드폰트 | `FluidR3_GM.sf2`(MIT)를 `Tools/Music/sf2/`에 두거나 환경 변수 `FK_SF2`에 경로 지정. 이 폴더는 git에 올라가지 않음 |

### 5.2 작업

```powershell
python Tools/Music/build.py --lint              # 렌더 없이 검사
python Tools/Music/build.py stage_lake          # 한 곡만 다시 만들기
python Tools/Music/build.py                     # 전부 (약 4분)
```

- **곡의 원본은 `Tools/Music/cues/*.py`입니다.** `Tools/Music/midi/*.mid`는 결과물이라 DAW에서 고쳐도 빌드에 반영되지 않습니다. MIDI로 고치고 싶으면 가져오기 스크립트를 먼저 만들어야 합니다(아직 없음).
- 다시 빌드하면 내용이 같아도 OGG가 바이트 단위로 달라질 수 있습니다. **고치지 않은 곡의 OGG 변경은 되돌리고**(`git checkout -- <파일>`) 고친 곡만 커밋하세요.
- 곡 목록, 템포, 스템 규칙은 [Tools/Music/README.md](../Tools/Music/README.md)의 큐 표가 기준입니다.

---

## 6. 효과음·환경음 파일을 넣을 때

파일 이름만 맞추면 코드는 바꿀 필요가 없습니다. 파일이 없으면 합성음으로 돌아갑니다.

| 파일 (`Assets/Resources/Audio/…`) | 쓰이는 곳 | 형식 |
|---|---|---|
| `Sfx/cast_swing.wav` | 캐스팅 휘두르기 | 모노, 앞 무음 없이 |
| `Sfx/float_land.wav` | 찌 착수 | 모노 |
| `Sfx/reel_click_1.wav` … `_N.wav` | 릴 클릭 (1번 = 감기 시작, 2…N 순환). 1번부터 빈 번호 전까지 읽음 | 모노, 앞 무음 없이 (1 ms 이내) |
| `Sfx/fish_thrash.wav` | 수면 몸부림 (반복 재생) | 모노, **끊김 없는 루프** |
| `Ambience/amb_<스테이지>.ogg` | 스테이지 기본 환경음. `lake`, `stream`, `sea`, `swamp`, `ocean`, `ice`, `cave` | 스테이지 하나에 루프 하나, RMS −24 dBFS 근처, **끊김 없는 루프** |

- 공통: 피크 −1 dB 근처로 정리, 직류 성분 제거. 루프는 끝과 처음을 겹쳐 이어 두어야 이음매가 안 들립니다.
- 녹음 환경음은 `Sfx.RecordedAmbGain`(1.4)배로 재생됩니다. 파일마다 크기가 다르면 파일 쪽 음량을 맞추세요.
- 새벽 새소리·밤 벌레 층(`amb_dawn`, `amb_night`, `amb_night_frogs`)은 아직 합성이고 파일로 바꾸는 경로가 없습니다(필요하면 `Sfx.Foley.cs`의 `DawnLoop`/`NightLoop`에 같은 방식으로 추가).
- 외부 음원을 쓰면 라이선스(CC0 / CC BY / CC BY-NC 등)를 확인하고 README 크레딧에 적습니다.

---

## 7. 머지 순서

1. **PR #1 (문서)** 을 검토하고 main에 머지합니다.
2. `claude/jolly-cannon-bpb1gt`로 PR을 만듭니다. 기준 브랜치를 `docs/overview`로 잡으면 배경음·효과음 변경만 보이고, #1이 먼저 머지됐다면 main을 기준으로 잡습니다(#1 머지 후에는 GitHub가 자동으로 기준을 바꿔 줌).
3. 머지 전 확인 목록:
   - [ ] Unity Console 컴파일 오류 0개
   - [ ] 3절의 플레이 확인을 한 번 거침 (특히 음량 균형, 루프 이음매, 전설어 조우 흐름)
   - [ ] `-fkauto music`과 기존 주요 시나리오가 통과하거나, 실패 원인을 파악함
   - [ ] Unity가 바꾼 `.meta`를 커밋함
   - [ ] 실행 파일 빌드가 됨
4. **Squash and merge**로 합칩니다. 커밋 메시지는 CHANGELOG의 "이후 변경" 항목을 요약하면 됩니다.
5. 머지 후 `docs/overview`, `claude/jolly-cannon-bpb1gt` 브랜치는 지워도 됩니다.

---

## 8. 아직 정하지 않은 것

- 잡을 때 `Sfx.Catch`와 팡파르 sting이 겹침: `Catch`를 빼거나 첨벙 소리로 바꿀지
- 전설어 전조의 드론 효과음(`Sfx.Drone`)과 `sting_omen`·조우 `lurk` 레이어의 역할 겹침
- 레벨업 효과음이 팡파르 직후 겹칠 수 있음: sting이 끝난 뒤로 미룰지
- 나머지 스테이지 환경음과 새벽·밤 층을 녹음으로 바꿀지
- `-fkauto music`의 허용 오차 조정 (첫 실행 결과를 보고)
- 배경음 음량(`Music.Volume`)과 각 스템 균형 (귀로 듣고)

---

## 9. 로컬 Claude Code 세션에 맡길 때

로컬 세션은 Unity와 실행 파일을 직접 돌릴 수 있고 로그를 읽을 수 있습니다. 소리는 여전히 듣지 못하니 **귀로 판단하는 일은 사람이**, 실행·로그·수치 확인은 세션이 맡는 게 좋습니다.

시작할 때 읽게 할 것: 이 문서, [music.md](music.md), [Tools/Music/README.md](../Tools/Music/README.md), [CHANGELOG.md](../CHANGELOG.md)의 "이후 변경".

요청 예시:

```
claude/jolly-cannon-bpb1gt 브랜치를 받아 Docs/local_workflow.md 4절대로 확인해 주세요.
1. Unity 6000.3.22f1 batchmode로 프로젝트를 열어 컴파일 오류와 경고를 정리
2. Windows 빌드 후 -fkauto music, -fkauto fish, -fkauto encounter를 돌리고 Player.log의 [MUSIC] CHECK / 실패 줄을 요약
3. 실패가 있으면 실제 버그인지 허용 오차 문제인지 코드로 가려서 보고 (고치기 전에 먼저 알려 주기)
4. Unity가 바꾼 .meta 파일만 따로 커밋
코드 수치(음량, 시간)를 바꿀 때는 이유와 이전 값을 커밋 메시지에 남겨 주세요.
```

주의할 점:
- 클라우드 세션이 쓴 컴파일 검사 환경과 효과음 미리듣기 도구는 저장소에 없습니다(임시 폴더). 로컬에서는 Unity가 그 역할을 합니다.
- 배경음을 다시 빌드하면 바이트만 달라진 OGG가 생기니, 커밋 전에 실제로 고친 곡만 남기게 하세요(5.2절).
