using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// All static game content: species, equipment, baits, stages and tank upgrades.
    /// Sprite ids match the files rendered by Tools/Blender (Sprites/Fish/&lt;id&gt;_0.png, Sprites/Items/&lt;id&gt;.png).
    /// </summary>
    public static class GameDatabase
    {
        public static readonly List<FishSpecies> Fish = new List<FishSpecies>();
        public static readonly List<RodDef> Rods = new List<RodDef>();
        public static readonly List<ReelDef> Reels = new List<ReelDef>();
        public static readonly List<LineDef> Lines = new List<LineDef>();
        public static readonly List<BaitDef> Baits = new List<BaitDef>();
        public static readonly List<TankDef> Tanks = new List<TankDef>();
        public static readonly List<StageDef> Stages = new List<StageDef>();

        static readonly Dictionary<string, FishSpecies> fishById = new Dictionary<string, FishSpecies>();
        static readonly Dictionary<string, ItemDef> itemById = new Dictionary<string, ItemDef>();
        static readonly Dictionary<string, StageDef> stageById = new Dictionary<string, StageDef>();

        public const string StarterRod = "rod_bamboo";
        public const string StarterReel = "reel_basic";
        public const string StarterLine = "line_nylon2";
        public const string StarterBait = "bait_paste";

        static GameDatabase()
        {
            BuildFish();
            BuildItems();
            BuildStages();
            foreach (var f in Fish) fishById[f.id] = f;
            foreach (var i in Rods) itemById[i.id] = i;
            foreach (var i in Reels) itemById[i.id] = i;
            foreach (var i in Lines) itemById[i.id] = i;
            foreach (var i in Baits) itemById[i.id] = i;
            foreach (var i in Tanks) itemById[i.id] = i;
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

        // ------------------------------------------------------------------------------------ fish
        static void F(string id, string name, Rarity r, float minCm, float maxCm, float k, int price, float power,
            float stamina, float speed, float aggr, float jump, float dMin, float dMax, string baits, string desc)
        {
            var f = new FishSpecies
            {
                id = id, name = name, rarity = r, minCm = minCm, maxCm = maxCm, weightK = k, basePrice = price,
                power = power, stamina = stamina, speed = speed, aggression = aggr, jump = jump,
                depthMin = dMin, depthMax = dMax, desc = desc,
            };
            // "worm:1,minnow:0.8,@twitch:0.8": bait ids without the bait_ prefix, lure actions after an @
            foreach (var part in baits.Split(','))
            {
                var kv = part.Split(':');
                string key = kv[0].Trim();
                float v = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                if (key.StartsWith("@"))
                {
                    if (System.Enum.TryParse(key.Substring(1), true, out LureAction a) && a != LureAction.None) f.actionPrefs[a] = v;
                    else Debug.LogWarning($"[GameDatabase] {id}: unknown lure action '{key}'");
                }
                else f.baitPrefs["bait_" + key] = v;
            }
            Fish.Add(f);
        }

        static void BuildFish()
        {
            const Rarity C = Rarity.Common, U = Rarity.Uncommon, R = Rarity.Rare, E = Rarity.Epic, L = Rarity.Legendary;
            // Lake
            F("crucian_carp", "붕어", C, 12, 35, 1.6f, 35, 1.6f, 6, 2.0f, 0.3f, 0f, 2, 7, "paste:1,corn:0.8,worm:0.7,golden:0.3",
                "호수와 저수지 어디에나 사는 친근한 물고기. 떡밥을 가장 좋아한다.");
            F("bluegill", "블루길", C, 10, 25, 2.0f, 26, 1.2f, 5, 2.4f, 0.5f, 0f, 1, 5, "worm:1,paste:0.5,shrimp:0.6,spinner:0.6,spoon:0.3,golden:0.3",
                "식욕이 왕성한 외래종. 무엇이든 덥석 문다.");
            F("carp", "잉어", U, 35, 90, 1.5f, 150, 5.0f, 14, 2.6f, 0.45f, 0.05f, 3, 7, "corn:1,paste:0.9,worm:0.4,golden:0.4",
                "힘이 장사인 호수의 터줏대감. 옥수수에 약하다.");
            F("largemouth_bass", "큰입배스", U, 25, 60, 1.4f, 180, 3.8f, 10, 3.4f, 0.7f, 0.35f, 1, 6, "minnow:1,crank:1,softworm:1,frog:0.9,popper:0.9,spinner:0.6,spoon:0.6,worm:0.4,golden:0.4,@twitch:0.8,@topwater:0.8,@bottom:0.8,@steady:0.7",
                "공격적인 육식어. 움직이는 루어에 반응하고 수면 위로 점프한다.");
            F("golden_carp", "황금잉어", L, 50, 100, 1.5f, 3000, 7.0f, 20, 3.0f, 0.5f, 0.1f, 4, 7, "golden:1,corn:0.5,softworm:0.4",
                "호수의 전설. 황금빛 비늘은 행운을 부른다고 한다.");
            // Stream
            F("pale_chub", "피라미", C, 8, 18, 1.0f, 15, 0.8f, 4, 3.0f, 0.5f, 0.1f, 0.5f, 3, "worm:1,paste:0.6,spinner:0.4,golden:0.3",
                "맑은 계곡을 떼지어 누비는 날쌘돌이.");
            F("cherry_salmon", "산천어", C, 15, 35, 1.1f, 45, 1.8f, 7, 3.2f, 0.55f, 0.2f, 1, 4, "worm:1,spinner:1,spoon:0.7,minnow:0.5,shrimp:0.4,golden:0.3",
                "옆구리의 타원형 무늬가 특징인 계곡의 요정.");
            F("rainbow_trout", "무지개송어", U, 25, 65, 1.2f, 160, 3.5f, 11, 3.6f, 0.6f, 0.4f, 1, 5, "spoon:1,spinner:0.9,minnow:0.8,worm:0.6,corn:0.3,golden:0.4,@steady:0.7",
                "몸의 분홍 띠가 무지개처럼 빛난다. 힘차게 뛰어오른다.");
            F("mandarin_fish", "쏘가리", R, 20, 50, 1.4f, 420, 3.2f, 12, 3.0f, 0.65f, 0.05f, 2, 5, "minnow:1,softworm:0.8,shrimp:0.6,spoon:0.5,golden:0.5",
                "바위틈에 숨어 사는 계곡의 제왕. 표범 무늬가 아름답다.");
            F("lenok", "열목어", E, 30, 70, 1.1f, 950, 4.5f, 16, 3.8f, 0.6f, 0.3f, 2, 5, "spoon:1,spinner:0.8,minnow:0.8,worm:0.3,golden:0.7",
                "차가운 1급수에만 사는 귀한 물고기. 눈이 붉게 빛난다.");
            // Sea
            F("horse_mackerel", "전갱이", C, 15, 40, 1.0f, 35, 1.8f, 7, 3.5f, 0.6f, 0f, 1, 5, "shrimp:1,sandworm:0.7,jig:0.6,spoon:0.4,golden:0.3",
                "방파제의 단골손님. 옆줄의 단단한 비늘이 특징.");
            F("mackerel", "고등어", C, 20, 45, 0.9f, 50, 2.4f, 8, 4.0f, 0.7f, 0f, 1, 4, "jig:1,shrimp:0.9,spoon:0.6,squid:0.5,sandworm:0.5,golden:0.3,@vertical:0.7",
                "등의 물결무늬가 멋진 등푸른 생선. 쉴 새 없이 헤엄친다.");
            F("rockfish", "우럭", U, 20, 55, 1.7f, 140, 3.0f, 9, 2.2f, 0.4f, 0f, 4, 7, "sandworm:1,softworm:0.9,squid:0.8,shrimp:0.7,jig:0.5,golden:0.4,@bottom:0.7",
                "바위 사이 깊은 곳에 사는 입 큰 물고기.");
            F("flounder", "광어", U, 30, 80, 1.2f, 200, 3.8f, 12, 2.4f, 0.45f, 0f, 5.5f, 7.5f, "squid:1,minnow:0.8,softworm:0.8,sandworm:0.6,golden:0.4",
                "바닥에 납작 붙어 숨어 있는 사냥꾼. 두 눈이 한쪽에 몰려 있다.");
            F("black_porgy", "감성돔", R, 25, 60, 1.8f, 480, 4.5f, 14, 3.0f, 0.55f, 0f, 3, 7, "shrimp:1,sandworm:0.8,corn:0.4,golden:0.5",
                "갯바위 낚시꾼들의 로망. 경계심이 강하다.");
            F("red_seabream", "참돔", E, 35, 100, 1.5f, 1300, 8.0f, 20, 3.6f, 0.6f, 0f, 4, 7, "squid:1,jig:0.8,egi:0.7,shrimp:0.7,golden:0.7",
                "바다의 여왕. 분홍빛 몸에 푸른 점이 반짝인다.");
            // Swamp
            F("piranha", "피라냐", C, 15, 35, 2.2f, 60, 2.2f, 6, 3.8f, 0.8f, 0.05f, 1, 5, "worm:0.8,squid:0.6,shrimp:0.7,minnow:0.6,spinner:0.5,golden:0.3",
                "날카로운 이빨의 늪지 무법자. 떼로 몰려다닌다.");
            F("catfish", "메기", C, 30, 100, 1.0f, 110, 5.0f, 13, 2.4f, 0.4f, 0f, 3.5f, 6, "worm:1,sandworm:0.5,squid:0.6,softworm:0.5,glow:0.4,golden:0.3",
                "긴 수염으로 진흙 바닥을 더듬는 야행성 물고기.");
            F("snakehead", "가물치", U, 40, 90, 1.1f, 300, 7.0f, 15, 3.2f, 0.7f, 0.2f, 1, 5, "frog:1,popper:0.8,minnow:0.8,crank:0.6,worm:0.3,golden:0.4,@topwater:0.8",
                "뱀 같은 무늬의 늪지 포식자. 개구리 루어에 사족을 못 쓴다.");
            F("arowana", "아로와나", R, 50, 110, 0.8f, 900, 7.5f, 16, 4.0f, 0.6f, 0.6f, 0.5f, 3, "frog:1,popper:1,minnow:0.7,shrimp:0.4,golden:0.5",
                "수면 가까이를 유유히 헤엄치다 먹이를 향해 도약한다.");
            F("arapaima", "피라루쿠", L, 150, 300, 1.0f, 6000, 24f, 35, 3.2f, 0.6f, 0.3f, 2, 5.5f, "frog:1,popper:0.8",
                "세계 최대급 민물고기. 붉은 비늘을 가진 고대어.");
            // Ice
            F("smelt", "빙어", C, 8, 15, 0.8f, 20, 0.6f, 3, 3.0f, 0.5f, 0f, 1, 6, "worm:1,glow:0.8,golden:0.3",
                "얼음 아래 떼지어 사는 투명한 은빛 물고기.");
            F("burbot", "모캐", U, 30, 80, 1.0f, 220, 4.0f, 12, 2.2f, 0.4f, 0f, 5, 7.5f, "worm:0.8,sandworm:0.6,glow:1,squid:0.5,softworm:0.5,golden:0.4",
                "차가운 호수 바닥에 사는 민물 대구.");
            F("northern_pike", "강꼬치고기", U, 50, 120, 0.7f, 350, 8.0f, 14, 4.2f, 0.75f, 0.1f, 1, 5, "jig:1,softworm:0.7,egi:0.5,golden:0.4,@vertical:0.7",
                "오리주둥이 같은 입의 민물 늑대. 순간 가속이 무시무시하다.");
            F("arctic_char", "북극곤들매기", R, 40, 80, 1.1f, 800, 7.0f, 16, 3.8f, 0.6f, 0.1f, 2, 6, "jig:1,egi:0.7,glow:0.6,golden:0.5",
                "주황빛 배를 가진 극지방의 연어과 물고기.");
            F("sturgeon", "철갑상어", L, 120, 280, 0.6f, 9000, 30f, 40, 2.6f, 0.5f, 0.05f, 5.5f, 7.5f, "softworm:1,jig:0.5",
                "공룡 시대부터 살아온 살아있는 화석.");
            // Ocean
            F("yellowtail", "방어", C, 50, 100, 1.1f, 280, 9.0f, 16, 4.5f, 0.7f, 0f, 1, 6, "jig:1,squid:0.8,kona:0.7,minnow:0.6,golden:0.3",
                "노란 줄무늬의 힘센 회유어. 겨울에 맛이 오른다.");
            F("mahi_mahi", "만새기", U, 60, 150, 0.7f, 520, 12f, 18, 5.0f, 0.7f, 0.6f, 0.5f, 3, "popper:1,minnow:1,kona:0.9,squid:0.8,jig:0.6,golden:0.4",
                "형광빛 몸에 튀어나온 이마. 화려한 점프의 달인.");
            F("bluefin_tuna", "참다랑어", R, 100, 250, 1.6f, 2600, 28f, 30, 5.5f, 0.8f, 0.05f, 3, 8, "jig:1,kona:0.9,squid:0.9,golden:0.6",
                "바다의 스프린터. 엄청난 속도와 체력을 가졌다.");
            F("ocean_sunfish", "개복치", E, 100, 250, 3.0f, 4000, 16f, 25, 1.6f, 0.2f, 0f, 2, 7, "squid:1,shrimp:0.7,golden:0.7",
                "느긋하고 거대한 바다의 괴짜. 의외로 끈질기다.");
            F("blue_marlin", "청새치", L, 200, 400, 0.8f, 15000, 45f, 45, 6.0f, 0.8f, 0.7f, 1, 6, "kona:1,jig:0.5",
                "창 같은 주둥이의 바다의 검객. 거친 점프로 낚싯줄을 시험한다.");
            F("great_white", "백상아리", L, 300, 500, 1.0f, 30000, 80f, 60, 5.0f, 0.85f, 0.15f, 3, 8, "kona:1,jig:0.6",
                "바다의 절대 포식자. 티타늄 와이어 없이는 상대할 수 없다.");
            // Cave
            F("cave_tetra", "장님동굴어", C, 6, 12, 1.5f, 90, 0.8f, 4, 3.0f, 0.5f, 0f, 1, 6, "glow:1,worm:0.5,golden:0.3",
                "빛이 없는 동굴에서 눈이 퇴화한 신비한 물고기.");
            F("crystal_koi", "수정비단잉어", R, 40, 80, 1.5f, 3200, 9f, 18, 3.2f, 0.5f, 0.1f, 2, 6, "glow:1,corn:0.6,golden:0.8",
                "수정 동굴의 빛을 머금은 비단잉어. 비늘이 보석처럼 빛난다.");
            F("anglerfish", "초롱아귀", E, 40, 100, 2.5f, 4500, 12f, 20, 2.4f, 0.6f, 0f, 5, 7.5f, "glow:0.8,egi:0.8,squid:0.6,jig:0.5,golden:0.7",
                "머리의 초롱불로 먹이를 꾀어내는 심연의 사냥꾼.");
            F("coelacanth", "실러캔스", L, 120, 200, 1.3f, 25000, 35f, 40, 2.8f, 0.55f, 0f, 5, 7.5f, "egi:1,softworm:0.6,jig:0.4",
                "멸종된 줄 알았던 4억 년 전의 고대어. 발 같은 지느러미를 가졌다.");

            // how the jumpers behave in the air; every other jumper just hops
            foreach (var id in new[] { "largemouth_bass", "rainbow_trout", "cherry_salmon", "lenok", "snakehead", "arowana", "northern_pike", "arctic_char", "arapaima" })
                Fish.Find(f => f.id == id).jumpStyle = JumpStyle.Shaker;
            foreach (var id in new[] { "blue_marlin", "mahi_mahi" })
                Fish.Find(f => f.id == id).jumpStyle = JumpStyle.TailWalker;

            // the cover seekers (Docs/obstacles_spec.md 7.1): seek (how often a run heads for its cover), reach (m; 0 = the
            // ice hole's rim), dig (how hard it holds there), the cover types; everyone else never seeks cover
            foreach (var (id, seek, reach, dig, types) in new[]
            {
                ("largemouth_bass", 0.55f, 10f, 1.2f, "post,pad,reed,weed,boat,log"),
                ("carp", 0.30f, 8f, 1.0f, "weed,reed,pad"),
                ("golden_carp", 0.45f, 10f, 1.3f, "weed,pad,reed"),
                ("mandarin_fish", 0.70f, 8f, 1.4f, "rock"),
                ("rainbow_trout", 0.20f, 8f, 0.8f, "rock"),
                ("lenok", 0.25f, 8f, 1.0f, "rock"),
                ("rockfish", 0.75f, 6f, 1.5f, "tet,rock"),
                ("black_porgy", 0.55f, 8f, 1.2f, "tet,rock"),
                ("red_seabream", 0.20f, 10f, 1.0f, "tet"),
                ("snakehead", 0.65f, 10f, 1.4f, "pad,reed,root,log,weed"),
                ("catfish", 0.45f, 8f, 1.1f, "root,log"),
                ("arowana", 0.20f, 8f, 0.8f, "root"),
                ("arapaima", 0.60f, 14f, 1.6f, "root,log,weed"),
                ("northern_pike", 0.55f, 0f, 1.2f, "rim"),
                ("arctic_char", 0.20f, 0f, 1.0f, "rim"),
                ("burbot", 0.30f, 6f, 1.0f, "rock"),
                ("sturgeon", 0.40f, 0f, 1.5f, "rim"),
                ("yellowtail", 0.40f, 12f, 1.1f, "hull,rock"),         // (the ocean's reef pinnacle too)
                ("mahi_mahi", 0.45f, 12f, 1.0f, "weed"),               // under the drifting weed mat
                ("bluefin_tuna", 0.35f, 14f, 1.3f, "hull,weed"),
                ("great_white", 0.45f, 16f, 1.6f, "hull"),
                ("coelacanth", 0.60f, 14f, 1.5f, "crystal,rock"),
                ("anglerfish", 0.40f, 8f, 1.1f, "rock"),
                ("crystal_koi", 0.35f, 10f, 1.0f, "crystal"),
            })
            {
                var cs = Fish.Find(x => x.id == id);
                if (cs == null) continue;
                cs.coverSeek = seek;
                cs.coverReach = reach;
                cs.coverDig = dig;
                cs.coverFor = types.Split(',');
            }

            // every legend is met through the underwater encounter (Docs/lures_legend_spec.md 2.1, Docs/legends_rollout.md
            // 3): FishSpawner.Pick never spawns them as ordinary fish
            Fish.Find(f => f.id == "coelacanth").encounter = Coelacanth();
            Fish.Find(f => f.id == "golden_carp").encounter = GoldenCarp();
            Fish.Find(f => f.id == "arapaima").encounter = Arapaima();
            Fish.Find(f => f.id == "sturgeon").encounter = Sturgeon();
            Fish.Find(f => f.id == "blue_marlin").encounter = BlueMarlin();
            Fish.Find(f => f.id == "great_white").encounter = GreatWhite();
            BuildSets();
            Diets();
        }

        /// <summary>
        /// The aquarium diets (AquaCare, AquaFeed): which of 사료 (pellets), 생새우 (live shrimp) and 정어리 (sardines) each
        /// species eats, and how it takes a dropped shrimp / sardine (Grab in mid-water, Bottom on the gravel, Surge: up to
        /// the surface and one gulp). Small fish and omnivores eat pellets (the omnivores shrimp too), mid-size predators
        /// and bottom feeders live shrimp, the big and sea predators sardines.
        /// </summary>
        static void Diets()
        {
            const Diet P = Diet.Pellet, S = Diet.Shrimp, D = Diet.Sardine;
            const FeedStyle G = FeedStyle.Grab, B = FeedStyle.Bottom, U = FeedStyle.Surge;
            var seen = new HashSet<string>();
            foreach (var (id, diet, style) in new[]
            {
                // lake
                ("crucian_carp", P, G),         // a small grazer raised on paste (떡밥): pellets
                ("bluegill", P | S, G),         // a greedy sunfish that bites anything: pellets or shrimp
                ("carp", P | S, B),             // an omnivore rooting the bottom: farm pellets, crustaceans off the gravel
                ("largemouth_bass", S, G),      // an ambush predator: live prey only, dead pellets are ignored
                ("golden_carp", P | S, B),      // a carp: the same omnivorous bottom rooter
                // stream
                ("pale_chub", P, G),            // a tiny insect / algae picker
                ("cherry_salmon", P | S, G),    // a small trout: hatchery pellets, stream shrimp and insects
                ("rainbow_trout", P | S, G),    // farmed on pellets; crustaceans give it the pink band
                ("mandarin_fish", S, G),        // 쏘가리 take live prey only
                ("lenok", S, G),                // a cold-stream salmonid predator of insects and small prey
                // sea
                ("horse_mackerel", P | S, G),   // a schooling krill / plankton eater: pellets or shrimp
                ("mackerel", P | S, G),         // the same
                ("rockfish", S, B),             // a rock-bottom ambusher of crabs and shrimp
                ("flounder", S, B),             // lies flat on the bottom and snaps what lands by it
                ("black_porgy", P | S, G),      // an omnivore (krill, corn, weed)
                ("red_seabream", S | D, G),     // a mid-size sea predator of crustaceans and small fish
                // swamp
                ("piranha", P | S, G),          // an omnivorous scavenger
                ("catfish", P | S, B),          // a whiskered bottom scavenger: sinking pellets and shrimp
                ("snakehead", S, G),            // an ambush predator of frogs and fish: no pellets
                ("arowana", S | D, U),          // hunts at the surface and leaps for prey: shrimp or fish
                ("arapaima", D, U),             // a giant that gulps fish at the surface
                // ice
                ("smelt", P, G),                // a tiny plankton eater
                ("burbot", S, B),               // a freshwater cod hunting the bottom at night
                ("northern_pike", S, G),        // a mid-size lurking predator
                ("arctic_char", P | S, G),      // an omnivorous char, farmed on pellets
                ("sturgeon", S, B),             // a bottom feeder vacuuming crustaceans off the gravel
                // ocean
                ("yellowtail", S | D, U),       // a fast pelagic hunter of small fish and shrimp
                ("mahi_mahi", S | D, U),        // a surface predator of flying fish, squid and shrimp
                ("bluefin_tuna", D, U),         // a big pelagic fish eater
                ("ocean_sunfish", S, G),        // a slow drifter on jellies and crustaceans, not a hunter: shrimp
                ("blue_marlin", D, U),          // a big-game fish eater
                ("great_white", D, U),          // the apex predator
                // cave
                ("cave_tetra", P, G),           // a tiny blind scavenger
                ("crystal_koi", P, G),          // koi are the pellet fish
                ("anglerfish", D, B),           // lies on the bottom and engulfs the fish its lure brings in
                ("coelacanth", D, U),           // a big ancient fish eater
            })
            {
                var f = Fish.Find(x => x.id == id);
                if (f == null) continue;
                f.diet = diet;
                f.feedStyle = style;
                seen.Add(id);
            }
            foreach (var f in Fish)
                if (!seen.Contains(f.id)) Debug.LogWarning($"[GameDatabase] {f.id}: no aquarium diet (pellets assumed)");
        }

        // ------------------------------------------------------------------------------------ encounter backdrops
        static readonly Dictionary<string, EncounterSetDef> sets = new Dictionary<string, EncounterSetDef>();

        /// <summary>The encounter backdrop and light profile (Docs/legends_rollout.md 4.1); the cave's for an unknown id.</summary>
        public static EncounterSetDef GetSet(string id) => id != null && sets.TryGetValue(id, out var s) ? s : sets["cave"];

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

        /// <summary>The values every rollout row shares (Docs/legends_rollout.md 2).</summary>
        static EncounterDef Row(string id, string backdrop, string eyeCore, string eyeGlow, string frameLine)
        {
            return new EncounterDef
            {
                model = "legend_" + id, backdrop = backdrop, eyeCore = eyeCore, eyeGlow = eyeGlow, frameLine = frameLine,
                gaugeStart = 20, pityStep = 10, pityMax = 30, decay = 3f, timeoutStrike = 60,
                appear = new Vector2(20f, 40f), relocate = new Vector2(90f, 150f), coolFail = 60f, coolFight = 180f,
                tell = 0.35f, perfectT = 0.25f, buffer = 0.15f, bottomBand = 99f, moodHold = 2.5f,
            };
        }

        static MoodDef HoldMood(string prompt, string credit, float grace, float gain) => new MoodDef
        {
            name = "흥분", prompt = prompt, verb = Verb.Hold, lo = grace, gain = gain, flickLoss = 20f, circleGrace = 0.25f,
            circleLoss = 15f, creditText = credit,
        };

        /// <summary>황금잉어: the lake's bait-eater; a bait lying still on the bottom by the weed edge (rollout 3.1).</summary>
        static EncounterDef GoldenCarp()
        {
            var e = Row("golden_carp", "lake", "#fff6d0", "#ffc830", "#a8e878");
            e.nameSub = "백 년을 산 호수의 주인";
            e.tipWrongLure = "바닥에 가만히 놓인 달콤한 먹이를 좋아하는 것 같다…";
            e.meterQ = MeterQ.Still;
            e.keyRules["bait_softworm"] = new KeyRule { meterQ = MeterQ.Lure };
            e.depthMin = 4.0f;
            e.bottomBand = 0.8f;
            e.lurkZMin = 14f;
            e.lurkZMax = 30f;
            e.nearLurk = 7f;
            e.minQ = 0.6f;
            e.minSoak = 10f;
            e.fillTime = 18f;
            e.chance = 0.6f;
            e.minLine = 11f;
            e.teaseLimit = 30f;
            e.noseIn = new Vector2(1.0f, 1.6f);
            e.fakeOuts = 1;
            e.fakeOutText = "…맛을 본다";
            e.hookWindow = 0.7f;
            e.lightGlow = e.lightPlain = 3.6f;
            e.orbitFar = 2.2f;
            e.orbitNear = 1.1f;
            e.noseDist = 0.3f;
            // the tasting up close: the camera moves in until the carp fills about half the window
            e.noseFill = new Vector2(0.52f, 0.78f);
            e.noseFrame = new Vector2(0.5f, 0.55f);
            e.noseCamTease = 0.35f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "살살… 아주 천천히 끌어요", verb = Verb.Wind, lo = 0.1f, hi = 0.4f, gain = 8f,
                    tooFast = 0.8f, fastLoss = 12f, flickLoss = 15f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "살짝 끌고… 멈춰요", verb = Verb.RunPause, lo = 0.1f, hi = 0.5f, gain = 15f,
                    runMin = 0.3f, runMax = 1.2f, pauseMin = 1.0f, pauseMax = 2.5f, tooFast = 0.8f, fastLoss = 10f,
                    overRun = 2.0f, overRunLoss = 6f, overRunText = "멈춰요, 살짝만!", boredAfter = 4f, boredLoss = 4f, flickLoss = 12f,
                    creditText = "따라와요!" },
                HoldMood("멈춰요! 빨아들이려 해요", "입술을 내민다…!", 0.5f, 11f),
            };
            e.tellText = "…입술이 움직인다";
            e.lungeT = 0.45f;
            e.hitStop = 0.10f;
            e.biteText = "쪼옥!";
            e.lurkText = "물속 깊은 곳에서 황금빛이 번뜩였다…";
            e.eyesText = "물풀 너머에서 무언가 다가온다…";
            e.lostText = "황금잉어가 물풀 속으로 사라졌다…";
            e.failTips = new[] { "다음엔: 미끼는 아주 살짝만 끌기!", "다음엔: 톡 당기지 말고 끌기", "다음엔: 흥분하면 멈추기!", "다음엔: 맛볼 때는 참았다가 챔질" };
            e.catchLine = "비늘 하나하나가 금화처럼 빛난다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Carp, swim = new[] { 2f, 5f, 8f, 11f, 16f }, tailHz = new Vector4(0.6f, 0.8f, 0.5f, 1.8f), cruise = 0.9f,
                eyes0 = new Vector3(4.6f, 0.3f, 7.6f), eyes1 = new Vector3(3.4f, 0.25f, 5.6f), deepDir = new Vector3(0.55f, 0f, 0.83f),
                orbitSpeed = new Vector2(0.30f, 0.45f), orbitDepth = 0.35f, hover = new Vector2(0.12f, 30f),
                finAmp = 0.8f, flare = 25f, scull = 12f, barbelSign = -1f,
            };
            e.keyLures["bait_golden"] = 1.0f;
            e.keyLures["bait_corn"] = 0.5f;
            e.keyLures["bait_softworm"] = 0.4f;
            return e;
        }

        /// <summary>피라루쿠: the swamp's surface-lure legend, striking from below (rollout 3.2).</summary>
        static EncounterDef Arapaima()
        {
            var e = Row("arapaima", "swamp", "#ffe6c8", "#ff7a3a", "#d8e070");
            e.nameSub = "숨 쉬러 떠오르는 늪의 거인";
            e.tipWrongLure = "수면에서 퍼덕이는 먹이를 노리는 것 같다…";
            e.depthMin = 0f;
            e.depthMax = 0.4f;
            e.lurkZMin = 10f;
            e.lurkZMax = 30f;
            e.nearLurk = 7f;
            e.lurkDepth = 1.2f;
            e.minQ = 0.55f;
            e.minSoak = 6f;
            e.fillTime = 16f;
            e.chance = 0.6f;
            e.minLine = 22f;
            e.teaseLimit = 28f;
            e.noseIn = new Vector2(0.8f, 1.4f);
            e.hookWindow = 0.5f;
            e.lightGlow = e.lightPlain = 2.2f;
            e.orbitFar = 2.0f;
            e.orbitNear = 1.2f;
            e.noseDist = 0.8f;
            e.camScale = 1.15f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "개구리처럼… 살살 감다 멈춰요", verb = Verb.RunPause, lo = 0.3f, hi = 0.8f, gain = 12f,
                    runMin = 0.5f, runMax = 2.0f, pauseMin = 0.5f, pauseMax = 2.0f, tooFast = 1.2f, fastLoss = 12f,
                    overRun = 2.5f, overRunLoss = 6f, boredAfter = 3.5f, boredLoss = 4f, flickLoss = 8f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "톡! 퐁— 물결이 잦아들 때까지", verb = Verb.FlickPause, lo = 1.0f, hi = 2.5f, gain = 14f,
                    maxStrength = 1f, strongAt = 0.95f, strongLoss = 8f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                    boredAfter = 3.5f, boredLoss = 5f, creditText = "올려다봐요!" },
                HoldMood("멈춰요! 바로 밑에 있어요", "떠오른다…!", 0.6f, 12f),
            };
            e.tellText = "…아래에서 노려본다";
            e.lungeT = 0.20f;
            e.hitStop = 0.14f;
            e.shake = new Vector2(0.25f, 0.25f);
            e.biteText = "펑!";
            e.lurkText = "탁한 물속에서 무언가 숨을 쉬러 떠올랐다…";
            e.eyesText = "탁한 물 아래에서 붉은 눈이 떠오른다…";
            e.lostText = "피라루쿠가 탁한 물속으로 가라앉았다…";
            e.failTips = new[] { "다음엔: 감다가 꼭 멈추기", "다음엔: 톡 당긴 뒤 물결이 잦아들 때까지", "다음엔: 흥분하면 멈추기!", "다음엔: 솟구치기 전엔 챔질 참기" };
            e.catchLine = "붉은 비늘이 달아오른 숯불처럼 번진다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Arapaima, swim = new[] { 2f, 4f, 7f, 10f, 14f }, tailHz = new Vector4(0.5f, 0.7f, 0.9f, 3.0f), cruise = 0.8f,
                eyes0 = new Vector3(2.4f, -2.4f, 5.2f), eyes1 = new Vector3(1.8f, -1.8f, 4.0f), deepDir = new Vector3(0.55f, -0.45f, 0.7f),
                orbitSpeed = new Vector2(0.28f, 0.5f), orbitDepth = -0.8f, hover = new Vector2(-0.7f, -50f),
                finAmp = 0.6f, flare = 25f, scull = 10f,
            };
            // the frog and the popper are topwater lures: the tease is watched from above, the legend a shadow under the
            // murk (each has its top frames, lure_frog_top_* / lure_popper_top_*; a key without them keeps the side view)
            e.teaseView = TeaseView.Top;
            e.top = new TopViewDef { set = "swamp_top", noseInText = "떠오른다…!", holdCredit = "…아래에서 노려본다" };
            e.keyLures["bait_frog"] = 1.0f;
            e.keyLures["bait_popper"] = 0.8f;
            return e;
        }

        /// <summary>철갑상어: under the ice, a worm dragged and hopped along the bottom by the hole (rollout 3.3).</summary>
        static EncounterDef Sturgeon()
        {
            var e = Row("sturgeon", "ice", "#f0fbff", "#9ad8ff", "#bfefff");
            e.nameSub = "공룡과 함께 살았던 갑옷 물고기";
            e.tipWrongLure = "바닥을 천천히 기어가는 먹이를 찾는 것 같다…";
            e.depthMin = 3.5f;
            e.bottomBand = 0.8f;
            e.lurkZMin = 6f;
            e.lurkZMax = 14f;
            e.nearLurk = 12f;
            e.minQ = 0.5f;
            e.minSoak = 8f;
            e.fillTime = 18f;
            e.chance = 0.6f;
            e.minLine = 22f;
            e.relocate = new Vector2(120f, 180f);
            e.teaseLimit = 30f;
            e.noseIn = new Vector2(1.2f, 1.8f);
            e.fakeOuts = 1;
            e.fakeOutText = "…수염으로 더듬는다";
            e.hookWindow = 0.7f;
            e.lightGlow = e.lightPlain = 3.0f;
            e.orbitFar = 2.6f;
            e.orbitNear = 1.4f;
            e.noseDist = 0.35f;
            e.camScale = 1.1f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "살살… 바닥을 천천히 끌어요", verb = Verb.Wind, lo = 0.1f, hi = 0.35f, gain = 8f,
                    tooFast = 0.7f, fastLoss = 12f, flickLoss = 15f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "바닥에서 살짝 톡… 기다려요", verb = Verb.FlickPause, lo = 1.2f, hi = 3.0f, gain = 14f,
                    maxStrength = 0.5f, strongAt = 0.7f, strongLoss = 10f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                    boredAfter = 4f, boredLoss = 4f, creditText = "더듬어 봐요!" },
                HoldMood("멈춰요! 수염이 닿았어요", "입이 내려온다…!", 0.6f, 10f),
            };
            e.tellText = "…입이 내려온다";
            e.lungeT = 0.30f;
            e.biteText = "후읍!";
            e.lurkText = "얼음 아래에서 무언가 바닥을 훑고 지나갔다…";
            e.eyesText = "얼음 밑 어둠 속에서 무언가 다가온다…";
            e.lostText = "철갑상어가 얼음 밑 어둠으로 사라졌다…";
            e.failTips = new[] { "다음엔: 바닥은 아주 천천히 끌기!", "다음엔: 톡은 아주 살짝", "다음엔: 흥분하면 멈추기!", "다음엔: 수염이 더듬을 땐 참기" };
            e.catchLine = "등의 뼈 비늘이 갑옷처럼 단단하다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Sturgeon, swim = new[] { 1f, 3f, 6f, 9f, 14f }, tailHz = new Vector4(0.45f, 0.6f, 0.5f, 1.5f), cruise = 0.6f,
                eyes0 = new Vector3(5.0f, 0.2f, 7.0f), eyes1 = new Vector3(3.8f, 0.2f, 5.0f), deepDir = new Vector3(0.7f, 0f, 0.7f),
                orbitSpeed = new Vector2(0.25f, 0.35f), orbitDepth = 0.25f, hover = new Vector2(0.06f, 6f),
                finAmp = 0.5f, flare = 20f, scull = 10f, barbelSign = 1f, barbelRoll = true,
            };
            e.keyLures["bait_softworm"] = 1.0f;
            e.keyLures["bait_jig"] = 0.5f;
            return e;
        }

        /// <summary>청새치: the ocean's speed legend: a kona run fast near the surface (rollout 3.4, 5).</summary>
        static EncounterDef BlueMarlin()
        {
            var e = Row("blue_marlin", "ocean", "#e8f8ff", "#4ab0ff", "#7fd4ff");
            e.nameSub = "대양을 가르는 푸른 창";
            e.tipWrongLure = "빠르게 달아나는 먹이를 쫓는 것 같다…";
            e.depthMax = 3.0f;
            e.lurkZMin = 20f;
            e.lurkZMax = 45f;
            e.nearLurk = 10f;
            e.lurkDepth = 6f;
            e.minQ = 0.6f;
            e.minSoak = 5f;
            e.fillTime = 14f;
            e.chance = 0.6f;
            e.minLine = 45f;
            e.teaseLimit = 26f;
            e.noseIn = new Vector2(0.6f, 1.0f);
            e.fakeOuts = 1;
            e.fakeOutText = "휙— 부리로 친다!";
            e.hookWindow = 0.5f;
            e.lightGlow = e.lightPlain = 6.0f;
            e.orbitFar = 3.0f;
            e.orbitNear = 1.5f;
            e.noseDist = 0.9f;
            e.camScale = 1.2f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "빠르게! 쉬지 말고 감아요", verb = Verb.Wind, lo = 1.6f, hi = 9f, gain = 9f,
                    tooSlow = 1.2f, slowAfter = 0.5f, slowLoss = 8f, slowText = "느려요! 계속 달려요", flickLoss = 10f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "휙 감다가 툭! 멈췄다 다시", verb = Verb.RunPause, lo = 1.8f, hi = 9f, gain = 13f,
                    runMin = 0.5f, runMax = 1.4f, pauseMin = 0.3f, pauseMax = 1.0f, overRun = 2.2f, overRunLoss = 4f,
                    overRunText = "툭 멈췄다 다시!", boredAfter = 1.6f, boredLoss = 8f, boredText = "너무 오래 멈췄어요", flickLoss = 10f,
                    creditText = "몸이 빛나요!" },
                HoldMood("멈춰요! 먹이를 놓아줘요", "푸른 줄무늬가 타오른다…!", 0.3f, 14f),
            };
            e.tellText = "…돛을 세운다";
            e.lungeT = 0.25f;
            e.hitStop = 0.10f;
            e.biteText = "콱!";
            e.lurkText = "수평선 아래에서 푸른 빛이 번뜩였다…";
            e.eyesText = "깊고 푸른 곳에서 무언가 치솟는다…";
            e.lostText = "청새치가 푸른 바다 너머로 사라졌다…";
            e.failTips = new[] { "다음엔: 경계할 땐 쉬지 말고 빠르게!", "다음엔: 감다가 짧게만 멈추기", "다음엔: 흥분하면 멈추기!", "다음엔: 부리로 칠 땐 참았다가 챔질" };
            e.catchLine = "푸른 줄무늬가 아직도 희미하게 빛난다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Marlin, swim = new[] { 0.5f, 1f, 2f, 5f, 18f }, tailHz = new Vector4(1.2f, 1.6f, 1.4f, 3.5f), cruise = 3.0f,
                eyes0 = new Vector3(2.5f, -3.6f, 9.0f), eyes1 = new Vector3(1.5f, -2.0f, 6.0f), deepDir = new Vector3(0.5f, -0.5f, 0.7f),
                orbitSpeed = new Vector2(0.5f, 0.9f), orbitDepth = -0.2f, hover = new Vector2(0f, 0f),
                finAmp = 0f, flare = 35f, scull = 5f,
            };
            e.keyLures["bait_kona"] = 1.0f;
            e.keyLures["bait_jig"] = 0.5f;
            return e;
        }

        /// <summary>백상아리: the ocean's stalker: a kona crawled like a crippled fish, or a jig worked deep (rollout 3.5, 5).</summary>
        static EncounterDef GreatWhite()
        {
            var e = Row("great_white", "ocean", "#f4f8fa", "#9aa8b4", "#7fd4ff");
            e.nameSub = "바다의 절대 포식자";
            e.tipWrongLure = "천천히 비틀거리는 먹이를 노리는 것 같다…";
            e.meterQ = MeterQ.Crawl;
            e.keyRules["bait_jig"] = new KeyRule { depthMin = 6f, meterQ = MeterQ.Lure };
            e.lurkZMin = 20f;
            e.lurkZMax = 45f;
            e.nearLurk = 10f;
            e.lurkDepth = 6f;
            e.minQ = 0.55f;
            e.minSoak = 6f;
            e.fillTime = 20f;
            e.chance = 0.55f;
            e.minLine = 100f;
            e.gearLine = "금속 줄";
            e.coolFail = 90f;
            e.coolFight = 240f;
            e.teaseLimit = 30f;
            e.noseIn = new Vector2(1.0f, 1.6f);
            e.fakeOuts = 1;
            e.fakeOutText = "쿵… 코로 들이받는다";
            e.hookWindow = 0.6f;
            e.lightGlow = e.lightPlain = 6.0f;
            e.orbitFar = 3.4f;
            e.orbitNear = 2.0f;
            e.noseDist = 1.5f;
            e.camScale = 1.4f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "비틀비틀… 천천히 감아요", verb = Verb.Wind, lo = 0.4f, hi = 1.2f, gain = 8f,
                    tooFast = 1.8f, fastLoss = 10f, tooSlow = 0.25f, slowAfter = 1.0f, slowLoss = 4f, slowText = "계속 비틀비틀 감아요",
                    flickLoss = 12f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "톡! 다친 물고기처럼… 멈춰요", verb = Verb.FlickPause, lo = 0.8f, hi = 2.0f, gain = 13f,
                    maxStrength = 0.7f, strongAt = 0.85f, strongLoss = 10f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                    boredAfter = 3f, boredLoss = 5f, creditText = "다가와요…" },
                HoldMood("멈춰요! 움직이면 끝이에요", "입을 벌린다…!", 0.5f, 11f),
            };
            e.tellText = "…눈이 뒤집힌다";
            e.lungeT = 0.30f;
            e.hitStop = 0.15f;
            e.shake = new Vector2(0.3f, 0.3f);
            e.biteText = "콰직!";
            e.lurkText = "배 밑으로 거대한 그림자가 지나갔다…";
            e.eyesText = "푸른 어둠 아래에서 거대한 그림자가 떠오른다…";
            e.lostText = "백상아리가 푸른 어둠 속으로 가라앉았다…";
            e.failTips = new[] { "다음엔: 경계할 땐 천천히 비틀비틀", "다음엔: 톡은 짧게, 너무 세지 않게", "다음엔: 흥분하면 멈추기!", "다음엔: 들이받을 땐 참았다가 챔질" };
            e.catchLine = "이빨 한 줄 한 줄이 칼날처럼 늘어서 있다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.GreatWhite, swim = new[] { 1f, 2f, 4f, 7f, 14f }, tailHz = new Vector4(0.35f, 0.45f, 0.55f, 2.2f), cruise = 1.0f,
                eyes0 = new Vector3(1.6f, -2.9f, 9.5f), eyes1 = new Vector3(1.4f, -2.1f, 5.6f), deepDir = new Vector3(0.3f, -0.8f, 0.5f),
                orbitSpeed = new Vector2(0.22f, 0.30f), orbitDepth = -0.6f, hover = new Vector2(0f, 0f),
                finAmp = 0.2f, flare = 12f, scull = 0f, cruiseJaw = 5f,
            };
            e.keyLures["bait_kona"] = 1.0f;
            e.keyLures["bait_jig"] = 0.6f;
            return e;
        }

        /// <summary>The cave coelacanth's encounter row (spec 2.1 / 2.8 / 2.9).</summary>
        static EncounterDef Coelacanth()
        {
            var e = new EncounterDef
            {
                model = "legend_coelacanth", backdrop = "cave", nameSub = "4억 년을 살아남은 고대어",
                tipWrongLure = "어둠 속에서 빛나며 가라앉는 먹이에 끌리는 것 같다…",
                depthMin = 5.0f, bottomBand = 1.5f, lurkZMin = 14f, lurkZMax = 30f, nearLurk = 8f,
                minQ = 0.55f, minSoak = 8f, fillTime = 16f, chance = 0.6f, minLine = 20f,
                coolFail = 60f, coolFight = 180f, appear = new Vector2(20f, 40f), relocate = new Vector2(90f, 150f),
                gaugeStart = 20, pityStep = 10, pityMax = 30, decay = 3f, teaseLimit = 28f, timeoutStrike = 60,
                noseIn = new Vector2(0.6f, 1.4f), tell = 0.35f, hookWindow = 0.6f, perfectT = 0.25f, buffer = 0.15f,
                // lure-light radius: the spec's 1.6 / 1.0 m leave the 1.3 m tease orbit almost black with the
                // smoothstep(0.35 R, R) falloff; 3.0 m lights it (0.95 at 1.3 m, the spots glint first at 2.4 m, none at 3 m)
                lightGlow = 3.0f, lightPlain = 2.0f,
                orbitFar = 2.4f, orbitNear = 1.3f, noseDist = 0.6f,
                eyeCore = "#f0ffd8", eyeGlow = "#b8ff8a", frameLine = "#6affea",
                moods = new[]
                {
                    new MoodDef { name = "경계", prompt = "살살… 천천히 감아요", verb = Verb.Wind, lo = 0.15f, hi = 0.7f, gain = 9f,
                        tooFast = 1.2f, fastLoss = 12f, flickLoss = 15f },
                    new MoodDef { name = "호기심", prompt = "톡! 당기고 기다려요", verb = Verb.FlickPause, lo = 0.6f, hi = 2.0f, gain = 14f,
                        maxStrength = 0.6f, strongAt = 0.8f, strongLoss = 10f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                        boredAfter = 3f, boredLoss = 5f },
                    new MoodDef { name = "흥분", prompt = "멈춰요! 움직이지 마요", verb = Verb.Hold, lo = 0.4f, gain = 12f,
                        flickLoss = 20f, circleGrace = 0.25f, circleLoss = 15f },
                },
            };
            e.keyLures["bait_egi"] = 1.0f;
            e.keyLures["bait_softworm"] = 0.6f;
            e.keyLures["bait_jig"] = 0.4f;
            return e;
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

            Lines.Add(new LineDef { id = "line_nylon2", name = "나일론 2호", price = 0, strength = 3f, tough = 1.0f, color = new Color(0.92f, 0.96f, 0.96f, 0.9f), desc = "기본 나일론 줄. 3kg까지 버틴다." });
            Lines.Add(new LineDef { id = "line_nylon4", name = "나일론 4호", price = 600, strength = 6f, tough = 1.15f, color = new Color(0.72f, 0.9f, 0.63f, 0.9f), desc = "조금 더 굵은 나일론 줄. 6kg." });
            Lines.Add(new LineDef { id = "line_fluoro6", name = "플로로카본 6호", price = 3000, strength = 11f, tough = 1.7f, stealth = 1.15f, color = new Color(0.95f, 0.8f, 0.86f, 0.7f), desc = "물속에서 잘 보이지 않아 입질이 늘어난다. 11kg." });
            Lines.Add(new LineDef { id = "line_pe3", name = "PE 합사 3호", price = 12000, strength = 22f, tough = 0.6f, color = new Color(0.25f, 0.7f, 0.3f, 1f), desc = "가늘지만 매우 질긴 합사. 22kg." });
            Lines.Add(new LineDef { id = "line_pe8", name = "PE 합사 8호", price = 40000, strength = 45f, tough = 0.8f, color = new Color(0.95f, 0.5f, 0.2f, 1f), desc = "대물 전용 굵은 합사. 45kg." });
            Lines.Add(new LineDef { id = "line_titan", name = "티타늄 와이어", price = 150000, strength = 100f, tough = 5.0f, color = new Color(0.8f, 0.82f, 0.86f, 1f), desc = "상어 이빨도 끊지 못하는 금속 줄. 100kg." });

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

            // capacity in 칸 (소형 <40cm 1, 중형 40-100cm 2, 대형 100-200cm 4, 초대형 200cm+ 8) and the biggest size class
            Tanks.Add(new TankDef { id = "tank_0", name = "작은 어항", level = 0, capacity = 6, maxClass = 0, price = 0, desc = "책상 위 작은 어항. 40cm 미만 소형 물고기만 살 수 있다." });
            Tanks.Add(new TankDef { id = "tank_1", name = "중형 수조", level = 1, capacity = 12, maxClass = 1, price = 2500, desc = "벽에 붙인 수조. 1m 미만 중형 물고기까지." });
            Tanks.Add(new TankDef { id = "tank_2", name = "대형 수조", level = 2, capacity = 24, maxClass = 2, price = 12000, desc = "넓은 대형 수조. 2m 미만 대형 물고기까지." });
            Tanks.Add(new TankDef { id = "tank_3", name = "아쿠아리움", level = 3, capacity = 40, maxClass = 3, maxHuge = 2, price = 45000, desc = "유리벽 아쿠아리움. 2m가 넘는 초대형 물고기도 두 마리까지." });
            Tanks.Add(new TankDef { id = "tank_4", name = "황금 대수족관", level = 4, capacity = 64, maxClass = 3, price = 150000, desc = "꿈의 수족관. 초대형 물고기 여러 마리가 함께 헤엄친다." });
        }

        // ------------------------------------------------------------------------------------ stages
        static StageDef S(string id, string name, string sub, int diff, int lvl, int cost, float pm, float bm, int pop, params (string, float)[] spawns)
        {
            var s = new StageDef
            {
                id = id, name = name, subtitle = sub, difficulty = diff, reqLevel = lvl, unlockCost = cost,
                powerMult = pm, biteMult = bm, population = pop,
            };
            foreach (var (fid, w) in spawns) s.spawns.Add(new KeyValuePair<string, float>(fid, w));
            Stages.Add(s);
            return s;
        }

        static void BuildStages()
        {
            S("lake", "고요한 호수", "낚시를 처음 배우기 좋은 잔잔한 호수", 1, 1, 0, 1.0f, 1.1f, 8,
                ("crucian_carp", 40), ("bluegill", 35), ("carp", 16), ("largemouth_bass", 14), ("golden_carp", 1.2f));
            S("stream", "산골 계곡", "차갑고 맑은 물이 흐르는 계곡", 2, 2, 1200, 1.0f, 1.0f, 8,
                ("pale_chub", 40), ("cherry_salmon", 30), ("rainbow_trout", 18), ("mandarin_fish", 8), ("lenok", 3));
            S("sea", "바다 방파제", "파도가 부서지는 테트라포드 방파제", 3, 4, 6000, 1.05f, 1.0f, 9,
                ("horse_mackerel", 35), ("mackerel", 30), ("rockfish", 20), ("flounder", 14), ("black_porgy", 8), ("red_seabream", 3));
            S("swamp", "안개 늪지", "안개 속에 괴어가 숨어 있는 늪", 3, 6, 15000, 1.1f, 0.95f, 8,
                ("piranha", 35), ("catfish", 28), ("snakehead", 20), ("arowana", 7), ("arapaima", 1.5f));
            S("ice", "얼음 호수", "얼음 구멍 아래로 미끼를 내리는 빙어낚시", 4, 8, 35000, 1.15f, 0.95f, 8,
                ("smelt", 45), ("burbot", 22), ("northern_pike", 18), ("arctic_char", 9), ("sturgeon", 1.5f));
            S("ocean", "먼바다", "배를 타고 나가는 대물 트롤링", 5, 11, 80000, 1.2f, 0.9f, 7,
                ("yellowtail", 34), ("mahi_mahi", 26), ("bluefin_tuna", 14), ("ocean_sunfish", 7), ("blue_marlin", 2.5f), ("great_white", 1.2f));
            S("cave", "수정 동굴", "빛나는 수정 아래 고대어가 잠든 지하 호수", 5, 14, 200000, 1.25f, 0.9f, 7,
                ("cave_tetra", 40), ("crystal_koi", 12), ("anglerfish", 10), ("coelacanth", 2f));
        }
    }
}
