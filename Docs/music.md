# 배경음 런타임

- **이 문서가 다루는 것**: 게임이 배경음을 트는 방법(덱·스템·크로스페이드·sting·덕킹·칩튠 대체), 음량과 오디오 설정(음량 슬라이더), 낚시 씬의 곡 규칙, 설정·세이브, 임포트 설정, 테스트 스위치
- **관련 코드**: `Assets/Scripts/Audio/Music.cs`, `Assets/Scripts/Audio/AudioMix.cs`, `Assets/Scripts/Audio/Sfx.cs`·`Sfx.Foley.cs`, `Assets/Scripts/Fishing/FishingController.Music.cs`, `Assets/Scripts/Scenes/*Scene.cs`, `Assets/Scripts/UI/SettingsUI.cs`, `Assets/Editor/MusicImporter.cs`, `Assets/Scripts/Debug/AutoPilot.Music.cs`, `Assets/Resources/Data/music.json`
- **관련 문서**: [README](../README.md) · [배경음 파이프라인 (작곡·빌드)](../Tools/Music/README.md) · [구조](architecture.md) · [테스트 스위치](testing.md) · [데이터 레퍼런스](data_reference.md)

곡을 만드는 쪽(큐 목록, 스템, 작곡 지침)은 [Tools/Music/README.md](../Tools/Music/README.md)에 있고, 이 문서는 그 4절 "런타임 계약"을 코드가 어떻게 지키는지 적습니다.

---

## 1. `Music` — 덱과 스템

`Music`은 `Sfx` 옆에 `[Game]` 오브젝트에 붙습니다(`Game.Boot`). 시작할 때 `Resources/Data/music.json`(`fk_music._register`가 씀)을 `JsonUtility`로 읽습니다.

| 개념 | 내용 |
|---|---|
| 큐(cue) | `music.json`의 한 항목: `id`, `kind`(`loop` / `sting`), `bpm`, `bars`, `loopSamples`, `stems[]`(`name`, `clip` = Resources 경로) |
| 덱(deck) | 재생 중인 loop 큐 하나. 스템마다 `AudioSource` 하나(`Music <cue>` 자식 오브젝트), `loop = true` |
| 샘플 단위 동기 | 덱의 모든 클립이 `loadState == Loaded`가 되면 모든 스템을 **같은 `AudioSettings.dspTime`**(지금 + 0.06초 `StartLead`)에 `PlayScheduled`. 스템 길이가 같으므로(`loopSamples`, 다르면 경고) 반복해도 어긋나지 않음 |
| 스템 음량 | 스템마다 목표 음량과 페이드(동일 전력 곡선: 올라갈 땐 처음이 빠르고 내려갈 땐 처음이 느려서 크로스페이드 중 크기가 유지됨). 모든 페이드는 `Time.unscaledDeltaTime`, 한 프레임에 최대 0.1초(`MaxStep`: 씬 로드 같은 끊김 뒤에는 페이드를 건너뛰지 않고 그만큼 늘림) |
| 큐 바꾸기 | 새 덱이 로드되어 시작되면 옛 덱이 페이드아웃 → 정지(음량 0이 0.05초 `StopHold` 걸린 뒤: 0을 넣은 프레임에 멈추면 믹서가 그 전 음량에서 끊어 딸깍 소리가 남. 끊긴 sting·칩튠도 같음) → `UnloadAudioData` → 오브젝트 삭제. 새 덱이 3초(`LoadTimeout`) 넘게 로드 중이거나 옛 덱이 이미 들리지 않으면(덕킹 0인 sting 밑 등) 옛 덱은 기다리지 않고 페이드아웃. 페이드아웃 중인 같은 큐를 다시 부르면 새로 만들지 않고 되살림 |
| sting | 자기 `AudioSource`에서 한 번. 재생 중에는 모든 덱(그동안 새로 현재가 된 덱, 페이드아웃 중인 덱 포함)을 `duck`까지 0.12초(`StingAttack`)에 낮추고, 끝나기 0.35초(`StingLead`) 전부터 0.9초(`StingRelease`)에 걸쳐 되돌림. 다음 sting이 끊은 sting은 두 번째 `AudioSource`에서 0.05초(`StingCut`)에 페이드아웃. sting 클립은 시작할 때 미리 로드해 둠(늦게 올 sting은 0.75초 `StingMaxWait` 지나면 버리고, 그것이 끊은 sting의 덕킹은 되돌림; 이미 읽기에 실패한 클립의 sting은 울리던 sting을 끊지 않고 무시) |
| 덕킹 | `Duck(level, s)`는 게임 쪽 덕킹(입질 등), sting 덕킹과 곱해짐. 게임 쪽 덕킹은 현재 덱에만 걸리고, 현재가 아니게 된 덱은 그때의 값으로 굳은 채 사라짐. sting 덕킹은 사라지는 덱에도 계속 걸림(전설어 파이트 끝·조우 실패의 덕킹 0 sting 밑에서는 옛 곡도 바로 조용해짐) |
| 칩튠 대체 | loop 큐(또는 `music.json`)가 없거나 클립을 하나도 못 읽으면 예전 합성 칩튠 `Sfx.Music(true)`이 1초(`FallbackFade`)에 걸쳐 들어옴. 진짜 덱이 시작되면 칩튠은 그 덱의 페이드인에 맞춰 사라짐(`Stop`·끔도 페이드, 음량은 `Sfx.MusicVolume`). 없는 큐·클립은 `[MUSIC] missing …` 한 줄(항상, 항목마다 한 번) |
| 오디오 리셋 | 기본 출력 장치가 바뀌거나(헤드셋 연결 등) `AudioSettings.Reset`이면 모든 `AudioSource`가 멈춤(`AudioSettings.OnAudioConfigurationChanged`). `Music`: 다음 `Update`에서 멈춘 현재 덱을 처음 시작할 때처럼 모든 스템을 한 DSP 시각에 루프 처음부터 다시 시작(0.5초 `ResetFade` 페이드인), 멈춘 페이드아웃 덱은 버리고, 칩튠은 다시 재생, 멈춘 sting은 덕킹을 되돌림. `Sfx`: 다음 `Update`에서 환경음·새벽·밤 층 루프와(울리던) 긁힘 루프를 다시 재생(드랙·참방거림은 다음 호출에 스스로 다시 켜짐). 출력이 정말 새 장치로 옮겨 가는지는 Windows에서 장치를 바꿔 가며 확인하지 않았습니다 |

### 1.1 API (`Music`의 정적 메서드)

| 호출 | 하는 일 |
|---|---|
| `Play(cue, fade = 1.5, params (stem, level)[])` | loop 큐 재생. 준 스템은 그 음량, 안 준 스템은 0, 하나도 안 주면 모두 1. 이미 재생 중인 큐면 스템 음량만 `fade`초에 걸쳐 바꿈 |
| `Stem(name, level, seconds)` | 현재 큐의 스템 하나 |
| `Sting(cue, duck = 0.35)` | sting 한 번 + 그동안 덕킹 |
| `WillSting(cue)` | 지금 부르면 그 sting이 울리는지(배경음 켜짐·슬라이더 0 초과, 매니페스트에 있고 클립이 실패하지 않음). 겹치는 예전 칩튠 징글을 빼는 데 씀(3절) |
| `Duck(level, seconds)` | 게임 쪽 덕킹 |
| `Stop(fade)` | 현재 큐 페이드아웃(칩튠도 안 틂) |
| `Preload(cue)` | loop 큐의 클립을 미리 로드 시작(`Play`할 때 바로 시작되게; 전설어 파이트 곡을 `sting_hook` 동안). 끝내 틀지 않은 클립은 씬이 바뀔 때 안 쓰는 에셋으로 풀림 |
| `Current` / `Wanted` / `Chiptune` / `Has(cue)` / `StingNow` / `StingLeft` | 재생 중인 덱의 큐 / 마지막으로 요청한 큐 / 칩튠이 대신하는 중 / 매니페스트에 loop 큐가 있음 / 울리는 sting / 남은 초 |

테스트용(`internal`): `Gain(stem)`(지금 들리는 크기 0..1, `Volume`·슬라이더 제외), `StemVolume(stem)`·`StingVolume`(실제 `AudioSource.volume`), `DuckNow`, `DyingGain`(페이드아웃 중인 덱 중 가장 큰 스템의 크기), `SyncMs()`(현재 덱 스템들의 재생 위치 차이, ms: 네 번 읽어 가장 작은 값 — 믹서가 소스마다 블록을 따로 넘기는 순간에 걸리면 한 블록(48 kHz에서 21 ms)이 거짓으로 보이므로; 진짜 어긋남은 매번 보임), `DeckCount`, `Loading`, `HasSting`, `CueIds`, `Describe()`. `Sfx` 쪽: `LastOneShotVol`, `ChipVolume`, `AmbienceVolumeNow`, `DragVolumeNow`, `RaspVolumeNow`, `TingCount`, `AmbienceRmsDb(stage)`.

### 1.2 음량과 슬라이더

**설계된 믹스**(슬라이더가 모두 100%일 때). 코드의 상수가 그대로 각 소리의 음량입니다.

| 소리 | 설계 음량 (상수) | 게임에서의 RMS (dBFS) |
|---|---|---|
| 곡 원본 (`Song.loudness`) | — | 전설어 −17, sting −17 목표(−1 dBFS 피크 제한에 걸려 실제 약 −18~−19), 메뉴 −19, 스테이지 바탕 −21, 수족관·조우 −22 |
| 덱과 sting | `Music.Volume` 0.4 (−8 dB) | 스테이지 바탕 약 −29 |
| 예전 칩튠 루프 | `Sfx.ChipBase` 0.22 | 약 −33 (사각파 믹스, 정규화로 키우지 않음) |
| 스테이지 환경음 (합성 기본 루프) | `Sfx.AmbBase` 0.35 × `SynthAmbGain` | 호수 −34.1, 계곡 −33.2(×0.25), 방파제 −34.0(×0.74), 늪 −39.6, 얼음 −38.7, 먼바다 −40.9, 동굴 −41.1 |
| 새벽·밤 층 (가중치 1일 때) | `Sfx.AmbLayer` 0.3 | 새벽 새 −46.8, 귀뚜라미 −51.1, 늪 밤(귀뚜라미+개구리) −48.8 |
| 녹음 환경음 (`Audio/Ambience/amb_<id>`, 지금은 없음) | `AmbBase` × `RecordedAmbGain` 1.4 | −24 dBFS RMS로 맞춘 파일이면 약 −30.5: 바탕보다 겨우 1.5 dB 아래. 다시 넣는다면 이 값을 줄일 것 |
| 합성 효과음 | 피크 상한 0.9(`Sfx.Make`: 그보다 큰 클립만 낮추고 작은 클립은 키우지 않음; 클립마다 피크 약 0.35~0.9, 대부분 0.75 아래), 그 위에 `Sfx.Play`의 음량 | 훨씬 큼 |

환경음 수치는 `-fkauto music`이 마지막에 합성 루프의 버퍼에서 잰 값입니다(`[MIX] ambience` 줄, `Sfx.AmbienceRmsDb`). 브랜치에서는 계곡 급류가 −21.2로 바탕(−29)보다 8 dB 컸고 방파제도 −31.4로 거의 같아서, 바탕의 쉬는 마디에 들리되 4~6 dB 아래에 오도록 두 곳만 낮췄습니다(`Sfx.SynthAmbGain`). 나머지는 원래 조용한 질감이라 그대로 둡니다.
실제 악기 음색은 같은 RMS의 사각파보다 작게 들려서, 칩튠보다 몇 dB 크게 두되 효과음보다 한참 아래, 바탕곡의 쉬는 마디에 환경음이 들리도록 맞춘 값입니다.

**슬라이더**(설정 → 음량, 4절)는 이 믹스를 **다시 맞추지 않고 위에 곱하기만** 합니다. 100%가 설계 음량 그대로(×1.0)이고 기본값도 모두 100입니다. 곡선은 `AudioMix.Curve`: (p/100)² — 70%는 약 −6 dB, 50%는 −12 dB, 10%는 −40 dB, 0은 무음.

| 슬라이더 | 곱해지는 곳 (`AudioMix`) |
|---|---|
| 전체 볼륨 (`masterVol`) | `AudioListener.volume = soundOn ? Master : 0` — 모든 소리(배경음·효과음·환경음·합성음 전부)가 리스너를 지나므로 하나로 충분 |
| 배경음악 (`musicVol`) | `Bgm`: 모든 덱 스템(페이드아웃 중인 덱 포함, 매 프레임 `Volume × Bgm × 들리는 크기`), sting과 끊기는 중인 sting(매 프레임), 칩튠(`ChipBase × 페이드 × Bgm`). 0이면 배경음 끔과 같게 덱을 풀어 줌(`Music.On`) |
| 효과음 (`sfxVol`) | `Effects`: 원샷 보이스 16개의 `AudioSource.volume`(그래서 `PlayOneShot` 음량에 곱해지고, 이미 울리는 소리도 바로 따라옴) — 버튼, 발소리, 릴, 줄 튕김, 조우 창의 드론·심장 박동, 수족관 소리 모두; 긁힘(`Rasp`)·드랙·참방거림 루프 |
| 환경음 (`ambVol`) | `Amb`: 기본 환경음 루프와 새벽·밤 층 (`Sfx.ApplyAmbience`: 밤에 ×0.8, 층 가중치를 기억해 두었다가 슬라이더가 바뀌면 바로 다시 적용) |

`AudioMix`는 세이브를 매 프레임 읽지 않고 값이 바뀔 때(`Game.SetVolume`·`SetSound`·`Boot`·`ResetProgress`) `Apply()`가 계산해 둡니다. `Music`은 매 프레임 `AudioMix.Bgm`을 읽고, `Sfx`의 원샷·루프는 `Sfx.ApplyMix()`가 다시 맞춥니다.

## 2. 씬별 곡

| 씬 | 호출 | 출처 |
|---|---|---|
| 타이틀 · 지도 · 수족관 | `Music.Play("title" / "map" / "aquarium")` (1.5초 크로스페이드) | `TitleScene.Start`, `MapScene.Start`, `AquariumScene.Start` |
| 낚시 | `stage_<id>` (아래 3절) | `FishingController.Init` → `InitMusic` |

`[Game]`이 씬을 넘어 살아 있으므로 이전 씬의 곡은 다음 씬의 `Start`가 새 곡을 부를 때까지 이어지다가 크로스페이드됩니다. 상점·도감·설정은 지금 씬 위의 대화상자라 곡이 따로 없습니다.

## 3. 낚시 씬 (`FishingController.Music.cs`)

감독(`TickMusic`)이 `Update`에서 `TickClock` 바로 다음(대화상자로 일찍 반환하기 전)에 매 프레임 상태를 보고, 바뀐 것만 `Music`에 요청합니다.
이벤트로 오는 것은 sting뿐입니다(`CatchMusic` ← `LandRoutine`, `FishOffMusic` ← `FishOff`, `SnagBreakMusic` ← `SnagBreak`).
지금 상태만 보고 정하므로 파이트·조우·전설어 파이트가 어떻게 끝나든 스테이지 곡으로 돌아옵니다(씬을 나갈 때는 `LeaveMusic`이 0.5초 `LeaveUnduck`에 덕킹을 풂).

| 상황 | 곡 / 스템 | 수치 (상수) |
|---|---|---|
| 기본 | `stage_<id>`: `day` = cos(w·π/2), `night` = sin(w·π/2), w = `GameClock.Look`의 저녁 + 밤 가중치(그림이 시간대 경계 30게임분에 걸쳐 섞는 것을 그대로 따름: 새벽·낮 `day`만, 저녁·밤 `night`만) | w가 0.02(`BlendStep`) 넘게 바뀔 때마다 0.5초(`BlendFollow`)에 따라감, 0.25(`BlendJump`) 넘게 뛰면(시계를 맞춤) `PeriodFade = 8`초 |
| 입질 (`Biting`) | 덕킹 | `BiteDuck = 0.55`, 0.15초(`BiteDown`)에 내리고 0.6초(`BiteBack`)에 되돌림 |
| 전설어 기척 (줄이 떨림: 미터 ≥ 0.7, 0.6 아래로 내려갈 때까지: `BuildOn`, `BuildOff`) | 덕킹, 최대 6초(`BuildMax`) | `BuildDuck = 0.65`, 2.5초(`BuildDim`), 가라앉거나 6초가 지나면 1.5초(`DuckBack`)에 되돌림(약한 장비로는 미터가 0.7에 붙어 있어 오지 않을 전설어 때문에 계속 어두워지던 것을 막음) |
| 파이트 (`Fighting`) | `fight` 스템 = `lerp(0.55, 1, inverseLerp(0.2, 0.9, 장력))`(`FightMin`, `TensionLo`, `TensionHi`), 장력(`FightModel.TensionRatio`)은 `TensionTau = 0.5`초로 평활 | 걸 때 0.8초(`FightRise`)에 올리고 그 뒤 0.02(`FightStep`) 넘게 바뀔 때마다 0.25초(`FightFollow`), 끝나면 `FightDown = 3`초에 내림 |
| 잡음 | 일반·고급 `sting_catch` (덕킹 0.35) / 희귀·영웅 `sting_rare` (0.25) / 전설 `sting_legend` (0) | `CatchDuck`, `RareDuck` |
| 놓침 · 줄 끊김 · 밑걸림을 억지로 감아 끊김 | `sting_escape` (0.4 / 밑걸림 0.45; `끊기` 버튼은 sting 없음) | `EscapeDuck`, `SnagDuck` |
| 조우 시작 (Omen) | 덕킹을 0.5초(`UnduckOmen`)에 풂, 스테이지 1.2초 페이드아웃, `encounter` 덱이 무음으로 시작, `sting_omen` (덕킹 없음) | `OmenFade` |
| Open · Eyes | `lurk` (2.5초), 덕킹 0.4초(`UnduckEnc`)에 풂 | `EyesFade` |
| Approach | `lurk` + `approach` (2초) | `ApproachFade` |
| Tease · NoseIn | `lurk` + `approach` + `tease` (1.5초) | `TeaseFade` |
| Lunge · HookWindow | 모두 무음 (덕킹 0, 0.12초) | `WindowDuck` |
| Hooked | 조우 덱 0.3초(`WindowStop`)에 정지, `sting_hook` (덕킹 0), 그동안 `legend_<id>` 클립을 미리 로드(`Music.Preload`) → 남은 길이가 0.3초 이하가 되면 `legend_<id>` (0.4초) | `HookHandoff`, `LegendFade`. 곡이 아직 없는 전설어: 스테이지 곡 + `fight` 1.0 (0.6초 `LegendStage`) |
| TurnAway (실패) | `sting_fail` (덕킹 0), 그 밑에서 스테이지 곡이 다시 시작 | `StageBack = 1`초 |
| 조우 없이 걸린 전설어(테스트 걸기 등) · 스테이지로 돌아옴 | 덕킹을 0.3초(`UnduckFast`)에 풂 | |
| 전설어 파이트 끝 | 잡으면 `sting_legend`, 놓치면 `sting_escape` (둘 다 덕킹 0: 페이드아웃 중인 파이트 곡도 sting 밑에서 조용함), 그 밑에서 스테이지 곡 | |

**이벤트 훅 계약** (줄 끊김 수정 브랜치가 `FishOff`·`LetGo`·`SnagBreak`를 다시 바꿀 예정이라, 줄 위치가 아니라 규칙으로 적습니다):
- 물고기가 빠지는 모든 길(바늘 털림, 줄 끊김, 테스트 놓아주기)이 지나는 **한 곳**(지금은 `FishOff`)의 맨 앞에서 sting 하나(`FishOffMusic`). 그 곳이 바뀌면 훅도 옮김.
- 밑걸림을 억지로 감아 줄이 끊길 때만 `sting_escape`(`SnagBreakMusic`, `SnagBreak(cut: false)`), `끊기` 버튼(`cut: true`)은 sting 없음.
- 전설어 파이트(`musMode == Legend`)에서 빠지면 덕킹 0.
- `-fkauto music`이 놓침·줄 끊김(0.4)·밑걸림 끊김(0.45)·`끊기`(sting 없음)를 따로 확인하므로, 훅이 빠지면 테스트가 실패합니다.

**예전 칩튠 징글과 sting**: 잡음·놓침에는 예전부터 사각파 징글(`Sfx.Catch` 도-미-솔-도, `Sfx.Escape` 내려가는 세 음)이 있었는데, 다른 조의 sting과 같이 울리면 부딪칩니다. 그래서 `Music.WillSting(그 sting)`이면 징글은 빼고(물 튀는 소리·줄 끊기는 소리는 그대로), 레벨업 아르페지오(`Sfx.LevelUp`)는 `Sfx.PlayAfterSting`으로 sting이 0.3초 남을 때까지(최대 3초) 미루며, 조우 시작 드론은 `sting_omen` 밑에서 0.6 → 0.25(`OmenDroneUnderSting`)로 줄입니다. 배경음이 꺼졌거나 sting이 없으면 예전 그대로입니다.

`encounter` 큐가 없으면 조우 창은 칩튠 대신 무음입니다(창의 드론·심장 박동 효과음이 이어 줌). 스테이지 곡이 없는 스테이지(곡을 만드는 중)는 칩튠이 대신합니다.

**일부러 소리가 없는 것** (main에 새로 들어온 기능 중): 화면 팬·줌아웃, 얼음 빛기둥, 물때·찌 고정 힌트(다른 힌트처럼 글만), 사이드 프레셔 화살표, 조우 게이지·카운터·동작 아이콘. 바다 환경음은 물때를 따르지 않습니다(나중에 파도 층을 `TideState.S`로 키우는 것은 가능).

### 3.1 스테이지별 곡과 환경음

| 스테이지 | 곡 스템 | 기본 환경음 | 새벽 층 | 밤 층 |
|---|---|---|---|---|
| 호수 `lake` | day · night · fight | 잔물결·먼 새 | 새 | 귀뚜라미 |
| 계곡 `stream` | day · night · fight | 급류(×0.25) | 새 | 귀뚜라미 |
| 방파제 `sea` | day · night · fight | 파도·갈매기(×0.74) | — | — |
| 늪 `swamp` | day · night · fight | 고인 물·벌레·개구리 | 새 | 귀뚜라미 + 개구리 |
| 얼음 `ice` | day · night · fight | 바람·얼음 갈라짐 | — | — |
| 먼바다 `ocean` | day · night · fight | 너울·선체 삐걱 | — | — |
| 동굴 `cave` | day · night · fight | 물방울(예전 루프) | — | — |

## 4. 설정과 세이브

**화면** (`SettingsUI`): 설정 창은 브랜치의 다섯 줄(높이 488, 리본 위 여유 4 px) 대신 main의 **네 줄 그대로**(`RowStep` 72, 높이 444)입니다. 소리 줄에 켜짐/꺼짐 토글과 파란 **음량** 버튼이 나란히 있고, 음량이 같은 크기(700×444)의 **음량** 창을 위에 엽니다. 두 창 모두 가장 작은 캔버스(540)에서 리본까지 26 px 여유가 있습니다(다섯 줄 + 슬라이더는 들어가지 않음).

음량 창(`SettingsUI.OpenAudio`)의 네 줄:

| 줄 | 막대 | 오른쪽 |
|---|---|---|
| 전체 볼륨 | `masterVol` | 소리 켜짐/꺼짐 (`soundOn`, 설정 창의 소리 줄과 같은 값) |
| 배경음악 | `musicVol` | 배경음 켜짐/꺼짐 (`musicOn`) |
| 효과음 | `sfxVol` | — |
| 환경음 | `ambVol` | — |

한 줄: 이름, `-` 버튼, 10칸 막대(`bar_bg` 위 칸, 켜진 칸 초록), `+` 버튼, `NN%`. 막대는 칸을 탭하거나 끌면 그 칸까지(첫 칸의 왼쪽 1/4보다 왼쪽은 0), `-`/`+`는 10씩. 효과음을 바꾸면 새 음량으로 클릭 소리를 들려 주고, 배경음·환경음은 바뀌는 그대로 들립니다. 이 화면은 키보드 조작이 없어(설정 창 전체가 터치·마우스 전용) 키보드로 움직이지 않습니다.

**세이브** (`SaveData`):
- `soundOn`(기본 `true`): 전체 음소거 그대로. 끄면 `AudioListener.volume = 0`, 켜면 전체 볼륨 값.
- `musicOn`(기본 `true`; `JsonUtility`가 필드 초기값을 남기므로 옛 세이브도 켬): 배경음 켜짐/꺼짐 그대로. `Game.SetMusic(bool)`이 저장하고, `Music`은 매 프레임 `On = !forceOff && musicOn && musicVol > 0`을 봅니다. 끄면 모든 덱(이미 더 길게 페이드아웃 중이던 덱도)과 sting·칩튠이 0.4초(`OffFade`) 안에 사라집니다(마지막으로 요청한 큐와 스템 음량은 기억). 다시 켜면 그 큐가 1초(`OnFade`)에 걸쳐 돌아옵니다.
- `masterVol`·`musicVol`·`sfxVol`·`ambVol`(int, 기본 100): 0~100, 10 단위. `Game.SetVolume(AudioChannel, int)`이 맞추고 저장한 뒤 `AudioMix.Apply()`.
- **옛 세이브**: 슬라이더 필드가 없으면 초기값 100(설계 음량), `soundOn=false`인 세이브는 그대로 음소거(전체 볼륨은 100으로 남아 소리를 켜면 원래 크기). `SaveSystem.Sanitize`가 범위 밖·10 단위가 아닌 값을 맞춤. 버전 올림은 필요 없음(`musicOn`이 main에 처음 들어가므로 옮길 옛 값도 없음).

## 5. 임포트 설정 (`Assets/Editor/MusicImporter.cs`)

- `Assets/Resources/Audio/Music/`: Vorbis, **Compressed In Memory**(재생하며 디코드; 스테이지 스템 3개를 통째로 풀면 수십 MB, Streaming은 여러 스템을 샘플 단위로 맞춰 시작하기 어려움), `loadInBackground = true`, `preloadAudioData = false`(곡을 부를 때 로드, `Music`이 모든 스템이 로드될 때까지 기다림), 품질 0.6, 샘플레이트·스테레오 유지, 플랫폼별 덮어쓰기 제거(스템끼리 다르게 인코딩되지 않게). 빌드에서 소리 전체가 19.3 MB. 원본이 `oggenc -q 2`라 0.6은 두 번째 손실 인코딩입니다; 크기를 줄여야 하면 0.4~0.5로 낮추고(들어 보고 결정), 음질이 문제면 원본을 `-q 5~6`으로 다시 렌더합니다.
- `Assets/Resources/Audio/Ambience/`(녹음 환경음, 지금은 없음): 위와 같되 `loadInBackground = false`, `preloadAudioData = true`(스테이지가 열리면 바로 재생).
- `Assets/Resources/Audio/Sfx/`(녹음 효과음, 지금은 없음): **Decompress On Load**, PCM(코덱 지연 없이 짧은 클릭의 어택 그대로), 모노, 미리 로드.
- 설정을 바꾸면 `GetVersion()`을 올립니다(지금 3). 처음 가져올 때 Unity가 각 `.meta`에 위 설정을 적으므로 그 `.meta`도 커밋합니다.

## 6. 테스트

| 스위치 | 용도 |
|---|---|
| `-fkmusic off` | 배경음 없이 (세이브 설정은 그대로) |
| `-fkmusic chiptune` | `music.json` 무시: 모든 loop 큐가 칩튠, sting 없음 |
| `-fkmusiclog` | 이벤트마다 `[MUSIC]` 로그 (`play`, `stem`, `sting`, `duck`, `start … at dsp …`, `stop`, `free`, `chiptune on/off`, `preload`, `audio reset`) |
| `-fkauto music` | `AutoPilot.Music.cs`, 처음부터 포인터를 가져감(`PointerInput.SimActive`): 타이틀 → 지도(없는 곡 → 칩튠 → 진짜 곡) → **오디오 설정**(아래) → 스테이지 낮 → 밤(크로스페이드 중간과 끝) → 입질 덕킹, 파이트 스템과 장력의 상관, 잡음 sting과 덕킹, 희귀어, 놓침(0.4), 줄 끊김(0.4), 밑걸림 억지로 끊김(0.45), `끊기`(sting 없음), 장력이 문턱에서 오르내릴 때 줄 튕김 간격(3초에 4번 이하) → (`-fkencounter`가 있으면) 조우 성공(각 단계의 스템, 챔질 창 무음, `sting_hook`, `legend_<id>`, `sting_legend`과 그 밑에서 조용한 파이트 곡, 스테이지 복귀)·실패(`sting_fail`과 그 밑에서 조용한 조우 덱, 스테이지 복귀) → 지도 → 수족관 → 스테이지별 환경음 RMS(`[MIX] ambience`). `[MUSIC] CHECK PASS/FAIL` 줄 끝에 그 순간의 `Music.Describe()`, 0.5초마다 스템 동기를 읽어 같은 덱에서 두 번 연달아 5 ms 넘으면 실패(한 번만 보이는 한 블록 21 ms는 `sync transient`로 기록만: 덱이 막 시작했거나 무거운 프레임에서 위치를 읽은 것으로, 다음 읽기에서 0으로 돌아옴 — 정말 어긋난 스템은 저절로 맞춰지지 않음), 마지막에 요약 |

**오디오 설정 검사** (`AudioSettingsTest`, 각 단계마다 실제 `AudioSource.volume`을 `[MIX]` 줄로 남김): 옛 세이브(`soundOn=false`, 슬라이더 없음) → `musicOn` 켬·슬라이더 100·음소거 유지, `Sanitize`(137 → 100, −5 → 0, 44 → 40), 기본값 = 설계 믹스(리스너 1, 스템 0.4) → 캡처 `settings_main` → 음량 버튼 → 캡처 `audio_settings` → 배경음 꺼짐(덱 0개, sting 무음)·켜짐(곡 복귀) → 배경음악 `-` 5번 = 50%(스템 0.4 × 0.25 × 크기, sting 0.1, 칩튠 0.055) → 효과음 막대를 왼쪽 끝으로 끌어 0(원샷·드랙·긁힘 0) → 환경음 다섯째 칸 탭 = 50%(환경음 × 0.25) → 전체 50%(리스너 0.25; 소리 꺼짐 0, 켜짐 0.25) → 캡처 `audio_settings_changed` → 디스크에서 다시 읽어 같은 값 → 모두 100으로.

예: `-fkfresh -fkrich -fkgear -fksave au_music -fkstage lake -fkencounter now -fkauto music -fkmusiclog -fkshots <폴더> -screen-width 960 -screen-height 540` (`-fkscene` 없이 타이틀에서 시작). 매니페스트에 아직 없는 곡은 칩튠이 대신하는지, 없는 sting은 울리지 않는지를 대신 검사합니다.

## 사양서와 달라진 점

브랜치의 이 문서(설계)에서 일부러 바꾼 것과 이유:
- **녹음 효과음·계곡 환경음을 넣지 않음**: 출처·라이선스 기록이 없음. 코드는 그대로 두어 파일이 오면 바로 씀.
- **설정 레이아웃**: 다섯 줄(`RowStep` 64, 높이 488)은 리본 여유가 4 px뿐이고 슬라이더를 넣을 자리가 없어, 네 줄 + 음량 창으로.
- **시간대 스템**: 시간대 경계에서 8초 크로스페이드 → 그림의 블렌드를 따라감(그림은 30게임분에 걸쳐 섞이는데 곡은 경계 한가운데서 갑자기 바뀌던 것). 시계를 건너뛸 때만 8초.
- **기척 덕킹 최대 6초**: 약한 장비에서 끝없이 어두워지던 것.
- **이름 없던 값에 이름**: `WindowStop`, `UnduckFast`, `UnduckEnc`, `UnduckOmen`, `LeaveUnduck`.
- **sting 음량을 매 프레임**: 시작할 때만 넣던 값이라 sting 중에 슬라이더를 움직여도 안 바뀌었음.
- **칩튠 징글과 sting이 겹치지 않게**(3절), **줄 튕김이 문턱에서 연달아 울리던 것**, **원샷 보이스가 울리는 소리의 음높이를 바꾸던 것**, **장치가 바뀌면 환경음이 멈춘 채로 있던 것**, **챔질 참방거림이 바닥 물고기·얼음 밑에서도 나던 것**을 고침.
- **계곡·방파제 환경음을 낮춤**(1.2절, 잰 값).
