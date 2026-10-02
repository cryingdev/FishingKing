using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The species tables as they stood in C# before the move to Resources/Data/Fish and Data/stages.json (base commit
    /// e402ee7: GameDatabase.F / BuildFish / Habitats / Diets / BuildStages, TimeActivity's table, FishAgent.HoldChance),
    /// cut verbatim, for the parity proof only (-fkauto species, P1 / P2 / P3): <see cref="Build"/> makes the old objects so
    /// the test can compare them with the loaded data field by field and through FishSpawner.Pick. The legends' rows come from
    /// LegendEncounters with the old key lure lines added back. Delete with the proof.
    /// </summary>
    public static class LegacySpecies
    {
        static readonly List<FishSpecies> Fish = new List<FishSpecies>();
        static readonly List<StageDef> Stages = new List<StageDef>();

        /// <summary>The old objects, in the old order, with activity and pocketHold filled from the old tables.</summary>
        public static void Build(out List<FishSpecies> fish, out List<StageDef> stages)
        {
            Fish.Clear();
            Stages.Clear();
            BuildFish();
            BuildStages();
            foreach (var f in Fish)
            {
                f.activity = table.TryGetValue(f.id, out var a) ? (float[])a.Clone() : null;
                f.pocketHold = HoldChance(f.id);
            }
            fish = new List<FishSpecies>(Fish);
            stages = new List<StageDef>(Stages);
        }

        /// <summary>The old TimeActivity.A (unknown ids: 1).</summary>
        public static float Activity(string speciesId, Period p) =>
            speciesId != null && table.TryGetValue(speciesId, out var a) ? a[(int)p] : 1f;

        // (GameDatabase.BuildFish ended with BuildSets: the backdrops stay in GameDatabase)
        static void BuildSets() { }

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

            Habitats();

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

        static void H(string id, string spec)
        {
            var f = Fish.Find(x => x.id == id);
            if (f == null)
            {
                Debug.LogWarning($"[GameDatabase] habitat for unknown fish '{id}'");
                return;
            }
            f.habitat = HabitatDef.Parse(id, spec, m => Debug.LogWarning("[GameDatabase] " + m));
        }

        /// <summary>
        /// Where the lake's fish live on its generated bed (Docs/terrain_depth_spec.md 7.1): the preferred water depth, the
        /// bed kinds and materials they favour, an edge's pull, their column, how strongly the habitat steers them (beta),
        /// the periods' depth shifts (- shallower: up at dawn and evening, down by day) and per-period kind weights, and
        /// whether a big one runs for the deep. Tuned against the bite budget (spec 8.3: the occupancy estimator over 50
        /// seeds and the three rods, the stock's bites per bait, rig class and period within 0.8..1.25 of today's) and the
        /// spec 14 D7 shifts: the crucian's day on the drop-offs and shoals, the bass's day on the drop-offs and humps
        /// (beta 0.8) and its shallower dawn and evening held to -0.4 m (topwater lures on the long rods are the budget's
        /// tightest). Only the lake has a bed: the other stages never read these.
        /// </summary>
        static void Habitats()
        {
            H("crucian_carp", "depth:1.2-4.0,flat:1.5,shoal:1.5,shelf:1.2,dropoff:1.3,hump:0.9,channel:0.8,hole:0.6,basin:0.5,weed:1.4,mud:1.2,sand:1.0,gravel:0.8,edge:0.4,col:bottom,beta:0.65,@dawn:-0.8,@day:1.0,@evening:-0.8,@night:-0.5,@night.flat:1.2,@day.dropoff:1.6,@day.shoal:1.5");
            H("bluegill", "depth:0.8-3.0,shelf:1.8,flat:1.6,shoal:1.5,dropoff:1.0,channel:0.7,hole:0.5,basin:0.4,sand:1.3,gravel:1.2,weed:1.3,mud:0.8,edge:0.3,col:mid,beta:0.75,@day:0.5,@night:1.0,@night.dropoff:1.5,@night.basin:0.6,@day.shelf:1.2,@day.shoal:1.2");
            H("carp", "depth:3.0-7.0,hole:1.5,channel:1.3,basin:1.3,dropoff:1.2,shoal:0.9,flat:0.7,shelf:0.5,mud:1.4,weed:1.1,gravel:0.8,edge:0.2,col:bottom,beta:0.65,@dawn:-1.0,@day:0.5,@evening:-1.0,@night:-2.0,@night.flat:3.0,@night.shoal:2.6,@night.dropoff:0.8,@day.hole:1.5,@day.channel:1.5,@day.dropoff:1.2,@day.flat:0.5,@day.shelf:0.5,runDeep");
            H("largemouth_bass", "depth:1.0-4.5,dropoff:1.8,hump:1.6,shoal:1.5,flat:1.2,shelf:1.1,channel:0.9,hole:0.8,basin:0.5,gravel:1.3,weed:1.2,mud:0.8,edge:0.5,col:mid,beta:0.8,@dawn:-0.4,@day:1.5,@evening:-0.4,@night:-0.3,@dawn.flat:1.8,@dawn.shelf:1.6,@dawn.dropoff:0.8,@day.hump:1.8,@day.dropoff:1.5,@day.shoal:1.3,@day.flat:0.8,runDeep");
            H("golden_carp", "runDeep");   // (only its hooked fight's deep runs are used)
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

        static EncounterDef GoldenCarp()
        {
            var e = LegendEncounters.Make("golden_carp");
            e.keyLures["bait_golden"] = 1.0f;
            e.keyLures["bait_corn"] = 0.5f;
            e.keyLures["bait_softworm"] = 0.4f;
            return e;
        }

        static EncounterDef Arapaima()
        {
            var e = LegendEncounters.Make("arapaima");
            e.keyLures["bait_frog"] = 1.0f;
            e.keyLures["bait_popper"] = 0.8f;
            return e;
        }

        static EncounterDef Sturgeon()
        {
            var e = LegendEncounters.Make("sturgeon");
            e.keyLures["bait_softworm"] = 1.0f;
            e.keyLures["bait_jig"] = 0.5f;
            return e;
        }

        static EncounterDef BlueMarlin()
        {
            var e = LegendEncounters.Make("blue_marlin");
            e.keyLures["bait_kona"] = 1.0f;
            e.keyLures["bait_jig"] = 0.5f;
            return e;
        }

        static EncounterDef GreatWhite()
        {
            var e = LegendEncounters.Make("great_white");
            e.keyLures["bait_kona"] = 1.0f;
            e.keyLures["bait_jig"] = 0.6f;
            return e;
        }

        static EncounterDef Coelacanth()
        {
            var e = LegendEncounters.Make("coelacanth");
            e.keyLures["bait_egi"] = 1.0f;
            e.keyLures["bait_softworm"] = 0.6f;
            e.keyLures["bait_jig"] = 0.4f;
            return e;
        }

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

        // ---- TimeActivity's table
        static readonly Dictionary<string, float[]> table = new Dictionary<string, float[]>
        {
            // lake
            { "crucian_carp", new[] { 1.3f, 0.8f, 1.3f, 0.9f } },
            { "bluegill", new[] { 1.0f, 1.3f, 1.0f, 0.4f } },
            { "carp", new[] { 1.2f, 0.7f, 1.2f, 1.4f } },
            { "largemouth_bass", new[] { 1.5f, 0.6f, 1.5f, 0.7f } },
            { "golden_carp", new[] { 1.4f, 0.7f, 1.4f, 1.0f } },
            // stream
            { "pale_chub", new[] { 1.0f, 1.3f, 1.0f, 0.3f } },
            { "cherry_salmon", new[] { 1.5f, 0.8f, 1.4f, 0.4f } },
            { "rainbow_trout", new[] { 1.4f, 0.8f, 1.4f, 0.6f } },
            { "mandarin_fish", new[] { 0f, 0f, 0f, 1.8f } },
            { "lenok", new[] { 1.6f, 0.5f, 1.6f, 0.8f } },
            // sea
            { "horse_mackerel", new[] { 1.3f, 1.0f, 1.3f, 1.1f } },
            { "mackerel", new[] { 1.5f, 1.1f, 1.4f, 0.4f } },
            { "rockfish", new[] { 1.0f, 0.6f, 1.2f, 1.6f } },
            { "flounder", new[] { 1.3f, 0.9f, 1.2f, 0.8f } },
            { "black_porgy", new[] { 1.4f, 0.6f, 1.3f, 1.3f } },
            { "red_seabream", new[] { 1.6f, 0.9f, 1.3f, 0.5f } },
            // swamp
            { "piranha", new[] { 0.9f, 1.4f, 1.0f, 0.5f } },
            { "catfish", new[] { 0f, 0f, 0f, 2.0f } },
            { "snakehead", new[] { 1.5f, 0.8f, 1.5f, 0.6f } },
            { "arowana", new[] { 1.2f, 1.3f, 1.0f, 0.3f } },
            { "arapaima", new[] { 1.3f, 1.1f, 1.0f, 0.6f } },
            // ice
            { "smelt", new[] { 1.3f, 1.2f, 1.0f, 0.5f } },
            { "burbot", new[] { 0f, 0f, 0f, 2.0f } },
            { "northern_pike", new[] { 1.4f, 1.0f, 1.3f, 0.3f } },
            { "arctic_char", new[] { 1.3f, 0.7f, 1.3f, 0.9f } },
            { "sturgeon", new[] { 0.8f, 0.6f, 1.0f, 1.6f } },
            // ocean
            { "yellowtail", new[] { 1.5f, 1.0f, 1.2f, 0.4f } },
            { "mahi_mahi", new[] { 1.1f, 1.4f, 1.0f, 0.3f } },
            { "bluefin_tuna", new[] { 1.6f, 0.8f, 1.3f, 0.5f } },
            { "ocean_sunfish", new[] { 0.6f, 1.8f, 0.6f, 0f } },
            { "blue_marlin", new[] { 1.3f, 1.2f, 0.9f, 0.4f } },
            { "great_white", new[] { 1.1f, 0.6f, 1.4f, 1.5f } },
            // cave
            { "cave_tetra", new[] { 1.0f, 1.0f, 1.0f, 1.0f } },
            { "crystal_koi", new[] { 1.0f, 1.3f, 1.0f, 0.8f } },
            { "anglerfish", new[] { 1.0f, 0.8f, 1.0f, 1.4f } },
            { "coelacanth", new[] { 1.0f, 0.8f, 1.0f, 1.4f } },
        };

        // ---- FishAgent.HoldChance
        static float HoldChance(string id) => id switch
        {
            "mandarin_fish" => 0.8f,
            "lenok" => 0.5f,
            "rainbow_trout" => 0.5f,
            "cherry_salmon" => 0.4f,
            "pale_chub" => 0.1f,
            _ => 0f,
        };
    }
}
