using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// Aquarium feeding test autopilot (its own switch, beside the fishing <see cref="AutoPilot"/>):
    /// <code>
    /// -fkaqua feed      buys both feeds in the shop, taps a sealed bag (hint), swipes off the line (nothing), swipes
    ///                   along the perforation (tears it open), drags the open bag over the tank and shakes it (pellets,
    ///                   eating, 배불러요!), 2 days hungry (no growth, lower income), a probe fish fed 1 / 3 / 7 / 10 days
    ///                   (growth), sells it at the grown price, empties a bag (folds, the next one sealed); shots into
    ///                   -fkshots &lt;dir&gt;, [AQUA] CHECK lines in the log. Quits when done.
    /// -fkaqua migrate   logs the migrated tank of an old save (run it with -fksave on a pre-feed save), then shoots the tank.
    /// -fkaqua live      the live food: logs every species' diet, the empty tub / cooler slots (tap: the shop), buys
    ///                   shrimp and sardines, opens the lids, picks a shrimp and drops it over a bass (it grabs it, the
    ///                   others ignore it), a sardine over an arapaima (it surges up and gulps it: 배불러요!), a sardine
    ///                   nobody wants (it sinks and dissolves, no refund), a shrimp let go off the tank (back in the tub),
    ///                   pellets over a tank of big fish (ignored, "이 물고기는 정어리를 좋아해요"), the info window of a
    ///                   hungry fish without its food (the 상점 button), the lids shut and the saved state.
    /// -fkaqua clean     the tank's dirt and the hand cleaning (AquaClean): a new / old save's tank state, the real-time
    ///                   rates for 6 fish over 24 h, the income penalty, the 7-day catch-up cap, uneaten food on the
    ///                   gravel; a filthy tank (algae, debris, murk), a tap on the sponge (hint), wiping the left half of
    ///                   the glass (half clean, the wiped pixels gone), buying the net and the siphon (청소), scooping
    ///                   every bit with the net, siphoning the gravel end to end, wiping the rest: 반짝반짝!; the maps'
    ///                   run-length save round trip.
    /// -fkaqua decor     the decorations (AquaDecor): slots by tank level, buying all of them (장식), the decoration
    ///                   mode (the 꾸미기 button: markers + the storage tray), drags from the tray onto slots, a wrong
    ///                   kind refused, a swap, one dragged off to storage, the bonus cap, the plants' / bubbler's effects,
    ///                   the light themes' tints, the chest opening and the wheel turning, the income breakdown, a long
    ///                   press into the mode, the placement kept over a scene reload and a smaller tank.
    /// -fkaqua tanks     the five tank levels (AquaLayout, AquaPilot.Tanks.cs): size classes and the 칸 capacity, the
    ///                   drawn-size curve, refusals (too big / no room, the catch card's 보관 button), an old save over
    ///                   the limits (kept, marked), the maps resampled to a bigger tank, every level rendered stocked,
    ///                   the shop's 수조 확장 rows and an upgrade bought in the scene, the info window's size class,
    ///                   feeding / live food / cleaning / decorating on the smallest and the largest tank.
    /// </code>
    /// The feed / live / clean scenarios run in the 중형 수조 (tank level 1: the legacy art's coordinates); fish over its
    /// limits are put in the way an older save has them.
    /// The simulated pointer is PointerInput's (the same one the fishing autopilot drives).
    /// </summary>
    public partial class AquaPilot : MonoBehaviour
    {
        string shots;
        int ok, fail;

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string sc = Arg("-fkaqua");
            if (sc == null) return;
            var go = new GameObject("[AquaPilot]");
            DontDestroyOnLoad(go);
            var ap = go.AddComponent<AquaPilot>();
            ap.shots = Arg("-fkshots") ?? Path.Combine(Application.persistentDataPath, "shots_aqua");
            Directory.CreateDirectory(ap.shots);
            ap.StartCoroutine(sc == "migrate" ? ap.Migrate() : sc == "live" ? ap.LiveTest() : sc == "clean" ? ap.CleanTest()
                : sc == "decor" ? ap.DecorTest() : sc == "tanks" ? ap.TanksTest() : ap.FeedTest());
        }

        static void Log(string m) => Debug.Log("[AQUA] " + m);

        void Check(bool pass, string what)
        {
            if (pass) ok++;
            else fail++;
            Log((pass ? "CHECK ok   " : "CHECK FAIL ") + what);
        }

        IEnumerator Shot(string name)
        {
            yield return AutoShot.Frame();
            string p = Path.Combine(shots, name + ".png");
            AutoShot.Save(p);
            var feed = AquaFeed.Current;
            if (feed != null && PixelView.Current != null)
            {
                var b = PixelView.Current.WorldToScreen(feed.BagCentre("feed_basic"));
                Log($"shot {name} basicbag {b.x:0} {Screen.height - b.y:0}");
            }
            else Log("shot " + name);
            yield return null;
        }

        static Vector2 Scr(Vector2 world) => PixelView.Current.WorldToScreen(world);

        static void Down(Vector2 world)
        {
            PointerInput.SimActive = true;
            PointerInput.SimPos = Scr(world);
            PointerInput.SimDown = true;
        }

        static void Up()
        {
            PointerInput.SimDown = false;
        }

        IEnumerator Tap(Vector2 world)
        {
            Down(world);
            yield return null;
            yield return null;
            Up();
            yield return null;
        }

        /// <summary>Moves the pressed pointer from a to b (world) in <paramref name="time"/> s, linearly.</summary>
        IEnumerator Drag(Vector2 a, Vector2 b, float time, float until = 1f)
        {
            float t0 = Time.time;
            while (true)
            {
                float k = Mathf.Clamp01((Time.time - t0) / time);
                PointerInput.SimActive = true;
                PointerInput.SimDown = true;
                PointerInput.SimPos = Scr(Vector2.Lerp(a, b, k));
                if (k >= until) yield break;
                yield return null;
            }
        }

        static void CloseDialogs()
        {
            var d = GameObject.Find("[Dialogs]");
            if (d == null) return;
            for (int i = d.transform.childCount - 1; i >= 0; i--) Destroy(d.transform.GetChild(i).gameObject);
        }

        static CaughtFish AddFish(string id, float cm, string stage)
        {
            var sp = GameDatabase.GetFish(id);
            var cf = Game.I.MakeCatch(sp, cm, stage);
            if (!Game.I.AddToAquarium(cf))
            {
                // over the tank's limits: put in the way an older save has it (kept, marked over)
                Log($"  {id} {cm:0}cm refused ({Game.I.KeepRefusal(cf)}): put in as an older save's fish");
                Game.Data.aquarium.Add(cf);
                AquaCare.OnAdded(cf);
            }
            return cf;
        }

        static float Growth(CaughtFish f) => (AquaCare.SizeRatio(f) - 1f) * 100f;

        // ------------------------------------------------------------------ -fkaqua feed
        IEnumerator FeedTest()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Game.Data;
            d.coins = Math.Max(d.coins, 100000);
            d.tankLevel = 1; // (the 중형 수조: the coordinates below are its art's)
            if (!d.ownedItems.Contains("tank_1")) d.ownedItems.Add("tank_1");
            d.aquarium.Clear();
            // a tank of pellet eaters (the bass and the 쏘가리 of this test's first version eat live shrimp now: -fkaqua live)
            foreach (var (id, cm, st) in new[]
            {
                ("crucian_carp", 25f, "lake"), ("bluegill", 18f, "lake"), ("carp", 60f, "lake"), ("cherry_salmon", 28f, "stream"),
                ("rainbow_trout", 45f, "stream"), ("pale_chub", 14f, "stream"), ("black_porgy", 42f, "sea"),
            }) AddFish(id, cm, st);
            AquaCare.LogCalibration();
            Log($"sample tank now (all at {AquaCare.StartFullness:0.0} fullness = fed): old formula {d.aquarium.Sum(AquaCare.OldIncome)}/min, new {Game.I.IncomePerMinute}/min");

            // ---- shop: buy both feeds
            SceneFlow.Go("Map");
            yield return new WaitForSeconds(1.6f);
            var canvas = GameObject.Find("MapUI").transform;
            ShopUI.TankFeed = true;
            ShopUI.Open(canvas, ItemKind.Tank);
            yield return new WaitForSeconds(0.6f);
            int coins0 = d.coins;
            foreach (var f in AquaCare.Feeds)
            {
                var row = GameObject.Find(f.id);
                var buy = row != null ? row.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Buy") : null;
                if (buy != null) buy.onClick.Invoke();
                else Log("buy button not found: " + f.id);
                yield return new WaitForSeconds(0.4f);
            }
            var basic = AquaCare.Feed("feed_basic");
            var prem = AquaCare.Feed("feed_premium");
            Check(AquaCare.Stock(basic.id).bags == basic.bagsPerBuy && AquaCare.Stock(prem.id).bags == prem.bagsPerBuy,
                $"bought feeds: basic {AquaCare.Stock(basic.id).bags} bags, premium {AquaCare.Stock(prem.id).bags} bags");
            Check(coins0 - d.coins == basic.price + prem.price, $"paid {coins0 - d.coins} (= {basic.price} + {prem.price})");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("shop_feed");
            var shop = GameObject.Find("Shop");
            if (shop != null) Destroy(shop.transform.parent.gameObject);
            yield return null;

            // ---- the aquarium: both bags sealed on the ledge
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.2f);
            var feed = AquaFeed.Current;
            var scene = FindAnyObjectByType<AquariumScene>();
            Check(feed != null && feed.BagStateOf(basic.id) == "Sealed" && feed.BagStateOf(prem.id) == "Sealed",
                $"bags on the ledge: basic {feed?.BagStateOf(basic.id)}, premium {feed?.BagStateOf(prem.id)}");
            for (float w = 0; w < 4f && !feed.ScissorsShown(prem.id); w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.45f);
            yield return Shot("ledge");

            // ---- 2 days hungry: 12 h (they get hungry), then 48 h more
            AquaCare.FastForward(d, 12f, null);
            var before = d.aquarium.Select(f => (f.fedDays, f.sizeCm, f.value, f.fullness)).ToList();
            float fedIncome = d.aquarium.Sum(f => AquaCare.BaseIncome(f) * AquaCare.FedMod);
            AquaCare.FastForward(d, 48f, null);
            bool noGrowth = true;
            for (int i = 0; i < d.aquarium.Count; i++)
                noGrowth &= Mathf.Abs(d.aquarium[i].fedDays - before[i].fedDays) < 1e-4f && Mathf.Abs(d.aquarium[i].sizeCm - before[i].sizeCm) < 1e-4f;
            Check(d.aquarium.All(AquaCare.Hungry), $"hungry after 60 h without food: fullness {string.Join(", ", d.aquarium.Select(f => f.fullness.ToString("0.00")))}");
            Check(noGrowth, "no growth in the 48 h hungry: fedDays / size unchanged");
            float hungryIncome = d.aquarium.Sum(AquaCare.IncomeF);
            Check(Mathf.Abs(hungryIncome / fedIncome - AquaCare.HungryMod / AquaCare.FedMod) < 0.01f,
                $"hungry income {hungryIncome:0.00}/min vs fed {fedIncome:0.00}/min (x{hungryIncome / fedIncome:0.000}, expected x{AquaCare.HungryMod / AquaCare.FedMod:0.000}); panel {Game.I.IncomePerMinute}/min");
            scene.AfterTimeJump();
            yield return new WaitForSeconds(1.4f);
            yield return Shot("hungry");

            // ---- a tap on a sealed bag: the hint; a swipe off the line: nothing
            var c = feed.BagCentre(basic.id);
            yield return Tap(c);
            yield return new WaitForSeconds(0.3f);
            Check(feed.HintShown == "점선을 따라 잘라요", $"tap on the sealed bag -> hint '{feed.HintShown}'");
            yield return new WaitForSeconds(0.4f);
            yield return Drag(c + new Vector2(-16f, -3f) / 16f, c + new Vector2(16f, -3f) / 16f, 0.3f);
            Up();
            yield return new WaitForSeconds(0.3f);
            Check(feed.BagStateOf(basic.id) == "Sealed" && feed.Tears == 0, $"swipe across the middle (off the line): still {feed.BagStateOf(basic.id)}");
            yield return new WaitForSeconds(0.5f);

            // ---- along the perforation (left to right): the scissors follow, it tears open
            var (pa, pb) = feed.Perforation(basic.id);
            var from = pa + new Vector2(-6f, 0f) / 16f;
            var to = pb + new Vector2(6f, 0f) / 16f;
            Down(from);
            yield return null;
            yield return Drag(from, to, 0.55f, 0.5f);
            yield return Shot("cutting");
            float tearWait = 0f;
            // continue from the halfway point to the end
            float t0 = Time.time;
            while (Time.time - t0 < 0.3f)
            {
                PointerInput.SimPos = Scr(Vector2.Lerp(Vector2.Lerp(from, to, 0.5f), to, (Time.time - t0) / 0.3f));
                if (feed.Tears > 0) break;
                yield return null;
            }
            while (feed.Tears == 0 && tearWait < 0.5f)
            {
                tearWait += Time.deltaTime;
                PointerInput.SimPos = Scr(to);
                yield return null;
            }
            yield return new WaitForSeconds(0.1f);
            yield return Shot("torn");
            Up();
            Check(feed.Tears == 1 && feed.BagStateOf(basic.id) == "Open" && Mathf.Approximately(AquaCare.Stock(basic.id).portions, basic.portions),
                $"swipe along the perforation: {feed.BagStateOf(basic.id)}, portions {AquaCare.Stock(basic.id).portions:0.0}, sealed left {AquaCare.Stock(basic.id).bags}");
            yield return new WaitForSeconds(0.8f);

            // ---- drag the open bag up over the tank, shake it: pellets, splashes, the fish eat
            float portions0 = AquaCare.Stock(basic.id).portions;
            Down(c);
            yield return null;
            var over = new Vector2(-3.5f, 6.4f);
            yield return Drag(c, over, 0.8f);
            yield return new WaitForSeconds(0.2f); // (it tips over: tilt, then pour)
            Check(feed.Pouring, $"over the tank: pouring {feed.Pouring}");
            float shake0 = Time.time;
            bool shotPour = false, shotEat = false;
            int eaten0 = feed.Eaten;
            while (Time.time - shake0 < 2.6f)
            {
                float k = Time.time - shake0;
                PointerInput.SimPos = Scr(over + new Vector2(Mathf.Sin(k * Mathf.PI * 2f / 0.55f) * 2.2f + k * 0.6f, Mathf.Sin(k * 9f) * 0.15f));
                if (!shotPour && k > 0.8f)
                {
                    shotPour = true;
                    yield return Shot("pouring");
                }
                if (!shotEat && feed.Eaten > eaten0)
                {
                    shotEat = true;
                    var e = PixelView.Current.WorldToScreen(feed.LastEatAt);
                    Log($"eat at {e.x:0} {Screen.height - e.y:0}");
                    yield return Shot("eating");
                }
                yield return null;
            }
            Log($"after shaking: emitted {feed.Emitted}, alive {feed.PelletsAlive}, eaten {feed.Eaten}, portions {AquaCare.Stock(basic.id).portions:0.0}");
            // hold it there (a trickle when needed) until everyone is full
            float hold0 = Time.time;
            while (feed.Celebrations == 0 && Time.time - hold0 < 40f)
            {
                if (!shotEat && feed.Eaten > eaten0)
                {
                    shotEat = true;
                    var e = PixelView.Current.WorldToScreen(feed.LastEatAt);
                    Log($"eat at {e.x:0} {Screen.height - e.y:0}");
                    yield return Shot("eating");
                }
                yield return null;
            }
            yield return new WaitForSeconds(0.35f);
            yield return Shot("full");
            Check(feed.Celebrations == 1 && d.aquarium.All(AquaCare.Full),
                $"배불러요!: celebrations {feed.Celebrations}, fullness {string.Join(", ", d.aquarium.Select(f => f.fullness.ToString("0.00")))}");
            float used = portions0 - AquaCare.Stock(basic.id).portions;
            Check(Mathf.Abs(used - feed.Emitted / (float)AquaCare.PelletsPerPortion) < 0.01f,
                $"feed used {used:0.0} portions for {feed.Emitted} pellets ({feed.Eaten} eaten, {feed.Emitted - feed.Eaten - feed.PelletsAlive} dissolved)");
            int emitted0 = feed.Emitted;
            yield return new WaitForSeconds(0.6f);
            Check(feed.Emitted == emitted0, $"all full: pouring more uses no feed ({feed.Emitted - emitted0} pellets), hint '{feed.HintShown}'");
            Up();
            yield return new WaitForSeconds(0.8f);
            Check(!feed.Dragging && feed.BagStateOf(basic.id) == "Open", $"released: the bag glided back ({feed.BagStateOf(basic.id)})");

            // ---- premium keeps them full longer and grows faster (a detached fish, not in the tank)
            var probe = new CaughtFish { speciesId = "carp", sizeCm = 60f, weightKg = 3.24f, value = 122, careAt = SaveSystem.Now, baseCm = 60f, baseKg = 3.24f, baseValue = 122 };
            for (int i = 0; i < 5; i++) AquaCare.Eat(d, probe, prem);
            Check(Mathf.Abs(AquaCare.HoursToHungry(probe) - 0.75f * prem.fullHours) < 0.05f && Mathf.Abs(AquaCare.GrowthRate(probe) - prem.growth) < 1e-3f,
                $"premium: 5 flakes -> fullness {probe.fullness:0.00}, share {probe.premiumShare:0.00}, hungry in {AquaCare.HoursToHungry(probe):0.0} h (basic 9 h), growth x{AquaCare.GrowthRate(probe):0.00}");

            // ---- a probe fish fed for 1 / 3 / 7 / 10 days
            var B = AddFish("lenok", 50f, "stream");
            scene.AfterTimeJump();
            yield return new WaitForSeconds(0.6f);
            scene.ShowInfo(B);
            yield return new WaitForSeconds(0.7f);
            yield return Shot("info_day0");
            CloseDialogs();
            float[] days = { 1f, 3f, 7f, 10f };
            float[] expect = { 2.82f, 5.83f, 7.62f, 7.90f };
            float done = 0f;
            for (int i = 0; i < days.Length; i++)
            {
                AquaCare.FastForward(d, (days[i] - done) * 24f, basic);
                done = days[i];
                float g = Growth(B);
                float want = AquaCare.GrowthOf(days[i]) * 100f;
                Check(Mathf.Abs(g - want) < 0.02f && Mathf.Abs(g - expect[i]) < 0.02f && g <= AquaCare.GrowthCap * 100f + 1e-3f,
                    $"fed {days[i]:0} d: fedDays {B.fedDays:0.000}, growth +{g:0.00}% (formula {want:0.00}%), {B.baseCm:0.0} -> {AquaCare.GrownCm(B):0.00}cm, {B.baseKg:0.00} -> {B.weightKg:0.00}kg, value {B.baseValue} -> {B.value} (+{(B.value / (float)B.baseValue - 1f) * 100f:0.0}%), income {AquaCare.IncomeF(B):0.00}/min");
                if (days[i] == 7f)
                {
                    scene.AfterTimeJump();
                    yield return new WaitForSeconds(0.5f);
                    scene.ShowInfo(B);
                    yield return new WaitForSeconds(0.7f);
                    yield return Shot("info_day7");
                    CloseDialogs();
                    yield return new WaitForSeconds(0.2f);
                }
            }
            foreach (var f in d.aquarium) Log("  after 10 d fed: " + AquaCare.Describe(f));

            // ---- sell the grown fish at the grown price
            scene.ShowInfo(B);
            yield return new WaitForSeconds(0.5f);
            int coinsBefore = d.coins, grownValue = B.value;
            var sell = FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text.StartsWith("판매"));
            bool found = sell != null; // (the button goes with its window once pressed)
            if (found) sell.onClick.Invoke();
            yield return new WaitForSeconds(0.5f);
            bool gone = !d.aquarium.Any(f => f.uid == B.uid);
            Check(found && d.coins - coinsBefore == grownValue && grownValue > B.baseValue && gone,
                $"sold the grown {B.speciesId} from its info window: +{d.coins - coinsBefore} coins (grown value {grownValue}, caught {B.baseValue}), out of the tank {gone} ({d.aquarium.Count} left)");
            CloseDialogs();

            // ---- an emptied bag folds away and the next spare stands there sealed
            AquaCare.FastForward(d, 14f, null);
            scene.AfterTimeJump();
            AquaCare.Stock(basic.id).portions = 0.6f;
            int sealedLeft = AquaCare.Stock(basic.id).bags;
            yield return new WaitForSeconds(0.6f);
            Down(c);
            yield return null;
            yield return Drag(c, over, 0.6f);
            float e0 = Time.time;
            while (Time.time - e0 < 3f && feed.Dragging)
            {
                PointerInput.SimPos = Scr(over + new Vector2(Mathf.Sin((Time.time - e0) * 12f) * 1.5f, 0f));
                yield return null;
            }
            Up();
            yield return new WaitForSeconds(1.8f);
            Check(AquaCare.Stock(basic.id).portions <= 0.001f && feed.BagStateOf(basic.id) == "Sealed" && AquaCare.Stock(basic.id).bags == sealedLeft,
                $"emptied bag folded, next one sealed: state {feed.BagStateOf(basic.id)}, sealed {AquaCare.Stock(basic.id).bags}, portions {AquaCare.Stock(basic.id).portions:0.0}");
            yield return new WaitForSeconds(6f); // (the leftovers dissolve)
            Log($"done: {ok} ok, {fail} failed; coins {d.coins}, pending {Game.I.PendingIncome}, income {Game.I.IncomePerMinute}/min");
            Game.I.Save();
            Application.Quit();
        }

        // ------------------------------------------------------------------ -fkaqua live
        /// <summary>A shot, logging the screen spot of interest (px from the top left) for the sheet's close-up.</summary>
        IEnumerator ShotAt(string name, Vector2 world)
        {
            yield return AutoShot.Frame();
            AutoShot.Save(Path.Combine(shots, name + ".png"));
            var s = Scr(world);
            Log($"shot {name} focus {s.x:0} {Screen.height - s.y:0}");
            yield return null;
        }

        /// <summary>Moves the pressed pointer from a to a (moving) target in <paramref name="time"/> s, eased.</summary>
        IEnumerator Carry(Vector2 a, Func<Vector2> b, float time)
        {
            float t0 = Time.time;
            while (true)
            {
                float k = Mathf.Clamp01((Time.time - t0) / time);
                PointerInput.SimActive = true;
                PointerInput.SimDown = true;
                PointerInput.SimPos = Scr(Vector2.Lerp(a, b(), k * k * (3f - 2f * k)));
                if (k >= 1f) yield break;
                yield return null;
            }
        }

        /// <summary>Keeps the pointer pressed over a (moving) spot for <paramref name="time"/> s.</summary>
        IEnumerator HoldAt(Func<Vector2> at, float time)
        {
            for (float w = 0f; w < time; w += Time.deltaTime)
            {
                PointerInput.SimPos = Scr(at());
                yield return null;
            }
        }

        static void PlaceFish(AquariumScene s, CaughtFish cf, Vector2 p, bool faceLeft)
        {
            var tf = s.TankOf(cf);
            if (tf != null) tf.Place(p, faceLeft);
        }

        static Diet DietOf(string id) => GameDatabase.GetFish(id)?.diet ?? Diet.None;

        IEnumerator LiveTest()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Game.Data;
            d.coins = Math.Max(d.coins, 200000);
            d.tankLevel = 1; // (the 중형 수조: the coordinates below are its art's; the big fish are put in as an older save's)
            foreach (var id in new[] { "tank_1" }) if (!d.ownedItems.Contains(id)) d.ownedItems.Add(id);
            d.feedTornOnce = true;
            d.feed.Clear();
            var basic = AquaCare.Feed("feed_basic");
            var prem = AquaCare.Feed("feed_premium");
            var shrimp = AquaCare.Feed("feed_shrimp");
            var sardine = AquaCare.Feed("feed_sardine");
            AquaCare.Stock(basic.id).bags = 2;
            AquaCare.Stock(prem.id).bags = 1;

            // ---- every species' diet
            AquaCare.LogDiets();
            int nP = GameDatabase.Fish.Count(f => (f.diet & Diet.Pellet) != 0), nS = GameDatabase.Fish.Count(f => (f.diet & Diet.Shrimp) != 0),
                nD = GameDatabase.Fish.Count(f => (f.diet & Diet.Sardine) != 0);
            Check(GameDatabase.Fish.All(f => f.diet != Diet.None && AquaCare.KindsOf(f.diet).Count() <= 2),
                $"every species eats one or two foods ({GameDatabase.Fish.Count} species: pellets {nP}, shrimp {nS}, sardines {nD})");
            Check(new[] { "arapaima", "blue_marlin", "great_white", "coelacanth", "bluefin_tuna" }.All(id => DietOf(id) == Diet.Sardine)
                  && new[] { "largemouth_bass", "mandarin_fish", "snakehead", "northern_pike", "sturgeon" }.All(id => DietOf(id) == Diet.Shrimp)
                  && new[] { "crucian_carp", "carp", "rainbow_trout", "crystal_koi" }.All(id => (DietOf(id) & Diet.Pellet) != 0)
                  && GameDatabase.Fish.Where(f => f.feedStyle == FeedStyle.Surge).All(f => (f.diet & Diet.Pellet) == 0),
                "diets: big / sea predators sardines, mid predators and bottom feeders shrimp, small fish and omnivores pellets; no surging fish eats pellets");

            d.aquarium.Clear();
            var crucian = AddFish("crucian_carp", 25f, "lake");
            var carp = AddFish("carp", 60f, "lake");
            var bass = AddFish("largemouth_bass", 40f, "lake");
            var mandarin = AddFish("mandarin_fish", 35f, "stream");
            var catfish = AddFish("catfish", 60f, "swamp");
            var arapaima = AddFish("arapaima", 220f, "swamp");
            AquaCare.FastForward(d, 12f, null); // (everyone hungry)
            Game.I.Save();

            // ---- the aquarium: the bags on the ledge, no shrimp / sardines yet: two faint slots
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.2f);
            var feed = AquaFeed.Current;
            var scene = FindAnyObjectByType<AquariumScene>();
            Check(feed.LiveState(shrimp.id) == "Slot" && feed.LiveState(sardine.id) == "Slot",
                $"none owned: faint slots for the tub ({feed.LiveState(shrimp.id)}) and the cooler ({feed.LiveState(sardine.id)})");
            Check(scene.TopHint == "큰입배스는 생새우를 좋아해요 · 상점에서 사요", $"top hint for the hungry bass without shrimp: '{scene.TopHint}'");
            yield return Tap(feed.BoxCentre(sardine.id) + new Vector2(0f, -10f) / 16f);
            yield return new WaitForSeconds(0.8f);
            var shopGo = GameObject.Find("Shop");
            Check(shopGo != null && ShopUI.TankFeed && GameObject.Find(sardine.id) != null, "a tap on the empty cooler slot opens the shop's 사료 section");

            // ---- the shop: buy shrimp and sardines with the rows' buttons
            int coins0 = d.coins;
            foreach (var f in new[] { shrimp, sardine })
            {
                var row = GameObject.Find(f.id);
                var buy = row != null ? row.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Buy") : null;
                if (buy != null) buy.onClick.Invoke();
                else Log("buy button not found: " + f.id);
                yield return new WaitForSeconds(0.5f);
            }
            Check(AquaCare.Pieces(shrimp) == shrimp.piecesPerBuy && AquaCare.Pieces(sardine) == sardine.piecesPerBuy,
                $"bought: {AquaCare.Pieces(shrimp)} shrimp, {AquaCare.Pieces(sardine)} sardines");
            Check(coins0 - d.coins == shrimp.price + sardine.price, $"paid {coins0 - d.coins} (= {shrimp.price} + {sardine.price})");
            yield return new WaitForSeconds(2.3f); // (the purchase toasts gone)
            yield return Shot("shop_live");
            shopGo = GameObject.Find("Shop");
            if (shopGo != null) Destroy(shopGo.transform.parent.gameObject);
            yield return new WaitForSeconds(0.8f);
            Check(feed.LiveState(shrimp.id) == "Closed" && feed.LiveState(sardine.id) == "Closed" && feed.BadgeOf(shrimp.id) == "10" && feed.BadgeOf(sardine.id) == "6",
                $"on the ledge, shut: tub {feed.LiveState(shrimp.id)} [{feed.BadgeOf(shrimp.id)}], cooler {feed.LiveState(sardine.id)} [{feed.BadgeOf(sardine.id)}]");
            yield return ShotAt("ledge", feed.BoxCentre(shrimp.id));

            // ---- the lids: a tap on the tub opens it; the cooler (its in-between frame in the shot)
            yield return Tap(feed.LidPoint(shrimp.id));
            yield return new WaitForSeconds(0.4f);
            Check(feed.LiveState(shrimp.id) == "Open" && AquaCare.Stock(shrimp.id).open && feed.BoxSprite(shrimp.id) == "feed_tub_open",
                $"tap on the tub lid: {feed.LiveState(shrimp.id)} ({feed.BoxSprite(shrimp.id)}), saved open {AquaCare.Stock(shrimp.id).open}");
            yield return Tap(feed.LidPoint(sardine.id));
            string ajar = feed.BoxSprite(sardine.id);
            yield return ShotAt("opening", feed.BoxCentre(shrimp.id));
            yield return new WaitForSeconds(0.4f);
            Check(ajar == "feed_cooler_ajar" && feed.LiveState(sardine.id) == "Open" && feed.BoxSprite(sardine.id) == "feed_cooler_open",
                $"tap on the cooler lid: {ajar} -> {feed.BoxSprite(sardine.id)}");
            yield return new WaitForSeconds(0.6f);

            // ---- a shrimp for the bass (the others out of the way)
            PlaceFish(scene, bass, new Vector2(-2f, 2.6f), false);
            PlaceFish(scene, mandarin, new Vector2(10.5f, -1.5f), true);
            PlaceFish(scene, carp, new Vector2(8f, -2.3f), true);
            PlaceFish(scene, catfish, new Vector2(3.5f, -2.5f), true);
            PlaceFish(scene, crucian, new Vector2(-9f, 0.5f), false);
            PlaceFish(scene, arapaima, new Vector2(6f, 2.2f), true);
            var bt = scene.TankOf(bass);
            var pick = feed.PickPoint(shrimp.id);
            float fBass = bass.fullness, fAra = arapaima.fullness, fCru = crucian.fullness;
            Down(pick);
            yield return null;
            yield return null;
            Check(feed.Holding && feed.BadgeOf(shrimp.id) == "9", $"press on the open tub: holding a shrimp {feed.Holding}, the tub shows {feed.BadgeOf(shrimp.id)}");
            Func<Vector2> overBass = () => new Vector2(bt.Mouth.x + (bt.FacingLeft ? -0.4f : 0.4f), 6.6f);
            yield return Carry(pick, overBass, 1.0f);
            yield return HoldAt(overBass, 0.45f);
            yield return ShotAt("hold_shrimp", feed.HeldAt);
            int grabs0 = feed.Grabs;
            string likeHint = "";
            Up();
            float t0 = Time.time;
            while (feed.Grabs == grabs0 && Time.time - t0 < 8f)
            {
                if (feed.HintShown.Contains("좋아해요")) likeHint = feed.HintShown;
                yield return null;
            }
            var grabber = feed.LastGrabBy;
            yield return ShotAt("bass_grab", grabber != null ? grabber.Mouth : Vector2.zero);
            yield return new WaitForSeconds(0.6f);
            Check(grabber == bt && Mathf.Abs(bass.fullness - (fBass + shrimp.fill)) < 0.02f && AquaCare.Pieces(shrimp) == shrimp.piecesPerBuy - 1,
                $"dropped over the bass: grabbed by {(grabber != null ? grabber.Data.speciesId : "nobody")} [{(grabber != null ? grabber.Style.ToString() : "")}] in {Time.time - t0 - 0.6f:0.00} s, fullness {fBass:0.00} -> {bass.fullness:0.00}, shrimp left {AquaCare.Pieces(shrimp)}");
            Check(arapaima.fullness <= fAra + 1e-3f && crucian.fullness <= fCru + 1e-3f && likeHint == "이 물고기는 정어리를 좋아해요",
                $"the arapaima and the crucian carp ignored it ({fAra:0.00} -> {arapaima.fullness:0.00}, {fCru:0.00} -> {crucian.fullness:0.00}); hint '{likeHint}'");

            // ---- a sardine for the arapaima: it surges up and gulps it at the surface
            var at = scene.TankOf(arapaima);
            PlaceFish(scene, arapaima, new Vector2(3.5f, 1.2f), false);
            var pickS = feed.PickPoint(sardine.id);
            int pops0 = feed.FullPops, cel0 = feed.Celebrations, gulps0 = feed.Gulps;
            fAra = arapaima.fullness;
            Down(pickS);
            yield return null;
            yield return null;
            Check(feed.Holding && feed.BadgeOf(sardine.id) == "5", $"press on the open cooler: holding a sardine {feed.Holding}, the cooler shows {feed.BadgeOf(sardine.id)}");
            Func<Vector2> overAra = () => new Vector2(at.Mouth.x + 0.5f, 6.9f);
            yield return Carry(pickS, overAra, 1.0f);
            yield return HoldAt(overAra, 0.5f);
            yield return ShotAt("hold_sardine", feed.HeldAt);
            Up();
            t0 = Time.time;
            while (feed.Gulps == gulps0 && Time.time - t0 < 8f) yield return null;
            float took = Time.time - t0;
            while (Time.time - feed.LastGulpTime < 0.08f) yield return null; // (the burst frame)
            yield return ShotAt("gulp", feed.LastGulpAt);
            yield return new WaitForSeconds(1.2f);
            Check(feed.Gulps == gulps0 + 1 && AquaCare.Full(arapaima) && Mathf.Abs(feed.LastGulpAt.y - 5.1875f) < 0.01f,
                $"dropped over the arapaima: it surged and gulped it at the surface in {took:0.00} s (gulp at y {feed.LastGulpAt.y:0.0000}), fullness {fAra:0.00} -> {arapaima.fullness:0.00}");
            Check(feed.FullPops > pops0 && feed.Celebrations > cel0 && AquaCare.Pieces(sardine) == sardine.piecesPerBuy - 1,
                $"one sardine = a full meal: 배불러요! over it ({feed.FullPops - pops0}), the sardine eaters full ({feed.Celebrations - cel0}), sardines left {AquaCare.Pieces(sardine)}");

            // ---- another sardine: the arapaima is full, nobody else eats sardines: it sinks and dissolves (no refund)
            int dis0 = feed.Dissolved, live0 = feed.LiveEaten;
            Down(pickS);
            yield return null;
            yield return null;
            yield return Carry(pickS, () => new Vector2(-4f, 6.8f), 0.8f);
            Up();
            yield return new WaitForSeconds(0.9f);
            string noneHint = feed.HintShown;
            t0 = Time.time;
            while (feed.Dissolved == dis0 && Time.time - t0 < 45f) yield return null;
            Check(feed.Dissolved == dis0 + 1 && feed.LiveEaten == live0 && AquaCare.Pieces(sardine) == sardine.piecesPerBuy - 2 && feed.PiecesAlive == 0,
                $"a sardine nobody wants: ignored, dissolved {Time.time - t0 + 0.9f:0.0} s after the drop, eaten {feed.LiveEaten - live0}, sardines left {AquaCare.Pieces(sardine)} (no refund); hint '{noneHint}'");

            // ---- a shrimp let go off the tank (over the cabinet): back into the tub
            int left = AquaCare.Pieces(shrimp), back0 = feed.PutBacks, drops0 = feed.Drops;
            Down(pick);
            yield return null;
            yield return null;
            yield return Carry(pick, () => pick + new Vector2(2.5f, -2.6f), 0.5f);
            Up();
            yield return new WaitForSeconds(0.5f);
            Check(feed.PutBacks == back0 + 1 && feed.Drops == drops0 && AquaCare.Pieces(shrimp) == left && !feed.Holding && feed.BadgeOf(shrimp.id) == left.ToString(),
                $"let go off the tank: back in the tub ({AquaCare.Pieces(shrimp)} left, badge {feed.BadgeOf(shrimp.id)}), hint '{feed.HintShown}'");

            // ---- a tank of big fish only: pellets are ignored, "이 물고기는 정어리를 좋아해요"
            d.aquarium.Clear();
            var ara2 = AddFish("arapaima", 240f, "swamp");
            var tuna = AddFish("bluefin_tuna", 180f, "ocean");
            AquaCare.FastForward(d, 12f, null);
            scene.AfterTimeJump();
            yield return new WaitForSeconds(0.5f);
            PlaceFish(scene, ara2, new Vector2(-4.5f, 2.2f), false);
            PlaceFish(scene, tuna, new Vector2(6f, 0.2f), true);
            AquaCare.Stock(basic.id).portions = basic.portions; // (a bag torn open)
            yield return new WaitForSeconds(0.4f);
            float fa = ara2.fullness, ft = tuna.fullness;
            int emitted0 = feed.Emitted, eatenP0 = feed.Eaten;
            var bagC = feed.BagCentre(basic.id);
            Down(bagC);
            yield return null;
            var over = new Vector2(-2.5f, 6.4f);
            yield return Drag(bagC, over, 0.7f);
            float sh0 = Time.time;
            bool shotIgnore = false;
            string ignoreHint = "";
            while (Time.time - sh0 < 3f)
            {
                float k = Time.time - sh0;
                PointerInput.SimPos = Scr(over + new Vector2(Mathf.Sin(k * 9f) * 1.2f, 0f));
                if (feed.HintShown.Contains("좋아해요")) ignoreHint = feed.HintShown;
                if (!shotIgnore && k > 1.4f && feed.PelletsAlive > 0 && ignoreHint != "")
                {
                    shotIgnore = true;
                    yield return ShotAt("ignore_pellets", scene.TankOf(ara2).Pos);
                }
                yield return null;
            }
            Up();
            yield return new WaitForSeconds(0.6f);
            if (!shotIgnore) yield return ShotAt("ignore_pellets", scene.TankOf(ara2).Pos);
            Check(feed.Emitted > emitted0 && feed.Emitted - emitted0 <= 2 && feed.Eaten == eatenP0 && ara2.fullness <= fa + 1e-3f && tuna.fullness <= ft + 1e-3f
                  && ignoreHint == "이 물고기는 정어리를 좋아해요",
                $"pellets over big fish only: {feed.Emitted - emitted0} poured (one at a time), eaten {feed.Eaten - eatenP0}, fullness {fa:0.00}/{ft:0.00} -> {ara2.fullness:0.00}/{tuna.fullness:0.00}; hint '{ignoreHint}'");

            // ---- the info window: the favourite food; hungry and none owned: what it likes, and the 상점 button
            int sardLeft = AquaCare.Pieces(sardine);
            AquaCare.Stock(sardine.id).pieces = 0;
            scene.ShowInfo(ara2);
            yield return new WaitForSeconds(0.7f);
            var dietText = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "DietValue");
            var dietIcon = FindObjectsByType<Image>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "DietIcon");
            var shopBtn = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "DietShop");
            yield return Shot("info_diet");
            Check(dietText != null && dietText.text.Contains("이 물고기는 정어리를 좋아해요") && dietIcon != null && dietIcon.sprite != null && dietIcon.sprite.name == "icon_diet_sardine" && shopBtn != null,
                $"info window of the hungry arapaima without sardines: '{dietText?.text}', icon {dietIcon?.sprite?.name}, 상점 button {shopBtn != null}");
            if (shopBtn != null) shopBtn.onClick.Invoke();
            yield return new WaitForSeconds(0.8f);
            var shop2 = GameObject.Find("Shop");
            Check(shop2 != null && !Dialog.Open && ShopUI.TankFeed, $"its 상점 button: the window closes ({!Dialog.Open}), the shop's 사료 section opens ({shop2 != null})");
            if (shop2 != null) Destroy(shop2.transform.parent.gameObject);
            AquaCare.Stock(sardine.id).pieces = sardLeft;
            yield return new WaitForSeconds(0.4f);
            scene.ShowInfo(ara2);
            yield return new WaitForSeconds(0.6f);
            dietText = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "DietValue");
            shopBtn = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "DietShop");
            Check(dietText != null && dietText.text == "정어리" && shopBtn == null, $"with sardines in the cooler: '{dietText?.text}', no 상점 button ({shopBtn == null})");
            CloseDialogs();
            yield return new WaitForSeconds(0.3f);

            // ---- shut the lids; the stock and the lids are saved (and an old save reads without them)
            yield return Tap(feed.LidPoint(shrimp.id));
            yield return new WaitForSeconds(0.3f);
            yield return Tap(feed.LidPoint(sardine.id));
            yield return new WaitForSeconds(0.3f);
            Check(!AquaCare.Stock(shrimp.id).open && !AquaCare.Stock(sardine.id).open && feed.LiveState(shrimp.id) == "Closed" && feed.LiveState(sardine.id) == "Closed",
                $"taps on the open lids shut them: tub {feed.LiveState(shrimp.id)}, cooler {feed.LiveState(sardine.id)}");
            yield return Tap(feed.LidPoint(shrimp.id)); // (the tub open again: it must come back open)
            yield return new WaitForSeconds(0.3f);
            Game.I.Save();
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(d));
            var bs = back.feed.FirstOrDefault(x => x.id == shrimp.id);
            var bd = back.feed.FirstOrDefault(x => x.id == sardine.id);
            Check(bs != null && bd != null && bs.pieces == AquaCare.Pieces(shrimp) && bs.open && bd.pieces == AquaCare.Pieces(sardine) && !bd.open,
                $"saved: shrimp {bs?.pieces} (open {bs?.open}), sardines {bd?.pieces} (open {bd?.open})");
            var old = JsonUtility.FromJson<SaveData>("{\"aquaVer\":1,\"feed\":[{\"id\":\"feed_basic\",\"bags\":2,\"portions\":3.5}]}");
            Check(old.feed.Count == 1 && old.feed[0].bags == 2 && old.feed[0].pieces == 0 && !old.feed[0].open,
                "an old save's feed list reads with no shrimp / sardines and the lids shut");
            SceneFlow.Go("Map");
            yield return new WaitForSeconds(1.5f);
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.2f);
            feed = AquaFeed.Current;
            Check(feed != null && feed.LiveState(shrimp.id) == "Open" && feed.LiveState(sardine.id) == "Closed" && feed.BadgeOf(shrimp.id) == AquaCare.Pieces(shrimp).ToString(),
                $"back in the aquarium: tub {feed?.LiveState(shrimp.id)} [{feed?.BadgeOf(shrimp.id)}], cooler {feed?.LiveState(sardine.id)} [{feed?.BadgeOf(sardine.id)}]");

            Log($"done: {ok} ok, {fail} failed; coins {d.coins}, shrimp {AquaCare.Pieces(shrimp)}, sardines {AquaCare.Pieces(sardine)}");
            Game.I.Save();
            Application.Quit();
        }

        // ------------------------------------------------------------------ -fkaqua migrate
        IEnumerator Migrate()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Game.Data;
            Log($"migrated save: aquaVer {d.aquaVer}, bank {d.aquariumBank}, pending {Game.I.PendingIncome}, collected {SaveSystem.Now - d.aquariumCollectedAt} s ago, feed stocks {d.feed.Count}");
            foreach (var f in d.aquarium) Log($"  {f.uid} kept {(SaveSystem.Now - f.addedAt) / 3600f:0.0} h | " + AquaCare.Describe(f));
            Check(d.aquaVer == 1 && d.aquarium.All(f => f.baseCm > 0f && f.baseValue > 0), "every kept fish has a base");
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.5f);
            yield return Shot("migrated");
            var scene = FindAnyObjectByType<AquariumScene>();
            if (d.aquarium.Count > 0)
            {
                scene.ShowInfo(d.aquarium[0]);
                yield return new WaitForSeconds(0.7f);
                yield return Shot("migrated_info");
                CloseDialogs();
            }
            Log($"done: {ok} ok, {fail} failed");
            Game.I.Save();
            Application.Quit();
        }
    }
}
