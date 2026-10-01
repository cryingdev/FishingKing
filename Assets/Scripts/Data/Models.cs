using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

    public enum ItemKind { Rod, Reel, Line, Bait, Tank }

    public static class RarityInfo
    {
        static readonly string[] Names = { "일반", "고급", "희귀", "영웅", "전설" };
        static readonly Color[] Colors =
        {
            new Color32(0xd8, 0xd8, 0xd8, 0xff), new Color32(0x6a, 0xd0, 0x6a, 0xff), new Color32(0x4a, 0xa8, 0xff, 0xff),
            new Color32(0xc0, 0x7a, 0xff, 0xff), new Color32(0xff, 0xc8, 0x30, 0xff),
        };
        // darker variants that stay readable on the cream paper panels
        static readonly Color[] Inks =
        {
            new Color32(0x6e, 0x66, 0x5a, 0xff), new Color32(0x2f, 0x8a, 0x2f, 0xff), new Color32(0x1f, 0x6f, 0xc4, 0xff),
            new Color32(0x8a, 0x3f, 0xd0, 0xff), new Color32(0xa8, 0x74, 0x00, 0xff),
        };
        static readonly int[] Xp = { 10, 25, 60, 150, 400 };

        public static string Name(Rarity r) => Names[(int)r];
        public static Color Color(Rarity r) => Colors[(int)r];
        public static Color InkColor(Rarity r) => Inks[(int)r];
        public static int BaseXp(Rarity r) => Xp[(int)r];
        public static int Stars(Rarity r) => (int)r + 1;
    }

    /// <summary>UI facts about lure actions: names, chip colours and icons (Sprites/UI/act_*), where a lure runs.</summary>
    public static class LureInfo
    {
        static readonly string[] Names = { "", "감기", "저킹", "수면", "바닥", "수직" };
        static readonly string[] Icons = { null, "act_steady", "act_twitch", "act_top", "act_bottom", "act_vertical" };
        static readonly Color[] Colors =
        {
            new Color32(0xff, 0xff, 0xff, 0xff), new Color32(0x6a, 0xb8, 0xff, 0xff), new Color32(0x6a, 0xd0, 0x6a, 0xff), new Color32(0xff, 0xd2, 0x4a, 0xff),
            new Color32(0xb8, 0x86, 0x5a, 0xff), new Color32(0xb5, 0x8a, 0xff, 0xff),
        };
        public static readonly LureAction[] Order = { LureAction.Steady, LureAction.Twitch, LureAction.Topwater, LureAction.Bottom, LureAction.Vertical };

        public static string Name(LureAction a) => Names[(int)a];
        public static string Icon(LureAction a) => Icons[(int)a];
        public static Color Color(LureAction a) => Colors[(int)a];

        /// <summary>Usable in an ice hole: natural baits, 바닥 and 수직 lures.</summary>
        public static bool IceOk(BaitDef b) => b.action == LureAction.None || b.action == LureAction.Bottom || b.action == LureAction.Vertical;

        public const string IceBanned = "얼음 구멍에서는 쓸 수 없어요";

        /// <summary>Where it works: 수면 (topwater, and the trolling lure that runs just under it), 중층, 바닥.</summary>
        public static string Zone(BaitDef b)
        {
            switch (b.action)
            {
                case LureAction.Topwater: return "수면";
                case LureAction.Bottom:
                case LureAction.Vertical: return "바닥";
                default: return b.buoyancy == Buoyancy.Sink && b.sinkSpeed < 0.5f && b.windLift >= 0.4f ? "수면" : "중층";
            }
        }
    }

    /// <summary>Hopper: short leap. Shaker: hangs in the air whipping its head. TailWalker: skips across the surface on its tail.</summary>
    public enum JumpStyle { Hopper, Shaker, TailWalker }

    /// <summary>The aquarium foods a kept fish eats (AquaCare; the table in GameDatabase.Diets): 사료 pellets, 생새우 live
    /// shrimp, 정어리 sardines. Omnivores take two.</summary>
    [System.Flags]
    public enum Diet { None = 0, Pellet = 1, Shrimp = 2, Sardine = 4 }

    /// <summary>How a kept fish takes a dropped shrimp / sardine: grabs it in mid-water, takes it on the gravel, or (the
    /// big ones) surges to the surface and gulps it.</summary>
    public enum FeedStyle { Grab, Bottom, Surge }

    public class FishSpecies
    {
        public string id;
        public string name;
        public Rarity rarity;
        public float minCm, maxCm;
        public float weightK;       // kg = k * cm^3 / 100000
        public int basePrice;       // price at average size
        public float power;         // pulling force (kgf) at max size
        public float stamina;       // seconds of hard fighting
        public float speed;         // swim speed (units/s) while running
        public float aggression;    // 0..1 tendency to run
        public float jump;          // 0..1 chance to jump when running near the surface
        public JumpStyle jumpStyle; // how it behaves in the air (see GameDatabase.BuildFish)
        public float depthMin, depthMax; // preferred depth below the surface (units)
        public Dictionary<string, float> baitPrefs = new Dictionary<string, float>();
        /// <summary>How much it likes a lure action in general ("@twitch:0.8" in the preference string).</summary>
        public Dictionary<LureAction, float> actionPrefs = new Dictionary<LureAction, float>();
        public string desc;
        /// <summary>
        /// A legend met through the underwater encounter (Docs/lures_legend_spec.md section 2) instead of spawning as a
        /// normal agent; null = the ordinary spawn-and-bite path.
        /// </summary>
        public EncounterDef encounter;
        // ---- cover (Docs/obstacles_spec.md 7.1): how often a run heads for its cover (0 = never), how far it looks for
        // one (m), how hard it digs in there, and the cover types it uses (rock, post, pad, reed, weed, boat, log, root, tet,
        // rim, hull, crystal)
        public float coverSeek, coverReach, coverDig = 1f;
        public string[] coverFor;
        // ---- the aquarium (GameDatabase.Diets): the foods it eats and how it takes a dropped piece
        public Diet diet = Diet.Pellet;
        public FeedStyle feedStyle;

        public float Pref(string baitId) => baitPrefs.TryGetValue(baitId, out float v) ? v : 0f;

        /// <summary>
        /// How much it likes this bait or lure: a natural bait by its own preference; a lure by the better of its own
        /// preference and 70 % of the fish's liking for the lure's action.
        /// </summary>
        public float Appeal(BaitDef b) => b == null ? 0f : b.action == LureAction.None ? Pref(b.id)
            : Mathf.Max(Pref(b.id), 0.7f * (actionPrefs.TryGetValue(b.action, out var a) ? a : 0f));

        public float WeightKg(float cm) => weightK * cm * cm * cm / 100000f;

        public float SizeT(float cm) => Mathf.InverseLerp(minCm, maxCm, cm);

        public int Price(float cm)
        {
            float t = SizeT(cm);
            return Mathf.Max(1, Mathf.RoundToInt(basePrice * (0.6f + 0.8f * t * t + 0.1f * t)));
        }
    }

    public abstract class ItemDef
    {
        public string id;
        public string name;
        public int price;
        public string desc;
        public abstract ItemKind Kind { get; }
    }

    public class RodDef : ItemDef
    {
        public float castDist;   // max cast distance (metres)
        public float flex;       // 0..1: absorbs tension spikes, longer break grace
        public float hookBonus;  // extra seconds in the hook-set window
        public float luck = 1f;  // rare fish multiplier
        public float length = 3f; // drawn rod length (units)
        public Color blank = Color.white, grip = Color.gray;
        // painted details (match the shop icon, Tools/Blender/fk_items.py RODS)
        public Color blank2 = Color.gray;        // shaded underside of the blank
        public Color seat = Color.gray;          // reel seat, winding check, gimbal
        public Color wrap = Color.gray;          // thread wraps under the guides
        public Color glow = Color.clear;         // glowing tip + pommel (alpha 0 = none)
        public bool nodes;                       // bamboo nodes along the blank
        public bool gimbal;                      // big-game gimbal butt
        public bool thick;                       // heavier blank: 2 px wide for longer
        public override ItemKind Kind => ItemKind.Rod;
    }

    public class ReelDef : ItemDef
    {
        public float retrieve;   // units of line per handle revolution
        public float dragMax;    // kgf at which the drag starts slipping
        public float dragSmooth; // 0..1: how much of the excess tension the drag absorbs
        public float lineCap;    // max line out (units / m)
        public float autoReel;   // passive retrieve (units/s) for electric reels
        public override ItemKind Kind => ItemKind.Reel;
    }

    public class LineDef : ItemDef
    {
        public float strength;   // kgf
        public float stealth = 1f; // bite rate multiplier
        /// <summary>Abrasion resistance: the line's wear rubbing on structure is divided by it (Docs/obstacles_spec.md 7.5).</summary>
        public float tough = 1f;
        public Color color = Color.white;
        public override ItemKind Kind => ItemKind.Line;
    }

    /// <summary>How a lure is worked (none = a natural bait under a float). UI names: 감기 · 저킹 · 수면 · 바닥 · 수직.</summary>
    public enum LureAction { None, Steady, Twitch, Topwater, Bottom, Vertical }

    /// <summary>What a lure does at rest: sinks to the bottom, hangs at its swim depth, or floats up.</summary>
    public enum Buoyancy { Sink, Suspend, Float }

    /// <summary>The input a lure's "work" is made of: reel circles or 톡 flicks (short quick pulls DOWN the screen, <see cref="LureInput"/>).</summary>
    public enum Work { Wind, Flick }

    public class BaitDef : ItemDef
    {
        public bool isLure;      // lures need motion and are not consumed (lost when the line breaks)
        public bool infinite;    // free starter bait
        public int packSize = 1;
        public float sinkSpeed = 1.2f;
        public float rareBoost = 1f;

        // ---- lures (natural baits keep the defaults, action None); see Docs/lures_legend_spec.md 1.2 / 1.6
        public LureAction action;
        public Buoyancy buoyancy;            // Sink
        public float fallSpeed;              // m/s after a flick or a stop (0 = sinkSpeed)
        public float riseSpeed = 0.35f;      // Float: back up to 0.05 m at rest
        public float swimDepth;              // Suspend: hang depth
        public float maxDepth;               // deepest running depth (0 = bottom)
        public float windLift = 0.45f;       // depth lost per metre wound (negative dives)
        public float hopM, hopBase = 1f;     // flick lift = hopM * (hopBase + (1-hopBase)*strength); negative dives
        public float pullM = 0.25f, dartM;   // flick drag to shore; sideways dart
        public Work work;
        public Vector2 reelBand;             // rev/s: Steady = the good wind; a Flick lure with runMax > 0 also takes a slow drag in it
        public Vector2Int flicks = new Vector2Int(1, 1);
        public Vector2 gap, rest;            // s
        public float runMax;                 // Wind work with a rest (frog 2 s)
        public float maxStrength = 1f;
        public bool needBottom, glow;
        public string hint;                  // one line: how to work it (Waiting hint, picker, shop)
        // strike windows (when a following fish may strike; 1.2 "Strike window")
        public float restStrike = -1f;       // cycle lures: open from this far (s) into the rest until it ends (-1 = no)
        public Vector2 fallStrike = new Vector2(-1f, -1f); // open while falling after a flick, fall time in [x, y] s (x < 0 = no)
        public float landStrike;             // open this long (s) after touching the bottom
        public bool burstStrike;             // open during the flick burst
        public float pauseStrike;            // Steady lures: open this long (s) into a pause (the spoon's flutter)

        public bool IsSteady => action != LureAction.None && work == Work.Wind && rest.y <= 0f;

        public override ItemKind Kind => ItemKind.Bait;
    }

    public class TankDef : ItemDef
    {
        public int level;
        public int capacity;                 // space units (칸): 소형 1, 중형 2, 대형 4, 초대형 8 (AquaTank.Space)
        public int maxClass;                 // the biggest size class it takes (0 소형 .. 3 초대형)
        public int maxHuge;                  // at most this many 초대형 fish (0 = only the 칸 limit)
        public override ItemKind Kind => ItemKind.Tank;
    }

    public class StageDef
    {
        public string id;
        public string name;
        public string subtitle;
        public int difficulty;      // 1..5 stars
        public int reqLevel;
        public int unlockCost;
        public float powerMult = 1f;
        public float biteMult = 1f;
        public int population = 8;
        public List<KeyValuePair<string, float>> spawns = new List<KeyValuePair<string, float>>();
    }

    // ---------------------------------------------------------------------------------------- legend encounter data
    // Docs/lures_legend_spec.md 2.1 and Docs/legends_rollout.md 1. Data only: the encounter itself (LegendWatch /
    // LegendEncounter / EncounterView) is built on top of it. A species without a row keeps encounter = null.

    /// <summary>What a mood rewards: a wind in a band, a 톡 then a pause, holding still, or a short wind run then a pause.</summary>
    public enum Verb { Wind, FlickPause, Hold, RunPause }

    public class MoodDef
    {
        public string name, prompt;
        public Verb verb;
        public float lo, hi;          // Wind / RunPause: rev/s band; FlickPause: pause window (s); Hold: grace before gains (s)
        public float gain;            // /s (Wind, Hold) or per credited cycle (FlickPause, RunPause)
        public float maxStrength = 1f, strongAt = 2f, strongLoss;   // FlickPause
        public float hurryLoss, circleAfter = 99f, circleLoss, boredAfter = 99f, boredLoss;
        public float tooFast = 99f, fastLoss, flickLoss;            // Wind / Hold / RunPause
        public float circleGrace;                                   // Hold
        // Wind: too slow (winding under tooSlow, or not winding for longer than slowAfter): slowLoss/s (0 = off)
        public float tooSlow, slowLoss, slowAfter = 99f;
        // RunPause: a run of runMin..runMax s, >= 70 % of it inside lo..hi, then a pause of pauseMin gives +gain once;
        // a run longer than overRun costs overRunLoss/s (pauseMax: the pause window's end, for the autopilot)
        public float runMin, runMax, pauseMin, pauseMax, overRun = 99f, overRunLoss;
        // caption overrides (null = the generic one)
        public string creditText, fastText, slowText, overRunText, boredText;
    }

    /// <summary>Where the lure rests in the encounter's set: on the floor, on the surface film, in open water.</summary>
    public enum LureAt { Floor, Surface, Mid }

    /// <summary>Which quality fills a legend's meter: the lure's action Q, stillness (natural baits), a slow crawl.</summary>
    public enum MeterQ { Lure, Still, Crawl }

    /// <summary>A key lure's own trigger rules (the ocean: one lure, two legends); NaN / null = the row's value.</summary>
    public class KeyRule
    {
        public float depthMin = float.NaN, depthMax = float.NaN, bottomBand = float.NaN;
        public MeterQ? meterQ;
    }

    /// <summary>How a legend moves and bites in its encounter (EncounterView branches on the style).</summary>
    public enum ChoreoStyle { Coelacanth, Carp, Arapaima, Sturgeon, Marlin, GreatWhite }

    /// <summary>
    /// A legend's swim profile and approach (Docs/legends_rollout.md 1.4, 3.x). Defaults are the coelacanth's
    /// (EncounterView's constants before the rollout), so its row needs none of this.
    /// </summary>
    public class Choreo
    {
        public ChoreoStyle style = ChoreoStyle.Coelacanth;
        /// <summary>Spine.F, B1, B2, B3, Tail swim amplitudes (degrees).</summary>
        public float[] swim = { 2f, 4f, 6f, 8f, 12f };
        /// <summary>Tail-beat Hz: 경계, 호기심, 흥분, lunge.</summary>
        public Vector4 tailHz = new Vector4(0.6f, 0.8f, 1.0f, 2.5f);
        public float cruise = 1.7f;               // m/s
        /// <summary>The Eyes beat's drift (the nose, set frame) and the direction of the deep.</summary>
        public Vector3 eyes0 = new Vector3(5.2f, 0.45f, 7.2f), eyes1 = new Vector3(4.0f, 0.4f, 5.2f), deepDir = new Vector3(0.63f, 0f, 0.78f);
        public Vector2 orbitSpeed = new Vector2(0.35f, 0.5f);   // 경계 / 호기심, rad/s
        public float orbitDepth;                  // offset of the orbits from the lure (m, + = above)
        /// <summary>The 흥분 pose: x = nose height over the lure (m), y = pitch (degrees, + = nose down).</summary>
        public Vector2 hover;
        public float finAmp = 1f;                 // x the paired fins' 25 degree trot
        public float flare = -30f;                // Pec.L yaw at full flare (Pec.R mirrored; pelvics 2/3 of it)
        public float scull = 25f;                 // Dorsal2 / Anal scull amplitude
        public float barbelSign = 1f;             // the barbels' "feel" curl: +X (the sturgeon) or -X (the carp: they trail back)
        public bool barbelRoll;                   // the barbels sway about Z (they hang down) instead of Y
        public float cruiseJaw;                   // the jaw's rest opening while swimming (the great white 5)
    }

    /// <summary>How the tease is shown: the underwater side view, or from above the surface (a topwater lure).</summary>
    public enum TeaseView { Side, Top }

    /// <summary>
    /// The top view of a topwater legend's tease (EncounterView's top mode; the arapaima's frog and popper): the encounter
    /// opens underwater as usual, the camera rises through the waterline at the approach's end and looks straight down at the
    /// lure for the whole tease and nose-in (the lure driven by the player's input, the legend a dark shadow under the
    /// murky surface), and cuts back to the full-screen underwater view for the lunge and the bite. The art is
    /// Tools/Blender/variants/hybrid/encounter_sets/&lt;set&gt;.py (Resources/Sprites/Encounter): uw_&lt;set&gt;_bg / _mid /
    /// _fore / _float_*, lure_&lt;lure&gt;_top_0..3 (24x28, the same pivot and line tie for every lure; 0 at rest, 1 / 2 the
    /// two beats of a swim stroke, 3 the pop) and the shared fx_top_*. A lure without top frames keeps the side view.
    /// Distances in metres (set frame), screen axes: x across the window, y up it (away from the angler).
    /// </summary>
    public class TopViewDef
    {
        public string set = "swamp_top";
        public float camHeight = 2.5f;                          // the camera over the surface (m)
        public float surfacePx = 56f;                           // px per metre on the surface
        // (the lure's pivot 67 px up a 136 px window: its box stays clear of the gauge's, the captions' and the name
        // card's bands above and below it)
        public Vector2 lureAt = new Vector2(0.5f, 0.49f);       // the lure's pivot in the window (0..1)
        public Vector2 lineDir = new Vector2(-0.28f, -1f);      // the line leaves the lure's nose this way (screen)
        public float travel = 0.3f;                             // m the water slides past the lure per reel turn
        /// <summary>
        /// 경계 / 호기심 patrols: ellipse semi-axes across and up the screen (m), depth of the swim (m). Wide and flat: the
        /// legend passes across the window and turns near its sides, so its head keeps out of the prompt and the gauge.
        /// </summary>
        public Vector3 waryPath = new Vector3(2.6f, 0.45f, 1.3f), curiousPath = new Vector3(1.5f, 0.35f, 0.6f);
        public Vector2 pathSpeed = new Vector2(0.3f, 0.5f);     // rad/s round them
        /// <summary>The approach ends on the 경계 path at this angle (rad; pi/2 = straight up the screen from the lure,
        /// swimming across it), arriving along the path.</summary>
        public float entryAt = 1.5708f;
        /// <summary>흥분 / nose-in: x = the nose's depth under the lure (m), y = pitch (degrees, - = head up).</summary>
        public Vector2 hover = new Vector2(0.45f, -18f), noseIn = new Vector2(0.22f, -30f);
        /// <summary>
        /// The legend's shadow (darkened with the water by the time of day). Against the murky water (luma ~0.24) it
        /// reads like the stage's fish shadows on dark water: at least 50% darker even at shadowDeep, ~70% through the
        /// tease's passes (경계, 호기심, 흥분; the coelacanth's silhouette against its cave water: ~79%).
        /// </summary>
        public string shadow = "#060905";
        public Vector2 shadowAlpha = new Vector2(1.0f, 0.6f);   // at the surface / at shadowDeep
        public Vector2 shadowSoft = new Vector2(0.6f, 2.4f);    // edge blur (px) at the surface / at shadowDeep
        public float shadowDeep = 2.0f;
        public float shadowDetail = 0.15f;                      // the fish's own colour showing through when shallow
        public float riseT = 0.7f;                              // the rise through the waterline (the approach's last s)
        public float surfaceBoil = 1.8f;                        // the boil at the fight's start (x the usual)
        /// <summary>Captions in the top view: the nose-in's (instead of tellText) and the 흥분 credit's (null = the row's).</summary>
        public string noseInText, holdCredit;
    }

    public class EncounterDef
    {
        public string model, backdrop, nameSub, tipWrongLure;
        /// <summary>The tease from above the surface (a topwater legend, <see cref="top"/>); Side = the underwater view.</summary>
        public TeaseView teaseView = TeaseView.Side;
        public TopViewDef top;
        public Dictionary<string, float> keyLures = new Dictionary<string, float>();
        public float depthMin, bottomBand, lurkZMin, lurkZMax, nearLurk;
        public float minQ, minSoak, fillTime, chance, minLine;
        public float coolFail, coolFight;
        public Vector2 appear, relocate;
        public int gaugeStart, pityStep, pityMax;
        public float decay, teaseLimit;
        public int timeoutStrike;
        public MoodDef[] moods;       // [0] 경계, [1] 호기심, [2] 흥분
        public int curiousAt = 40, excitedAt = 75, hysteresis = 5, turnAwayBelow = 15, earlyGauge = 65;
        public Vector2 noseIn;
        public float tell, hookWindow, perfectT, buffer;
        public float lightGlow, lightPlain, orbitFar, orbitNear, noseDist;
        public string eyeCore, eyeGlow, frameLine;

        // ---- the rollout (Docs/legends_rollout.md 1.1); the defaults play and look exactly like the coelacanth
        public float depthMax;                    // the lure at most this deep (0 = off)
        public float lurkDepth;                   // lurk depth under the surface (0 = DepthAt(z) - 0.5)
        public Dictionary<string, KeyRule> keyRules = new Dictionary<string, KeyRule>();
        public MeterQ meterQ = MeterQ.Lure;
        public int fakeOuts;                      // fake-outs in NoseIn (0 or 1)
        public string fakeOutText;
        public string biteText = "덥석!";
        public float lungeT = 0.3f, hitStop = 0.12f;
        public Vector2 shake = new Vector2(0.15f, 0.15f);
        public string lurkText = "깊은 곳에서 무언가 눈을 떴다…";
        public string eyesText = "어둠 속에서 무언가 다가온다…";
        public string tellText = "…노려본다";
        public string hookedText = "{name} 걸렸다! 원을 그려 감아요!";   // {name} = the name with its subject particle
        public string lostText = "{name} 어둠 속으로 사라졌다…";
        /// <summary>The fail tips by the worst mistake: 경계, 호기심, 흥분, early tap (null = the generic kind-based tips).</summary>
        public string[] failTips;
        public string catchLine;                  // an extra line on the catch card
        public string gearLine = "더 튼튼한 줄";   // "이 녀석을 상대하려면 {gearLine}이 필요할 것 같다… (N kg 이상)"
        public Choreo choreo = new Choreo();
        public float camScale = 1f;               // x the camera's rest distance (huge fish)
        /// <summary>
        /// The nose-in's push-in (x or y 0 = none): the camera moves in on the hovering fish until its outline fills
        /// noseFill of the window (x: its width, y: its height, whichever is reached first), the fish and the lure centred on
        /// <see cref="noseFrame"/>, so the head, the lips and the bait read through the fake-out and the tell;
        /// <see cref="noseCamTease"/> of the way in already in 흥분. The lunge ends on its usual framing.
        /// </summary>
        public Vector2 noseFill;
        public float noseCamTease;
        public Vector2 noseFrame = new Vector2(0.5f, 0.5f);
        /// <summary>A mood just entered is kept this long before the gauge can drop it back (0 = the hysteresis alone).</summary>
        public float moodHold;

        // ---- the spot (Docs/lures_legend_spec.md 2.2.1): with each cue a spot on the water near the lurk point blinks;
        // a new cast must come down within spotRadius of it inside spotWindow s for the meter to run at all
        public float spotWindow = 12f;            // s the spot blinks (the blink quickens over its last 4 s)
        public float spotRadius = 1.5f;           // m: a landing this close to it claims it
        public float spotRetry = 8f;              // s after a miss (or a claim lost) before the next spot (no cooldown, no pity)
        public float spotHold;                    // m the claimed rig may move from it (0 = nearLurk: worked lures travel)

        /// <summary>How much this lure draws the legend (0 = not a key lure).</summary>
        public float KeyWeight(string baitId) => baitId != null && keyLures.TryGetValue(baitId, out float w) ? w : 0f;

        public KeyRule Rule(string baitId) => baitId != null && keyRules.TryGetValue(baitId, out var r) ? r : null;

        /// <summary>A natural bait is among the keys (the golden carp): wrong natural baits count for the wrong-lure tip.</summary>
        public bool BaitEater
        {
            get
            {
                foreach (var k in keyLures.Keys)
                {
                    var b = GameDatabase.GetItem<BaitDef>(k);
                    if (b != null && !b.isLure) return true;
                }
                return false;
            }
        }

        /// <summary>A text with "{name}" replaced by the legend's name and its subject particle (이 / 가).</summary>
        public static string Named(string text, string name) =>
            string.IsNullOrEmpty(text) ? text : text.Replace("{name}", LegendEncounter.Ga(name));
    }

    /// <summary>One god-ray: tint, alpha, x of its left edge from the view's right edge, y of its top from the view's top (px).</summary>
    public struct RayDef
    {
        public string tint;
        public float alpha, xFromRight, y;
        public RayDef(string tint, float alpha, float xFromRight, float y)
        {
            this.tint = tint;
            this.alpha = alpha;
            this.xFromRight = xFromRight;
            this.y = y;
        }
    }

    /// <summary>
    /// An encounter backdrop and its light profile (Docs/legends_rollout.md 4.1): the set frame has the lure's rest
    /// point at the origin (x, z) and the layers from Tools/Blender/variants/hybrid/encounter_sets/&lt;id&gt;.py. The cave
    /// row carries EncounterView's constants from before the rollout.
    /// </summary>
    public class EncounterSetDef
    {
        public string id;
        public LureAt lureAt = LureAt.Floor;
        public float lureRest = 0.04f;            // the lure's rest height (set y)
        public bool hasFloor = true;
        public float floorY;                      // set y of the floor
        public float surfaceY = float.NaN;        // set y of the surface / ice underside (NaN = not in the set)
        public float horizonY;                    // set y of the point the bg / mid / floor bands sit on (30 m out)
        public int bgHorizon = 150;               // the bg row (from its top) on that horizon
        public bool ceiling;                      // uw_<id>_ceiling: bottom row on (cam.x, surfaceY, cam.z + 30)
        public string ceilFill;                   // the underside's colour above the ceiling layer's top edge (null = none)
        public Vector3 camRest = new Vector3(-1.2f, 0.45f, -2.7f);
        public float camDist = 3f;                // the camera's rest distance from the lure (x the legend's camScale)
        public Vector2 frameAt = new Vector2(0.33f, 0.30f);   // where the lure sits in the window (0..1)
        public Vector3 lineUp = new Vector3(-0.9f, 2.2f, -1.6f);
        public Vector3 dragDir = new Vector3(-0.45f, 0f, -0.9f);
        public float lureBound = 1.5f;            // how far the lure may move from its rest (m, flat)
        public float lureDrag = 0.08f;            // m the lure is dragged per reel turn (floor sets)
        public float lureHop, lureFall;           // a flick's hop (m) and the fall (m/s) in this set (0 = by the lure)
        public string clear = "#04101a", abyss = "#03080c", fogOutline = "#1f5f70";
        /// <summary>
        /// The silhouette outside the lure light where abyss is the water itself (a daylight murk): the body in
        /// <see cref="abyssNear"/> (a step darker than the water) with a <see cref="fogNear"/> contour while the fish is
        /// within silFade.x m of the light, melting into abyss / fogOutline by silFade.y m (null = abyss / fogOutline:
        /// no silhouette of its own).
        /// </summary>
        public string abyssNear, fogNear;
        public Vector2 silFade;
        public float sunMix;                      // ActorToon _SunMix (0 = lit by the lure alone)
        public Vector3 keyDir = new Vector3(0.30f, 0.93f, 0.20f);   // the sun (towards the light, set frame)
        public float lightUp;                     // the light's centre this far above the lure (ice: the hole's column)
        public RayDef[] rays = { new RayDef("#86dcff", 0.08f, 150f, -20f) };
        public bool rayAnchored;                  // uw_<id>_ray from the projected hole (surfaceY) down to the floor
        public string halo = "#9affea";           // null = no halo
        public float haloGlow = 0.9f, haloPlain = 0.35f;
        public string haloBait;                   // this bait's halo alpha is haloBaitAlpha (the lake's golden bait)
        public float haloBaitAlpha;
        public string snowFar = "#3a6a7a", snowNear = "#9fd8e0";
        public string silt = "#8fb8b8";           // null = open water (bubbles instead)
        public string line = "#8fb8c0";
        public string rimCol;                     // null = the stage's rim (ActorArt.RimFor)
        public float rimStrength;
        public Vector3[] rimDirs;
        public string veil;                       // the Eyes beat darkens the back layers (null = none)
        public float veilAlpha;
        public string frameLine = "#6affea";
        public float foreX = -20f;
        public float midPulse = 0.1f;             // the cave's crystals pulse (+-, 0.3 Hz)
    }
}
