# 배경음 런타임

- **이 문서가 다루는 것**: 게임이 배경음을 트는 방법(덱·스템·크로스페이드·sting·덕킹·칩튠 대체), 음량, 낚시 씬의 곡 규칙, 설정·세이브, 임포트 설정, 테스트 스위치
- **관련 코드**: `Assets/Scripts/Audio/Music.cs`, `Assets/Scripts/Fishing/FishingController.Music.cs`, `Assets/Scripts/Scenes/*Scene.cs`, `Assets/Scripts/UI/SettingsUI.cs`, `Assets/Editor/MusicImporter.cs`, `Assets/Scripts/Debug/AutoPilot.Music.cs`, `Assets/Resources/Data/music.json`
- **관련 문서**: [README](../README.md) · [배경음 파이프라인 (작곡·빌드)](../Tools/Music/README.md) · [구조](architecture.md) · [테스트 스위치](testing.md) · [데이터 레퍼런스](data_reference.md)

곡을 만드는 쪽(큐 목록, 스템, 작곡 지침)은 [Tools/Music/README.md](../Tools/Music/README.md)에 있고, 이 문서는 그 4절 "런타임 계약"을 코드가 어떻게 지키는지 적습니다.

---

## 1. `Music` — 덱과 스템

`Music`은 `Sfx` 옆에 `[Game]` 오브젝트에 붙습니다(`Game.Boot`). 시작할 때 `Resources/Data/music.json`(`fk_music._register`가 씀)을 `JsonUtility`로 읽습니다.

| 개념 | 내용 |
|---|---|
| 큐(cue) | `music.json`의 한 항목: `id`, `kind`(`loop` / `sting`), `bpm`, `bars`, `loopSamples`, `stems[]`(`name`, `clip` = Resources 경로) |
| 덱(deck) | 재생 중인 loop 큐 하나. 스템마다 `AudioSource` 하나(`Music <cue>` 자식 오브젝트), `loop = true` |
| 샘플 단위 동기 | 덱의 모든 클립이 `loadState == Loaded`가 되면 모든 스템을 **같은 `AudioSettings.dspTime`**(지금 + 0.06초)에 `PlayScheduled`. 스템 길이가 같으므로(`loopSamples`, 다르면 경고) 반복해도 어긋나지 않음 |
| 스템 음량 | 스템마다 목표 음량과 페이드(동일 전력 곡선: 올라갈 땐 처음이 빠르고 내려갈 땐 처음이 느려서 크로스페이드 중 크기가 유지됨). 모든 페이드는 `Time.unscaledDeltaTime`, 한 프레임에 최대 0.1초(`MaxStep`: 씬 로드 같은 끊김 뒤에는 페이드를 건너뛰지 않고 그만큼 늘림) |
| 큐 바꾸기 | 새 덱이 로드되어 시작되면 옛 덱이 페이드아웃 → 정지 → `UnloadAudioData` → 오브젝트 삭제. 새 덱이 3초 넘게 로드 중이거나 옛 덱이 이미 들리지 않으면(덕킹 0인 sting 밑 등) 옛 덱은 기다리지 않고 페이드아웃. 페이드아웃 중인 같은 큐를 다시 부르면 새로 만들지 않고 되살림 |
| sting | 자기 `AudioSource`에서 한 번. 재생 중에는 모든 덱(그동안 새로 현재가 된 덱, 페이드아웃 중인 덱 포함)을 `duck`까지 0.12초(`StingAttack`)에 낮추고, 끝나기 0.35초 전부터 0.9초(`StingRelease`)에 걸쳐 되돌림. 다음 sting이 끊은 sting은 두 번째 `AudioSource`에서 0.05초에 페이드아웃. sting 클립은 시작할 때 미리 로드해 둠(늦게 올 sting은 0.75초 지나면 버리고, 그것이 끊은 sting의 덕킹은 되돌림) |
| 덕킹 | `Duck(level, s)`는 게임 쪽 덕킹(입질 등), sting 덕킹과 곱해짐. 게임 쪽 덕킹은 현재 덱에만 걸리고, 현재가 아니게 된 덱은 그때의 값으로 굳은 채 사라짐. sting 덕킹은 사라지는 덱에도 계속 걸림(전설어 파이트 끝·조우 실패의 덕킹 0 sting 밑에서는 옛 곡도 바로 조용해짐) |
| 칩튠 대체 | loop 큐(또는 `music.json`)가 없거나 클립을 하나도 못 읽으면 예전 합성 칩튠 `Sfx.Music(true)`이 1초(`FallbackFade`)에 걸쳐 들어옴. 진짜 덱이 시작되면 칩튠은 그 덱의 페이드인에 맞춰 사라짐(`Stop`·끔도 페이드, 음량은 `Sfx.MusicVolume`). 없는 큐·클립은 `[MUSIC] missing …` 한 줄(항상, 항목마다 한 번) |
| 오디오 리셋 | 기본 출력 장치가 바뀌거나(헤드셋 연결 등) `AudioSettings.Reset`이면 모든 `AudioSource`가 멈춤(`AudioSettings.OnAudioConfigurationChanged`). 다음 `Update`에서 멈춘 현재 덱을 처음 시작할 때처럼 모든 스템을 한 DSP 시각에 루프 처음부터 다시 시작(0.5초 `ResetFade` 페이드인), 멈춘 페이드아웃 덱은 버리고, 칩튠은 다시 재생. 멈춘 sting은 덕킹을 되돌림 |

### 1.1 API (`Music`의 정적 메서드)

| 호출 | 하는 일 |
|---|---|
| `Play(cue, fade = 1.5, params (stem, level)[])` | loop 큐 재생. 준 스템은 그 음량, 안 준 스템은 0, 하나도 안 주면 모두 1. 이미 재생 중인 큐면 스템 음량만 `fade`초에 걸쳐 바꿈 |
| `Stem(name, level, seconds)` | 현재 큐의 스템 하나 |
| `Sting(cue, duck = 0.35)` | sting 한 번 + 그동안 덕킹 |
| `Duck(level, seconds)` | 게임 쪽 덕킹 |
| `Stop(fade)` | 현재 큐 페이드아웃(칩튠도 안 틂) |
| `Preload(cue)` | loop 큐의 클립을 미리 로드 시작(`Play`할 때 바로 시작되게; 전설어 파이트 곡을 `sting_hook` 동안). 끝내 틀지 않은 클립은 씬이 바뀔 때 안 쓰는 에셋으로 풀림 |
| `Current` / `Wanted` / `Chiptune` / `Has(cue)` / `StingNow` / `StingLeft` | 재생 중인 덱의 큐 / 마지막으로 요청한 큐 / 칩튠이 대신하는 중 / 매니페스트에 loop 큐가 있음 / 울리는 sting / 남은 초 |

테스트용(`internal`): `Gain(stem)`(지금 들리는 크기 0..1, `Volume` 제외), `DuckNow`, `DyingGain`(페이드아웃 중인 덱 중 가장 큰 스템의 크기), `SyncMs()`(현재 덱 스템들의 재생 위치 차이, ms), `DeckCount`, `Loading`, `HasSting`, `CueIds`, `Describe()`.

### 1.2 음량

`Music.Volume = 0.4`(−8 dB)가 모든 덱과 sting에 곱해집니다.

| 소리 | RMS (dBFS) |
|---|---|
| 곡 원본 (`Song.loudness`) | 전설어·sting −17, 메뉴 −19, 스테이지 바탕 −21, 수족관·조우 −22 |
| 스테이지 바탕 × 0.4 | 약 −29 |
| 예전 칩튠 루프 (`Sfx` 0.22) | 약 −33 (사각파 믹스, 정규화로 키우지 않음) |
| 물 환경음 (`Sfx.Ambience` 0.35) | 약 −34 |
| 합성 효과음 | 피크가 풀 스케일의 0.9로 정규화 (훨씬 큼) |

실제 악기 음색은 같은 RMS의 사각파보다 작게 들려서, 칩튠보다 몇 dB 크게 두되 효과음보다 한참 아래, 바탕곡의 쉬는 마디에 환경음이 들리도록 맞춘 값입니다.
소리 끔(`soundOn`, `AudioListener.volume = 0`)은 여전히 배경음까지 전부 끕니다.

## 2. 씬별 곡

| 씬 | 호출 | 출처 |
|---|---|---|
| 타이틀 · 지도 · 수족관 | `Music.Play("title" / "map" / "aquarium")` (1.5초 크로스페이드) | `TitleScene.Start`, `MapScene.Start`, `AquariumScene.Start` |
| 낚시 | `stage_<id>` (아래 3절) | `FishingController.Init` → `InitMusic` |

`[Game]`이 씬을 넘어 살아 있으므로 이전 씬의 곡은 다음 씬의 `Start`가 새 곡을 부를 때까지 이어지다가 크로스페이드됩니다.

## 3. 낚시 씬 (`FishingController.Music.cs`)

감독(`TickMusic`)이 `Update`에서 `TickClock` 바로 다음(대화상자로 일찍 반환하기 전)에 매 프레임 상태를 보고, 바뀐 것만 `Music`에 요청합니다.
이벤트로 오는 것은 sting뿐입니다(`CatchMusic` ← `LandRoutine`, `FishOffMusic` ← `FishOff`, `SnagBreakMusic` ← `SnagBreak`).
지금 상태만 보고 정하므로 파이트·조우·전설어 파이트가 어떻게 끝나든 스테이지 곡으로 돌아옵니다(씬을 나갈 때는 `LeaveMusic`이 덕킹을 풂).

| 상황 | 곡 / 스템 | 수치 (상수) |
|---|---|---|
| 기본 | `stage_<id>`: 새벽·낮 `day`, 저녁·밤 `night` (`GameClock.Now`를 매 프레임 확인) | 시간대 전환 `PeriodFade = 8`초 |
| 입질 (`Biting`) | 덕킹 | `BiteDuck = 0.55`, 0.15초(`BiteDown`)에 내리고 0.6초(`BiteBack`)에 되돌림 |
| 전설어 기척 (줄이 떨림: 미터 ≥ 0.7, 0.6 아래로 내려갈 때까지: `BuildOn`, `BuildOff`) | 덕킹 | `BuildDuck = 0.65`, 2.5초(`BuildDim`), 기척이 가라앉으면 1.5초(`DuckBack`)에 되돌림 |
| 파이트 (`Fighting`) | `fight` 스템 = `lerp(0.55, 1, inverseLerp(0.2, 0.9, 장력))`(`FightMin`, `TensionLo`, `TensionHi`), 장력(`FightModel.TensionRatio`)은 `TensionTau = 0.5`초로 평활 | 걸 때 0.8초(`FightRise`)에 올리고 그 뒤 0.02(`FightStep`) 넘게 바뀔 때마다 0.25초(`FightFollow`), 끝나면 `FightDown = 3`초에 내림 |
| 잡음 | 일반·고급 `sting_catch` (덕킹 0.35) / 희귀·영웅 `sting_rare` (0.25) / 전설 `sting_legend` (0) | `CatchDuck`, `RareDuck` |
| 놓침 · 줄 끊김 · 밑걸림을 억지로 감아 끊김 | `sting_escape` (0.4 / 밑걸림 0.45; `끊기` 버튼은 sting 없음) | `EscapeDuck`, `SnagDuck` |
| 조우 시작 (Omen) | 스테이지 1.2초 페이드아웃, `encounter` 덱이 무음으로 시작, `sting_omen` (덕킹 없음) | `OmenFade` |
| Open · Eyes | `lurk` (2.5초) | `EyesFade` |
| Approach | `lurk` + `approach` (2초) | `ApproachFade` |
| Tease · NoseIn | `lurk` + `approach` + `tease` (1.5초) | `TeaseFade` |
| Lunge · HookWindow | 모두 무음 (덕킹 0, 0.12초) | `WindowDuck` |
| Hooked | 조우 덱 정지, `sting_hook` (덕킹 0), 그동안 `legend_<id>` 클립을 미리 로드(`Music.Preload`) → 남은 길이가 0.3초 이하가 되면 `legend_<id>` (0.4초) | `HookHandoff`, `LegendFade`. 곡이 아직 없는 전설어: 스테이지 곡 + `fight` 1.0 (0.6초 `LegendStage`) |
| TurnAway (실패) | `sting_fail` (덕킹 0), 그 밑에서 스테이지 곡이 다시 시작 | `StageBack = 1`초 |
| 전설어 파이트 끝 | 잡으면 `sting_legend`, 놓치면 `sting_escape` (둘 다 덕킹 0: 페이드아웃 중인 파이트 곡도 sting 밑에서 조용함), 그 밑에서 스테이지 곡 | |

`encounter` 큐가 없으면 조우 창은 칩튠 대신 무음입니다(창의 드론·심장 박동 효과음이 이어 줌). 스테이지 곡이 없는 스테이지(곡을 만드는 중)는 칩튠이 대신합니다.

## 4. 설정과 세이브

- 설정 창에 **배경음** 켜짐/꺼짐 줄(`SettingsUI`, 소리 바로 아래). 창은 다섯 줄이 되면서 `RowStep` 72 → 64, 높이 444 → 488(가장 작은 캔버스 540 안에 리본까지 들어감).
- `SaveData.musicOn`(기본 `true`; `JsonUtility`가 필드 초기값을 남기므로 옛 세이브도 켬). `Game.SetMusic(bool)`이 저장하고, `Music`은 매 프레임 이 값을 봅니다.
- 끄면 모든 덱과 sting·칩튠이 0.4초(`OffFade`)에 사라집니다(마지막으로 요청한 큐와 스템 음량은 기억). 다시 켜면 그 큐가 1초(`OnFade`)에 걸쳐 돌아옵니다.

## 5. 임포트 설정 (`Assets/Editor/MusicImporter.cs`)

`Assets/Resources/Audio/Music/` 아래 오디오에만: Vorbis, **Compressed In Memory**(재생하며 디코드; 스테이지 스템 3개를 통째로 풀면 수십 MB), `loadInBackground = true`, `preloadAudioData = false`(곡을 부를 때 로드, `Music`이 모든 스템이 로드될 때까지 기다림), 품질 0.6(원본이 `oggenc -q 2`라 다시 인코딩해도 깨끗한 값), 샘플레이트·스테레오 유지, 플랫폼별 덮어쓰기 제거(스템끼리 다르게 인코딩되지 않게). 설정을 바꾸면 `GetVersion()`을 올립니다.

## 6. 테스트

| 스위치 | 용도 |
|---|---|
| `-fkmusic off` | 배경음 없이 (세이브 설정은 그대로) |
| `-fkmusic chiptune` | `music.json` 무시: 모든 loop 큐가 칩튠, sting 없음 |
| `-fkmusiclog` | 이벤트마다 `[MUSIC]` 로그 (`play`, `stem`, `sting`, `duck`, `start … at dsp …`, `stop`, `free`, `chiptune on/off`, `preload`, `audio reset`) |
| `-fkauto music` | `AutoPilot.Music.cs`: 타이틀 → 지도(없는 곡 → 칩튠 → 진짜 곡, 옛 세이브의 `musicOn`, 설정 → 배경음 끔/켬) → 스테이지 낮 → 밤(크로스페이드 중간과 끝) → 입질 덕킹, 파이트 스템과 장력의 상관, 잡음 sting과 덕킹, 희귀어, 놓침 → (`-fkencounter`가 있으면) 조우 성공(각 단계의 스템, 챔질 창 무음, `sting_hook`, `legend_<id>`, `sting_legend`과 그 밑에서 조용한 파이트 곡, 스테이지 복귀)·실패(`sting_fail`과 그 밑에서 조용한 조우 덱, 스테이지 복귀) → 지도 → 수족관. `[MUSIC] CHECK PASS/FAIL` 줄 끝에 그 순간의 `Music.Describe()`, 0.5초마다 스템 동기를 읽어 5 ms 넘으면 실패, 마지막에 요약 |

예: `-fkfresh -fkrich -fkgear -fksave music -fkstage lake -fkencounter now -fkauto music -fkmusiclog -fkshots <폴더>` (`-fkscene` 없이 타이틀에서 시작). 매니페스트에 아직 없는 곡은 칩튠이 대신하는지, 없는 sting은 울리지 않는지를 대신 검사합니다.
