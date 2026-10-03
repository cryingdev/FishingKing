using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FishingKing
{
    [Serializable]
    public class CaughtFish
    {
        public string uid;
        public string speciesId;
        public float sizeCm;
        public float weightKg;
        public int value;
        public string stageId;
        public long caughtAt; // unix seconds
        // aquarium care (AquaCare): size / weight / value when put in the tank, when it was put in, the last real-time
        // update, fed days accrued (growth), fullness 0..1 and the premium share of what is in its stomach
        public float baseCm, baseKg;
        public int baseValue;
        public long addedAt, careAt;
        public float fedDays, fullness, premiumShare;

        public FishSpecies Species => GameDatabase.GetFish(speciesId);
    }

    [Serializable]
    public class BaitCount
    {
        public string id;
        public int count;
    }

    /// <summary>The line left on a line item's spool (m) once it has been cut; a line with no entry is full.</summary>
    [Serializable]
    public class LineSpool
    {
        public string id;
        public float left;
    }

    [Serializable]
    public class SpeciesRecord
    {
        public string id;
        public int caught;
        public float bestCm;
    }

    /// <summary>A legend met through its underwater encounter: times seen (reached the approach), failed, and the pity
    /// bonus on the next encounter's starting interest (rises by the legend's pityStep per fail, reset when it is landed),
    /// and its cooldown: away until coolUntil (unix seconds, wall clock: it runs on while the app is closed; 0 = none),
    /// coolLen the length it was given (a device clock set back never makes it longer: LegendWatch.Remaining).</summary>
    [Serializable]
    public class LegendRecord
    {
        public string id;
        public int seen, fails, pity;
        public long coolUntil;
        public int coolLen;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public int coins = 300;
        public int level = 1;
        public int xp;
        public List<string> ownedItems = new List<string>();
        public List<BaitCount> baits = new List<BaitCount>();
        public List<LineSpool> lineSpools = new List<LineSpool>();
        public string rod = GameDatabase.StarterRod;
        public string reel = GameDatabase.StarterReel;
        public string line = GameDatabase.StarterLine;
        public string bait = GameDatabase.StarterBait;
        // hooks for the natural-bait rigs (a save from before them: none bought, the free small hook on)
        public List<BaitCount> hooks = new List<BaitCount>();
        public string hook = GameDatabase.StarterHook;
        public int tankLevel;
        public List<CaughtFish> aquarium = new List<CaughtFish>();
        public long aquariumCollectedAt;
        public int aquariumBank;
        public double aquaCarry;                                     // the income's fraction of a coin (AquaCare)
        public int aquaVer;                                          // 0 = a save from before feeding (AquaCare.Ensure migrates it)
        public List<FeedStock> feed = new List<FeedStock>();         // feed bags owned (AquaCare)
        public bool feedTornOnce;                                    // a bag has been torn open once: the scissors hint stops looping
        public TankCare tank = new TankCare();                       // the tank's dirt, tools and decorations (AquaTank)
        public int capVer;                                           // 0 = a save from before the 칸 capacity (AquaCare.Ensure logs its fit)
        public List<string> unlockedStages = new List<string> { "lake" };
        public List<SpeciesRecord> records = new List<SpeciesRecord>();
        public List<LegendRecord> legends = new List<LegendRecord>();
        public string lastStage = "lake";
        public bool soundOn = true;
        public bool musicOn = true;      // 설정 → 음량 → 배경음 켜짐/꺼짐 (Music); a save from before it reads true (JsonUtility keeps the initialiser)
        // 설정 → 음량 (AudioMix): 0..100 in steps of 10, multipliers on the designed mix (100 = as designed); a save from
        // before them reads 100 (the initialisers), soundOn stays the master mute on top
        public int masterVol = 100, musicVol = 100, sfxVol = 100, ambVol = 100;
        public bool reelRing = true;     // the circle + direction arrows shown while drawing reel circles
        public bool guideText = true;    // 설정 → 조작 → 조작 안내 문구: the how-to lines (the hint bar, the flashes' instructions, the snag strip's guide); a save from before it reads true
        public bool reelReverse;         // counter-clockwise winds in (default: clockwise)
        public int zoomMode;             // 캐스팅 후 줌인 (ZoomMode): 0 1.25배 (default; older saves), 1 끔, 2 1.5배, 3 액티브
        // 설정 → 조작 (Angler.ReadSettings): 왼손 = the rod in the right hand, the left hand cranking (default 오른손: the rod in
        // the left hand); 가운데 = the rod held in front of the belly (default 옆: out at the hip). A save from before them reads
        // false: 오른손, 옆 (the hold the game always had)
        public bool leftHanded;
        public bool rodCentre;
        public int totalCaught;
        public int totalEarned;
        public bool tutorialDone;
        public bool sweepHint;           // the rod sweep's one-time hint (first rig in the water) was shown
        public bool sideHint;            // side pressure's one-time hint (first long run in a fight) was shown
        // the game clock (Docs/time_currents_spec.md 1, 13): game minutes since 00:00 and the game day; runs only on a stage
        public float clockMin = 600f;    // 10:00
        public int clockDay = 1;
        public bool timeHint;            // the first period change's hint was shown
        public bool tideHint;            // the sea's tide hint was shown
        public bool driftHint;           // the stream drift hint was shown
        public bool mendHint;            // the mending hint was shown
        public bool pinHint;             // the hint for a float the tide holds at the edge of the view was shown
        public bool spotHint;            // the legend spot's one-time hint (cast into the splashing spot) was shown
        // the generated lake bed (Docs/terrain_depth_spec.md 12): its world seed (a new game is a new lake; 0 = a save from
        // before the terrain: Game.Boot mints one, once) and the lying float's one-time hint
        public int worldSeed;
        public bool lieHint;

        public static SaveData NewGame()
        {
            var d = new SaveData();
            d.worldSeed = SaveSystem.NewSeed();
            d.ownedItems.AddRange(new[] { GameDatabase.StarterRod, GameDatabase.StarterReel, GameDatabase.StarterLine, GameDatabase.StarterBait, GameDatabase.StarterHook, "tank_0" });
            d.aquariumCollectedAt = SaveSystem.Now;
            return d;
        }
    }

    public static class SaveSystem
    {
        static string PathFile
        {
            get
            {
                // -fksave <name> keeps test runs away from the real save
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-fksave");
                string name = i >= 0 && i + 1 < args.Length ? args[i + 1] : "fishingking_save";
                return System.IO.Path.Combine(Application.persistentDataPath, name + ".json");
            }
        }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>A new world seed: positive, never 0 (0 marks a save from before the terrain). Not UnityEngine.Random.</summary>
        public static int NewSeed()
        {
            int s;
            do { s = Guid.NewGuid().GetHashCode() & 0x7fffffff; } while (s == 0);
            return s;
        }

        /// <summary>A save from before the terrain gets its world seed (once: it is then saved). True when one was minted.</summary>
        internal static bool EnsureWorldSeed(SaveData d)
        {
            if (d.worldSeed != 0) return false;
            d.worldSeed = NewSeed();
            return true;
        }

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFile));
                    if (d != null) return Sanitize(d);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FishingKing] Could not read save, starting fresh: " + e.Message);
            }
            return SaveData.NewGame();
        }

        public static void Save(SaveData d)
        {
            try
            {
                string tmp = PathFile + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d));
                if (File.Exists(PathFile)) File.Delete(PathFile);
                File.Move(tmp, PathFile);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FishingKing] Save failed: " + e.Message);
            }
        }

        public static void Delete()
        {
            if (File.Exists(PathFile)) File.Delete(PathFile);
        }

        internal static SaveData Sanitize(SaveData d)
        {
            d.ownedItems ??= new List<string>();
            d.baits ??= new List<BaitCount>();
            d.hooks ??= new List<BaitCount>();
            d.lineSpools ??= new List<LineSpool>();
            d.aquarium ??= new List<CaughtFish>();
            d.records ??= new List<SpeciesRecord>();
            d.legends ??= new List<LegendRecord>();
            d.unlockedStages ??= new List<string>();
            // (not the starter line: one thrown away when too little was left stays so until it is bought again, free)
            foreach (var id in new[] { GameDatabase.StarterRod, GameDatabase.StarterReel, GameDatabase.StarterBait, GameDatabase.StarterHook, "tank_0" })
                if (!d.ownedItems.Contains(id)) d.ownedItems.Add(id);
            if (!d.unlockedStages.Contains("lake")) d.unlockedStages.Add("lake");
            if (GameDatabase.GetItem<RodDef>(d.rod) == null) d.rod = GameDatabase.StarterRod;
            if (GameDatabase.GetItem<ReelDef>(d.reel) == null) d.reel = GameDatabase.StarterReel;
            if (GameDatabase.GetItem<LineDef>(d.line) == null) d.line = GameDatabase.StarterLine;
            if (GameDatabase.GetItem<BaitDef>(d.bait) == null) d.bait = GameDatabase.StarterBait;
            if (GameDatabase.GetItem<HookDef>(d.hook) == null) d.hook = GameDatabase.StarterHook;
            if (GameDatabase.LoadErrors.Count == 0) d.aquarium.RemoveAll(f => f == null || GameDatabase.GetFish(f.speciesId) == null);
            else
            {
                // the species data did not load cleanly (GameDatabase.LoadErrors): a species may only be missing for now,
                // so its fish are kept rather than dropped and saved
                d.aquarium.RemoveAll(f => f == null);
                int kept = 0;
                foreach (var f in d.aquarium) if (GameDatabase.GetFish(f.speciesId) == null) kept++;
                if (kept > 0) Debug.LogError($"[DATA] aquarium kept: {kept} fish of unloaded species");
            }
            if (d.aquariumCollectedAt <= 0) d.aquariumCollectedAt = Now;
            d.level = Mathf.Max(1, d.level);
            d.clockMin = float.IsNaN(d.clockMin) || float.IsInfinity(d.clockMin) ? 600f : Mathf.Clamp(d.clockMin, 0f, 1439.99f);
            d.clockDay = Mathf.Max(1, d.clockDay);
            if (d.zoomMode < 0 || d.zoomMode > 3) d.zoomMode = 0;
            d.masterVol = AudioMix.Snap(d.masterVol);
            d.musicVol = AudioMix.Snap(d.musicVol);
            d.sfxVol = AudioMix.Snap(d.sfxVol);
            d.ambVol = AudioMix.Snap(d.ambVol);
            return d;
        }
    }
}
