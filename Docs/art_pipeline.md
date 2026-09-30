# Blender 에셋 파이프라인

- **이 문서가 다루는 것**: `Tools/Blender`의 스크립트가 무엇을 만들고 어디에 설치하는지, 하이브리드 스타일 규칙, 빌드 명령과 순서, Unity 쪽 임포터(`PixelArtImporter`, `ActorModelImporter`) 설정
- **관련 코드**: `Tools/Blender/fk_*.py`, `Tools/Blender/build_all.ps1`, `Tools/Blender/variants/hybrid/` (`hyb_*.py`, `build_*.ps1`, `periods/`, `legends/`, `encounter_sets/`, `obstacles/`), `Assets/Editor/PixelArtImporter.cs`, `Assets/Editor/ActorModelImporter.cs`, `Assets/Editor/FishingKingSetup.cs`
- **관련 문서**: [README](../README.md) · [아키텍처(Persp·그리기 순서·가림 런타임)](architecture.md) · [데이터 참조](data_reference.md) · [수족관](aquarium.md) · [시간대 사양](time_currents_spec.md) · [전설어 확장](legends_rollout.md) · [장애물 사양](obstacles_spec.md) · [변경 이력](../CHANGELOG.md)

게임의 모든 그래픽은 Blender를 백그라운드(`-b --python`)로 돌리는 스크립트가 만듭니다. 손으로 그린 픽셀은 없습니다.
세부 API는 각 폴더의 README에 있으니 여기서는 되풀이하지 않고 링크만 겁니다.

- 하이브리드 키트 API, 스테이지 제작 순서, 리뷰에서 나온 규칙: [`notes.md`](../Tools/Blender/variants/hybrid/notes.md)
- 시간대 모듈 API: [`periods/README.md`](../Tools/Blender/variants/hybrid/periods/README.md)
- 전설어 모델 모듈 API(뼈 이름, 팔레트 슬롯, 모델 규칙): [`legends/README.md`](../Tools/Blender/variants/hybrid/legends/README.md)
- 조우 배경 세트 모듈 API(파일 이름과 크기): [`encounter_sets/README.md`](../Tools/Blender/variants/hybrid/encounter_sets/README.md)

---

## 1. 폴더와 스타일 변형

| 위치 | 상태 | 출력 |
|---|---|---|
| `Tools/Blender/fk_*.py` + `build_all.ps1` | 1세대 공용 모듈. 아이템·UI·시간대 HUD·수족관 청소 아트는 지금도 여기서 만듦 | `Assets/Resources/`에 **바로** 씀 (`fk_common.SPRITES`, `fk_common.DATA`) |
| `Tools/Blender/variants/hybrid/` | **현재 스타일** (README "`variants/hybrid` 가 현재 스타일") | `Tools/Blender/_tmp/variants/hybrid/` (`hyb_core.OUT`)에 만든 뒤 설치 단계에서 복사 |
| `variants/retro16/` | 이전 시안 (클래식 16비트). 하이브리드의 바탕 | `_tmp/variants/retro16/`만 씀 (`build_retro16.ps1` 주석: "never touches Assets/") |
| `variants/cinematic/` | 이전 시안 (시네마틱 분위기). 하이브리드의 분위기 쪽 | `_tmp/variants/cinematic/`만 씀 (`build_cinematic.ps1` 주석, `cine_common.py` 독스트링) |
| `variants/natural/` | 이전 시안 (자연주의). 빌드 스크립트 없음 | — |

하이브리드는 retro16의 기법(한정 팔레트, 팔레트 램프 재질, 그라데이션에만 쓰는 Bayer 디더, 선택적 색조 외곽선)에
cinematic의 분위기(시간대 프리셋이 정하는 하늘·빛·안개·반사·림)를 섞은 것입니다 (`hyb_core.py` 독스트링).

`_tmp/`는 `.gitignore`에 있는 작업 폴더라 저장소에는 없습니다. 게임이 쓰는 결과물만 `Assets/Resources/` 아래에 커밋됩니다.

### Blender 버전과 경로

모든 `.ps1`의 `-Blender` 기본값은 `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`입니다
(`build_all.ps1`, `build_hybrid.ps1`, `build_periods.ps1`, `build_obstacles.ps1`의 `param`).
다른 위치에 설치했다면 `-Blender "<경로>"`로 넘기면 됩니다. 파이썬 스크립트를 직접 돌릴 때는 `Tools/Blender`에서
`blender -b --python variants/hybrid/<스크립트>.py -- <인자>` 형식을 씁니다.

---

## 2. 공통 기반: 카메라·캔버스·렌더 설정

### 2.1 원근 카메라 (`fk_persp.py`)

스테이지는 낚시꾼 등 뒤의 원근 카메라 하나로 렌더합니다. Unity의 `Persp.cs`가 `stage_<id>.json`에 기록된 같은 값으로
투영하므로 배경과 물고기·찌·줄이 픽셀 단위로 맞습니다 (런타임 쪽은 [architecture.md](architecture.md)).

| 상수 (`fk_persp.py`) | 값 | 의미 |
|---|---|---|
| `CAM_BACK` | 10.5 | 발 뒤로 m |
| `CAM_UP` | 4.75 | 발 위로 m |
| `PITCH` | 12.0 | 내려다보는 각도(도) |
| `F_PX` | 520.0 | 400 px 높이 이미지 기준 초점거리(px). `setup_camera`가 `lens = F_PX * 24 / H`, `sensor_height = 24`, `sensor_fit = "VERTICAL"`로 맞춤 |
| `W, H, PPU` | 640, 400, 16 | 스테이지 캔버스 크기와 유닛당 픽셀 |

- 카메라 위치는 `cam_pos(stand_h)` = `(0, -CAM_BACK, stand_h + CAM_UP)`입니다. 스테이지마다 다른 것은 `standH`(발 높이)뿐입니다.
  `standH` 값은 `Assets/Resources/Data/stage_<id>.json`에 있습니다 (lake 1.0, stream 1.4, sea 3.0, swamp 0.9, ice 0.25, ocean 1.7, cave 1.2).
- `fk_stages.render_stage`가 `camBack / camUp / pitch / focalPx / widthPx / heightPx / ppu`를 `stage_<id>.json`에 씁니다.
  하이브리드 스테이지 스크립트는 이 JSON을 새로 만들지 않고 **복사**합니다 (2.4 참고).
- 게임이 실제로 보여 주는 영역은 `hyb_core.CROP = (80, 65, 480, 270)`(x0, y0, w, h, 위에서부터)입니다.
  진짜 수평선 행은 `hyb_core.HORIZON_ROW = H / 2 - F_PX * tan(PITCH)` = 89.5이고 `standH`와 무관합니다.

### 2.2 렌더 설정 (`fk_common.reset_scene`)

하이브리드 스크립트도 모두 `C.reset_scene()`을 먼저 부릅니다.

| 설정 | 값 | 목적 |
|---|---|---|
| `render.engine` | `BLENDER_EEVEE` | |
| `render.film_transparent` | `True` | 알파 컷용 투명 배경 |
| `render.filter_size` | `0.0` | **안티앨리어싱 없음** (픽셀 필터 0) |
| `eevee.taa_render_samples` | `1` | 샘플 1개: 가장자리 섞임 없음 |
| `render.dither_intensity` | `0.0` | Blender 자체 디더 끔 (디더는 후처리에서 직접) |
| `view_settings.view_transform` / `look` | `"Standard"` / `"None"` | 색 변환 없이 재질 색 그대로 |
| 월드 배경 | 검정, 세기 0 | 환경광 없음 |
| 출력 | PNG RGBA 8비트 | |

하이브리드 키트의 `hyb_core.render_passes`는 PNG 대신 32비트 EXR(`render_exr`)로 색 패스와 ID/깊이 패스를 받아
numpy에서 sRGB로 바꿉니다. 그래서 16진 팔레트 색이 정확히 왕복합니다.
검사용 스크립트 `hyb_actors3d_check.py`만 AA를 켜고 끄는 옵션이 있습니다 (`filter_size = 1.2`, `taa_render_samples = 16`).

### 2.3 1세대 후처리: 알파 컷 + 1px 외곽선 (`fk_common.pixelize`)

`fk_*.py`의 스프라이트는 `render_sprite` → `pixelize`를 거칩니다.

- **알파 컷**: `alpha_cut=0.5`. 이 값보다 큰 픽셀은 알파 1, 나머지는 완전 투명 (반투명 가장자리 없음).
- **1px 외곽선**: 실루엣 밖의 4방향 이웃 픽셀에 이웃 색 평균 × `outline_mul`(기본 0.28) + `outline_tint`(기본 `(0.03, 0.02, 0.08)`)를 칠함.
  스테이지 앞 레이어와 수족관 앞 레이어는 `outline_mul=0.4` (`fk_stages.render_stage`, `fk_misc`).
- **툰 셰이딩**: `fk_common.toon_material`은 노멀과 `LIGHT_DIR = (-0.45, -0.62, 0.64)`(정규화)의 램버트를 `SHADE_STOPS` 3단
  (`#5a5a86` / `#a8a8c4` / `#ffffff`, 경계 0.30, 0.55)으로 끊어 기본색에 곱하는 **발광(emission)** 재질입니다. 조명 계산을 렌더러에 맡기지 않습니다.

---

## 3. 하이브리드 스타일 규칙 (`hyb_core.py`)

`hyb_core.py`가 스타일 키트이고, 스테이지·물고기·캐릭터 스크립트는 이 파일을 고치지 않고 헬퍼만 부릅니다 (API 목록은 [`notes.md`](../Tools/Blender/variants/hybrid/notes.md) §2).

### 3.1 툰 셰이딩: 팔레트 램프 재질

- `m_tone(cols, bounds, soft, ...)`은 `lam × 램버트 + 축 그라데이션 + 노이즈 + 줄무늬 + bias`로 스칼라 하나를 만들고,
  `band_ramp`로 팔레트 16진 색의 램프에 통과시킨 뒤 발광으로 출력합니다.
- `soft = 0`이면 `CONSTANT` 램프라 **정확한 팔레트 색만** 나옵니다 (단단한 물체). `soft > 0`이면 경계에 좁은 `LINEAR` 구간이 생기고,
  그 중간색이 후처리에서 순서 디더로 바뀝니다.
- 키 라이트는 모듈 변수 `LIGHT`이고, `use_preset()`이 프리셋의 `key_dir`로 바꿉니다. 초기값은 `(-0.55, -0.5, 0.67)`(정규화).
- 램프를 만드는 `shade` / `ramp_from` / `Pal.darker`는 OKLab에서 밝은 쪽을 프리셋의 `key_col` 색조로, 어두운 쪽을 `shadow_col` 색조로 돌립니다
  (`SHIFT`, 초기값 `dark=285.0, light=95.0, amt=18.0`).

### 3.2 팔레트

- 팔레트는 `hyb_core.Pal(hexes)` 객체입니다. 이미지는 팔레트 인덱스 배열로 다루고 `-1`이 투명입니다.
  마지막에 `to_rgba`가 RGBA로 바꾸므로 **팔레트 밖의 색은 나오지 않습니다**.
- 에셋마다 손으로 고른 한정 팔레트를 쓰고, 분위기 보정은 팔레트 자체에 적용합니다 (`grade_hex`: 틸-오렌지 분할 톤, 채도, S커브.
  시간대용 `grade.gain` / `grade.tint` 포함). 여러 스테이지가 함께 쓰는 에셋은 보정을 약하게 합니다 (낚시꾼 0.55, 물고기 0.35: `notes.md` §2).
- 후처리로 새 색이 필요하면 `recolour` / `blend_idx` / `Pal.extend`가 OKLab 거리 `snap`(기본 0.018) 안의 기존 색을 재사용해 팔레트를 작게 유지합니다.
- 색 수는 `count_colours`로 **출력만** 하고 한도를 강제하는 코드는 없습니다 (`hyb_check.py`, `hyb_period_preview.py`).
- 3D 액터(낚시꾼·릴·전설어)는 스프라이트 대신 **팔레트 JSON**을 씁니다: 재질 이름마다 `{"ramp": [dark, mid, light], "outline": hex}`
  (`hyb_actors3d.palette_json`, `hyb_legend3d`). Unity의 `ActorToon.shader`가 이 3색을 밴드 경계 `_Bands = (0.45, 0.74)`로 끊어 칠합니다.

### 3.3 양자화와 디더 (`quantize`)

- 각 픽셀에서 가장 가까운 팔레트 색과, 그 픽셀을 가장 잘 포함하는 팔레트 선분을 찾습니다. 선형 RGB에서 계산합니다.
- 선분 위치 `t`가 임계값보다 크면 두 번째 색으로 바꿉니다. 기본 임계값은 `BAYER4`(4×4 Bayer)이고, 물·안개에는 `dash_threshold`(가로 대시 디더)를 넘깁니다.
- 팔레트 색과 정확히 같은 픽셀(`dmin < 2e-6`)은 절대 바뀌지 않으므로 **디더는 그라데이션을 의도한 곳에만** 생깁니다.

### 3.4 외곽선·림·정리 (후처리 순서는 `notes.md` §3)

| 함수 | 하는 일 |
|---|---|
| `remove_specks(idx, min_px=3)` | 8연결 조각 중 `min_px`보다 작은 것을 지움 (서브픽셀 지오메트리 파편) |
| `despeckle` | 한 물체 안의 외톨이 픽셀을 이웃 다수 색으로 (디더 픽셀은 보호) |
| `inner_lines` | 깊이 차가 나는 두 물체 경계에서 먼 쪽 픽셀을 한 단계 어둡게 (윤곽선) |
| `outer_outline(idx, pal, lit_steps=1, dark_steps=2, light=...)` | 실루엣 **바깥 1px** 외곽선. 빛 반대쪽과 아래는 `dark_steps` 단계, 빛 쪽은 `lit_steps` 단계 어두운 **팔레트 안의 색조 이동 색**. 검정 잉크를 쓰지 않음 |
| `rim_light` | 프리셋 `rim` 방향의 **바깥 실루엣에만** 림 (`outer_empty`의 `min_gap=3`, 1px 두께 부분은 건너뜀). 외곽선보다 먼저 적용 |

### 3.5 알파와 안티앨리어싱

- `render_passes`의 알파는 `col[..., 3] > 0.5`인 불리언입니다. 반투명 픽셀은 만들지 않습니다.
- 렌더 설정(2.2)으로 AA를 끄고, 결과는 팔레트 인덱스로만 저장합니다 (`save_png`는 정확한 8비트 값으로 저장).
- 예외: 일부 FX·오버레이는 알파를 4분의 1 단계로 씁니다 (`fk_tod.py`, `fk_aquaclean.py` 독스트링의 "quarter step").

리뷰에서 정해진 금지 규칙(반짝임은 먼 3분의 1에만, 연잎 등 떠 있는 것은 물보다 밝게, 물고기 그림자만 어둡게, 물고기에 광택 줄 금지 등)은
[`notes.md`](../Tools/Blender/variants/hybrid/notes.md) "Rules that came out of the reviews"에 있습니다.

---

## 4. 스크립트별 역할

### 4.1 1세대 모듈 (`Tools/Blender/fk_*.py`)

| 스크립트 | 만드는 것 | 설치 위치 | `build_all.ps1` |
|---|---|---|---|
| `fk_common.py` | 공용 헬퍼: 장면 초기화, 툰 재질, 직교 카메라(`ortho_camera`, `fit_camera`), `pixelize`, `write_json` | — | (임포트) |
| `fk_persp.py` | 공용 원근 카메라 (2.1) | — | (임포트) |
| `fk_scene.py` | 스테이지·지도용 재질/프리미티브 헬퍼, 구름 스프라이트 | `Sprites/Stages/cloud_*.png` | `fk_stages.py -- clouds`로 |
| `fk_fish.py` | 36종 옆모습 2프레임 + 위에서 본 그림자 2프레임 (`<id>_0/_1/_t0/_t1.png`) | `Sprites/Fish/` | O |
| `fk_items.py` | 낚싯대·릴·줄·미끼 아이콘, 월드 루어, UI 아이콘, 9-slice 프레임, 찌, 장애물·수족관 먹이 FX 등 (그룹 인자는 독스트링) | `Sprites/Items/`, `Sprites/World/`, `Sprites/UI/`, `Data/ui_borders.json` | O (인자 없이) |
| `fk_character.py` | 1세대 낚시꾼 7포즈 + `character.json` (크롭 `CROP_W, CROP_H, FEET_PX = 96, 112, 10`) | `Sprites/Character/`, `Data/` | O |
| `fk_stages.py` | 1세대 스테이지 7곳의 `<id>_back.png` / `_front.png` + **`stage_<id>.json` 레이아웃** | `Sprites/Stages/`, `Data/` | O (`lake stream sea swamp ice ocean cave clouds`) |
| `fk_misc.py` | 1세대 지도·수족관·로고 | `Sprites/Stages/`, `Sprites/UI/logo.png`, `Data/map.json`, `Data/aquarium.json` | O |
| `fk_tod.py` | 시계·물때·물살 HUD 아이콘과 물 흐름 FX (`-- [ui fx sheet] [dry]`) | `Sprites/UI/`, `Sprites/World/` | X (따로 실행) |
| `fk_aquaclean.py` | 수족관 청소 아트: 이끼·흐림·바닥 때 오버레이, 도구, FX (`-- [dry] [overlays tools fx mock]`) | `Sprites/World/` 등 | X (따로 실행) |

주의: `build_all.ps1`은 `fk_fish` / `fk_character` / `fk_stages` / `fk_misc`의 **1세대 그림을 Assets에 바로 덮어씁니다.**
하이브리드 그림으로 되돌리려면 이어서 `build_hybrid.ps1 -Install`을 돌려야 합니다 (README의 명령 순서와 같음).
또 하이브리드 스테이지의 `stage_<id>.json`은 `fk_stages.py`가 쓴 `Data/stage_<id>.json`을 읽어 복사하므로(2.4), 레이아웃을 바꿀 때는 `fk_stages.py`가 원본입니다.

### 4.2 하이브리드 공용 키트와 도구

| 스크립트 | 역할 |
|---|---|
| `hyb_core.py` | 스타일 키트: 색 계산, `Pal`, **`PRESETS`**, 하늘·물·안개·반짝임·빛줄기, 재질, 지오메트리, 카메라 헬퍼, 렌더 패스·거울 패스, 양자화·외곽선·림, `stage_json` |
| `hyb_presets.py` | 프리셋 견본 시트 `_tmp/variants/hybrid/presets.png`만 렌더 (지오메트리 없이 키트 API로 만든 분위기 판, 스테이지 7개 순서). **프리셋 값은 여기가 아니라 `hyb_core.PRESETS`에 있음** |
| `hyb_period.py` | 시간대 러너 (5장) |
| `hyb_check.py` | 게임 데이터 대비 점검: 물고기 36종×4 파일 크기, `character.json` 필드, `stage_lake.json` 키 (`waterTint` / `waterDeep` / `clouds` / `birds`만 달라도 됨), 색 수 출력 |
| `hyb_zoom.py`, `hyb_review.py` | 확대(최근접)·리뷰 시트 헬퍼 |

### 4.3 스테이지와 프리셋

| 스크립트 | 만드는 것 |
|---|---|
| `hyb_lake.py`, `hyb_stream.py`, `hyb_sea.py`, `hyb_swamp.py`, `hyb_ice.py`, `hyb_ocean.py`, `hyb_cave.py` | 스테이지별 `<stage>_back.png`, `<stage>_front.png`, `stage_<stage>.json` (인자 없음 = 오늘 모습). `-- --period <p>`면 시간대 모습 (5장) |
| `hyb_preview.py`, `hyb_preview_<stage>.py` | 게임 크롭 480×270 합성 미리보기 `preview_<stage>.png` (물고기 그림자·낚시꾼 포함) |

**프리셋 `hyb_core.PRESETS`**: 스테이지마다 하나씩 `lake`, `stream`, `sea`, `swamp`, `ice`, `ocean`, `cave` 7개가 있습니다.
각 프리셋은 하늘 띠(`sky`), 해(`sun`), 광륜(`glow`), 층운(`streaks`), 오로라, 빛줄기(`shaft`), 키 라이트(`key_dir`, `key_col`, `shadow_col`),
림, 대기 원근(`haze`), 안개(`mist`), 반짝임(`glitter`), 물(`water.bands` 6색, `tint`, `deep`), 보정(`grade`)을 정합니다.
하늘·해·광륜 크기는 640×400 캔버스의 **픽셀**(`up` = 수평선 위, `dx` = 중앙 기준 오른쪽), 대기 원근 거리는 **m**입니다.
필드 의미와 스테이지별 분위기 표는 [`notes.md`](../Tools/Blender/variants/hybrid/notes.md) §1에 있습니다.

스테이지 스크립트는 모듈 맨 위에서 `R.use_preset("<stage>", **덮어쓰기)`(시간대 대응 후에는 `PER.use_preset`)를 먼저 부르고,
`hyb_core.py`는 고치지 않습니다. 하위 dict는 병합되고, `{"__whole__": True, ...}`는 통째로 바꿉니다 (`Preset.copy`).

### 4.4 캐릭터·물고기·지도·수족관

| 스크립트 | 만드는 것 | 설치 |
|---|---|---|
| `hyb_fish.py` | 36종 `fish/<id>_0/_1/_t0/_t1.png` (`fk_fish` 지오메트리를 읽기 전용으로 쓰고, 이 프로세스 안에서만 재질을 교체) | `build_hybrid.ps1 -Install` → `Sprites/Fish/` |
| `hyb_character.py` | 낚시꾼 7포즈 `character/angler_<pose>.png` + `character.json` (`fk_persp.setup_camera(0.0)`으로 렌더, 96×112 크롭, 발은 아래에서 10 px) | `-Install` → `Sprites/Character/`, `Data/character.json` |
| `hyb_actors3d.py` | 3D 낚시꾼 `angler.fbx` + `angler_palette.json`, 릴 `reel_<id>.fbx` + `reel_palette.json`, `actors3d_data.json` → `_tmp/actors3d/` | 설치 단계가 스크립트에 없음. `Assets/Resources/Models/`에 파일이 있으나 복사하는 코드는 찾지 못함 (코드에서 확인하지 못함) |
| `hyb_actors3d_check.py` | FBX 재임포트 검사, `check_angler.png` / `check_reels.png` / `check_report.json` | — |
| `hyb_map.py` | 지도 `map_world.png`(640×400), `map.json`, `preview_map.png` (`use_preset("sea", key_dir=(0.5, 0.4, 0.77))`) | `-Install` → `Sprites/Stages/`, `Data/map.json` |
| `hyb_aquarium.py` | 1단 수족관 `aquarium_back.png` / `aquarium_front.png` / `aquarium.json` | `-Install` → `Sprites/Stages/`, `Data/` |
| `hyb_preview_aquarium.py` | 수족관 미리보기 | — |
| `hyb_aquatanks.py` | 수조 5단계 `aquarium_<n>_back/front.png` + `aquarium_tank_<n>.json` (`-- [0 1 2 3 4] [install]`) | `install` 인자 → `Sprites/Stages/`, `Data/` |
| `hyb_aquadecor.py` | 수족관 장식 `decor_*.png` | 기본이 설치: `Sprites/World/`, `Sprites/Items/` (`-- dry`면 `_tmp/aquadecor/`) |

수족관 쪽 규칙(좌표 변환, 슬롯 등)은 [aquarium.md](aquarium.md)에 있습니다.

### 4.5 조우 배경 (`hyb_encounter.py`, `encounter_sets/`)

- `hyb_encounter.py`가 러너이고 `hyb_encounter_kit.py`가 헬퍼와 공유 스프라이트입니다. 세트 하나 = `encounter_sets/<set>.py` 파일 하나.
- 현재 세트 모듈: `cave`, `lake`, `swamp`, `ice`, `ocean`, `swamp_top` (`_template.py`처럼 `_`로 시작하면 `--all`에서 제외).
  `swamp_top`은 피라루쿠 유혹 장면을 수면 **위에서 내려다보는** 세트로, 떠 있는 소품과 `lure_frog_top_0..3.png`도 만듭니다.
- 프리셋은 모듈의 `PRESET`(없으면 세트 id)을 씁니다: `R.use_preset(getattr(S, "PRESET", sid))`.
- 출력: `_tmp/variants/hybrid/encounter/*.png`. `--install` 또는 `build_hybrid.ps1 -Install`이 `Assets/Resources/Sprites/Encounter/`로 복사합니다.
  리뷰 시트 `encounter_sheet*.png`와 `_`로 시작하는 파일은 복사하지 않습니다.
- 파일 이름·크기 표(`uw_<set>_bg.png` 640×400 불투명 등)와 모듈 API는 [`encounter_sets/README.md`](../Tools/Blender/variants/hybrid/encounter_sets/README.md).
- 새 프레임 `enc_frame_<set>.png`를 추가하면 `PixelArtImporter.Borders`에 6 px 테두리를 코드로 등록해야 합니다 (7장).

### 4.6 전설어 3D 모델 (`hyb_legend3d.py`, `legends/`)

- `hyb_legend3d.py`는 범용 빌더(불러오기·검증·리그·내보내기·검사), `hyb_legend_kit.py`는 지오메트리 헬퍼, `hyb_legend_preview.py`는 리뷰 목업입니다.
- 현재 모듈: `coelacanth`, `golden_carp`, `arapaima`, `sturgeon`, `blue_marlin`, `great_white`.
- 계약은 `hyb_actors3d.FBX_OPTS`와 같습니다: `axis_forward="-Z"`, `axis_up="Y"`, `global_scale=1.0`, `apply_unit_scale=True`,
  `mesh_smooth_type="FACE"`, `add_leaf_bones=False`, `bake_anim=False`, `embed_textures=False`.
  모델 길이 1.0 m(코끝 z +0.50, 꼬리 끝 z −0.50), 런타임 스케일 = cm / 100, 뼈마다 강체 메시 `geo_<Bone>`, 뼈 휴식 회전은 항등.
- 삼각형 한도 `TRI_BUDGET = 3500` (넘으면 `LEG ERROR`로 내보내기 중단).
- 재질 이름은 `<prefix>_<slot>` (Unity의 `ActorArt`가 이름으로 전역 캐시하므로). `_glow`로 끝나는 슬롯은 조명 없이 밝은 톤, 외곽선 없음.
  외곽선 색 기본값은 `#0b1322` (`hyb_legend3d`의 `g("OUTLINE", "#0b1322")`).
- 출력: `_tmp/actors3d/legend_<id>.fbx`, `legend_<id>_palette.json`(+ report, check, blend). `--install`이 FBX와 팔레트 JSON을
  `Assets/Resources/Models/`로 복사합니다 (`hyb_legend3d.MODELS`).
- 모듈 API, 뼈 이름 규칙, 팔레트 슬롯은 [`legends/README.md`](../Tools/Blender/variants/hybrid/legends/README.md).

---

## 5. 시간대 (`hyb_period.py`, `periods/`)

- 스테이지 스크립트는 한 번 실행에 한 모습만 렌더합니다. `--period` 없이 돌리면 **오늘 모습**(레거시) → `_tmp/variants/hybrid/<stage>_back.png` 등.
- `-- --period <p>` (`dawn | day | evening | night`)면 스테이지 프리셋 위에 `periods/<stage>.py`의 `PERIODS[p]`를 얹어 렌더하고,
  `--dry`가 아니면 `Sprites/Stages/<stage>_<p>_back.png` / `_front.png`와 `Data/Periods/<stage>_<p>.json`(룩 JSON)에 설치합니다
  (`hyb_period.INSTALL_SPR`, `INSTALL_DATA`). 시간대 실행은 레거시 이름을 절대 쓰지 않습니다.
- 스테이지마다 오늘 모습에 해당하는 **네이티브 시간대**가 있습니다 (`hyb_period.NATIVE`):
  lake·swamp·ice `evening`, stream·ocean `dawn`, sea·cave `day`. 네이티브 실행은 설치된 `<stage>_<layer>.png`와 바이트 단위로 같아야 하고
  `HYB PERIOD CHECK ... identical`을 출력합니다. `PERIODS[NATIVE]`는 비어 있어야 합니다.
- 룩 JSON 기본값은 `hyb_period.LOOK_DEFAULTS` (예: 밤 `actorTint="#8e9ac0"`, `glintDensity=0.3`, `birds=False`), 모듈의 `LOOK[p]`가 덮어씁니다.
- 앞 레이어를 먼저, 뒤 레이어를 나중에 렌더합니다 (뒤 레이어가 앞 레이어의 오버레이를 읽음).
- 리뷰 시트: `hyb_period_preview.py -- <stage>` → `_tmp/variants/hybrid/periods/periods_<stage>.png` (물 L 대비 그림자 L을 `OK` / `TOO FAINT`로 출력).
- 모듈 API와 규칙: [`periods/README.md`](../Tools/Blender/variants/hybrid/periods/README.md). 시간대별 목표 색은 [time_currents_spec.md](time_currents_spec.md) §3.

---

## 6. 장애물 내보내기와 앞 레이어 깊이 지도

### 6.1 장애물 (`hyb_obstacles.py`, `obstacles/`, `build_obstacles.ps1`)

- `hyb_<stage>.py`의 장면을 같은 빌더 함수와 같은 시드로 만들되, 첫 `render_passes`에서 멈춰 **렌더하지 않습니다**.
  그다음 `obstacles/<stage>.py`의 `RULES`가 물체에 `obst*` 커스텀 속성을 찍고, `extra()`가 물속 전용 헬퍼(`OBST_<kind>_<id>`, 렌더에서 숨김)를 더합니다.
- 태그된 물체를 게임 공간(Blender (X, Y, Z) → 게임 (X, Z, Y))으로 바꿔 `obstacles_<stage>.json`을 씁니다. 종류는 `KINDS = ("solid", "pad", "snag", "weed", "cover", "rim")`,
  재질은 `MATS`(rock, concrete, wood, root, reed, leaf, hull, crystal, ice, pad, weed, gravel), `obst_tiers`는 1..4로 잘립니다.
- 스테이지 모듈의 `LAYERS`: 모두 `("front", "render_front", 31)`이고, `ice`와 `cave`는 `("back", "render_back", 77)`도 씁니다.
- 검사(기본 켜짐): `Data/stage_<stage>.json`의 카메라 값으로(`Persp.ToPixel`과 같은 수식) 다시 투영해 `Sprites/Stages/<stage>_front.png`와 비교하고
  `HYB OBST CHECK ...` 줄과 오버레이 PNG를 남깁니다.
- 출력: `_tmp/variants/hybrid/obstacles/obstacles_<stage>.json`, `obstacles_<stage>_overlay.png`. `--dry`가 아니면 `Assets/Resources/Data/obstacles_<stage>.json`.
- `build_obstacles.ps1`은 스테이지마다 Blender 프로세스를 따로 띄웁니다 (스테이지 모듈이 모듈 수준 상태를 가지므로).
- 태그 규칙과 JSON 형식은 [obstacles_spec.md](obstacles_spec.md) §2, §13.

### 6.2 앞 레이어 깊이 지도 (`hyb_frontdepth.py`)

게임은 싸우는 물고기·물보라·줄 등을 앞 레이어(`StageView.OrderFront = 40`) **위에** 그리므로, 발판(부두·바위·배) 밑으로 들어간 것을 가리려면
앞 레이어 픽셀마다 깊이가 필요합니다. 이 스크립트가 그 지도를 만듭니다 (런타임 쪽은 [architecture.md](architecture.md)).

- 스테이지의 앞 장면을 `hyb_<stage>.py`와 똑같이(`render_front(Random(31))`, `SEED = 31`) 만들고 첫 렌더 패스에서 멈춘 뒤,
  ID 패스와 정밀 깊이 패스(R = floor(View Z), G = frac(View Z), B = 월드 높이)를 렌더합니다.
- 저장값 = 카메라 깊이 d(m) = `(p.z + camBack) * cos(pitch) - (p.y - standH - camUp) * sin(pitch)`.
- 인코딩 `<stage>_front_depth.png` (RGBA8, 640×400, 0행 = 위, `<stage>_front.png`와 픽셀 정렬):
  - A = 255: 네 시간대 앞 레이어 **모두**에서 불투명한 픽셀. 0 = 가리는 것 없음.
  - `code = R * 256 + G`, `d = near + code * (far - near) / 65535` (불투명 코드는 0..65534로 자름, 빈 픽셀은 `EMPTY = 65535` → R = G = 255, A = 0).
  - B = 255: 물 위에 떠 있는 납작한 것 (물에서 ±`FLOAT_Y = 0.12` m 안에 있는 소품이나 수면의 점). 0 = 단단한 소품.
  - `near = floor(최소 깊이) - 1`, `far = ceil(최대 깊이) + 1` (스테이지마다 다름, `frontdepth_<stage>.json`에 기록. 예: lake 7.0 / 41.0).
- 검사: 시간대 앞 레이어 비교, 채움 분류, Blender 카메라 대 Persp 수식, 격자 레이캐스트, 스테이지별 탐침 3개(`PROBES`). 오버레이는 `_tmp/frontdepth/frontdepth_<stage>_check.png`.
- 출력: `_tmp/frontdepth/<stage>_front_depth.png`, `frontdepth_<stage>.json`. `--dry`가 아니면 `Assets/Resources/Sprites/Stages/<stage>_front_depth.png`와
  `Assets/Resources/Data/frontdepth_<stage>.json`.
- `.ps1` 래퍼가 없습니다. 직접 실행합니다 (8장).

---

## 7. Unity 임포터

### 7.1 `Assets/Editor/PixelArtImporter.cs` (`AssetPostprocessor`, `GetVersion() => 5`)

**대상**: 경로가 `Assets/Resources/Sprites/`로 시작하는 텍스처 (`Root`).

| 설정 | 일반 스프라이트 | `Sprites/Stages/*_front_depth` (깊이 지도) |
|---|---|---|
| `textureType` | `Sprite` (`spriteImportMode = Single`) | `Default` |
| PPU | `PixelsPerUnit = 16` | — |
| `filterMode` | `Point` | `Point` |
| `textureCompression` | `Uncompressed` | `Uncompressed` |
| `mipmapEnabled` | `false` | `false` |
| `wrapMode` | `Clamp` | `Clamp` |
| `alphaIsTransparency` | `true` | `false` |
| `sRGBTexture` | (기본값 유지) | `false` |
| `npotScale` | (기본값 유지) | `None` |
| `alphaSource` | (기본값 유지) | `FromInput` |
| `isReadable` | 아래 조건일 때만 `true` | `true` |
| 스프라이트 메시 | `spriteMeshType = FullRect`, `spriteExtrude = 0`, 물리 모양 생성 끔 | — |

- **읽기 가능(`isReadable`)**: 경로에 `/Fish/`가 있거나(게임이 밝기를 재서 그림자를 보이게 함), 파일 이름이 `clean_algae_`·`clean_dirt_`로 시작하거나,
  `_grow`로 끝나거나, `clean_`으로 시작하고 `_brush`로 끝나는 경우 (수족관 청소 마스크).
- **피벗**: 경로에 `/Character/`가 있으면 `Custom`, `spritePivot = (0.5, 10/112)` (96×112 프레임에서 발이 아래 10 px). 나머지는 모두 `Center`.
- **9-slice 테두리 `Borders`** (이름 → 네 변 같은 px): `panel_wood` 14, `panel_dark` 9, `panel_paper` 8, `btn_green`/`btn_blue`/`btn_red`/`btn_yellow`/`btn_grey` 9,
  `slot` 7, `bar_bg` 4, `bar_fill` 4, `badge` 5, `enc_frame_cave`/`enc_frame_lake`/`enc_frame_swamp`/`enc_frame_ice`/`enc_frame_ocean` 6. 나머지는 0.
  UI 프레임 값은 `fk_items.build_frames()`와 맞아야 하고, 같은 값이 `Data/ui_borders.json`에 기록됩니다.
- **폰트** (`OnPreprocessAsset`): `Assets/Resources/Fonts/`의 TTF는 `Dynamic`, `HintedRaster`(AA 없는 래스터), `includeFontData = true`, `characterSpacing = 0`, `characterPadding = 1`.
- 설정을 바꾸면 `GetVersion()`을 올려야 해당 에셋이 다시 임포트됩니다.

### 7.2 `Assets/Editor/ActorModelImporter.cs` (`GetVersion() => 1`)

**대상**: 경로가 `Assets/Resources/Models/`로 시작하는 모델 (`Root`). 낚시꾼·릴·전설어 FBX.

- **모델 임포트** (`OnPreprocessModel`): `globalScale = 1`, `useFileScale = true`, `bakeAxisConversion = false`,
  카메라·라이트·가시성·블렌드셰이프 임포트 끔, `importNormals = Import`, `importTangents = None`,
  `animationType = None`, `importAnimation = false`, `optimizeGameObjects = false`(뼈 계층을 일반 Transform으로 유지),
  `meshCompression = Off`, `isReadable = false`, `addCollider = false`, `generateSecondaryUV = false`.
- **재질 재지정**: `materialImportMode = ImportViaMaterialDescription`, `materialLocation = InPrefab`.
  `OnPreprocessMaterialDescription`에서 셰이더를 `Assets/Shaders/ActorToon.shader`로 바꾸고, FBX의 `DiffuseColor`를 `_Mid`에 넣습니다.
  FBX 재질은 **이름을 위해서만** 남고, 게임은 팔레트 JSON으로 만든 툰 재질로 바꿉니다 (`ActorArt`).
- **외곽선용 노멀** (`OnPostprocessModel`): 메시마다 같은 위치(1/20000 m 격자로 반올림) 정점의 노멀을 평균해 **UV3**에 씁니다.
  부품이 플랫 셰이딩이라, 쪼개진 면 노멀로 외곽선 헐을 밀면 틈이 생기기 때문입니다.
- **렌더러**: 그림자 캐스트·수신 끔, 라이트 프로브·리플렉션 프로브 끔.

### 7.3 다시 임포트

메뉴 `FishingKing > Reimport Sprites`는 `Assets/Resources/Sprites`를 `ImportRecursive | ForceUpdate`로 다시 임포트합니다 (`FishingKingSetup.ReimportSprites`).

---

## 8. 빌드·설치 명령 (PowerShell)

### 8.1 전체 재생성 순서

```powershell
# 1) 1세대 아트 + stage_<id>.json 레이아웃 (Assets에 바로 씀)
.\Tools\Blender\build_all.ps1
# 2) 하이브리드: 물고기 → 캐릭터 → 스테이지 7곳과 미리보기 → 지도 → 수족관 → 프리셋 시트 → 점검 → 조우 세트 전부
.\Tools\Blender\variants\hybrid\build_hybrid.ps1 -Install
# 3) 시간대 모습 (스테이지마다, 4개 시간대 + 리뷰 시트, -Dry가 아니면 설치)
.\Tools\Blender\variants\hybrid\build_periods.ps1 -Stage lake
# 4) 장애물 (설치 + 검사)
.\Tools\Blender\variants\hybrid\build_obstacles.ps1 -All
```

```text
# 5) 앞 레이어 깊이 지도 (래퍼 없음, Tools/Blender에서)
blender -b --python variants/hybrid/hyb_frontdepth.py -- --all          # 빌드 + 설치 + 검사
blender -b --python variants/hybrid/hyb_frontdepth.py -- lake sea --dry # _tmp만
# 6) 3D 모델·조우 세트·수족관 단계·장식 (필요할 때)
blender -b --python variants/hybrid/hyb_legend3d.py -- --all --install
blender -b --python variants/hybrid/hyb_encounter.py -- --all --install
blender -b --python variants/hybrid/hyb_actors3d.py                     # _tmp/actors3d/ 까지만
blender -b --python variants/hybrid/hyb_aquatanks.py -- install
blender -b --python variants/hybrid/hyb_aquadecor.py                    # 기본이 설치
# 7) build_all에 없는 1세대 그룹
blender -b --python fk_tod.py
blender -b --python fk_aquaclean.py
```

순서가 중요한 이유:
- `build_all.ps1`은 하이브리드 그림을 1세대 그림으로 덮어쓰므로 반드시 2)보다 먼저입니다.
- 하이브리드 스테이지의 `stage_<id>.json`은 `Data/stage_<id>.json`을 복사해 `waterTint` / `waterDeep` / `clouds` / `birds`만 바꿉니다 (`hyb_core.stage_json`).
- `build_hybrid.ps1` 안에서도 물고기가 먼저입니다 (미리보기가 `_t` 스프라이트를 씀: 스크립트 주석).
- 장애물 검사와 깊이 지도는 설치된 `Sprites/Stages/<stage>_front.png`와 `Data/stage_<stage>.json`을 읽고, 깊이 지도는 시간대 앞 레이어
  `<stage>_<p>_front.png`까지 비교하므로 2)·3) 다음에 돌립니다.

### 8.2 `build_hybrid.ps1 -Install`이 복사하는 것

`Tools/Blender/_tmp/variants/hybrid/` → `Assets/Resources/`:

| 원본 | 대상 |
|---|---|
| `<id>_back.png`, `<id>_front.png` (7 스테이지) | `Sprites/Stages/` |
| `stage_<id>.json` | `Data/` |
| `map_world.png`, `aquarium_back.png`, `aquarium_front.png` | `Sprites/Stages/` |
| `map.json`, `aquarium.json`, `character/character.json` | `Data/` |
| `character/angler_*.png` | `Sprites/Character/` |
| `fish/*.png` | `Sprites/Fish/` |
| `encounter/*.png` (`encounter_sheet*`, `_*` 제외) | `Sprites/Encounter/` (없으면 폴더 생성) |

`presets.png`, `preview_*.png`, 리뷰 시트는 설치하지 않습니다. 시간대 모습은 `build_hybrid.ps1`이 만들지 않습니다 (`build_periods.ps1` 담당).

### 8.3 `build_periods.ps1` / `build_obstacles.ps1` 옵션

| 스크립트 | 옵션 |
|---|---|
| `build_periods.ps1` | `-Stage <lake…cave>`(필수, 하나), `-Period dawn,day,evening,night`(기본 넷 다), `-Dry`(설치 안 함), `-Parallel`(시간대마다 Blender 프로세스, 로그는 `$env:TEMP\fk_period_<stage>_<p>.log`). 끝에 `hyb_period_preview.py`로 리뷰 시트 |
| `build_obstacles.ps1` | `-Stage <s>[,<s>]` 또는 `-All`(둘 중 하나 필수), `-Dry`, `-NoCheck` |

### 8.4 결과물 위치 요약 (`Assets/Resources/`)

| 폴더 | 내용 | 만드는 스크립트 |
|---|---|---|
| `Sprites/Stages/` | `<stage>_back/front.png`, `<stage>_<p>_back/front.png`, `<stage>_front_depth.png`, `map_world.png`, `aquarium*_back/front.png`, `cloud_*.png` | `hyb_<stage>`(설치), `hyb_period`, `hyb_frontdepth`, `hyb_map`, `hyb_aquarium`, `hyb_aquatanks`, `fk_scene` |
| `Sprites/Fish/` | `<id>_0/_1/_t0/_t1.png` | `hyb_fish` (1세대 `fk_fish`) |
| `Sprites/Character/` | `angler_<pose>.png` | `hyb_character` (1세대 `fk_character`) |
| `Sprites/Encounter/` | `uw_*`, `enc_frame_*`, `lure_*`, 공유 스프라이트, `swamp_top` 소품 | `hyb_encounter` |
| `Sprites/Items/`, `Sprites/World/`, `Sprites/UI/` | 아이콘, 월드 루어·FX, UI, 장식 | `fk_items`, `fk_tod`, `fk_aquaclean`, `hyb_aquadecor` |
| `Models/` | `angler.fbx`, `reel_*.fbx`, `legend_<id>.fbx` + `*_palette.json` | `hyb_actors3d`(수동 복사), `hyb_legend3d --install` |
| `Data/` | `stage_<id>.json`, `obstacles_<stage>.json`, `frontdepth_<stage>.json`, `map.json`, `aquarium*.json`, `character.json`, `ui_borders.json` | 위 표의 각 스크립트 |
| `Data/Periods/` | `<stage>_<p>.json` 룩 JSON | `hyb_period` |

각 JSON의 필드는 [data_reference.md](data_reference.md)에 있습니다.

---

## 사양서와 달라진 점

- **`lures_legend_spec.md` §3.3**: 전설어 삼각형 한도를 "≤ 3000"이라고 적었지만, 코드는 `hyb_legend3d.TRI_BUDGET = 3500`입니다 (`legends/README.md`도 3500).
- **`lures_legend_spec.md` §3.2**: "`enc_frame_cave`를 `Borders`에 추가하고 `GetVersion()`을 3으로"라고 적었지만, 지금 `PixelArtImporter.GetVersion()`은 5이고,
  `Borders`에는 `enc_frame_lake` / `_swamp` / `_ice` / `_ocean`(모두 6)도 있습니다.
- **`lures_legend_spec.md` §3.2**: `hyb_encounter.py`가 cave 프리셋(`use_preset("cave")`)을 쓴다고 적었지만, 지금은 범용 러너라 세트 모듈의 `PRESET`(없으면 세트 id)을 씁니다
  (`hyb_encounter.py`의 `R.use_preset(getattr(S, "PRESET", sid))`). cave 모양은 `encounter_sets/cave.py`에 있습니다.
- **`lures_legend_spec.md` §3.3**: 설치를 "copy into"라고만 적었지만, 코드에는 `--install` 옵션이 있어 `Assets/Resources/Models/`로 복사합니다 (`hyb_legend3d.MODELS`).
- **`legends_rollout.md` §4.1 / §8**: 배경 세트를 cave·lake·swamp·ice·ocean 다섯 개로 적었지만, 코드에는 `encounter_sets/swamp_top.py`(수면 위 탑뷰 유혹 장면)도 있고
  `uw_swamp_top_*`, `lure_frog_top_0..3`, `enc_frame_swamp_top.png`, `fx_top_*`를 만듭니다. `enc_frame_swamp_top`은 `PixelArtImporter.Borders`에 없어 테두리가 0으로 임포트됩니다
  (`swamp_top.py` 독스트링: 창은 swamp 프레임을 계속 씀).
- **`time_currents_spec.md` §3.1 규칙 4**: 색 예산 "back ≤ 90, front ≤ 42"를 규칙으로 적었지만, 코드는 색 수를 출력만 하고 강제하지 않습니다
  (`hyb_period_preview.py`의 `HYB PREVIEW ... colours`, `hyb_check.py`의 `CHECK ... colours`). `notes.md` §3은 front를 "≤ ~40"으로 적어 사양서와도 조금 다릅니다.
- **`notes.md` (Tools/Blender/variants/hybrid) 머리말**: "Nothing here writes into `Assets/`"라고 적었지만, 지금은 여러 스크립트가 Assets에 씁니다:
  `hyb_period.py`(`--dry`가 아니면), `hyb_obstacles.py`, `hyb_frontdepth.py`, `hyb_legend3d.py --install`, `hyb_encounter.py --install`, `hyb_aquatanks.py install`, `hyb_aquadecor.py`(기본).
- **`notes.md` 파일 표**: `build_hybrid.ps1`을 "fish → character → lake → preview → presets → check"로 적었지만, 코드는 7개 스테이지와 각 미리보기, `hyb_map.py`,
  `hyb_aquarium.py` + 미리보기, `hyb_presets.py`, `hyb_check.py`, `hyb_encounter.py -- --all`까지 돌립니다 (`build_hybrid.ps1`의 `$scripts`).
- **프리셋 위치**: 이 문서 작성 요청에는 스테이지 PRESETS가 `hyb_presets.py`에 있다고 되어 있었지만, 코드에서 프리셋 값은 `hyb_core.PRESETS`에 있고
  `hyb_presets.py`는 견본 시트 `presets.png`만 렌더합니다 (`notes.md` §1도 `hyb_core.PRESETS`로 적음).
- **`obstacles_spec.md` §13.3**: 예시 `LAYERS`는 시드 31인데, `obstacles/_template.py`의 `LAYERS`는 `("front", "render_front", 0)`입니다. 실제 스테이지 모듈 7개는 모두 31을 쓰므로
  템플릿을 복사할 때 시드를 31로 바꿔야 합니다.
- **`ActorModelImporter.cs` 주석**: 모델 계약이 `Tools/Blender/_tmp/actors3d/actors3d_notes.md`에 있다고 하지만, 이 파일은 저장소에 없고(`_tmp`는 gitignore) 이 파일을 쓰는 스크립트도 찾지 못했습니다.
  계약 내용은 `hyb_actors3d.py`와 `hyb_legend3d.py` 독스트링에 있습니다.
