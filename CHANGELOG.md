# 변경 기록 (CHANGELOG)

- **이 문서가 다루는 것**: 지금까지 들어간 기능을 기능 단위로 묶어 "무엇이 추가/변경되었는지" 정리
- **관련 코드**: `Assets/Scripts/` 전체, `Assets/Shaders/`, `Assets/Editor/`, `Tools/Blender/`, `Tools/Music/`
- **관련 문서**: [README](README.md) · [구조](Docs/architecture.md) · [낚시 플레이](Docs/fishing_gameplay.md) · [수족관](Docs/aquarium.md) · [아트 파이프라인](Docs/art_pipeline.md) · [데이터 표](Docs/data_reference.md) · [배경음](Docs/music.md)

커밋 히스토리는 영역별(아트 파이프라인 → 렌더 결과물 → 에디터 도구 → 코어 → 낚시 → 전설어 → 수족관 → 테스트 → 사양서 → README)로
한 번에 들어가 있어 기능별 세부 순서는 남아 있지 않습니다. 그래서 아래는 **날짜·버전 순이 아니라 기능별**로 묶었습니다.
수치는 해당 설계 문서에 출처(파일·클래스·상수)와 함께 있으니 여기서는 요약만 적습니다.

---

## 이후 변경

### 호수 2단계: 신종 6종 (통합)
- 추가: 호수에 우리 민물고기 6종 — **떡붕어**(일반, 얕은 수초 평지의 중층, 떡밥), **끄리**(일반, 새벽·저녁에 수중 둔덕·브레이크라인을
  누비는 사냥꾼, 스피너·미노우), **누치**(고급, 물골·자갈·모래 바닥, 지렁이·소프트웜), **동자개**(빠가사리, 고급, 깊은 웅덩이·진흙 바닥,
  밤, 지렁이·새우), **뱀장어**(희귀, 가장 깊은 웅덩이, 주로 밤에 나옴), **강준치**(영웅, 옛 물골의 깊은 물을 누비는 큰 포식자, 새벽·저녁,
  미노우·스푼). 어종 파일마다 크기·가격·파이트·점프·미끼·활동도·백분위 서식지·커버·수족관 먹이가 있고, Blender 그림 4장씩(뱀장어의
  위 그림자는 갈래 꼬리 없이 S자로 가늘어짐). 호수 목록에는 가중치 없이 적어 지형에서 끌어냄. **어종 42종**, 호수 11종(일반 10 + 전설).
  수족관 먹이 수: 사료 19종, 생새우 30종, 정어리 11종.
- 변경: 지도의 스테이지 창이 어종 10종을 넘는 스테이지(호수)는 6칸 × 2줄(86×88)로. 먹이 확률 F의 기본값을 10종 호수의 50 시드
  가운데값으로(대나무 0.537, 카본 0.483, 용 0.435).
- 테스트: `-fkauto depth` D7′에 신종의 자리(시드 1: 누치 자갈+모래 ×1.59, 떡붕어 수초 ×1.56, 뱀장어 밤 웅덩이 ×2.08, 강준치 물골
  ×1.54–2.63, 동자개 밤 순위 52)와 세 호수 순위 차이(1단계 4종 10, 2단계 6종 12.5 이하), `-fkauto cards`(잡은 결과 카드),
  `-fkrecords`(스크린샷용 도감 채우기), `-fkaqua live`에 강준치(정어리를 솟구쳐 삼킴)·동자개(새우를 바닥에서) 먹이기.
- 경제(50 시드 × 대나무·카본·용): F 0.41–0.56, F를 적용한 분당 입질 예전의 ×1.01–1.06, 수입 ×0.94–0.99(값싼 끄리가 입질의 5분의 1).
  실제 담금(찌 네 가지, 대나무 시드 1·13·40과 카본 시드 1, 24 프로세스): 분당 입질 예전의 ×0.89–0.92, 수입 ×0.84–0.87, 추정기 예측의 1.08–1.14배
  (찌만 쓰면 루어를 무는 끄리·강준치 몫만큼 덜 낚임; 루어를 포함한 기준 플레이어는 ×1.02–1.06). 자세히: [lake_phase2_spec.md 13절](Docs/lake_phase2_spec.md)

### 호수 2단계: 자유 지형 · 물고기 자동 조정 · 경제 기준 (코드)
- 변경: 호수 지형이 예전 거리 프로필에 묶이지 않고 **시드마다 성격을 뽑아 자유롭게** 만들어짐(레시피 v2: 모든 세이브의 호수가 다시
  뽑힘). 성격 네 가지(깊이·수초·돌·옆 얕음)로 주 수심 3.1–6.7 m, 턱 끝·경사 폭, 옆·먼 쪽이 얕아지는 정도, 웅덩이 1–2, 수중 둔덕 1–4,
  수초 평지 크기·깊이와 추가 수초 평지 0–3(구역 "수초 평지 1..3"), 재질(수초·자갈·모래)의 문턱. 핀(연잎·갈대·보트·수초밭·통나무·
  잔교 앞·깊은 골)과 물가 1.2 m, 경사 1.5 m/m는 그대로. 깊은 골은 5.4 m 이상을 향해 파 들어감. 호수의 거리 프로필은 만든 바닥의
  줄마다 가운데값(P\*)이고 격자 밖이 그 값(`Bathymetry.Profile`, `StageLayout.ProfileDepth`); 작성된 프로필은 `AuthoredDepth`.
- 추가: 검증 V10(수심 분위)·V11(재질·종류 몫), 50 시드에서 서로 다른 호수(V12), 호수 통계(`Quantile`, `NodeRankPct`, 몫),
  성격 로그 `[BATHY] character …`. 고침: 수심 질의(`MinDepthAlong`/`MinDepthDisc`, V8)가 만드는 중인 격자가 아니라 이전 격자/프로필을
  읽던 것.
- 변경: 서식지의 선호 깊이를 **그 호수의 수심 백분위**로(`depth:p55-p100`, 시간대 이동 `@dawn:-20p`); 얕은 호수든 깊은 호수든
  잉어는 그 호수의 깊은 쪽에. 출현 가중치를 **지형에서 끌어냄**(희귀도 기본값 일반 40·고급 16·희귀 3.5·영웅 1.5·전설 1.2 × 그 호수의
  가용도 0.6–1.4 × 활동도; `stages.json`의 호수 `weight`는 생략, 적으면 덮어쓰기). 호수 개체 수 8 → 15, 대신 미끼 근처의 물고기가
  만남마다 한 번 "먹는 중인지"를 굴림(먹이 확률 F). `RowKeep` 1 → 0.5.
- 삭제: 감지 거리 배율(입질 예산)과 그 추정기, `-fkauto habitat`. 대신 **경제 추정기**(`LakeEconomy`)가 기준 플레이어(부채꼴 전체·찌
  절반 루어 절반·모든 시간대)의 분당 입질·수입을 예전 호수와 비교해 F를 정함(스테이지를 열 때 작업 스레드에서). 50 시드 × 3
  낚싯대에서 F 0.38–0.53, 입질·수입 예전의 0.97–1.03배; 어종마다 가장 좋은 자리는 부채꼴 평균의 1.9–3배.
- 테스트: `-fkauto depth`(D2 V1–V11·V12, D3b, D4 통계, D7′ 세 호수, D11′ 경제, D11b 가중치, D14 먹이 굴림), `-fkauto economy`
  (`-fkecomode new|legacy`, `-fkecorod`, `-fkecoperiods`, `-fkecorigs`, `-fkecosecs`, `-fkecorestock`), `-fkecomode legacy`,
  `-fkfeedall`, 검사기 E8/E9(백분위 띠, 단위 섞임) 망가뜨린 데이터 56가지. 실제 담금(대나무, 찌 4가지, 70자리 × 30초): 분당 입질
  예전의 ×0.93, 수입 ×0.84, 추정기 예측의 1.03·1.04배. 자세히: [lake_phase2_spec.md](Docs/lake_phase2_spec.md),
  [data_reference.md 1.2·2.6절](Docs/data_reference.md), [fishing_gameplay.md 11절](Docs/fishing_gameplay.md)

### 어종 데이터 파일 (1단계: 정리만, 동작은 그대로)
- 변경: 한 어종의 정의가 9곳(`GameDatabase.F`, 점프 목록, 커버 표, `Habitats`, `Diets`, 조우 연결, `BuildStages`, `TimeActivity` 표,
  `FishAgent.HoldChance`)에 흩어져 있던 것을 **어종마다 파일 하나** `Assets/Resources/Data/Fish/<id>.json`(이름 붙은 키: 크기·가격·파이트·
  점프 스타일·수심·미끼·시간대 활동도·커버·수족관 먹이·호수 서식지·계곡 웅덩이·전설어 연결)으로 모음. 스테이지와 거기 나오는 어종·가중치는
  `Assets/Resources/Data/stages.json`. 로더 `SpeciesData`가 예전과 같은 객체를 같은 순서로 만들고, 전설어 조우 행 6개는 그대로
  `LegendEncounters.cs`로 옮김(유인 미끼는 어종의 `baits`에서 — 6종 모두 원래 같았음).
- 추가: 검사기 `SpeciesCheck` — 에디터 메뉴 `FishingKing > Validate Species Data`, batch `SpeciesValidator.Batch`, 빌드 전 검사(오류면
  빌드 안 함), `-fkauto species`. 필수 값·모르는 키·범위·미끼/루어 id·스테이지에 있는 커버 종류·그림 4장·노출·서식지·전설어·Blender
  모델을 봄. 어종 추가 = 파일 하나 + 그림 4장 + `stages.json` 한 줄([data_reference.md](Docs/data_reference.md) 2.7절).
- 변경: 한 번도 맞지 않던 커버 종류 4개를 뺌(우럭·감성돔 `rock`, 가물치·피라루쿠 `weed`: 그 스테이지에 그런 커버 구역이 없음). 게임
  동작은 같음.
- 검증: 예전 C# 표(그대로 옮긴 사본)와 새 데이터의 모든 값이 비트 단위로 같고(위 4개 빼고), 같은 시드에서 `FishSpawner.Pick`이
  7 스테이지 × 시각 8 × 장비 2 × 5000번 모두 같은 어종을 뽑음. 사본은 검증 뒤 지움.
- 테스트: `-fkauto species [-fkrepo <저장소>]` (검사기, 망가뜨린 데이터 49가지, `species_dump.txt`·`spawn_baseline.txt`).

### 호수 바닥 지형 (1단계: 데이터 · 생태 · 누운 찌)
- 추가: 호수의 수심이 **세이브의 세계 시드로 실행 중에 만드는 지형 데이터**가 됨(물속 그림은 없음, 그림은 그대로). 예전 거리 프로필을 바탕으로 얕은 턱, 연잎·갈대 밑 수초 평지, 수초 둔덕, 잔교 앞 깊은 골, 옛 물골, 깊은 웅덩이 1~2, 수중 둔덕 1~3. 0.5 m 격자(x −48..48, z 0..64), 칸마다 수심·바닥 종류·재질(진흙·모래·자갈·수초)·구역 이름(`Bathymetry`, `BathyGen`, `TerrainRecipes`). 그림과 맞추는 핀(연잎 1–2 m, 갈대 ≤ 1.2, 보트 1.2–2.5, 수초밭 2.4–3.0, 가라앉은 통나무 6.225, 잔교 앞은 예전 수심), 0.35–9 m, 경사 1.5 m/m, 검증 8회 + 대체 레시피.
- 변경: 수심은 모두 `StageLayout.DepthAt(x, z)`(24곳). 다른 스테이지와 `-fkbathy off`의 호수는 예전과 비트 단위로 같음, 물때 보정은 그대로.
- 추가: `SaveData.worldSeed`(새 게임 = 새 호수, 옛 세이브는 처음 읽을 때 한 번 뽑음), `lieHint`.
- 추가: 어종별 서식지(`HabitatDef`: 선호 깊이·바닥 종류/재질·브레이크라인·층·시간대 이동 — 새벽·저녁엔 얕게, 낮엔 깊게, 잉어는 밤에 평지로). 목표·등장 위치를 서식지로 뽑되(거리마다 예전 몫은 그대로, 같은 거리 안에서만 옮김), 같은 미끼·채비·시간대·낚싯대의 분당 입질은 감지 거리 배율(0.82–1.22, 물고기가 실제로 시간을 보내는 곳을 흉내 낸 추정기로 계산, 스테이지를 열 때 작업 스레드에서 미리)로 예전의 0.8–1.25배에 맞춤. 물고기는 몸보다 얕은 물에 안 들어가고(톡톡 입질·미끼를 물고 갈 때도), 큰 잉어·배스·황금잉어는 질주의 30%를 깊은 쪽으로.
- 추가: **누운 찌** — 찌 수심이 물보다 깊으면 미끼가 바닥에 닿고 찌가 기울었다 눕고(새 스프라이트 `float_stick_lie`·`float_stick_tilt`), HUD가 금색 "찌 누움", 처음 한 번 힌트. 찌 수심을 늘리고 줄여 그 자리 수심을 잴 수 있음(호수에선 9 m까지).
- 변경: 황금잉어 은신처는 수초 옆 브레이크라인, 물보라 자리는 닿는 칸 중 충분히 깊은 곳(대나무 낚싯대로도).
- 테스트: `-fkbathy off|log|show|dump`, `-fkbathyseed`, `-fkauto depth`, `-fkauto habitat [-fkhabsecs] [-fkhabmode] [-fkhabperiods] [-fkhabrigs]`. 자세히: [terrain_depth_spec.md](Docs/terrain_depth_spec.md), [fishing_gameplay.md 11절](Docs/fishing_gameplay.md), [data_reference.md 2.6절](Docs/data_reference.md)

### 손잡이·낚싯대 위치 (설정 → 조작)
- 추가: 설정 창 왼쪽 아래 **조작** 버튼 → 같은 크기의 **조작** 창(`SettingsUI.OpenControls`): 손잡이 오른손(기본)/왼손, 낚싯대 위치 옆(기본)/가운데. `SaveData.leftHanded`·`rodCentre`(옛 세이브는 오른손·옆), `Game.SetLeftHanded`·`SetRodCentre`.
- 왼손: 자세를 거울로(낚싯대는 오른손·오른쪽 허리, 왼손으로 감는 왼손 감기 릴 — 릴 모델을 실행 중에 좌우로 뒤집음), 회전 한계·몸 돌림·스윕 때 끝이 내려가는 쪽·캐스팅 준비의 쉬는 기울기도 좌우가 바뀜. 모자·옷은 뒤집지 않음.
- 가운데: 배 앞에서 두 손으로(낚싯대 든 손이 손잡이, 다른 손이 릴), 끝은 정면, 회전 한계 양쪽 35°. 정면 근처에선 낚싯대가 머리 뒤로 지나 모자 위로 솟아 보이므로, 모자 비킴은 "모자 테두리를 스치지 않음"(가로지르면 가장 깊이 들어간 깊이를 간격으로 셈).
- 낚시 중·파이트 중에도 바로 적용(자세가 새 손으로 곧바로 옮겨 감). 초릿대를 따르는 모든 것(줄·캐스팅·회수·랜딩·줌/화면 따라가기·장애물·조우 창)은 그대로 따라감.
- 변경: 줌이 아직 풀리는 동안엔 1x 팬이 따라가기 속도(180 px/s)로 홈에 돌아옴(가운데로 들어 줌인 채로 랜딩하면 카메라가 한 프레임 7 px씩 움직였음, `ViewZoom.FollowOne`).
- 테스트: `-fkhand`·`-fkrodpos`·`-fkrodcentremax`, `-fkauto hold`, 스티어 검사가 조합마다의 한계를 씀(`-fksteer edge`·`-fksteeredge`, `[STEER] graze`·`[PAN] home steps` 로그). 자세히: [controls.md](Docs/controls.md) "손잡이와 낚싯대 위치", [testing.md](Docs/testing.md)

### 오디오 설정 (음량)
- 추가: 설정 → **음량** 창(`SettingsUI.OpenAudio`): 전체 볼륨(+ 소리 켜짐/꺼짐), 배경음악(+ 배경음 켜짐/꺼짐), 효과음, 환경음. 0~100%를 10% 단위로, `-`/`+` 버튼·막대 칸 탭·막대 드래그.
  설정 창은 예전처럼 네 줄(높이 444)이고 소리 줄 옆의 `음량` 버튼이 같은 크기 창을 위에 엽니다(가장 작은 캔버스 540에서 리본까지 26 px 여유).
- 추가: `SaveData.masterVol`·`musicVol`·`sfxVol`·`ambVol`(기본 100, 옛 세이브도 100; `SaveSystem.Sanitize`가 0~100·10 단위로 맞춤), `Game.SetVolume`, `AudioMix`.
  슬라이더는 설계된 믹스 위에 곱하는 값(100% = 설계 음량, 곡선 (p/100)²)이고, `soundOn`은 그대로 전체 음소거, `musicOn`은 배경음 켜짐/꺼짐 그대로입니다.
- 자세히: [music.md 1.2·4절](Docs/music.md)

### 팽팽한 줄이 튕기는 소리 (합성)
- 추가: `Sfx.LineStrain`(`LineTing`): 장력이 줄 한계의 60%(`FishingController.StrainFrom`)를 넘으면 고무줄을 튕기는 "띵"(고무줄 녹음을 분석해 맞춘 합성음: 113 Hz, 24번째까지의 배음 중 높은 것부터 빨리 사라짐, 튕긴 뒤 60 ms에 음이 6% 올랐다 처짐, 약 0.3초)이 나기 시작해,
  끊어지기 직전(100%)으로 갈수록 빨라지고(0.9초 → 0.16초 간격) 커지고(0.12 → 0.45) 높아짐(음높이 ×1.05 → ×1.9, 처음엔 천천히·끊어지기 직전에 급하게: `Sfx.StrainPitchCurve` 2.5제곱). 파이트와 밑걸림(걸림 장력) 모두.
  간격은 마지막 "띵"부터 재므로 장력이 60% 언저리에서 오르내려도 넘을 때마다 울리지 않습니다.
- 삭제: 장력 경고음(85% 위 삑삑, `Sfx.Warn`)을 파이트와 밑걸림에서 뺌.

### 녹음 효과음은 넣지 않음 (출처 미확인)
- 음악 브랜치에 있던 녹음 파일 `cast_swing.wav`·`float_land.wav`·`reel_click_1..9.wav`·`fish_thrash.wav`·`amb_stream.ogg`는 출처·라이선스 기록이 없어 넣지 않았습니다.
  코드는 그대로 `Resources/Audio/Sfx/<이름>`·`Resources/Audio/Ambience/amb_<스테이지>`를 찾아 쓰고, 없으면 합성음(캐스팅 `Cast`+`Whoosh`, 착수 `Plop`, 릴 `ReelTick`, 참방거림 합성 루프, 계곡 합성 급류)을 씁니다.
  출처와 라이선스가 확인되면 README 크레딧에 적고 파일을 넣으면 됩니다(임포트 설정은 `MusicImporter`에 이미 있음).

### 효과음 추가 (코드 합성)
- 추가(`Assets/Scripts/Audio/Sfx.Foley.cs`): 발소리 `StepWood`(호수·늪)/`StepStone`(계곡·방파제·동굴)/`StepSnow`(얼음)/`StepDeck`(먼바다, `Sfx.Step`, 0.3 m마다 `Angler.StepEvery`),
  드랙 풀림 루프 `DragLoop`(`Sfx.Drag`: 장력이 드랙을 넘는 동안, 멈추면 저절로 사라짐; 예전엔 `ReelTick` 재사용), `Keep`(수조에 넣기)·`Release`(놓아주기),
  `Success`(뱅크샷·가지 아래로 쏙·방향을 꺾었다·커버에서 끌어냈다·빠졌다), 루어 `Pop`(포퍼, 조우 창의 포퍼도)·`Scurry`(개구리 루어가 연잎 위를 기어감·톡)·`Rattle`(미노우 저킹).
- 추가: 물고기 참방거림 루프(`Sfx.Thrash`, 합성): 챔질 순간 1.2초(수면 가까이 1 m 안의 물고기만, 얼음 구멍 아래는 없음), 파이트 중 물고기가 수면 가까이(0.7 m 안) 있는 동안(질주 중 0.75, 아니면 0.45),
  둘 다 크기에 따라 ×0.5(20 cm)~×1(150 cm). 점프 중엔 멈추고, 호출이 끊기면 0.25초에 걸쳐 사라짐.
- 변경: 환경음이 스테이지마다 따로(`Sfx.StageAmbience`): 호수 잔물결·먼 새, 계곡 급류, 방파제 파도·갈매기, 늪 개구리·벌레, 먼바다 너울·선체 삐걱, 얼음 바람·얼음 갈라짐, 동굴은 그대로.
  시간대 층(`Sfx.AmbienceLayers`): 새벽 새소리(호수·계곡·늪), 밤 귀뚜라미(호수·계곡)·개구리(늪). 예전엔 호수·계곡·방파제·늪·먼바다가 물 루프 하나를 같이 썼습니다.
  계곡(×0.25)·방파제(×0.74)는 잰 크기가 배경음 바탕보다 커서 낮춤(`Sfx.SynthAmbGain`, music.md 1.2절).
- 변경: 효과음 보이스 16개, 쉬는 보이스부터 씀(재생 중인 소리의 음높이가 바뀌지 않게). 출력 장치가 바뀌면 환경음 루프도 다시 재생.

### 배경음 (MIDI 작곡 → OGG 스템)
- 추가: 배경음 파이프라인 `Tools/Music/`(Python으로 음표를 적어 MIDI → FluidSynth 렌더 → 루프·음량을 맞춘 OGG 스템, `Resources/Data/music.json` 매니페스트)과
  임포터 `MusicImporter`(Vorbis, Compressed In Memory, 백그라운드 로드). 빌드에서 곡이 차지하는 크기 약 19 MB.
- 추가: `Music`(`[Game]`): 큐마다 스템별 `AudioSource`를 한 DSP 시각에 `PlayScheduled`해 샘플 단위로 맞물린 **덱**, 스템별 페이드,
  큐 사이 크로스페이드, sting과 그 덕킹, 곡이 없으면 예전 칩튠으로 대체, 출력 장치가 바뀌면 다시 시작.
- 추가: 타이틀·지도·수족관 곡, 낚시 씬의 감독(`FishingController.Music.cs`): 스테이지 곡의 `day`/`night` 스템(그림의 시간대 블렌드를 따름, 시계를 건너뛰면 8초 크로스페이드),
  입질 덕킹, 전설어 기척 덕킹(최대 6초), 장력을 따르는 `fight` 스템, 잡음·놓침·줄 끊김·밑걸림 끊김 sting, 전설어 조우 단계별 스템과 `sting_hook` → `legend_<id>` → `sting_legend` / `sting_escape`.
- 변경: sting이 울리면 예전 칩튠 징글(`Sfx.Catch`·`Sfx.Escape`)은 울리지 않고, 레벨업 소리는 sting이 끝날 무렵으로 미룸, 조우 시작 드론은 `sting_omen` 밑에서 작게(0.6 → 0.25).
- 추가: 테스트 스위치 `-fkmusic off|chiptune`, `-fkmusiclog`, `-fkauto music`(`AutoPilot.Music.cs`).
- 자세히: [music.md](Docs/music.md), [Tools/Music/README.md](Tools/Music/README.md)

### main 쪽 변경 (배경음 브랜치 이후, 요약)
- 사이드 프레셔 S1–S3: 낚싯대-줄 각도로 계산한 부하 +10 / −3 / +5%, 물고기가 실제로 옆으로 휩쓸 때만 셈(커버 질주는 항상), 데드존 0.2 하나, 보이는 기울기 사용, `-fksidelog`·`-fkrodright`.
- 전설어 조우: 안전 영역 안 8자리 게이지, 포퍼 위에서 본 프레임, `verb_runpause`, 피라루쿠 그림자, 황금 잉어 실루엣·코 들이밀기, 얼음 빛기둥(2배), 쿨다운을 벽시계 끝 시각으로 저장(`-fkencplay coolsave`/`-fkauto legcool`).
- 물때: 바다 최대 유속 0.8 → 0.65, `TideReach` 0.85–1.3, 테트라포드 앞 정조(`Cushion`)·`pinHint`, `-fkauto tidebites`.
- 화면 팬: 모든 모드에서 오버스캔 위로 1x 팬, 바다 그림 800 px, 물고기가 멀면 줌아웃, `build_overscan.ps1`, `-fkauto pan`/`panmeasure`.
- 장애물: 먼바다 암초·해초(방어 rock, 만새기·참다랑어 weed), 마모율 0.22 → 0.11, 연잎 흔들림, 얼음 루어는 드랙 깊이에서 멈춤.
- 수족관: 성장 프리미엄 1.6. 3D 낚시꾼 림 0.72.

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
