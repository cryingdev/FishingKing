using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// A food sold in the shop's 사료 section (수조 tab). Pellets come in bags: a bag holds <see cref="portions"/> portions,
    /// one portion is one fish-meal (0 -> 100 % fullness, <see cref="AquaCare.PelletsPerPortion"/> pellets poured). Live
    /// food (<see cref="live"/>: 생새우, 정어리) comes by the piece in a container on the ledge and is given one piece at a
    /// time; a piece fills <see cref="fill"/> of a stomach. Only fish whose diet has its <see cref="kind"/> eat it.
    /// </summary>
    public class FeedDef
    {
        public string id, name, desc;
        public string key;          // sprite key: World/feed_<key>_*, Items/feed_<key>
        public int price;           // coins per purchase
        public int bagsPerBuy;      // sealed bags per purchase
        public int portions;        // portions per bag
        public float fullHours;     // a full stomach of it lasts this long (100 % -> 0 %)
        public float growth;        // x the growth rate while fed on it
        public bool premium;
        public Color food;          // crumb / dissolve tint
        public Diet kind = Diet.Pellet; // the diet a fish needs to eat it
        public string who;          // shop: who eats it
        // ---- live food
        public bool live;           // bought by the piece, given one piece at a time from its container
        public int piecesPerBuy;    // pieces per purchase
        public float fill;          // fullness per piece
        public string box;          // the container's sprite key: World/feed_<box>_*
    }

    /// <summary>
    /// The food a player owns: sealed bags and the portions left in the opened one (0 = no bag open); for live food the
    /// pieces left and whether its container's lid is open. (Fields missing in an older save read as 0 / false.)
    /// </summary>
    [Serializable]
    public class FeedStock
    {
        public string id;
        public int bags;            // sealed bags (the one standing on the ledge included while no bag is open)
        public float portions;      // portions left in the open bag
        public int pieces;          // live food: shrimp / sardines left
        public bool open;           // live food: the container's lid is open
    }

    /// <summary>
    /// Kept fish care on real time (offline included): fullness drains (basic 12 h, premium 18 h from full to empty),
    /// a fed fish (fullness &gt;= 25 %) accrues fed days and grows at most +8 % length
    /// (size = base x (1 + 0.08 (1 - e^(-fedDays / 2.3))), premium x1.25 rate, never above the species max), weight
    /// ~ length^3, value by the catch-time formula at the grown size. Viewing income per fish = max(1, 0.003 x rarity
    /// weight x value) coins/min, x1.2 fed / x0.7 hungry, banked piecewise so feeding mid-window is exact and capped
    /// at 12 h after the last collect (as before). Each species eats only the foods of its diet (FishSpecies.diet:
    /// pellets, live shrimp, sardines; GameDatabase.Diets); a shrimp fills 40 %, a sardine 100 %, both digest like the
    /// basic feed. The tank's decorations and dirt (<see cref="AquaTank"/>) multiply the income (+decor, -dirt: banked at
    /// the span's average dirt). Test switches: -fkaquahours &lt;h&gt; fast-forwards the tank at boot (-fkaquafeed
    /// basic|premium|shrimp|sardine keeps it fed meanwhile), -fkaqualog logs [AQUA] lines, -fkaquadirt &lt;0..1&gt; sets
    /// the tank's dirt.
    /// </summary>
    public static class AquaCare
    {
        // ------------------------------------------------------------------ constants
        public const float HungryAt = 0.25f;          // below: hungry (no growth, slower, -30 % income)
        public const float FullAt = 0.95f;            // at or above: full (does not eat, counts as 배불러요)
        public const float StartFullness = 0.6f;      // a fish just put in the tank (or migrated from an old save)
        public const float GrowthCap = 0.08f;         // +8 % length at most
        public const float GrowthTau = 2.3f;          // fed days
        public const float PelletFill = 0.2f;         // fullness per pellet eaten
        public const int PelletsPerPortion = 5;       // 1 portion = 1.0 fullness
        public const float FedMod = 1.2f, HungryMod = 0.7f;
        public const float IncomeK = 0.003f;          // coins/min per (rarity weight x value)
        public const float IncomeFloor = 1f;          // coins/min, before the fed / hungry modifier
        static readonly float[] RarityW = { 1f, 1.3f, 1.7f, 2.2f, 3f };
        public const long IncomeWindow = 12 * 3600;   // income accrues up to 12 h after the last collect
        public const long MaxCatchUp = 7 * 86400;     // one catch-up covers at most 7 days (hunger bottoms out in 18 h anyway)
        const float OldIncomeK = 0.006f;              // the pre-feed income (value x 0.006, at least 1)

        public static readonly List<FeedDef> Feeds = new List<FeedDef>
        {
            new FeedDef
            {
                id = "feed_basic", key = "basic", name = "기본 사료", price = 200, bagsPerBuy = 2, portions = 20, fullHours = 12f, growth = 1f,
                food = new Color32(0xa0, 0x6a, 0x3a, 0xff),
                desc = "갈색 알갱이 사료. 봉투를 뜯어 수조 위에서 뿌려요.",
                who = "작은 물고기와 잡식성 (붕어·잉어·송어)",
            },
            new FeedDef
            {
                id = "feed_premium", key = "premium", name = "고급 사료", price = 450, bagsPerBuy = 1, portions = 20, fullHours = 18f, growth = 1.25f,
                premium = true, food = new Color32(0xff, 0xcc, 0x40, 0xff),
                desc = "황금 플레이크. 더 오래 배부르고, 25% 더 빨리 자라요.",
                who = "작은 물고기와 잡식성 (붕어·잉어·송어)",
            },
        };

        /// <summary>
        /// The live food: shrimp in a tub, sardines in a cooler, given one at a time. They digest like the basic feed
        /// (12 h, growth x1: the food mix model only tells premium flakes apart). Priced against the viewing income of the
        /// fish that eat them: a fed fish earns x1.2 instead of x0.7 for ~9 h per full meal, i.e. +270 x its base income
        /// (coins/min) per meal. A shrimp (40 %, 2.5 a meal = 75 coins) still pays off threefold for the weakest shrimp
        /// eaters (bass, snakehead, burbot: base 1/min); a sardine (one big meal, 250 coins) is ~8 % of that gain for the
        /// cheapest sardine eater (bluefin tuna, base ~11/min) and far less for the legends (arapaima ~46/min). Pellets
        /// stay the cheap meal (5 coins basic).
        /// </summary>
        public static readonly List<FeedDef> Live = new List<FeedDef>
        {
            new FeedDef
            {
                id = "feed_shrimp", key = "shrimp", box = "tub", name = "생새우", kind = Diet.Shrimp, live = true,
                price = 300, piecesPerBuy = 10, fill = 0.4f, fullHours = 12f, growth = 1f,
                food = new Color32(0xf7, 0xa0, 0x7c, 0xff),
                desc = "통 뚜껑을 열고 한 마리씩 집어서 수조에 넣어요.",
                who = "중형 육식어와 바닥 물고기 (배스·쏘가리·메기)",
            },
            new FeedDef
            {
                id = "feed_sardine", key = "sardine", box = "cooler", name = "정어리", kind = Diet.Sardine, live = true,
                price = 1500, piecesPerBuy = 6, fill = 1f, fullHours = 12f, growth = 1f,
                food = new Color32(0xc3, 0xd0, 0xda, 0xff),
                desc = "아이스박스를 열고 한 마리씩 집어서 수조에 넣어요.",
                who = "대형·바다 포식자 (피라루쿠·청새치·상어)",
            },
        };

        /// <summary>Every food in shop order: the pellet bags, then the live food.</summary>
        public static IEnumerable<FeedDef> All => Feeds.Concat(Live);

        public static FeedDef Feed(string id) => All.FirstOrDefault(f => f.id == id);
        static FeedDef Basic => Feeds[0];
        static FeedDef Premium => Feeds[1];

        // ------------------------------------------------------------------ diets
        public static readonly Diet[] Kinds = { Diet.Pellet, Diet.Shrimp, Diet.Sardine };

        public static Diet DietOf(CaughtFish f)
        {
            var sp = f?.Species;
            return sp != null && sp.diet != Diet.None ? sp.diet : Diet.Pellet;
        }

        public static bool Eats(CaughtFish f, Diet kind) => (DietOf(f) & kind) != 0;

        public static string KindName(Diet k) => k == Diet.Shrimp ? "생새우" : k == Diet.Sardine ? "정어리" : "사료";

        /// <summary>The Sprites/UI icon of a food (the info window, the hungry fish's thought bubble).</summary>
        public static string KindIcon(Diet k) => k == Diet.Shrimp ? "icon_diet_shrimp" : k == Diet.Sardine ? "icon_diet_sardine" : "icon_diet_pellet";

        public static IEnumerable<Diet> KindsOf(Diet diet) => Kinds.Where(k => (diet & k) != 0);

        /// <summary>"정어리", "사료와 생새우" (every food name ends in a vowel: 와 / 를 fit all of them).</summary>
        public static string DietText(Diet diet) => string.Join("와 ", KindsOf(diet).Select(KindName));

        /// <summary>"이 물고기는 정어리를 좋아해요".</summary>
        public static string LikesText(CaughtFish f) => $"이 물고기는 {DietText(DietOf(f))}를 좋아해요";

        /// <summary>The live food of a kind (null for pellets).</summary>
        public static FeedDef LiveOf(Diet k) => Live.FirstOrDefault(f => f.kind == k);

        /// <summary>The player has some of this food: a pellet bag, or a shrimp / sardine left.</summary>
        public static bool Owns(Diet k)
        {
            if (k == Diet.Pellet) return AnyFeed;
            var f = LiveOf(k);
            return f != null && Stock(f.id).pieces > 0;
        }

        /// <summary>The player has some food this fish eats.</summary>
        public static bool OwnsFoodFor(CaughtFish f) => KindsOf(DietOf(f)).Any(Owns);

        /// <summary>The food of that kind the fast-forward feeds it (basic for pellets).</summary>
        static FeedDef FoodOf(Diet k) => k == Diet.Pellet ? Basic : LiveOf(k);

        /// <summary>Logs every species' diet (the -fkaqua runs).</summary>
        public static void LogDiets()
        {
            foreach (var sp in GameDatabase.Fish)
                Log($"diet {sp.id} {sp.name} ({sp.rarity}, {sp.minCm:0}-{sp.maxCm:0}cm): {DietText(sp.diet)} [{sp.feedStyle}]");
            foreach (var k in Kinds)
                Log($"diet {KindName(k)}: {GameDatabase.Fish.Count(sp => (sp.diet & k) != 0)} species");
        }

        public static bool LogOn;
        public static void Log(string m)
        {
            if (LogOn) Debug.Log("[AQUA] " + m);
        }

        // ------------------------------------------------------------------ per fish
        public static float RarityWeight(Rarity r) => RarityW[Mathf.Clamp((int)r, 0, RarityW.Length - 1)];

        public static bool Hungry(CaughtFish f) => f.fullness < HungryAt;
        public static bool Full(CaughtFish f) => f.fullness >= FullAt;

        /// <summary>Viewing income before the fed / hungry modifier (coins/min).</summary>
        public static float BaseIncome(CaughtFish f)
        {
            var sp = f.Species;
            return Mathf.Max(IncomeFloor, IncomeK * (sp != null ? RarityWeight(sp.rarity) : 1f) * f.value);
        }

        public static float Mod(CaughtFish f) => Hungry(f) ? HungryMod : FedMod;

        /// <summary>Viewing income now (coins/min).</summary>
        public static float IncomeF(CaughtFish f) => BaseIncome(f) * Mod(f);

        /// <summary>The pre-feed income of a fish (for the calibration log and the old-save migration).</summary>
        public static int OldIncome(CaughtFish f) => Mathf.Max(1, Mathf.RoundToInt(f.value * OldIncomeK));

        /// <summary>Hours a full stomach lasts for this fish's food mix (basic 12 h .. premium 18 h).</summary>
        public static float FullHours(CaughtFish f) => Mathf.Lerp(Basic.fullHours, Premium.fullHours, f.premiumShare);

        public static float GrowthRate(CaughtFish f) => Mathf.Lerp(Basic.growth, Premium.growth, f.premiumShare);

        static double DrainPerSec(CaughtFish f) => 1.0 / (FullHours(f) * 3600.0);

        /// <summary>Hours until it gets hungry (0 = hungry now).</summary>
        public static float HoursToHungry(CaughtFish f) => Mathf.Max(0f, (f.fullness - HungryAt) * FullHours(f));

        /// <summary>Growth so far (0 .. 0.08) from the fed days, before the species cap.</summary>
        public static float GrowthOf(float fedDays) => GrowthCap * (1f - Mathf.Exp(-Mathf.Max(0f, fedDays) / GrowthTau));

        /// <summary>The grown length (cm, unrounded), capped at the species max.</summary>
        public static float GrownCm(CaughtFish f)
        {
            if (f.baseCm <= 0f) return f.sizeCm;
            float cm = f.baseCm * (1f + GrowthOf(f.fedDays));
            var sp = f.Species;
            if (sp != null) cm = Mathf.Min(cm, Mathf.Max(f.baseCm, sp.maxCm));
            return cm;
        }

        /// <summary>Grown / caught length (1 .. 1.08): the tank sprite's scale.</summary>
        public static float SizeRatio(CaughtFish f) => f.baseCm > 0f ? GrownCm(f) / f.baseCm : 1f;

        static void ApplyGrowth(CaughtFish f)
        {
            if (f.baseCm <= 0f) return;
            float cm = GrownCm(f);
            float r = cm / f.baseCm;
            f.sizeCm = Mathf.Round(cm * 10f) / 10f;
            f.weightKg = Mathf.Round(f.baseKg * r * r * r * 100f) / 100f;
            var sp = f.Species;
            if (sp != null)
            {
                // the catch-time formula at the grown size, kept in proportion to what the fish was worth when caught
                float p0 = Mathf.Max(1, sp.Price(f.baseCm));
                f.value = Mathf.Max(1, Mathf.RoundToInt(f.baseValue * sp.Price(cm) / p0));
            }
        }

        /// <summary>Seconds of <paramref name="span"/> from now on during which the fish stays fed (fullness &gt;= 25 %).</summary>
        static double FedSeconds(CaughtFish f, double span)
        {
            if (f.fullness < HungryAt) return 0;
            return Math.Min(span, (f.fullness - HungryAt) / DrainPerSec(f));
        }

        /// <summary>One stretch of real time: fullness drains, fed time adds fed days (the growth follows).</summary>
        static void Step(CaughtFish f, double span)
        {
            if (span <= 0) return;
            double fed = FedSeconds(f, span);
            f.fedDays += (float)(fed / 86400.0 * GrowthRate(f));
            f.fullness = Mathf.Max(0f, (float)(f.fullness - DrainPerSec(f) * span));
            ApplyGrowth(f);
        }

        /// <summary>Income of one fish over [from, from + span], cut at the 12 h window after the last collect.</summary>
        static double IncomeOver(SaveData d, CaughtFish f, long from, long span)
        {
            long capEnd = d.aquariumCollectedAt + IncomeWindow;
            double T = Math.Min(span, capEnd - from);
            if (T <= 0) return 0;
            double fed = FedSeconds(f, T);
            return BaseIncome(f) / 60.0 * (FedMod * fed + HungryMod * (T - fed)) * AquaTank.AvgMult(d, f, from, from + (long)T);
        }

        static void Bank(SaveData d, double coins)
        {
            if (coins <= 0) return;
            d.aquaCarry += coins;
            double whole = Math.Floor(d.aquaCarry);
            if (whole >= 1)
            {
                d.aquariumBank += (int)whole;
                d.aquaCarry -= whole;
            }
        }

        // ------------------------------------------------------------------ setup / migration
        static void Init(CaughtFish f, long now, long addedAt)
        {
            f.baseCm = f.sizeCm;
            f.baseKg = f.weightKg;
            f.baseValue = f.value;
            f.addedAt = addedAt;
            f.careAt = now;
            f.fullness = StartFullness;
            f.fedDays = 0f;
            f.premiumShare = 0f;
        }

        /// <summary>A fish just put in the tank.</summary>
        public static void OnAdded(CaughtFish f)
        {
            long now = SaveSystem.Now;
            Init(f, now, now);
            Log($"added {f.speciesId} {f.sizeCm:0.0}cm value {f.value} fullness {f.fullness:0.00}");
        }

        /// <summary>
        /// Old saves (before feeding): the income pending by the old formula is banked (the 12 h window keeps its start),
        /// every kept fish's current size / weight / value become its base, it starts at 60 % fullness from now.
        /// Also fills in any fish without a base (put in by an older code path).
        /// </summary>
        public static void Ensure(SaveData d)
        {
            if (d == null) return;
            d.feed ??= new List<FeedStock>();
            d.aquarium ??= new List<CaughtFish>();
            AquaTank.Ensure(d);
            long now = SaveSystem.Now;
            if (d.aquaVer < 1)
            {
                long secs = Math.Min(IncomeWindow, Math.Max(0, now - d.aquariumCollectedAt));
                int oldRate = d.aquarium.Sum(OldIncome);
                int oldPending = (int)(oldRate * secs / 60);
                int bankBefore = d.aquariumBank;
                d.aquariumBank += oldPending;
                foreach (var f in d.aquarium)
                {
                    Init(f, now, f.caughtAt > 0 ? Math.Min(f.caughtAt, now) : now);
                    Log($"migrate {f.speciesId} base {f.baseCm:0.0}cm {f.baseKg:0.00}kg value {f.baseValue} fullness {f.fullness:0.00}");
                }
                d.aquaVer = 1;
                Log($"migrate save: {d.aquarium.Count} fish, old income {oldRate}/min x {secs / 60} min = {oldPending} banked (bank {bankBefore} -> {d.aquariumBank})");
            }
            foreach (var f in d.aquarium)
                if (f.baseCm <= 0f) Init(f, now, now);
            if (d.capVer < 1)
            {
                // the capacity became 칸 by size class: every kept fish stays; the ones over the tank's limits are only
                // marked (shown as such, and no new fish goes in until there is room)
                d.capVer = 1;
                var t = GameDatabase.TankForLevel(d.tankLevel);
                var over = AquaTank.Over(d);
                Log($"migrate capacity: {d.aquarium.Count} fish, {AquaTank.Used(d)}/{t.capacity}칸 in {t.name} (up to {AquaTank.ClassNames[t.maxClass]}), over {over.Count}: "
                    + string.Join(", ", over.Select(f => $"{f.speciesId} {f.sizeCm:0}cm {AquaTank.ClassNames[AquaTank.ClassOf(f)]}")));
            }
        }

        // ------------------------------------------------------------------ real time
        /// <summary>
        /// Brings every kept fish up to <paramref name="now"/>, banking its viewing income on the way (at the tank's dirt
        /// over that span), then the tank's dirt.
        /// </summary>
        public static void Advance(SaveData d, long now)
        {
            Ensure(d);
            foreach (var f in d.aquarium) AdvanceFish(d, f, now);
            AquaTank.Advance(d, now);
        }

        static void AdvanceFish(SaveData d, CaughtFish f, long now)
        {
            if (f.careAt <= 0) f.careAt = now;
            long span = now - f.careAt;
            if (span <= 0)
            {
                if (span < 0) f.careAt = now; // the clock was set back: resync, nothing happens
                return;
            }
            span = Math.Min(span, MaxCatchUp);
            Bank(d, IncomeOver(d, f, f.careAt, span));
            Step(f, span);
            f.careAt = now;
        }

        /// <summary>Viewing income waiting to be collected (banked + accrued since each fish's last update).</summary>
        public static int Pending(SaveData d)
        {
            Ensure(d);
            long now = SaveSystem.Now;
            double sum = d.aquariumBank + d.aquaCarry;
            foreach (var f in d.aquarium)
            {
                long span = now - f.careAt;
                if (span > 0) sum += IncomeOver(d, f, f.careAt, Math.Min(span, MaxCatchUp));
            }
            return (int)Math.Floor(sum + 1e-6);
        }

        /// <summary>
        /// A pellet (or a live piece) eaten: the fish is brought up to now, then fills by 20 % (a shrimp 40 %, a sardine
        /// 100 %; the food mix follows the feed).
        /// </summary>
        public static void Eat(SaveData d, CaughtFish f, FeedDef feed)
        {
            AdvanceFish(d, f, SaveSystem.Now);
            float nf = Mathf.Min(1f, f.fullness + (feed != null && feed.live ? feed.fill : PelletFill));
            float add = nf - f.fullness;
            if (add <= 0f) return;
            f.premiumShare = Mathf.Clamp01((f.premiumShare * f.fullness + (feed != null && feed.premium ? add : 0f)) / nf);
            f.fullness = nf;
        }

        static void TopUp(CaughtFish f, FeedDef feed)
        {
            float add = 1f - f.fullness;
            if (add <= 0f) return;
            f.premiumShare = Mathf.Clamp01(f.premiumShare * f.fullness + (feed.premium ? add : 0f));
            f.fullness = 1f;
        }

        /// <summary>
        /// Test fast-forward: the tank lives <paramref name="hours"/> more hours right now (timestamps moved back, the
        /// span walked in half-hour steps, income banked within its 12 h window); <paramref name="keepFed"/> tops every
        /// fish up to full with that feed at each step, or with a food it eats when it does not eat that one (null =
        /// nobody feeds them).
        /// </summary>
        public static void FastForward(SaveData d, float hours, FeedDef keepFed)
        {
            Ensure(d);
            long now = SaveSystem.Now;
            Advance(d, now);
            long span = (long)(hours * 3600.0);
            if (span <= 0) return;
            d.aquariumCollectedAt -= span;
            AquaTank.Shift(d, span); // (the tank's dirt lives the span too, after the fish)
            const long StepSec = 1800;
            foreach (var f in d.aquarium)
            {
                f.addedAt -= span;
                long t = now - span;
                var food = keepFed == null || Eats(f, keepFed.kind) ? keepFed : FoodOf(KindsOf(DietOf(f)).First());
                while (t < now)
                {
                    long step = Math.Min(StepSec, now - t);
                    if (food != null) TopUp(f, food);
                    Bank(d, IncomeOver(d, f, t, step));
                    Step(f, step);
                    t += step;
                }
                f.careAt = now;
            }
            AquaTank.Advance(d, now);
            Log($"fast-forward {hours:0.#} h, {(keepFed != null ? "fed " + keepFed.key : "no feeding")}: pending {Pending(d)}");
            Log("  tank " + AquaTank.Describe(d));
            foreach (var f in d.aquarium) Log("  " + Describe(f));
        }

        public static string Describe(CaughtFish f) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} fedDays {1:0.000} growth {2:+0.00}% size {3:0.00}cm (base {4:0.0}) kg {5:0.00} (base {6:0.00}) value {7} (base {8}, {9:+0.0}%) fullness {10:0.00} premium {11:0.00} income {12:0.00}/min (base {13:0.00}, x{14})",
                f.speciesId, f.fedDays, (SizeRatio(f) - 1f) * 100f, GrownCm(f), f.baseCm, f.weightKg, f.baseKg, f.value, f.baseValue,
                f.baseValue > 0 ? (f.value / (float)f.baseValue - 1f) * 100f : 0f, f.fullness, f.premiumShare, IncomeF(f), BaseIncome(f), Mod(f));

        // ------------------------------------------------------------------ feed inventory
        public static FeedStock Stock(string id)
        {
            var d = Game.Data;
            d.feed ??= new List<FeedStock>();
            var s = d.feed.FirstOrDefault(x => x.id == id);
            if (s == null) d.feed.Add(s = new FeedStock { id = id });
            return s;
        }

        /// <summary>Bags owned: the sealed ones plus the open one.</summary>
        public static int Bags(FeedDef f)
        {
            var s = Stock(f.id);
            return s.bags + (s.portions > 0.001f ? 1 : 0);
        }

        public static bool AnyFeed => Feeds.Any(f => Bags(f) > 0);

        /// <summary>Shrimp / sardines left.</summary>
        public static int Pieces(FeedDef f) => Stock(f.id).pieces;

        /// <summary>Any food at all: a pellet bag, a shrimp or a sardine.</summary>
        public static bool AnyFood => AnyFeed || Live.Any(f => Pieces(f) > 0);

        /// <summary>Buys one pack (<see cref="FeedDef.bagsPerBuy"/> sealed bags, or <see cref="FeedDef.piecesPerBuy"/> pieces).</summary>
        public static bool Buy(FeedDef f)
        {
            if (Game.I.Coins < f.price) return false;
            if (f.live) Stock(f.id).pieces += f.piecesPerBuy;
            else Stock(f.id).bags += f.bagsPerBuy;
            Game.I.Spend(f.price); // (saves and notifies)
            Log(f.live ? $"buy {f.id}: pieces {Stock(f.id).pieces}, coins {Game.I.Coins}"
                : $"buy {f.id}: bags {Stock(f.id).bags}, portions {Stock(f.id).portions:0.0}, coins {Game.I.Coins}");
            return true;
        }

        /// <summary>A sealed bag torn open.</summary>
        public static void Open(FeedDef f)
        {
            var s = Stock(f.id);
            if (s.bags <= 0) return;
            s.bags--;
            s.portions = f.portions;
            Game.I.Save();
            Log($"open {f.id}: sealed {s.bags}, portions {s.portions:0.0}");
        }

        // ------------------------------------------------------------------ calibration log
        /// <summary>Logs the viewing income of a sample tank by the old formula and the new one (base / fed / hungry).</summary>
        public static void LogCalibration()
        {
            (string id, float cm)[] tank =
            {
                ("crucian_carp", 25f), ("bluegill", 18f), ("carp", 60f), ("largemouth_bass", 40f),
                ("rainbow_trout", 45f), ("mandarin_fish", 35f), ("black_porgy", 42f), ("lenok", 50f),
            };
            (string id, float cm)[] late = { ("red_seabream", 70f), ("golden_carp", 75f) };
            void One(string name, (string id, float cm)[] list)
            {
                int old = 0;
                float nb = 0f;
                foreach (var (id, cm) in list)
                {
                    var sp = GameDatabase.GetFish(id);
                    if (sp == null) continue;
                    var f = new CaughtFish { speciesId = id, sizeCm = cm, value = sp.Price(cm) };
                    old += OldIncome(f);
                    nb += BaseIncome(f);
                    Log($"calib {name} {id} {cm:0}cm value {f.value} rarity {sp.rarity}: old {OldIncome(f)}/min, new base {BaseIncome(f):0.00} fed {BaseIncome(f) * FedMod:0.00} hungry {BaseIncome(f) * HungryMod:0.00}");
                }
                Log($"calib {name}: old {old}/min -> new base {nb:0.0}/min, fed {nb * FedMod:0.0}/min, hungry {nb * HungryMod:0.0}/min");
            }
            One("tank8", tank);
            One("tank4", tank.Take(4).ToArray());
            One("late", late);
        }

        // ------------------------------------------------------------------ boot
        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Game.I == null) return;
            LogOn = Array.IndexOf(Environment.GetCommandLineArgs(), "-fkaqualog") >= 0 || Arg("-fkaqua") != null;
            var go = new GameObject("[AquaCare]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AquaCareTicker>();
        }

        /// <summary>The test switches, run on the first frame (after Game's own boot switches such as -fkfresh).</summary>
        internal static void DebugStart()
        {
            var d = Game.Data;
            Ensure(d);
            Advance(d, SaveSystem.Now);
            if (float.TryParse(Arg("-fkaquahours"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h) && h > 0f)
            {
                string fed = Arg("-fkaquafeed"); // basic | premium | shrimp | sardine
                FastForward(d, h, fed != null ? Feed("feed_" + fed) : null);
            }
            if (float.TryParse(Arg("-fkaquadirt"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dirt))
                AquaTank.SetAll(dirt);
            Game.I.Save();
        }
    }

    /// <summary>Keeps the tank on real time while the game runs (every 30 s, and when the app is paused).</summary>
    public class AquaCareTicker : MonoBehaviour
    {
        float t;

        void Start() => AquaCare.DebugStart();

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (t < 30f) return;
            t = 0f;
            if (Game.I == null) return;
            AquaCare.Advance(Game.Data, SaveSystem.Now);
            AquaTank.Commit(); // (the dirt maps into the save strings)
        }

        void OnApplicationPause(bool pause)
        {
            if (!pause || Game.I == null) return;
            AquaCare.Advance(Game.Data, SaveSystem.Now);
            AquaTank.Commit();
            Game.I.Save();
        }

        void OnApplicationQuit()
        {
            if (Game.I == null) return;
            AquaTank.Commit();
            Game.I.Save();
        }
    }
}
