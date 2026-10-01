# 테스트 스위치 (빌드 실행 인자)


`-fksave <이름>` 별도 세이브 · `-fkfresh` 새 게임 · `-fkrich` 코인/레벨/전 스테이지 해금 ·
`-fkgear` 최고 장비 · `-fkbait <id>` 미끼 지급 · `-fkscene Fishing -fkstage lake` 바로 이동 ·
`-fkauto fish|tour|walk|flick|windup -fkshots <폴더> [-fkcount N]` 자동 플레이 + 스크린샷 (windup: 캐스팅 준비 가이드 화살표 전체 화면 샷) ·
`-fkflick <속도>[:<각도>]` fish/walk 자동 캐스팅의 튕김 (창 높이/초, 화면 기준 각도, 기본 3.4:0) ·
`-fkaim <-1..1>` fish 자동 캐스팅 방향 (±1 = 최대 각도) ·
`-fkencounter [now|natural]` 전설어 조우 테스트 (그 전설어의 첫째 키 지급(동굴은 야광 에기), `-fkbait`/`-fklure`로 준 미끼가 그 전설어의 키면 그대로 씀 — 예: 늪 `-fkbait bait_popper`; 쿨다운/보정 없음; now = 착수 1초 후 바로 조우, natural = 발밑 앞 16m에 은신처·조건 완화) ·
`-fkauto lure -fklure <id>|all` 루어 액션 테스트 (톡 = 아래로 당기기; 먼저 미노우로 위로 쓸기는 톡이 아닌지·아래로 당기면 낚싯대가 들리는지 확인, 루어별 q_good≥0.7 / q_bad≤0.3; Q를 재는 동안 물고기는 안 덤빔(입질이 스크립트 사이클을 끊어 점수가 무작위로 흔들림), `-fklurebites`면 예전처럼 입질 허용, 톡 루어는 사이클마다 톡 수·간격·쉬는 시간 로그) ·
`-fkauto steer [-fksteer clear|fights|arrow]` 낚싯대 스윕·사이드 프레셔 테스트 (루어 캐스팅 후 왼쪽→오른쪽 스윕하며 감기 궤적, 찌 옆으로 끌기, 같은 물고기로 반대/같은 쪽/중앙 사이드 프레셔 파이트(꺾임·꺾인 시간·최대 기울임에서 모델 배율), 체력 소모·장력·달리기 길이는 파이트 모델을 같은 물고기·같은 달리기로 짝지어 1/60초 고정 스텝으로 측정(`[SIDE] BENCH`, 결정적; 장력은 반대쪽 > 같은 쪽, 낚싯대를 가로지르는 옆 달리기 > 낚싯대가 가리키는 쪽으로 가는 옆 달리기), 사이드 프레셔는 물고기가 실제로 옆으로 휩쓸 때만(목표 방위에 0.7초 이상 머문 프레임엔 화살표·사이드 0, 화살표 반대 방향 0, 모델과 HUD/화살표의 기울기 데드존 같은 값·어긋난 프레임 0; `sweeping:` 줄에 달리기 중 켜진 비율), 밀기와 원·톡·탭 구분, 극단 기울기와 파이트 중 낚싯대-모자 간격, 낚싯대 회전 한계에서(물고기 +35°·±45°, 왼쪽/가운데/오른쪽) 그려진 기울기·파이트 바 기울기·모델 기울기가 같은지와 한계 밖 물고기에 안 기울이면 장력 배율 ≈1(`[SIDE] limit` 줄에 예전 값(요청 기울기·예전 낚싯대-줄 각도)도), 좌우 최대 회전 캡처 fight_yaw_max_right/left; 얼음에선 스윕이 없는지 확인. clear = 간격만(`-fkactors2d`와 함께 2D 스프라이트 확인), fights = 파이트 비교만(+화살표), arrow = 사이드 프레셔 화살표만: 찌·루어로 안 밀기/맞게/반대로 캡처, `arrowshot` 로그에 화면 좌표) ·
`-fkfloatshots` (fish / steer에 추가) 찌 채비 파이트의 찌 순간 캡처(수면 끌림·잠김·떠오름·점프·화살표·랜딩·놓침), `floatshot` 로그에 화면 좌표; 얼음 steer에선 구멍 파이트를 따로 한 번 더(`-fkicecm <cm> -fkicedepth <m>`, 기본 45cm·3m) ·
`-fkobstacles show|off` 장애물 전부 표시 / 장애물 없이(예전처럼) · `-fkobstlog` 장애물 이벤트마다 `[OBST]` 로그 · `-fksnag <배율>` 밑걸림 확률 배율(0 = 없음, 99 = 즉시) · `-fkobstseed <n>` 장애물 난수 시드 ·
`-fkauto obstacles` 장애물 테스트 + 캡처 (예: `-fkfresh -fkrich -fkgear -fksave obst -fkscene Fishing -fkstage stream -fkauto obstacles -fkobstlog -fkshots <폴더>`: 조준 윤곽, 바위에 튕기는 캐스팅·뱅크샷, 스푼 밑걸림 톡/크랭크 스윕/억지로 감아 끊김, 호수 연잎 개구리·지렁이, 보트로 도망치는 배스를 사이드 프레셔로 끌어냄(0.3회전/초로 감아 억지로 끌어내기 없음), 바다(만조 정조) 테트라포드로 파고드는 감성돔을 PE 3호로 방치 → 쓸려 끊김, 잡으면 결과 카드 닫고 계속, 커버 근처 입질 비교, 늪 조준 윤곽, 스테이지별 장애물 표시; 캡처 obst_aim·obst_aim_swamp·obst_bounce·obst_bankshot·obst_snag·obst_snag_free·obst_snag_arrow·obst_pad·obst_pad_snag·obst_rub·obst_break·obst_pullout·obst_show_<스테이지>; `[OBST] CHECK` 줄) ·
`-fkauto encounter [-fkencplay perfect|bad|early|both]` 조우 자동 플레이 + 캡처 (예: `-fkfresh -fkrich -fkgear -fkrod rod_bamboo -fksave enc -fkscene Fishing -fkstage cave -fkencounter now -fkauto encounter -fkshots <폴더>`; 캡션이 얼굴·위에서 본 개구리를 가리지 않는지, 게이지가 개구리를 가리지 않는지·조우마다 4번 넘게 자리를 옮기지 않는지 검사, 옮길 때마다 `[CAP] gauge` 줄, 위에서 본 조우는 처음 세 번을 gauge_<id>_N_<자리>로 캡처; 키에 위에서 본 프레임(lure_<루어>_top_*)이 있으면 견제를 위에서 보는지(늪: 개구리·포퍼), 프롬프트 동사 아이콘이 기분의 동사와 맞는지(감다 멈춤 = verb_runpause) 검사, 프롬프트가 보이는 첫 견제 샷의 아이콘을 확대해 enc_<id>_verb_zoom 저장)

캐스팅 세기 조정: `Assets/Scripts/Fishing/FlickCast.cs` 맨 위 상수 (터치는 cm/s, 마우스는 모니터 높이/초).
놓을 때마다 `Player.log`에 `[Flick]` 줄이 남으니 실제 기기에서 튕긴 속도를 보고 `TouchSpeedFull` 등을 맞추세요.


## 이후 추가된 스위치

| 스위치 | 용도 |
|---|---|
| `-fklegend <id>` | `-fkencounter`와 함께: 불러낼 전설어 (먼바다: `blue_marlin` / `great_white`) |
| `-fkencdebug` | 조우 중 0.1초마다 감기 속도·원 인식 등 `[ENC] dbg` 로그 |
| `-fkauto periods` / `-fkauto current` | 시간대 4종 렌더 확인 / 물살·물때·멘딩·파이트 검사 (물때: 같은 물고기·같은 프레임에서 들물 최강 / 만조 정조의 한 번 굴림당 접근 확률 비교, 실제 접근 횟수는 로그만) |
| `-fkauto tidebites [-fktidesecs <s>]` | 물때별 분당 입질 수: 만조 정조 / 들물 최강에서 같은 채비(찌, `-fkbait`)를 같은 시드로 각각 2400초(게임 시간, 1/60초 고정 스텝으로 빨리 감기) 담가 입질마다 세고 놓아줌, 8 m 흘러가거나 가장자리에 걸려 멈추면 다시 던짐. 바다는 바뀌기 전 규칙(도달 거리 고정)과 지금 규칙 둘 다 재고 지금 들물/정조 ≥ 1.3배 확인 (예: `-fkfresh -fkrich -fksave tide -fkscene Fishing -fkstage sea -fkbait bait_shrimp -fkfish mackerel -fkauto tidebites`), 물때 없는 곳(먼바다 `-fkbait bait_squid`)은 물때 배율이 두 위상 모두 1인지 확인(두 입질 수는 같은 규칙의 실행 간 편차로 로그만); `[TIDE] CHECK` 줄 |
| `-fklurebites` | `-fkauto lure`와 함께: Q를 재는 동안에도 물고기가 덤빔 (기본은 안 덤빔) |
| `-fkencwinh <px>` | 테스트 전용(기본 꺼짐): 조우 창 높이를 강제로 줄임(64..136, 위쪽 고정). 개구리·얼굴이 HUD 자리를 막아 게이지가 다른 자리로 옮겨 가는지 확인 — 예: `-fkstage swamp -fkencounter now -fkauto encounter -fkencwinh 96` (`[CAP] gauge a -> b`·페이드 인 줄, gauge_<id>_N_<자리> 캡처, "gauge moved in the narrowed window" 검사) |
| `-fktime <hh:mm>` · `-fktimescale <x>` · `-fktide <phase>` | 게임 시계 시각·속도·물때 강제 |
| `-fkauto zoom [-fkzoommode off\|125\|150\|active]` | 캐스팅 후 줌 검사 (모드별) · `-fkzoomsettingsonly` 설정 창만 (크롭이 경계(스테이지 그림의 오버스캔까지) 안에 있는지·UV가 렌더 타깃 안인지도 셈) |
| `-fkauto pan [-fkzoommode off\|125\|150\|active]` | 1x 프레임 밖으로 달아나는 물고기를 화면이 따라가는지 검사 (기본 바다, `-fkstage`로 다른 곳; 모드 안 주면 1.25배와 끔 둘 다): 오른쪽에 건 물고기를 정해진 길로 홈 프레임 밖(90 px 밖에서 사진) → 그림 가장자리 근처 → 그림 밖까지 빠르게 → 안으로 → 왼쪽 밖 → 안으로 끌고 다니며 매 프레임 물고기·초릿대가 화면 안(그림 밖 프레임은 따로 셈)·크롭이 경계 안·줌 정지 시 픽셀 정확·카메라가 정수 픽셀·한 프레임 이동 6 px 이하(긴 프레임은 그 시간에 400 px/s까지; 0.25초 평균 320 px/s 이하) 확인, 파이트가 끝나면(1.25배 랜딩 / 끔 놓침) 2초 안에 홈(1x, 카메라·1x 팬 0)으로 돌아오는지, 같은 순간을 오버스캔 끄고(예전처럼 홈 프레임 가장자리에서 멈춤) / 켜고 찍은 pan_<모드>_before / _after, 결과 카드 pan_125_card; 이어서 실제 파이트(파이트 모델 그대로, fish 시나리오처럼 감기) 같은 검사와 pan_natural, 마지막으로 카메라를 판 채 시간을 멈추고 렌더 타깃이 홈 타깃을 팬만큼 민 것과 픽셀 단위로 같은지(스테이지·3D 액터·발판 가림·장애물 윤곽·물 효과가 함께 움직임 — 물 효과는 화면 밖으로 나간 것을 다시 뿌리므로 그린 그대로 멈춰 둠; pan_align_*_rt.png). 1.25배에서 물고기가 초릿대에서 너무 멀어 줌 화면 하나에 둘 다 안 들어가면 1x로 빠졌다가 다시 들어오는지(줌 아웃 프레임 수)도 기록, 테스트 동안 밑걸림 끔. `[PAN] CHECK` 줄, 끝에 "pan test done: N failed" (예: `-fkfresh -fkrich -fkgear -fksave pan -fkscene Fishing -fkstage sea -fkauto pan -fkshots <폴더> -screen-width 1920 -screen-height 1080`) |
| `-fkauto panmeasure [-fkpanstages sea,ocean] [-fkpanfights N] [-fkpanspeed x]` | 스테이지별로 물고기·채비가 1x 프레임 밖으로 얼마나 자주·멀리 나가는지 측정: 캐스팅 부채꼴 모서리(가장 긴 낚싯대, 걷는 범위 양끝에서 ±38°)와 그 스테이지 어종 N마리(기본 8, 시드 고정)를 캐스팅이 떨어지는 곳에 걸어 x배속(기본 3)으로 파이트, 매 프레임 물고기를 홈 1x 프레임·스테이지 그림과 비교 → `[PANM] FIGHT` / `STAGE` / `ALL` 줄(밖에 나간 프레임, 방향별 최대 px, 그림 밖 프레임, 20 px 여유로 그림이 못 보여 주는 프레임) |
| `-fkauto occlusion` · `-fkocclusion off` · `-fkoccwatch` | 발판 가림 검사 / 가림 끄기 / 아무 시나리오에서나 프레임 단위 노출 검출 |
| `-fkaqua feed\|live\|clean\|decor\|tanks\|migrate` | 수족관 시나리오 (사료·생먹이·청소·꾸미기·수조 단계·구 세이브) |
| `-fkaquahours <h>` · `-fkaquafeed basic\|premium` · `-fkaquadirt <0..1>` · `-fkaqualog` | 수족관 시간 빨리 감기·먹이 유지·오염도·로그 |
| `-fkactors2d` | 3D 캐릭터 대신 2D 스프라이트 |
| `-fkrodright <도>` | `-fkauto steer`와 함께: 낚싯대 오른쪽 회전 한계 (기본 30; 40은 파이트 중 가까이 온 오른쪽 물고기에서 낚싯대가 모자에 겹침) |
| `-fksidelog` | 파이트 중 0.25초마다 `[SIDELOG]` 줄: 단계·달리기 방향·fightYaw·목표·물고기 방위·기울임(lean = 보이는 기울기: 사이드 프레셔·파이트 바가 쓰는 값, leanReq = 요청, shown = 보이는 기울기(도), req/eff = 요청/한계 적용(도))·낚싯대 yaw·사이드 프레셔·장력·배율(Tmult), 낚싯대-줄 각도(rodOff 도, 달리는 반대쪽 비율 ang)·물고기가 도는 속도(sweep rad/s)·낚싯대를 가로지르는 정도(across)·옆으로 휩쓰는 중인지(sweeping 0/1)·사이드 프레셔가 셈하는지(active 0/1)·요청 기울기로 잰 예전 낚싯대-줄 각도(rodOffReq 도) |
| `-fkencplay coolsave` → `-fkauto legcool` | 저장된 전설어 쿨다운 검사: `-fkencounter natural -fkauto encounter -fkencplay coolsave`로 실패 조우(쿨다운 저장) 후, 같은 `-fksave`로 `-fkencounter` 없이 `-fkscene Fishing -fkstage <같은 곳> -fkauto legcool` 재실행 → 아직 자리 비움·시간 지나면 복귀·시계 되돌림 상한 확인 |

에디터 빌드: `Unity.exe -batchmode -quit -projectPath <프로젝트> -executeMethod FishingKing.EditorTools.FishingKingSetup.BuildWindows [-fkBuildOut <폴더>]` (기본 출력 `Builds/Windows`).
