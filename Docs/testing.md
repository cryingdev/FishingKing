# 테스트 스위치 (빌드 실행 인자)


`-fksave <이름>` 별도 세이브 · `-fkfresh` 새 게임 · `-fkrich` 코인/레벨/전 스테이지 해금 ·
`-fkgear` 최고 장비 · `-fkbait <id>` 미끼 지급 · `-fkscene Fishing -fkstage lake` 바로 이동 ·
`-fkauto fish|tour|walk|flick|windup -fkshots <폴더> [-fkcount N]` 자동 플레이 + 스크린샷 (windup: 캐스팅 준비 가이드 화살표 전체 화면 샷) ·
`-fkflick <속도>[:<각도>]` fish/walk 자동 캐스팅의 튕김 (창 높이/초, 화면 기준 각도, 기본 3.4:0) ·
`-fkaim <-1..1>` fish 자동 캐스팅 방향 (±1 = 최대 각도) ·
`-fkencounter [now|natural]` 전설어 조우 테스트 (야광 에기 지급, 쿨다운/보정 없음; now = 착수 1초 후 바로 조우, natural = 발밑 앞 16m에 은신처·조건 완화) ·
`-fkauto lure -fklure <id>|all` 루어 액션 테스트 (톡 = 아래로 당기기; 먼저 미노우로 위로 쓸기는 톡이 아닌지·아래로 당기면 낚싯대가 들리는지 확인, 루어별 q_good≥0.7 / q_bad≤0.3; Q를 재는 동안 물고기는 안 덤빔(입질이 스크립트 사이클을 끊어 점수가 무작위로 흔들림), `-fklurebites`면 예전처럼 입질 허용, 톡 루어는 사이클마다 톡 수·간격·쉬는 시간 로그) ·
`-fkauto steer [-fksteer clear|fights|arrow]` 낚싯대 스윕·사이드 프레셔 테스트 (루어 캐스팅 후 왼쪽→오른쪽 스윕하며 감기 궤적, 찌 옆으로 끌기, 같은 물고기로 반대/같은 쪽/중앙 사이드 프레셔 파이트(꺾임·꺾인 시간·최대 기울임에서 모델 배율), 체력 소모·장력·달리기 길이는 파이트 모델을 같은 물고기·같은 달리기로 짝지어 1/60초 고정 스텝으로 측정(`[SIDE] BENCH`, 결정적), 밀기와 원·톡·탭 구분, 극단 기울기와 파이트 중 낚싯대-모자 간격; 얼음에선 스윕이 없는지 확인. clear = 간격만(`-fkactors2d`와 함께 2D 스프라이트 확인), fights = 파이트 비교만(+화살표), arrow = 사이드 프레셔 화살표만: 찌·루어로 안 밀기/맞게/반대로 캡처, `arrowshot` 로그에 화면 좌표) ·
`-fkfloatshots` (fish / steer에 추가) 찌 채비 파이트의 찌 순간 캡처(수면 끌림·잠김·떠오름·점프·화살표·랜딩·놓침), `floatshot` 로그에 화면 좌표; 얼음 steer에선 구멍 파이트를 따로 한 번 더(`-fkicecm <cm> -fkicedepth <m>`, 기본 45cm·3m) ·
`-fkobstacles show|off` 장애물 전부 표시 / 장애물 없이(예전처럼) · `-fkobstlog` 장애물 이벤트마다 `[OBST]` 로그 · `-fksnag <배율>` 밑걸림 확률 배율(0 = 없음, 99 = 즉시) · `-fkobstseed <n>` 장애물 난수 시드 ·
`-fkauto obstacles` 장애물 테스트 + 캡처 (예: `-fkfresh -fkrich -fkgear -fksave obst -fkscene Fishing -fkstage stream -fkauto obstacles -fkobstlog -fkshots <폴더>`: 조준 윤곽, 바위에 튕기는 캐스팅·뱅크샷, 스푼 밑걸림 톡/크랭크 스윕/억지로 감아 끊김, 호수 연잎 개구리·지렁이, 보트로 도망치는 배스를 사이드 프레셔로 끌어냄(0.3회전/초로 감아 억지로 끌어내기 없음), 바다(만조 정조) 테트라포드로 파고드는 감성돔을 PE 3호로 방치 → 쓸려 끊김, 잡으면 결과 카드 닫고 계속, 커버 근처 입질 비교, 늪 조준 윤곽, 스테이지별 장애물 표시; 캡처 obst_aim·obst_aim_swamp·obst_bounce·obst_bankshot·obst_snag·obst_snag_free·obst_snag_arrow·obst_pad·obst_pad_snag·obst_rub·obst_break·obst_pullout·obst_show_<스테이지>; `[OBST] CHECK` 줄) ·
`-fkauto encounter [-fkencplay perfect|bad|early|both]` 조우 자동 플레이 + 캡처 (예: `-fkfresh -fkrich -fkgear -fkrod rod_bamboo -fksave enc -fkscene Fishing -fkstage cave -fkencounter now -fkauto encounter -fkshots <폴더>`)

캐스팅 세기 조정: `Assets/Scripts/Fishing/FlickCast.cs` 맨 위 상수 (터치는 cm/s, 마우스는 모니터 높이/초).
놓을 때마다 `Player.log`에 `[Flick]` 줄이 남으니 실제 기기에서 튕긴 속도를 보고 `TouchSpeedFull` 등을 맞추세요.


## 이후 추가된 스위치

| 스위치 | 용도 |
|---|---|
| `-fklegend <id>` | `-fkencounter`와 함께: 불러낼 전설어 (먼바다: `blue_marlin` / `great_white`) |
| `-fkencdebug` | 조우 중 0.1초마다 감기 속도·원 인식 등 `[ENC] dbg` 로그 |
| `-fkauto periods` / `-fkauto current` | 시간대 4종 렌더 확인 / 물살·물때·멘딩·파이트 검사 (물때: 같은 물고기·같은 프레임에서 들물 최강 / 만조 정조의 한 번 굴림당 접근 확률 비교, 실제 접근 횟수는 로그만) |
| `-fklurebites` | `-fkauto lure`와 함께: Q를 재는 동안에도 물고기가 덤빔 (기본은 안 덤빔) |
| `-fktime <hh:mm>` · `-fktimescale <x>` · `-fktide <phase>` | 게임 시계 시각·속도·물때 강제 |
| `-fkauto zoom [-fkzoommode off\|125\|150\|active]` | 캐스팅 후 줌 검사 (모드별) · `-fkzoomsettingsonly` 설정 창만 |
| `-fkauto occlusion` · `-fkocclusion off` · `-fkoccwatch` | 발판 가림 검사 / 가림 끄기 / 아무 시나리오에서나 프레임 단위 노출 검출 |
| `-fkaqua feed\|live\|clean\|decor\|tanks\|migrate` | 수족관 시나리오 (사료·생먹이·청소·꾸미기·수조 단계·구 세이브) |
| `-fkaquahours <h>` · `-fkaquafeed basic\|premium` · `-fkaquadirt <0..1>` · `-fkaqualog` | 수족관 시간 빨리 감기·먹이 유지·오염도·로그 |
| `-fkactors2d` | 3D 캐릭터 대신 2D 스프라이트 |
| `-fkencplay coolsave` → `-fkauto legcool` | 저장된 전설어 쿨다운 검사: `-fkencounter natural -fkauto encounter -fkencplay coolsave`로 실패 조우(쿨다운 저장) 후, 같은 `-fksave`로 `-fkencounter` 없이 `-fkscene Fishing -fkstage <같은 곳> -fkauto legcool` 재실행 → 아직 자리 비움·시간 지나면 복귀·시계 되돌림 상한 확인 |

에디터 빌드: `Unity.exe -batchmode -quit -projectPath <프로젝트> -executeMethod FishingKing.EditorTools.FishingKingSetup.BuildWindows [-fkBuildOut <폴더>]` (기본 출력 `Builds/Windows`).
