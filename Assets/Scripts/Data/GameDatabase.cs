using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// All static game content: species, equipment, baits, stages and tank upgrades. The species and the stages are data:
    /// one file per species (Resources/Data/Fish/&lt;id&gt;.json) and the stage roster (Resources/Data/stages.json), read by
    /// <see cref="SpeciesData"/> (Docs/data_reference.md 1, 2); equipment, baits, tanks and the encounter backdrops are
    /// built here. Sprite ids match the files rendered by Tools/Blender (Sprites/Fish/&lt;id&gt;_0.png, Sprites/Items/&lt;id&gt;.png).
    /// </summary>
    public static class GameDatabase
    {
        public static readonly List<FishSpecies> Fish = new List<FishSpecies>();
        public static readonly List<RodDef> Rods = new List<RodDef>();
        public static readonly List<ReelDef> Reels = new List<ReelDef>();
        public static readonly List<LineDef> Lines = new List<LineDef>();
        public static readonly List<BaitDef> Baits = new List<BaitDef>();
        public static readonly List<HookDef> Hooks = new List<HookDef>();
        public static readonly List<TankDef> Tanks = new List<TankDef>();
        public static readonly List<StageDef> Stages = new List<StageDef>();

        static readonly Dictionary<string, FishSpecies> fishById = new Dictionary<string, FishSpecies>();
        static readonly Dictionary<string, ItemDef> itemById = new Dictionary<string, ItemDef>();
        static readonly Dictionary<string, StageDef> stageById = new Dictionary<string, StageDef>();

        public const string StarterRod = "rod_bamboo";
        public const string StarterReel = "reel_basic";
        public const string StarterLine = "line_nylon2";
        public const string StarterBait = "bait_paste";
        public const string StarterHook = "hook_small";

        /// <summary>
        /// What went wrong reading the species and stage data (empty when it loaded cleanly; each is logged once as an
        /// error with [DATA]). A species with a broken file is left out; SaveSystem.Sanitize then keeps saved fish of
        /// species it does not know instead of dropping them. The validator (SpeciesCheck) finds the same and more.
        /// </summary>
        public static IReadOnlyList<string> LoadErrors { get; private set; } = new string[0];

        static GameDatabase()
        {
            // (never throws: an exception here would break every later touch of the database for the whole session)
            var r = SpeciesData.Build(SpeciesData.FromResources(out var fishFiles), fishFiles);
            BuildItems();
            BuildSets();
            Install(r.fish, r.stages);
            foreach (var i in Rods) itemById[i.id] = i;
            foreach (var i in Reels) itemById[i.id] = i;
            foreach (var i in Lines) itemById[i.id] = i;
            foreach (var i in Baits) itemById[i.id] = i;
            foreach (var i in Hooks) itemById[i.id] = i;
            foreach (var i in Tanks) itemById[i.id] = i;
            var errors = new List<string>();
            foreach (var f in r.findings)
                if (f.error) errors.Add(f.ToString());
            LoadErrors = errors;
            foreach (var e in errors) Debug.LogError("[DATA] " + e);
        }

        /// <summary>Puts these species and stages in place (the loader's, from the static constructor).</summary>
        internal static void Install(List<FishSpecies> fish, List<StageDef> stages)
        {
            Fish.Clear();
            Fish.AddRange(fish);
            Stages.Clear();
            Stages.AddRange(stages);
            fishById.Clear();
            foreach (var f in Fish) fishById[f.id] = f;
            stageById.Clear();
            foreach (var s in Stages) stageById[s.id] = s;
        }

        public static FishSpecies GetFish(string id) => id != null && fishById.TryGetValue(id, out var f) ? f : null;
        public static ItemDef GetItem(string id) => id != null && itemById.TryGetValue(id, out var i) ? i : null;
        public static T GetItem<T>(string id) where T : ItemDef => GetItem(id) as T;
        public static StageDef GetStage(string id) => id != null && stageById.TryGetValue(id, out var s) ? s : null;

        public static IEnumerable<FishSpecies> FishOfStage(string stageId)
        {
            var st = GetStage(stageId);
            if (st == null) yield break;
            foreach (var kv in st.spawns)
            {
                var f = GetFish(kv.Key);
                if (f != null) yield return f;
            }
        }

        public static string StageOfFish(string fishId)
        {
            foreach (var s in Stages)
                if (s.spawns.Any(kv => kv.Key == fishId)) return s.id;
            return null;
        }

        public static TankDef TankForLevel(int level) => Tanks[Mathf.Clamp(level, 0, Tanks.Count - 1)];

        // ------------------------------------------------------------------------------------ encounter backdrops
        static readonly Dictionary<string, EncounterSetDef> sets = new Dictionary<string, EncounterSetDef>();

        /// <summary>The encounter backdrop and light profile (Docs/legends_rollout.md 4.1); the cave's for an unknown id.</summary>
        public static EncounterSetDef GetSet(string id) => id != null && sets.TryGetValue(id, out var s) ? s : sets["cave"];

        /// <summary>The backdrop ids there are sets for (a legend's backdrop must be one: GetSet falls back to the cave quietly).</summary>
        public static IEnumerable<string> SetIds => sets.Keys;

        static readonly Vector3 RimT = new Vector3(0, 1, 1f), RimTL = new Vector3(-1, 1, 0.8f), RimTR = new Vector3(1, 1, 0.8f);

        static void BuildSets()
        {
            // the cave: EncounterView's constants from before the rollout (its rim is the cave stage's own)
            sets["cave"] = new EncounterSetDef { id = "cave" };
            // murky green daylight: the bait on the mud, sunbeams from the top right, visibility 3.6 m
            sets["lake"] = new EncounterSetDef
            {
                id = "lake", clear = "#1a3024", abyss = "#1a3024", fogOutline = "#3a5a3a", sunMix = 0.75f, lureDrag = 0.05f, lureHop = 0.1f,
                // the carp's silhouette past the light: darker than the backdrop's greens with a pale contour, full within
                // 5 m of the bait, gone into the water by 7.5 m
                abyssNear = "#10221a", fogNear = "#5a7e4e", silFade = new Vector2(5.0f, 7.5f),
                keyDir = new Vector3(0.30f, 0.93f, 0.20f),
                rays = new[] { new RayDef("#f0ffc0", 0.14f, 150f, -20f), new RayDef("#f0ffc0", 0.14f, 232f, -44f) },
                halo = "#fff0a0", haloGlow = 0f, haloPlain = 0f, haloBait = "bait_golden", haloBaitAlpha = 0.5f,
                snowFar = "#6a8a5a", snowNear = "#c8e0a8", silt = "#a8966a", line = "#d8e8c8",
                rimCol = "#e8f8c0", rimStrength = 0.35f, rimDirs = new[] { RimT, RimTL, RimTR },
                veil = "#0c1a10", veilAlpha = 0.55f, frameLine = "#a8e878", midPulse = 0f,
            };
            // brown-green murk, the lure floats on the surface film: the floor 3.05 m under it, the surface's
            // underside as the ceiling layer, visibility 2.2 m
            sets["swamp"] = new EncounterSetDef
            {
                id = "swamp", lureAt = LureAt.Surface, lureRest = 0f, floorY = -3.0f, surfaceY = 0.05f, horizonY = -3.0f,
                ceiling = true, ceilFill = "#6a6a3a", camRest = new Vector3(-1.0f, -0.9f, -2.6f), camDist = 2.93f, frameAt = new Vector2(0.40f, 0.72f),
                lineUp = new Vector3(-1.4f, 0.05f, -1.2f), dragDir = new Vector3(-0.5f, 0f, -0.85f),
                clear = "#10180e", abyss = "#10180e", fogOutline = "#2e3a22", sunMix = 0.8f, keyDir = new Vector3(0.15f, 0.98f, 0.1f),
                rays = new[] { new RayDef("#c8b478", 0.05f, 150f, -20f) }, halo = null,
                snowFar = "#6a6a44", snowNear = "#a8a070", silt = "#8a7a50", line = "#c8c098",
                rimCol = "#d8c888", rimStrength = 0.25f, rimDirs = new[] { RimT },
                veil = "#080c06", veilAlpha = 0.6f, frameLine = "#d8e070", midPulse = 0f,
            };
            // dark under the ice: the worm on the bottom in the light column from the hole 4.3 m straight above it
            sets["ice"] = new EncounterSetDef
            {
                id = "ice", surfaceY = 4.3f, bgHorizon = 200, ceiling = true, lineUp = new Vector3(0f, 4.3f, 0f), lureDrag = 0.06f, lureHop = 0.15f, lureFall = 0.4f,
                clear = "#04080f", abyss = "#04080f", fogOutline = "#2a4a6a", sunMix = 0.6f, keyDir = new Vector3(0f, 1f, 0f), lightUp = 1.2f,
                rays = new RayDef[0], rayAnchored = true,
                halo = "#bfe8ff", haloGlow = 0.25f, haloPlain = 0.25f,
                snowFar = "#3a5a7a", snowNear = "#cfe8ff", silt = "#8aa0b8", line = "#d8e8f8",
                rimCol = "#cfefff", rimStrength = 0.4f, rimDirs = new[] { RimT },
                veil = "#02040a", veilAlpha = 0.5f, frameLine = "#bfefff", midPulse = 0f,
            };
            // deep blue open water off the drift: the lure runs 1.2 m under the surface, no floor, visibility 6 m
            sets["ocean"] = new EncounterSetDef
            {
                id = "ocean", lureAt = LureAt.Mid, lureRest = 0f, hasFloor = false, floorY = -18f, surfaceY = 1.2f, horizonY = 0f,
                bgHorizon = 250, ceiling = true, camRest = new Vector3(-1.3f, -0.7f, -3.0f), camDist = 3.34f, frameAt = new Vector2(0.36f, 0.62f),
                lineUp = new Vector3(-1.0f, 1.2f, -1.4f), dragDir = new Vector3(-0.6f, 0f, -0.8f), lureBound = 99f,
                clear = "#081e44", abyss = "#081e44", fogOutline = "#2a5a90", sunMix = 0.8f, keyDir = new Vector3(0.2f, 0.95f, 0.2f),
                rays = new[] { new RayDef("#c8ecff", 0.12f, 150f, -20f), new RayDef("#c8ecff", 0.12f, 236f, -34f), new RayDef("#c8ecff", 0.12f, 318f, -12f) },
                halo = null, snowFar = "#4a80b8", snowNear = "#b8e0ff", silt = null, line = "#d0e8f8",
                rimCol = "#bfe8ff", rimStrength = 0.45f, rimDirs = new[] { RimT, RimTL, RimTR },
                veil = "#020a1a", veilAlpha = 0.45f, frameLine = "#7fd4ff", midPulse = 0f,
            };
        }

        // ------------------------------------------------------------------------------------ items
        static void BuildItems()
        {
            Rods.Add(new RodDef { id = "rod_bamboo", name = "대나무 낚싯대", price = 0, castDist = 16, flex = 0.15f, hookBonus = 0f, length = 3.0f, blank = Art.Hex("#c9a95e"), grip = Art.Hex("#8a5a30"), blank2 = Art.Hex("#8e6a32"), seat = Art.Hex("#6a4424"), wrap = Art.Hex("#6a4424"), nodes = true, desc = "할아버지께 물려받은 튼튼한 대나무 낚싯대." });
            Rods.Add(new RodDef { id = "rod_glass", name = "글라스 로드", price = 900, castDist = 20, flex = 0.25f, hookBonus = 0.1f, length = 3.0f, blank = Art.Hex("#ecc93e"), grip = Art.Hex("#c9a070"), blank2 = Art.Hex("#b8942a"), seat = Art.Hex("#2a2a2a"), wrap = Art.Hex("#2a2a2a"), desc = "잘 휘어서 충격을 흡수하는 입문용 로드." });
            Rods.Add(new RodDef { id = "rod_carbon", name = "카본 루어 로드", price = 4000, castDist = 24, flex = 0.2f, hookBonus = 0.25f, length = 2.9f, blank = Art.Hex("#34343e"), grip = Art.Hex("#1c1c1c"), blank2 = Art.Hex("#1a1a20"), seat = Art.Hex("#b8c0c8"), wrap = Art.Hex("#d83434"), desc = "가볍고 예민해 입질을 놓치지 않는다." });
            Rods.Add(new RodDef { id = "rod_surf", name = "원투 서프 로드", price = 14000, castDist = 34, flex = 0.3f, hookBonus = 0.1f, length = 3.7f, blank = Art.Hex("#eceef2"), grip = Art.Hex("#5a5e66"), blank2 = Art.Hex("#b8bcc4"), seat = Art.Hex("#b8c0c8"), wrap = Art.Hex("#2f6fd4"), desc = "멀리 던지는 데 특화된 긴 로드." });
            Rods.Add(new RodDef { id = "rod_biggame", name = "빅게임 로드", price = 45000, castDist = 27, flex = 0.5f, hookBonus = 0.2f, length = 2.7f, blank = Art.Hex("#1c2c5c"), grip = Art.Hex("#3c2c1c"), blank2 = Art.Hex("#101a3a"), seat = Art.Hex("#d8b040"), wrap = Art.Hex("#d8b040"), gimbal = true, thick = true, desc = "대형 어종과의 파이트를 위한 굵은 로드." });
            Rods.Add(new RodDef { id = "rod_dragon", name = "용왕의 낚싯대", price = 180000, castDist = 36, flex = 0.6f, hookBonus = 0.4f, luck = 1.6f, length = 3.3f, blank = Art.Hex("#b82424"), grip = Art.Hex("#e2b432"), blank2 = Art.Hex("#701010"), seat = Art.Hex("#ffe070"), wrap = Art.Hex("#ffd040"), glow = Art.Hex("#ffb040"), thick = true, desc = "바다의 용왕이 쓰던 전설의 낚싯대. 희귀어가 잘 문다." });

            Reels.Add(new ReelDef { id = "reel_basic", name = "기본 스피닝 릴", price = 0, retrieve = 0.8f, dragMax = 2.5f, dragSmooth = 0.3f, lineCap = 60, desc = "평범한 입문용 릴." });
            Reels.Add(new ReelDef { id = "reel_light", name = "라이트 스피닝 릴", price = 1200, retrieve = 0.9f, dragMax = 5f, dragSmooth = 0.45f, lineCap = 80, desc = "가볍고 부드러운 드랙." });
            Reels.Add(new ReelDef { id = "reel_highgear", name = "하이기어 릴", price = 5000, retrieve = 1.2f, dragMax = 9f, dragSmooth = 0.5f, lineCap = 100, desc = "한 바퀴에 많이 감기는 고속 릴." });
            Reels.Add(new ReelDef { id = "reel_baitcast", name = "베이트캐스팅 릴", price = 16000, retrieve = 1.05f, dragMax = 18f, dragSmooth = 0.65f, lineCap = 120, desc = "강한 드랙으로 큰 고기를 제압한다." });
            Reels.Add(new ReelDef { id = "reel_electric", name = "전동 빅게임 릴", price = 50000, retrieve = 1.3f, dragMax = 40f, dragSmooth = 0.75f, lineCap = 200, autoReel = 0.9f, desc = "모터가 자동으로 줄을 감아 준다." });
            Reels.Add(new ReelDef { id = "reel_poseidon", name = "포세이돈 릴", price = 200000, retrieve = 1.5f, dragMax = 90f, dragSmooth = 0.9f, lineCap = 300, autoReel = 0.6f, desc = "바다의 신이 축복한 황금 릴." });

            Lines.Add(new LineDef { id = "line_nylon2", name = "나일론 2호", price = 0, strength = 3f, tough = 1.0f, spool = 100f, color = new Color(0.92f, 0.96f, 0.96f, 0.9f), desc = "기본 나일론 줄. 3kg까지 버틴다." });
            Lines.Add(new LineDef { id = "line_nylon4", name = "나일론 4호", price = 600, strength = 6f, tough = 1.15f, spool = 100f, color = new Color(0.72f, 0.9f, 0.63f, 0.9f), desc = "조금 더 굵은 나일론 줄. 6kg." });
            Lines.Add(new LineDef { id = "line_fluoro6", name = "플로로카본 6호", price = 3000, strength = 11f, tough = 1.7f, stealth = 1.15f, spool = 100f, color = new Color(0.95f, 0.8f, 0.86f, 0.7f), desc = "물속에서 잘 보이지 않고 쓸림에도 강하다. 11kg." });
            Lines.Add(new LineDef { id = "line_pe3", name = "PE 합사 3호", price = 12000, strength = 22f, tough = 0.6f, spool = 150f, color = new Color(0.25f, 0.7f, 0.3f, 1f), desc = "가늘고 강하지만 쓸림에는 약한 합사. 22kg." });
            Lines.Add(new LineDef { id = "line_pe8", name = "PE 합사 8호", price = 40000, strength = 45f, tough = 0.8f, spool = 200f, color = new Color(0.95f, 0.5f, 0.2f, 1f), desc = "대물 전용 굵은 합사. 쓸림에는 여전히 약하다. 45kg." });
            Lines.Add(new LineDef { id = "line_titan", name = "티타늄 와이어", price = 150000, strength = 100f, tough = 5.0f, spool = 150f, color = new Color(0.8f, 0.82f, 0.86f, 1f), desc = "상어 이빨도 끊지 못하는 금속 줄. 100kg." });

            Baits.Add(new BaitDef { id = "bait_paste", name = "떡밥", price = 0, infinite = true, sinkSpeed = 1.0f, desc = "무한 제공되는 기본 미끼. 잉어과 물고기가 좋아한다." });
            Baits.Add(new BaitDef { id = "bait_worm", name = "지렁이", price = 120, packSize = 20, sinkSpeed = 1.1f, desc = "대부분의 민물고기가 좋아하는 만능 미끼." });
            Baits.Add(new BaitDef { id = "bait_corn", name = "옥수수", price = 150, packSize = 20, sinkSpeed = 1.2f, desc = "잉어가 사족을 못 쓰는 달콤한 미끼." });
            Baits.Add(new BaitDef { id = "bait_shrimp", name = "새우", price = 350, packSize = 15, sinkSpeed = 1.2f, desc = "바다와 민물 모두 잘 통하는 새우." });
            Baits.Add(new BaitDef { id = "bait_sandworm", name = "갯지렁이", price = 450, packSize = 15, sinkSpeed = 1.4f, desc = "바다 바닥고기용 특효 미끼." });
            Baits.Add(new BaitDef { id = "bait_squid", name = "오징어", price = 900, packSize = 10, sinkSpeed = 1.5f, desc = "대형 바다고기가 좋아하는 미끼." });
            Baits.Add(new BaitDef { id = "bait_glow", name = "야광 웜", price = 1500, packSize = 10, sinkSpeed = 1.1f, glow = true, desc = "어둠 속에서 빛나는 웜. 얼음 아래와 동굴에서 강하다." });
            Baits.Add(new BaitDef { id = "bait_golden", name = "황금 떡밥", price = 5000, packSize = 3, sinkSpeed = 1.0f, rareBoost = 3f, desc = "황금잉어를 부르는 신비한 미끼. 희귀어 확률 3배." });

            // lures (Docs/lures_legend_spec.md 1.2 / 1.6), grouped by action: 감기 · 저킹 · 수면 · 바닥 · 수직.
            // The four old lure ids (spoon, minnow, frog, jig) are kept, so saves that own them stay valid.
            // 감기 (Steady): wind inside the reel band
            Baits.Add(new BaitDef { id = "bait_spinner", name = "스피너", price = 1200, isLure = true, action = LureAction.Steady,
                sinkSpeed = 0.9f, windLift = 0.35f, reelBand = new Vector2(0.5f, 1.1f),
                hint = "천천히 일정하게 감기 — 날개가 반짝여요",
                desc = "회전하는 날개가 반짝이는 입문용 루어. 계곡과 호수의 작은 육식어에게 잘 통한다." });
            Baits.Add(new BaitDef { id = "bait_spoon", name = "스푼", price = 1500, isLure = true, action = LureAction.Steady,
                sinkSpeed = 1.8f, fallSpeed = 1.2f, windLift = 0.30f, reelBand = new Vector2(0.8f, 1.6f), pauseStrike = 1.0f,
                hint = "보통 속도로 꾸준히, 멈추면 팔랑팔랑 가라앉아요",
                desc = "번쩍이는 금속 스푼. 꾸준히 감다가 멈추면 팔랑이며 가라앉는다. 송어류에 효과적." });
            Baits.Add(new BaitDef { id = "bait_crank", name = "크랭크베이트", price = 2200, isLure = true, action = LureAction.Steady,
                buoyancy = Buoyancy.Float, riseSpeed = 0.3f, windLift = -0.6f, maxDepth = 2.5f, reelBand = new Vector2(1.2f, 2.2f),
                hint = "빨리 감을수록 깊이 잠수, 멈추면 떠올라요",
                desc = "립이 달린 통통한 루어. 감으면 잠수하고 멈추면 떠오른다. 배스와 가물치용." });
            Baits.Add(new BaitDef { id = "bait_kona", name = "트롤링 루어", price = 12000, isLure = true, action = LureAction.Steady,
                sinkSpeed = 0.3f, windLift = 0.45f, reelBand = new Vector2(1.6f, 5f),
                hint = "빠르게 쉬지 않고 감기",
                desc = "먼바다 회유어를 노리는 스커트 루어. 빠르게 감으면 수면 바로 아래를 달린다." });
            // 저킹 (Twitch): 1-3 flicks, then a pause
            Baits.Add(new BaitDef { id = "bait_minnow", name = "미노우", price = 2500, isLure = true, action = LureAction.Twitch,
                buoyancy = Buoyancy.Suspend, sinkSpeed = 0.9f, swimDepth = 1.2f, maxDepth = 1.5f, windLift = 0.10f,
                hopM = -0.15f, dartM = 0.3f, work = Work.Flick, flicks = new Vector2Int(1, 3), gap = new Vector2(0.2f, 0.6f),
                rest = new Vector2(0.5f, 1.5f), restStrike = 0.3f,
                hint = "아래로 톡톡 당기고(1~3번) 잠깐 멈추기",
                desc = "작은 물고기 모양 루어. 톡톡 당기면 옆으로 퍼덕이며 육식어를 유혹한다." });
            // 수면 (Topwater): floats; a flick or a short wind, then wait
            Baits.Add(new BaitDef { id = "bait_popper", name = "포퍼", price = 3000, isLure = true, action = LureAction.Topwater,
                buoyancy = Buoyancy.Float, riseSpeed = 0.4f, windLift = 0f, pullM = 0.2f, work = Work.Flick,
                flicks = new Vector2Int(1, 1), rest = new Vector2(1.0f, 2.5f), restStrike = 0.3f,
                hint = "톡 당겨 '퐁'! — 물결이 잦아들 때까지 기다리기",
                desc = "컵 모양 입으로 수면을 '퐁' 때리는 루어. 물결이 잦아들 때 덮친다." });
            Baits.Add(new BaitDef { id = "bait_frog", name = "개구리 루어", price = 4000, isLure = true, action = LureAction.Topwater,
                buoyancy = Buoyancy.Float, riseSpeed = 0.4f, windLift = 0f, hopM = 0f, pullM = 0.2f, work = Work.Wind,
                reelBand = new Vector2(0.3f, 0.8f), runMax = 2f, rest = new Vector2(0.5f, 2.0f), restStrike = 0.3f,
                hint = "아주 천천히 감다가 멈추기를 반복",
                desc = "수면 위를 헤엄치는 개구리 루어. 늪지 포식자가 사족을 못 쓴다." });
            // 바닥 (Bottom): let it reach the bottom, one light hop (or a slow drag), rest on the bottom
            Baits.Add(new BaitDef { id = "bait_softworm", name = "소프트 웜", price = 1800, isLure = true, action = LureAction.Bottom,
                sinkSpeed = 1.2f, fallSpeed = 1.0f, windLift = 0.15f, hopM = 0.4f, pullM = 0.3f, work = Work.Flick,
                flicks = new Vector2Int(1, 1), rest = new Vector2(1.0f, 3.0f), maxStrength = 0.6f, needBottom = true,
                reelBand = new Vector2(0.1f, 0.5f), runMax = 2f, fallStrike = new Vector2(0f, 99f), landStrike = 1.5f,
                hint = "바닥까지 가라앉힌 뒤 살짝 톡 당기고, 바닥에서 기다리기",
                desc = "꼬리가 말린 말랑한 웜. 바닥을 톡톡 뛰게 하면 바닥고기가 문다." });
            // 수직 (Vertical): flick it up, let it fall (the strike comes on the fall)
            Baits.Add(new BaitDef { id = "bait_jig", name = "메탈 지그", price = 8000, isLure = true, action = LureAction.Vertical,
                sinkSpeed = 2.6f, fallSpeed = 1.2f, windLift = 0.45f, hopM = 1.5f, hopBase = 0.4f, pullM = 0.15f, work = Work.Flick,
                flicks = new Vector2Int(2, 4), gap = new Vector2(0.25f, 0.6f), rest = new Vector2(0.8f, 2.0f),
                burstStrike = true, fallStrike = new Vector2(0f, 0.6f),
                hint = "바닥까지 내린 뒤 빠르게 연속으로 톡톡 당겨 올리기",
                desc = "빠르게 가라앉는 무거운 금속 루어. 바닥에서 톡톡 당겨 올리면 회유어가 덤빈다." });
            Baits.Add(new BaitDef { id = "bait_egi", name = "야광 에기", price = 6000, isLure = true, action = LureAction.Vertical,
                sinkSpeed = 0.6f, fallSpeed = 0.6f, windLift = 0.30f, hopM = 1.2f, hopBase = 0.42f, pullM = 0.2f, work = Work.Flick,
                flicks = new Vector2Int(1, 2), gap = new Vector2(0.2f, 0.8f), rest = new Vector2(1.5f, 4.0f), glow = true,
                fallStrike = new Vector2(0.5f, 99f), landStrike = 1.0f,
                hint = "톡 당겨 올린 뒤 손을 떼고 가라앉히기 — 입질은 떨어질 때!",
                desc = "어둠 속에서 푸르게 빛나는 새우 모양 루어. 천천히 가라앉을 때 입질이 온다." });

            // hooks for the natural-bait rigs (Docs/data_reference.md, hooks): the small one free, the rest in packs of 10
            Hooks.Add(new HookDef { id = "hook_small", name = "소형 바늘", price = 0, infinite = true, fitCm = new Vector2(0f, 35f), desc = "무한 제공되는 기본 바늘. 작은 고기가 잘 물지만 큰 고기는 잘 털린다." });
            Hooks.Add(new HookDef { id = "hook_medium", name = "중형 바늘", price = 200, fitCm = new Vector2(20f, 70f), desc = "붕어·배스·고등어 크기에 알맞은 바늘." });
            Hooks.Add(new HookDef { id = "hook_large", name = "대형 바늘", price = 500, fitCm = new Vector2(50f, 999f), hold = 1.2f, desc = "큰 고기를 단단히 잡는 굵은 바늘. 작은 고기는 잘 안 문다." });
            Hooks.Add(new HookDef { id = "hook_weedless", name = "위드리스 바늘", price = 800, fitCm = new Vector2(20f, 70f), setBonus = -0.15f, hold = 0.9f, snagK = 0.4f, grabK = 0.4f, desc = "바늘 끝을 철사로 막아 밑걸림이 적다. 대신 챔질이 조금 어렵다." });

            // capacity in 칸 (소형 <40cm 1, 중형 40-100cm 2, 대형 100-200cm 4, 초대형 200cm+ 8) and the biggest size class
            Tanks.Add(new TankDef { id = "tank_0", name = "작은 어항", level = 0, capacity = 6, maxClass = 0, price = 0, desc = "책상 위 작은 어항. 40cm 미만 소형 물고기만 살 수 있다." });
            Tanks.Add(new TankDef { id = "tank_1", name = "중형 수조", level = 1, capacity = 12, maxClass = 1, price = 2500, desc = "벽에 붙인 수조. 1m 미만 중형 물고기까지." });
            Tanks.Add(new TankDef { id = "tank_2", name = "대형 수조", level = 2, capacity = 24, maxClass = 2, price = 12000, desc = "넓은 대형 수조. 2m 미만 대형 물고기까지." });
            Tanks.Add(new TankDef { id = "tank_3", name = "아쿠아리움", level = 3, capacity = 40, maxClass = 3, maxHuge = 2, price = 45000, desc = "유리벽 아쿠아리움. 2m가 넘는 초대형 물고기도 두 마리까지." });
            Tanks.Add(new TankDef { id = "tank_4", name = "황금 대수족관", level = 4, capacity = 64, maxClass = 3, price = 150000, desc = "꿈의 수족관. 초대형 물고기 여러 마리가 함께 헤엄친다." });
        }
    }
}
