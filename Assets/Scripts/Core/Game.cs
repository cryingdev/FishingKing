using System;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    public enum BuyResult { Ok, NotEnoughCoins, AlreadyOwned, NeedPreviousTank }

    public struct CatchReport
    {
        public bool newSpecies;
        public bool newRecord;
        public int xpGained;
        public int levelsGained;
    }

    /// <summary>
    /// Persistent game state + economy. Lives across scenes (created before the first scene loads).
    /// </summary>
    public class Game : MonoBehaviour
    {
        public static Game I { get; private set; }
        public static SaveData Data => I.data;

        public event Action Changed;
        public event Action<int> LeveledUp;

        SaveData data;
        const long MaxIdleSeconds = 12 * 3600;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("[Game]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<Game>();
            I.data = SaveSystem.Load();
            go.AddComponent<Sfx>();
            go.AddComponent<SceneFlow>();
            AudioListener.volume = I.data.soundOn ? 1f : 0f;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }

        // ------------------------------------------------------------------ test switches
        // -fkfresh           start from a new save
        // -fkrich            test profile: lots of coins, high level, everything unlocked
        // -fkscene <name>    jump to a scene after boot (with -fkstage <id> for Fishing)
        // -fkfish <id>       stock the stage with this species only
        // -fkjump <kind>     a hooked fish jumps after every rest: hop | shake | tailwalk
        // -fkreverse         reversed reel direction (counter-clockwise winds in)
        // -fkrod <id>        equip this rod
        // -fkangx <metres>   the angler starts at this feet x (clamped to the stage's standing area, see Angler)
        // -fkbait <id>       own and equip this bait / lure
        // -fklure <id>       own and equip this lure (with -fkauto lure: the lure action test, see AutoPilot)
        // -fkencounter [now|natural]  the legend encounter test (Docs/lures_legend_spec.md 4.4, Docs/legends_rollout.md 6):
        //                    with -fkstage <id>, owns and equips the stage legend's top key (the cave's 야광 에기, the lake's
        //                    황금 떡밥, ...; a -fkbait / -fklure that is one of its keys instead, e.g. the swamp's
        //                    bait_popper) and, when the line is under its minLine, the weakest line that holds it; no
        //                    cooldowns, no pity; now: the encounter starts 1 s after the lure lands; natural: the lurk point in
        //                    front of him (the ice: by the hole; the ocean: 24 m out), a 2 s soak, the meter x8, the roll
        //                    always succeeds (see LegendWatch)
        // -fklegend <id>     with -fkencounter: which of the stage's legends (the ocean: blue_marlin (default) / great_white)
        // -fkencwinh <px>    the encounter window this many px tall (64..136, its top kept): the HUD's bands crowd the frog
        //                    and the face, so the gauge's other spots get used (EncounterView.DebugWinH)
        // -fktime <hh:mm>    the game clock at boot; -fkperiod <dawn|day|evening|night> its centre; -fktimescale <x> game
        //                    minutes per real second (0 frozen); -fktide <low|flood|high|ebb|0..1> the sea's tide fixed;
        //                    -fkcurrent <x> every current x x; -fkgust lake / swamp gusts at 2 s then every 12 s;
        //                    -fkcurrentvivid the current as arrows; -fkclocklog a [CLOCK] line a second (GameClock)
        // -fkobstacles show|off  obstacles (Docs/obstacles_spec.md 12): show = every obstacle drawn in every state, off = no
        //                    obstacle data loaded (the game as before); -fkobstlog an [OBST] line for every event;
        //                    -fksnag <x> the snag rates x x (0 = never, 99 = at once); -fkobstseed <n> the obstacle rolls' seed
        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Flag(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void DebugBoot()
        {
            if (Flag("-fkfresh")) I.data = SaveData.NewGame();
            // the clock / tide / current switches (-fktime, -fkperiod, -fktimescale, -fktide, -fkcurrent, -fkgust,
            // -fkcurrentvivid, -fkclocklog: see GameClock.DebugBoot)
            GameClock.DebugBoot();
            if (Flag("-fkrich"))
            {
                var d = I.data;
                d.coins = Math.Max(d.coins, 500000);
                d.level = Math.Max(d.level, 20);
                d.tutorialDone = true;
                foreach (var s in GameDatabase.Stages) if (!d.unlockedStages.Contains(s.id)) d.unlockedStages.Add(s.id);
            }
            if (Flag("-fkreverse")) I.data.reelReverse = true; // counter-clockwise winds in
            if (Flag("-fkgear"))
            {
                var d = I.data;
                foreach (var it in new ItemDef[] { GameDatabase.Rods[^1], GameDatabase.Reels[^1], GameDatabase.Lines[^1] })
                {
                    if (!d.ownedItems.Contains(it.id)) d.ownedItems.Add(it.id);
                    I.Equip(it);
                }
            }
            var rod = GameDatabase.GetItem<RodDef>(Arg("-fkrod"));
            if (rod != null)
            {
                if (!I.Owns(rod.id)) I.data.ownedItems.Add(rod.id);
                I.Equip(rod);
            }
            string bait = Arg("-fkbait");
            if (!string.IsNullOrEmpty(bait) && GameDatabase.GetItem<BaitDef>(bait) != null)
            {
                var b = GameDatabase.GetItem<BaitDef>(bait);
                if (b.isLure) { if (!I.Owns(b.id)) I.data.ownedItems.Add(b.id); }
                else I.AddBait(b.id, 99);
                I.Equip(b);
            }
            // -fklure <id>: own and equip that lure (with -fkauto lure: the lure action test)
            var lure = GameDatabase.GetItem<BaitDef>(Arg("-fklure"));
            if (lure != null && lure.isLure)
            {
                if (!I.Owns(lure.id)) I.data.ownedItems.Add(lure.id);
                I.Equip(lure);
            }
            if (Flag("-fkencounter"))
            {
                // the stage's legend (-fklegend <id> picks one, e.g. the ocean's great white; default the stage's first):
                // own and equip its top key, and the weakest line that meets its minLine when the equipped one does not
                var stageId = Arg("-fkstage") ?? "cave";
                var legends = GameDatabase.FishOfStage(stageId).Where(f => f.encounter != null).ToList();
                string want = Arg("-fklegend");
                var sp = legends.FirstOrDefault(f => f.id == want) ?? legends.FirstOrDefault() ?? GameDatabase.GetFish("coelacanth");
                var ed = sp.encounter;
                // (a -fkbait / -fklure that is one of its keys stays on: e.g. the arapaima's popper instead of the frog)
                var asked = GameDatabase.GetItem<BaitDef>(Arg("-fklure") ?? Arg("-fkbait"));
                var key = asked != null && ed.keyLures.ContainsKey(asked.id) ? asked
                    : GameDatabase.GetItem<BaitDef>(ed.keyLures.OrderByDescending(kv => kv.Value).First().Key);
                if (key.isLure) { if (!I.Owns(key.id)) I.data.ownedItems.Add(key.id); }
                else I.AddBait(key.id, 99);
                I.Equip(key);
                if (I.Line.strength < ed.minLine)
                {
                    var line = GameDatabase.Lines.Where(l => l.strength >= ed.minLine).OrderBy(l => l.strength).FirstOrDefault() ?? GameDatabase.Lines[^1];
                    if (!I.Owns(line.id)) I.data.ownedItems.Add(line.id);
                    I.Equip(line);
                }
                string mode = Arg("-fkencounter");
                LegendWatch.DebugMode = mode == "natural" ? "natural" : "now";
                LegendWatch.DebugLegend = sp.id;
                LegendWatch.ClearCooldowns();
                foreach (var lr in I.data.legends) lr.pity = 0;
            }
            if (float.TryParse(Arg("-fkencwinh"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float encWinH))
                EncounterView.DebugWinH = encWinH;
            FishSpawner.OnlySpecies = GameDatabase.GetFish(Arg("-fkfish"));
            string obst = Arg("-fkobstacles");
            if (obst == "off") Obstacles.Off = true;
            else if (obst == "show") Obstacles.Show = true;
            if (Flag("-fkobstlog")) Obstacles.Log = true;
            if (float.TryParse(Arg("-fksnag"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float snag))
                Obstacles.SnagMult = Mathf.Max(0f, snag);
            if (int.TryParse(Arg("-fkobstseed"), out int oseed)) Obstacles.Rnd = new System.Random(oseed);
            string jump = Arg("-fkjump");
            if (!string.IsNullOrEmpty(jump) && Enum.TryParse(jump, true, out FightModel.JumpKind kind)) FightModel.ForceJump = kind;
            string scene = Arg("-fkscene");
            if (!string.IsNullOrEmpty(scene))
            {
                SceneFlow.PendingStage = Arg("-fkstage");
                UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            }
        }

        void OnApplicationPause(bool pause)
        {
            if (pause) Save();
        }

        void OnApplicationQuit() => Save();

        public void Save() => SaveSystem.Save(data);

        void Notify()
        {
            Save();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ coins & xp
        public int Coins => data.coins;

        public void AddCoins(int amount)
        {
            data.coins += amount;
            if (amount > 0) data.totalEarned += amount;
            Notify();
        }

        public bool Spend(int amount)
        {
            if (data.coins < amount) return false;
            data.coins -= amount;
            Notify();
            return true;
        }

        public static int XpToNext(int level) => 60 + level * level * 35;

        public int AddXp(int amount)
        {
            data.xp += amount;
            int gained = 0;
            while (data.xp >= XpToNext(data.level))
            {
                data.xp -= XpToNext(data.level);
                data.level++;
                gained++;
                data.coins += data.level * 100;
                LeveledUp?.Invoke(data.level);
            }
            Notify();
            return gained;
        }

        // ------------------------------------------------------------------ inventory
        public bool Owns(string id) => data.ownedItems.Contains(id);

        public RodDef Rod => GameDatabase.GetItem<RodDef>(data.rod);
        public ReelDef Reel => GameDatabase.GetItem<ReelDef>(data.reel);
        public LineDef Line => GameDatabase.GetItem<LineDef>(data.line);
        public BaitDef Bait => GameDatabase.GetItem<BaitDef>(data.bait);
        public TankDef Tank => GameDatabase.TankForLevel(data.tankLevel);

        public int BaitCount(string id)
        {
            var b = GameDatabase.GetItem<BaitDef>(id);
            if (b == null) return 0;
            if (b.infinite) return int.MaxValue;
            if (b.isLure) return Owns(id) ? 1 : 0;
            var e = data.baits.FirstOrDefault(x => x.id == id);
            return e?.count ?? 0;
        }

        public bool HasBait(string id) => BaitCount(id) > 0;

        public bool IsEquipped(ItemDef item)
        {
            switch (item.Kind)
            {
                case ItemKind.Rod: return data.rod == item.id;
                case ItemKind.Reel: return data.reel == item.id;
                case ItemKind.Line: return data.line == item.id;
                case ItemKind.Bait: return data.bait == item.id;
                case ItemKind.Tank: return ((TankDef)item).level == data.tankLevel;
            }
            return false;
        }

        public BuyResult Buy(ItemDef item)
        {
            if (item is TankDef tank)
            {
                if (tank.level <= data.tankLevel) return BuyResult.AlreadyOwned;
                if (tank.level != data.tankLevel + 1) return BuyResult.NeedPreviousTank;
                if (!Spend(tank.price)) return BuyResult.NotEnoughCoins;
                data.tankLevel = tank.level;
                if (!Owns(tank.id)) data.ownedItems.Add(tank.id);
                Notify();
                return BuyResult.Ok;
            }
            if (item is BaitDef bait && !bait.isLure)
            {
                if (bait.infinite) return BuyResult.AlreadyOwned;
                if (!Spend(bait.price)) return BuyResult.NotEnoughCoins;
                AddBait(bait.id, bait.packSize);
                return BuyResult.Ok;
            }
            if (Owns(item.id)) return BuyResult.AlreadyOwned;
            if (!Spend(item.price)) return BuyResult.NotEnoughCoins;
            data.ownedItems.Add(item.id);
            Equip(item);
            return BuyResult.Ok;
        }

        public void AddBait(string id, int n)
        {
            var e = data.baits.FirstOrDefault(x => x.id == id);
            if (e == null) data.baits.Add(e = new BaitCount { id = id, count = 0 });
            e.count += n;
            if (!Owns(id)) data.ownedItems.Add(id);
            Notify();
        }

        public void Equip(ItemDef item)
        {
            switch (item.Kind)
            {
                case ItemKind.Rod: data.rod = item.id; break;
                case ItemKind.Reel: data.reel = item.id; break;
                case ItemKind.Line: data.line = item.id; break;
                case ItemKind.Bait: if (HasBait(item.id)) data.bait = item.id; break;
            }
            Notify();
        }

        /// <summary>Natural bait used up by a bite. Falls back to the free paste when empty.</summary>
        public void ConsumeBait()
        {
            var b = Bait;
            if (b == null || b.infinite || b.isLure) return;
            var e = data.baits.FirstOrDefault(x => x.id == b.id);
            if (e != null) e.count = Mathf.Max(0, e.count - 1);
            if (e == null || e.count <= 0) data.bait = GameDatabase.StarterBait;
            Notify();
        }

        /// <summary>
        /// A lure gone with a parted line (a fight's break: a natural bait on the hook was already paid for at the hook
        /// set, so nothing else is taken): off the owned list, and if it was the equipped one, the free paste goes on.
        /// False for a natural bait or a lure no longer owned.
        /// </summary>
        public bool LoseLure(BaitDef lure)
        {
            if (lure == null || !lure.isLure || !Owns(lure.id)) return false;
            data.ownedItems.Remove(lure.id);
            if (data.bait == lure.id) data.bait = GameDatabase.StarterBait;
            Notify();
            return true;
        }

        // ------------------------------------------------------------------ stages
        public bool IsUnlocked(string stageId) => data.unlockedStages.Contains(stageId);

        public bool Unlock(StageDef s)
        {
            if (IsUnlocked(s.id)) return true;
            if (data.level < s.reqLevel) return false;
            if (!Spend(s.unlockCost)) return false;
            data.unlockedStages.Add(s.id);
            Notify();
            return true;
        }

        // ------------------------------------------------------------------ catches
        public CaughtFish MakeCatch(FishSpecies sp, float cm, string stageId)
        {
            return new CaughtFish
            {
                uid = Guid.NewGuid().ToString("N").Substring(0, 12),
                speciesId = sp.id,
                sizeCm = Mathf.Round(cm * 10f) / 10f,
                weightKg = Mathf.Round(sp.WeightKg(cm) * 100f) / 100f,
                value = sp.Price(cm),
                stageId = stageId,
                caughtAt = SaveSystem.Now,
            };
        }

        public SpeciesRecord Record(string speciesId) => data.records.FirstOrDefault(r => r.id == speciesId);

        /// <summary>The encounter record of a legend (created on first use).</summary>
        public LegendRecord Legend(string speciesId)
        {
            var r = data.legends.FirstOrDefault(x => x.id == speciesId);
            if (r == null)
            {
                r = new LegendRecord { id = speciesId };
                data.legends.Add(r);
            }
            return r;
        }

        /// <summary>The encounter record of a legend, or null when it was never met.</summary>
        public LegendRecord FindLegend(string speciesId) => data.legends.FirstOrDefault(x => x.id == speciesId);

        public CatchReport RegisterCatch(CaughtFish cf)
        {
            var rep = new CatchReport();
            var rec = Record(cf.speciesId);
            if (rec == null)
            {
                rec = new SpeciesRecord { id = cf.speciesId };
                data.records.Add(rec);
                rep.newSpecies = true;
            }
            rep.newRecord = !rep.newSpecies && cf.sizeCm > rec.bestCm;
            rec.caught++;
            rec.bestCm = Mathf.Max(rec.bestCm, cf.sizeCm);
            data.totalCaught++;
            var sp = cf.Species;
            float sizeBonus = 0.8f + 0.6f * sp.SizeT(cf.sizeCm);
            rep.xpGained = Mathf.RoundToInt(RarityInfo.BaseXp(sp.rarity) * sizeBonus * (rep.newSpecies ? 2f : 1f));
            rep.levelsGained = AddXp(rep.xpGained);
            return rep;
        }

        public void Sell(CaughtFish cf) => AddCoins(cf.value);

        // ------------------------------------------------------------------ aquarium
        /// <summary>The tank's space in 칸 (a fish takes 1 / 2 / 4 / 8 by its size class: AquaTank.Space).</summary>
        public int Capacity => Tank.capacity;
        public int UsedSpace => AquaTank.Used(data);
        /// <summary>Not even a small fish fits any more.</summary>
        public bool AquariumFull => UsedSpace + 1 > Capacity;

        /// <summary>Whether this fish goes into the tank ("" = yes; else "이 수조엔 너무 커요 · …" / "자리가 부족해요 · …").</summary>
        public string KeepRefusal(CaughtFish cf) => AquaTank.RefuseText(data, cf);

        /// <summary>Puts a fish in the tank if it fits (its size class and the 칸 left); false: refused (KeepRefusal says why).</summary>
        public bool AddToAquarium(CaughtFish cf)
        {
            if (AquaTank.CanAdd(data, cf, out _) != AquaTank.Fit.Ok) return false;
            CollectIncomeSilently();
            data.aquarium.Add(cf);
            AquaCare.OnAdded(cf); // (its caught size / value become the base it grows from)
            Notify();
            return true;
        }

        public int SellFromAquarium(string uid)
        {
            var f = data.aquarium.FirstOrDefault(x => x.uid == uid);
            if (f == null) return 0;
            CollectIncomeSilently();
            data.aquarium.Remove(f);
            data.coins += f.value;
            data.totalEarned += f.value;
            Notify();
            return f.value;
        }

        // viewing income (AquaCare): rarity x value, fed +20 % / hungry -30 %, banked on real time, 12 h after a collect
        public static int IncomeOf(CaughtFish f) => Mathf.Max(1, Mathf.RoundToInt(AquaCare.IncomeF(f)));

        public int IncomePerMinute => Mathf.RoundToInt(AquaTank.IncomePerMin(data)); // (decorations and dirt included)

        public int PendingIncome => AquaCare.Pending(data);

        /// <summary>Banks the income earned so far (and brings the fish up to now: a sale gets the grown price).</summary>
        void CollectIncomeSilently()
        {
            AquaCare.Advance(data, SaveSystem.Now);
            data.aquariumCollectedAt = SaveSystem.Now;
        }

        public int CollectIncome()
        {
            AquaCare.Advance(data, SaveSystem.Now);
            int p = data.aquariumBank;
            data.aquariumCollectedAt = SaveSystem.Now;
            data.aquariumBank = 0;
            if (p > 0) data.coins += p;
            data.totalEarned += p;
            Notify();
            return p;
        }

        public void SetSound(bool on)
        {
            data.soundOn = on;
            AudioListener.volume = on ? 1f : 0f;
            Notify();
        }

        public void SetReelRing(bool on)
        {
            data.reelRing = on;
            Notify();
        }

        public void SetReelReverse(bool on)
        {
            data.reelReverse = on;
            Notify();
        }

        /// <summary>설정 → 캐스팅 후 줌인 (the fishing view's zoom reads it every frame).</summary>
        public void SetZoomMode(ZoomMode m)
        {
            data.zoomMode = (int)m;
            Notify();
        }

        public void ResetProgress()
        {
            data = SaveData.NewGame();
            Notify();
        }
    }
}
