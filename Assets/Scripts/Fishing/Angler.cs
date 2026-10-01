using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The angler seen from behind: a rod that bends towards the line under tension and the fishing line (with sag when
    /// slack). Rod and line are built in 3D game space and projected through the stage camera, then drawn as 1-2 px
    /// lines in the pixel scene. The angler and his reel are real-time 3D models (<see cref="Angler3D"/>,
    /// <see cref="Reel3D"/>) drawn as pixel layers (<see cref="ActorLayer"/>): the left fist holds the rod grip, the
    /// crank turns with the reel revolutions and the right fist follows its knob. Without the models (or with the
    /// -fkactors2d switch) the pose sprites and the 2D reel sprite are used instead.
    /// He can walk sideways along the stage's standing area (<see cref="WalkInput"/>, <see cref="X"/>); the 3D
    /// figure turns towards <see cref="FaceTarget"/> and the rod swings round with him.
    /// </summary>
    // LateUpdate after the fish (0: their drawn mouth, which the underwater line ends at) and before the Tackle (90: the
    // float on this frame's line), the arrows (100) and the view's zoom (950): everything drawn from the rod tip and the
    // line in a frame uses the tip and the line of that same frame
    [DefaultExecutionOrder(50)]
    public class Angler : MonoBehaviour
    {
        // the underwater part of the line sits above the fish shadows (10-19) and under the front layer (40)
        // the reel sits just under the rod (rod - 1) and the line along the rod under the reel (rod - 2)
        public const int OrderBody = 50, OrderRodBehind = 47, OrderRodFront = 53, OrderLine = 46, OrderUnderLine = 20;

        // how far the entry point moves from the straight tip-to-fish crossing towards the point above the
        // fish: the underwater part runs steeper, so the line looks bent at the surface (refraction)
        const float SurfaceBend = 0.35f;

        /// <summary>The rod tip as drawn this frame (the hat guard's tilt included).</summary>
        public Vector3 RodTip { get; private set; }
        /// <summary>
        /// The rod tip this frame as the rod is held (the hand and the pose's rod, lifted / jerked), before it bends towards
        /// the line and before the hat guard's tilt (<see cref="KeepOffHat"/>). What the fight measures the line from and
        /// places the fish by: both the bend and the tilt follow the line to the fish, so a tip that includes them makes the
        /// fish's place feed back into itself through the line's direction. Close in under the stand, with the line steep,
        /// that loop overshot (the fish, the line, the water entry and the rod swapping between two places every frame), and
        /// the tilt's instant change of side made it flip for single frames. The drawn rod still bends and tilts
        /// (<see cref="RodTip"/>).
        /// </summary>
        public Vector3 RodTipPlan { get; private set; }
        public Vector3 Hand { get; private set; }
        public string Pose { get; private set; } = "idle";

        /// <summary>
        /// Where the line goes (float, lure entry point or fish). Null = dangling bait. A point under water
        /// (a hooked fish's mouth) makes the line enter the water at <see cref="WaterEntry"/> and run on, dimmed, to it.
        /// </summary>
        public Vector3? LineTarget;

        /// <summary>
        /// The rig (set once by the controller): while it rests perched on a prop's top, the line's end lying there is tested
        /// against the front layer a little nearer (<see cref="FrontOcclusion.Resting"/>), like the bait resting there, so the
        /// top it lies on never cuts it short of the bait (<see cref="RestReach"/>). Read from the rig's own state in the same
        /// LateUpdate that draws the line: no per-frame flag to miss.
        /// </summary>
        public Tackle Rig;
        // the line's vertices within RestFull m of the perched bait are tested nearer by the whole Resting, fading to none at
        // RestReach: only the part lying on the prop's top (the rest of the line keeps its true depth)
        const float RestFull = 0.25f, RestReach = 0.5f;

        /// <summary>The hooked fish: the underwater line ends at its drawn mouth.</summary>
        public FishAgent Fish;

        /// <summary>Where the line cuts the surface while <see cref="LineTarget"/> is under water.</summary>
        public Vector3 WaterEntry { get; private set; }
        public bool LineUnderwater { get; private set; }
        public float Tension01;
        public float Slack01 = 0.3f;
        /// <summary>0..1 how close the line is to breaking: the rod tip and the line shiver with it.</summary>
        public float Strain01;
        /// <summary>0..1 raises the rod from the pose's angle to upright (landing a fish).</summary>
        public float RodLift01;
        /// <summary>
        /// 0..1 the 톡's rod jerk (set by the controller: a quick rise, a short hold, an easy return): the rod snaps up and
        /// back towards him from its hold (<see cref="RodJerkUp"/>, just past upright) and the line comes taut; the 3D
        /// figure's rod fist lifts and draws back a little with it (the forearm only, no body motion). The pose sprites keep
        /// their fist: only the painted rod lifts.
        /// </summary>
        public float RodJerk01;
        static readonly Vector3 RodJerkUp = new Vector3(0f, 1f, -0.12f).normalized; // just behind upright (~7 deg): the tip comes back towards him
        const float JerkHandUp = 0.07f, JerkHandBack = 0.05f;   // m the 3D rod fist rises / draws back at a full jerk
        const float JerkSlack = 0.85f;                          // a full jerk takes this much of the line's sag out
        /// <summary>Reel revolutions so far (+ = winding in): the 3D reel's crank angle, the right hand follows its knob.</summary>
        public float ReelRevs;
        static readonly Vector3 RodUpright = new Vector3(0f, 1f, 0.2f).normalized; // near upright, ~79 deg, tip a little forward

        // ---- where he stands and where he looks
        /// <summary>Feet x (m): he walks sideways along the stage's standing area (<see cref="Range"/>).</summary>
        public float X { get; private set; }
        /// <summary>The feet (the 3D model's origin) in game space.</summary>
        public Vector3 Feet => new Vector3(X, P.StandH, 0f);
        /// <summary>Walkable feet x on this stage (min, max).</summary>
        public Vector2 Range { get; private set; }
        /// <summary>-1..1: walk left / right this frame (the controller sets it only while he may walk).</summary>
        public float WalkInput;
        public bool Walking => Mathf.Abs(walkV) > 0.05f;
        /// <summary>What he turns to (aim point, float, hooked fish); null = straight ahead. 3D figure only.</summary>
        public Vector3? FaceTarget;

        const float WalkSpeed = 0.8f;       // m/s: a calm shuffle, ~2.7 steps/s (Angler3D.Steps)
        const float WalkTau = 0.07f;        // speeding up / slowing down (~95 % after 0.2 s)
        const float ViewMarginPx = 56f;     // the feet stay this far inside the pixel view's sides (his body and rod stay in view)
        const float FaceTau = 0.085f;       // turning towards the target: ~95 % after 0.25 s
        const float BodyShare = 0.8f;       // the body turns this much of the way, the rod and head the rest
        const float BodyMax = 35f, HeadMax = 45f, RodMax = 40f; // degrees either way
        // the rod is held on his left: turning right would swing the rod hand, the reel and the rod's butt behind his
        // head (seen from behind), so to the right the body turns less and the hand follows only part of the body turn
        // (the left arm reaches out) and the rod turns less (RodMaxRight: at 40 deg, as to the left, the fish close in out to
        // his right brought it onto the hat with the hat guard's tilt at its most, KeepOffHat); the head turns the whole way
        const float BodyMaxRight = 25f, HandRight = 0.35f;
        /// <summary>Degrees the rod may yaw to his right (static: the -fkrodright test switch; to the left <see cref="RodMax"/>).</summary>
        internal static float RodMaxRight = 30f;
        const float WalkFace = 25f;         // degrees he turns towards the side he walks to
        float walkV, faceS;

        // ---- the rod's sideways sweep (the controller: the rod sweep while a rig is in the water, side pressure in a fight)
        /// <summary>
        /// Degrees (+ = to his right) the rod is swept to the side on top of where he faces (<see cref="SweepMax"/> at most).
        /// The body turns with it as with a turn of his face (so the rod keeps its clearance from the hat as when he faces
        /// that way), the head keeps looking at the rig / fish, and swept out to his left the rod comes down
        /// <see cref="SweepDrop"/> towards the water at a full sweep (held low and to the side; not to the right, where a
        /// lower rod would pass closer to the hat). Within the rod's yaw limits (<see cref="RodMax"/> left,
        /// <see cref="RodMaxRight"/> right of straight ahead): <see cref="SweepEff"/> is what is left of it.
        /// </summary>
        public float Sweep;
        public const float SweepMax = 30f;
        const float SweepTau = 0.07f;       // easing to the wanted sweep (~95 % after 0.2 s)
        const float SweepDrop = 10f;        // degrees the rod's top comes down at a full sweep to his left...
        const float SweepDropLow = 25f;     // ...and with SideLow (side pressure in a fight: the rod held low, so the tip bending
                                            // towards a line out to his right passes in front of him, below the hat)
        /// <summary>The sweep is side pressure (the controller, in a fight): swept to his left the rod is held lower.</summary>
        public bool SideLow;
        const float SpriteSweepRight = 0.4f; // the pose sprites' painted rod shows this much of a sweep to his right
        /// <summary>Degrees the rod's top comes up (towards upright) at a full sweep to his right. (Static: the -fksweepraise test switch.)</summary>
        internal static float SweepRaiseRight = 0f;
        float sweepS;
        /// <summary>The sweep this frame after the rod's yaw limits (degrees, + = right): the rod's yaw minus where it would point unswept (what is drawn).</summary>
        public float SweepEff { get; private set; }
        /// <summary>The rod's yaw this frame (degrees, + = right, within its limits): where he faces plus the sweep.</summary>
        public float RodYaw { get; private set; }
        /// <summary>
        /// The sweep asked for this frame (degrees, + = right, eased), before the rod's yaw limits (sweeping a snag free reads
        /// it; side pressure reads what the drawn rod shows, <see cref="SweepLine"/>).
        /// </summary>
        public float SweepReq => sweepS;
        /// <summary>Where he faces this frame (degrees, + = right, eased; in a fight the hooked fish's bearing), before the rod's yaw limits.</summary>
        public float Facing => faceS;
        /// <summary>The rod's yaw as asked for (degrees, + = right): where he faces plus the sweep asked for, before the rod's yaw limits (for the tests).</summary>
        public float RodYawHeld => faceS + sweepS;
        /// <summary>
        /// How far the rod tip is off the line to the rig / fish, towards the side it is swept to (degrees, the sweep's sign,
        /// at most the sweep): the drawn rod's yaw (within its limits) minus where he faces, only the part on the side swept
        /// to. What bends a wound-in rig's path, and the lean side pressure counts (FishingController.Lean, the fight strip):
        /// a rod pinned at a yaw limit by a rig / fish far out that way and swept back inwards counts in full; swept on
        /// outwards it cannot go, so that counts nothing; unswept it is 0 (the limit's own angle off the line is no lean).
        /// </summary>
        public float SweepLine { get; private set; }
        /// <summary>
        /// The rod's yaw as side pressure counts it (degrees, + = right): where he faces plus <see cref="SweepLine"/>. The
        /// fight's load reads the rod's angle to the line from it (FishingController.TrackRodLine): the lean the drawn rod
        /// shows; a fish beyond the rod's yaw limits with no lean is no angle.
        /// </summary>
        public float RodYawShown => faceS + SweepLine;

        // ---- the wind-up of a flick cast: the rod follows the finger
        /// <summary>
        /// The wind-up of a flick cast (not on the ice): the finger's offset from the press point, x in screen widths (right
        /// +), y in screen heights (below +), smoothed by the controller; null = not winding up. While he holds the aim pose
        /// the rod follows it (<see cref="WindAngles"/>): pulled down, it goes back over his shoulder; coming back up it
        /// swings forward over his head, past upright once the finger is above the press point; sideways its top leans
        /// with the finger. Visual only: the throw is still the flick's at the release (<see cref="FlickCast"/>), and the
        /// cast swing carries on from wherever the rod is.
        /// </summary>
        public Vector2? WindUp;
        // pitch: degrees from upright (+ = forward, towards the water); lean: degrees the top leans (+ = to his right)
        internal const float WindBack = 62f, WindBackAt = 0.25f;  // this far behind upright with the finger this far (H) below the press point
        internal const float WindFwd = 25f, WindFwdAt = 0.10f;    // this far past upright towards the water this far (H) above it
        internal const float WindLean = 30f, WindLeanAt = 0.25f;  // the top leans this far towards the finger this far (W) to the side...
        internal const float WindLeanOut = 10f;                   // ...from leaning out to his left this much (the finger straight below)
        // the rod hand along the swing (character space from the feet): at the top beside his head, a little behind it
        // pulled back, a little ahead and lower swung forward; leaning the rod left takes the hand further out, so the
        // butt behind the fist stays clear of the hat brim
        static readonly Vector3 WindHandBack = new Vector3(-0.29f, 1.72f, -0.13f);
        const float WindHandFwdZ = 0.20f, WindHandFwdDrop = 0.10f, WindHandOut = 0.10f;
        const float WindFollowIn = 0.15f;   // entering the aim pose the hand and rod glide in (HandTau), then follow the finger directly
        // the rod is drawn in front of the body while it is up / back over his shoulder (aim), and coming out of the aim
        // until its direction points this far forward (z of the unit direction)
        const float RodFrontZ = 0.2f;
        float windIn;
        bool rodFrontOut;

        /// <summary>The wind-up rod's pitch (x: degrees from upright, + = forward) and lean (y: degrees, + = top to his right) for a finger offset (see <see cref="WindUp"/>).</summary>
        internal static Vector2 WindAngles(Vector2 off)
        {
            float pitch = off.y >= 0f ? -WindBack * Mathf.Clamp01(off.y / WindBackAt) : WindFwd * Mathf.Clamp01(-off.y / WindFwdAt);
            float lean = Mathf.Clamp(-WindLeanOut + WindLean * Mathf.Clamp(off.x / WindLeanAt, -1f, 1f), -RodMax, RodMaxRight);
            return new Vector2(pitch, lean);
        }

        /// <summary>The rod's direction (character space) for a wind-up pitch / lean: tipped forward / back, then its top leant sideways.</summary>
        static Vector3 WindDir(Vector2 a)
        {
            float p = a.x * Mathf.Deg2Rad;
            return Quaternion.AngleAxis(-a.y, Vector3.forward) * new Vector3(0f, Mathf.Cos(p), Mathf.Sin(p));
        }

        /// <summary>The rod hand (character space from the feet) for a wind-up pitch / lean.</summary>
        static Vector3 WindHand(Vector2 a)
        {
            float fwd = Mathf.InverseLerp(-WindBack, WindFwd, a.x);
            float left = Mathf.Clamp01((-a.y - WindLeanOut) / (RodMax - WindLeanOut));
            return WindHandBack + new Vector3(-WindHandOut * left, -WindHandFwdDrop * fwd * fwd, WindHandFwdZ * fwd);
        }

        /// <summary>The rod's pitch (degrees from upright, + = forward) and lean (degrees, + = top to his right) this frame, before any lift / bend.</summary>
        internal Vector2 RodAngles { get; private set; }
        /// <summary>True while the rod is drawn in front of the body.</summary>
        internal bool RodFront => rod.Sr.sortingOrder == OrderRodFront;

        /// <summary>
        /// The middle of his hat in the pixel scene this frame (the deck's bob included), for what hangs off his head (the
        /// wind-up's <see cref="CastArrow"/>): the 3D figure's head bone raised to the hat, or the pose sprite's hat (the
        /// aim pose's; the other poses' hats sit within a couple of pixels of it).
        /// </summary>
        public Vector2 HatPos2D { get; private set; }
        const float HatMid = 0.12f;                                     // m above the head bone: brim ~0.05, crown ~0.25
        static readonly Vector2 SpriteHatPx = new Vector2(0f, 71f);    // the pose sprites' hat above the feet (px)

        /// <summary>Where he stood on each stage, for this session (not saved).</summary>
        static readonly System.Collections.Generic.Dictionary<string, float> LastX = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>
        /// Walkable feet x (m) at the standing depth per stage, read off the painted front layers (Stages/&lt;id&gt;_front.png
        /// projected through <see cref="Persp"/>): the feet stay on the planks / rock / concrete / deck / ledge and clear
        /// of the props standing at the feet line (lake: tackle box and bucket on the left; sea: cooler and bucket;
        /// swamp: the lamp post; ocean: the cooler and the life ring; cave: the lantern post). Ice: open ice, he always
        /// fishes the hole (x 1.9); further left than -0.5 he turns so far right towards it that the rod swings round
        /// behind his head and seems to come out of it.
        /// </summary>
        public static Vector2 WalkRange(string stageId) => stageId switch
        {
            "lake" => new Vector2(-0.25f, 1.0f),
            "stream" => new Vector2(-0.8f, 0.8f),
            "sea" => new Vector2(-0.5f, 0.8f),
            "swamp" => new Vector2(-0.85f, 0.7f),
            "ice" => new Vector2(-0.5f, 2.6f),
            "ocean" => new Vector2(-0.65f, 1.5f),
            "cave" => new Vector2(-0.85f, 1.5f),
            _ => Vector2.zero,
        };

        /// <summary>-fkangx &lt;metres&gt;: start at this feet x (test switch).</summary>
        static float? StartX
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-fkangx");
                return i >= 0 && i + 1 < args.Length &&
                       float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    ? x : (float?)null;
            }
        }

        /// <summary>True when the angler is the real-time 3D model (false: the pose sprites).</summary>
        public bool Uses3D => a3d != null;
        public Angler3D Model3D => a3d;
        public Reel3D Reel3D => reel3d;

        StageView stage;
        Persp P;
        CharacterData cd;
        SpriteRenderer body;
        LineRenderer line, dangle, underLine, rodLine;
        RodPainter rod;

        // the reel clamps under the rod just ahead of the hand; the line runs from its spool along the rod's underside
        // (16 px sprites from fk_items.py build_world_reels: the foot top is 5 px above the centre in all of them)
        /// <summary>
        /// Where the reel sits on the rod, in the rod painter's s (metres from the hand along the rod curve's parameter,
        /// ~1.4x the true distance near the hand): the middle of the painted reel seat (<see cref="RodPainter.ReelS"/>),
        /// just past the fist. The pose sprites' right fists were rendered on the knob of a reel at 0.33
        /// (hyb_character GAME_REEL_S); the 3D figure holds the reel closer, so the right arm cranks it with a bent elbow.
        /// </summary>
        public const float ReelS2D = 0.33f;
        internal static float ReelS3D = 0.22f;  // (static: the -fkarmtune test switch)
        float ReelS => Uses3D ? ReelS3D : ReelS2D;
        const float ReelFootPx = 5f;        // sprite centre -> top of the reel foot (px)
        const float ReelTilt = 0.35f;       // 0 = upright, 1 = foot square to the rod's underside
        SpriteRenderer reelSr;
        Sprite[] reelFrames;
        Vector2 spoolPx;                    // sprite centre -> where the line leaves the spool (px)
        SpriteRenderer dangleBait;
        RodDef rodDef;
        LineDef lineDef;
        float breathe;
        bool poseSet;
        static Material lineMat;

        // ---- 3D actors
        // The reel model is true to size (0.10-0.13 m, ~5 px at the angler); drawn larger so the turning crank and the
        // hand on it read in the pixel scene (1.5x: a ~6 px crank circle). (Static: the -fkarmtune test switch.)
        internal static float ReelScale = 1.5f;
        const float ReelUnderRodPx = 1.5f;  // the reel foot sits this far under the painted rod's axis (it is 3 px thick there)
        // degrees the reel is rolled about the rod from hanging straight down, + towards the angler's right: at -42 the
        // reel swings in under the rod towards him, beside the torso where his right fist reaches it with the elbow bent,
        // and the crank's plane (which holds the rod) is seen nearly edge on, so the knob and the fist on it travel a
        // narrow ellipse along the arm (minor / major ~0.4 on screen, like a spinning reel seen from behind; at -55 it
        // read as a round O). The major axis is ~5-15 degrees off the rod's line: it only lines up exactly when the
        // plane is fully edge on (a flat line). Rolled out (+) the knob hangs out of reach or, reached, behind his back.
        internal static float ReelRoll = -42f;
        /// <summary>
        /// The 3D figure's rod hand (character space from the feet, like <see cref="PoseAnchor.Hand"/>) in the reeling
        /// hold (idle / reel / fight): the left fist holds the rod grip out at the left hip, a little higher and closer
        /// in than the sprites' hand, so the reel hangs in open space just left of the torso (neither the torso nor the
        /// left arm hides the knob's circle, and the rod stays clear of the head) and within reach of the right arm with
        /// the elbow bent (~90-120 degrees round the crank turn). A grip in front of the belly would hide the reel behind
        /// his back and swing the rod through his head when he turns right. Null: the pose sprite's hand.
        /// (Static: the -fkarmtune test switch.)
        /// </summary>
        internal static Vector3? HoldIdle = new Vector3(-0.46f, 1.02f, 0.04f), HoldReel = HoldIdle, HoldFight = HoldIdle;
        static Vector3? HoldHand(string pose) => pose switch
        {
            "idle" => HoldIdle,
            "reel" or "reel2" => HoldReel,
            "fight" => HoldFight,
            _ => null,
        };
        /// <summary>Degrees the rod's top leans further out (to his left) in the 3D reeling hold. (Static: -fkarmtune.)</summary>
        internal static float HoldRodLean = 0f;
        /// <summary>The rod's direction on screen at the hand this frame (for measurements).</summary>
        internal Vector2 RodScreenDir { get; private set; }
        internal RodPainter RodPaint => rod;
        /// <summary>The line above the water, the dangling bait's line and the dangling bait (the occlusion watch reads them as drawn).</summary>
        internal LineRenderer LineR => line;
        internal LineRenderer DangleR => dangle;
        internal SpriteRenderer DangleBaitR => dangleBait;
        /// <summary>Test switch: stand at feet x (clamped to the walk range like walking there).</summary>
        internal void DebugPlace(float x) => X = Mathf.Clamp(x, Range.x, Range.y);
        const float HatTop = 1.74f;         // the figure's height (feet to the hat's crown): the rim fades down over it
        const float PoseHold = 0.12f;       // reel <-> fight <-> idle only switch after the new pose held this long
        const float HandTau = 0.05f;        // hand / rod-angle smoothing between poses (~95 % after 0.15 s)
        Angler3D a3d;
        Reel3D reel3d;
        ActorLayer bodyLayer, reelLayer;
        string shownPose;
        float holdT;
        Vector3 handS, dirS;
        bool smoothInit;

        /// <summary>-fkactors2d: keep the 2D sprites (for comparisons).</summary>
        static bool Force2D => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fkactors2d") >= 0;

        public static Material LineMaterial
        {
            get
            {
                if (lineMat != null) return lineMat;
                var sh = Shader.Find("Sprites/Default");
                if (sh != null) lineMat = new Material(sh);
                else
                {
                    // the built-in sprite material is always present, even if the shader was stripped from lookup
                    var tmp = new GameObject("tmp").AddComponent<SpriteRenderer>();
                    lineMat = tmp.sharedMaterial;
                    Destroy(tmp.gameObject);
                }
                return lineMat;
            }
        }

        public static Angler Create(StageView s)
        {
            var a = new GameObject("Angler").AddComponent<Angler>();
            a.Init(s);
            return a;
        }

        void Init(StageView s)
        {
            stage = s;
            P = s.P;
            cd = Art.Character;
            Range = WalkRange(s.Def.id);
            X = StartX ?? (LastX.TryGetValue(s.Def.id, out float lx) ? lx : 0f);
            X = Mathf.Clamp(X, Range.x, Range.y);
            body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.transform.SetParent(transform, false);
            body.sortingOrder = OrderBody;
            body.transform.position = P.To2D(Feet);
            rod = new RodPainter(transform);
            line = MakeLine("Line", 24, OrderLine);
            dangle = MakeLine("Dangle", 2, OrderLine);
            underLine = MakeLine("UnderLine", 2, OrderUnderLine);
            underLine.enabled = false;
            rodLine = MakeLine("RodLine", 12, OrderRodBehind - 1);
            reelSr = new GameObject("Reel").AddComponent<SpriteRenderer>();
            reelSr.transform.SetParent(transform, false);
            dangleBait = new GameObject("DangleBait").AddComponent<SpriteRenderer>();
            dangleBait.transform.SetParent(transform, false);
            dangleBait.sortingOrder = OrderLine + 1;
            // the line and the dangling bait are hidden where the front layer is nearer (the line running down behind the
            // pier's edge is cut there): the line's vertices carry their depth (FrontOcclusion.Point), the bait its own
            FrontOcclusion.Use(line);
            FrontOcclusion.Use(dangle);
            FrontOcclusion.Use(dangleBait);
            if (!Force2D) Init3D();
            rod.ReelS = ReelS;
            RefreshGear();
            SetPose("idle");
            SetRodFront(false);
        }

        void Init3D()
        {
            a3d = Angler3D.TryCreate(ActorLayer.AnglerLayer, transform);
            if (a3d == null)
            {
                Debug.Log("[Angler] 3D angler model missing: using the pose sprites");
                return;
            }
            bodyLayer = ActorLayer.Create("AnglerLayer", ActorLayer.AnglerLayer, OrderBody, stage, transform);
            reelLayer = ActorLayer.Create("ReelLayer", ActorLayer.ReelLayer, OrderRodBehind - 1, stage, transform);
            body.enabled = false;
        }

        LineRenderer MakeLine(string name, int points, int order)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.material = LineMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.sortingOrder = order;
            lr.textureMode = LineTextureMode.Stretch;
            lr.widthMultiplier = 1f / PixelView.PPU;
            return lr;
        }

        public void RefreshGear()
        {
            rodDef = Game.I.Rod;
            lineDef = Game.I.Line;
            tintShown = Color.white;
            deepShown = Color.clear;   // (the line's colours are set again with the period tint next frame)
            rod.SetRod(rodDef);
            var lc = lineDef.color;
            line.startColor = line.endColor = lc;
            dangle.startColor = dangle.endColor = lc;
            rodLine.startColor = rodLine.endColor = lc;
            underLine.startColor = underLine.endColor = stage.UnderwaterLine(lc);
            string reelId = Game.I.Reel.id;
            reelFrames = new[] { Art.Get($"World/{reelId}_w0"), Art.Get($"World/{reelId}_w1") };
            // spinning reels feed the line from the spool front; baitcast / electric sit flat on the rod
            // (the rod leans up-left from the left hand, so the spool front is left of the sprite centre)
            spoolPx = reelId == "reel_baitcast" || reelId == "reel_electric" ? new Vector2(-2, 5) : new Vector2(-2, 3);
            dangleBait.sprite = Art.WorldBait(Game.I.Bait.id);
            if (Uses3D && (reel3d == null || reel3d.Id != reelId))
            {
                reel3d?.Destroy();
                reel3d = Reel3D.TryCreate(reelId, ActorLayer.ReelLayer, transform);
                if (reel3d == null) Debug.Log($"[Angler] 3D reel {reelId} missing: using the reel sprite");
            }
            reelSr.enabled = reel3d == null;
        }

        public void SetPose(string pose)
        {
            // the 3D crank turns continuously: the alternating reel / reel2 sprite frames are one pose
            if (Uses3D && pose == "reel2") pose = "reel";
            if (Pose == pose && poseSet) return;
            Pose = pose;
            poseSet = true;
            if (!Uses3D) body.sprite = Art.Pose(pose);
            // (the rod's drawing order follows the shown pose and the rod's direction every frame: LateUpdate)
        }

        /// <summary>
        /// The rod in front of the body (up / back over his shoulder) or behind it; the reel and the line along the rod go
        /// with it, and so does the bait dangling from the tip (pulled back, the tip is between him and the camera).
        /// </summary>
        void SetRodFront(bool front)
        {
            int o = front ? OrderRodFront : OrderRodBehind;
            if (rod.Sr.sortingOrder == o) return;
            rod.Sr.sortingOrder = o;
            reelSr.sortingOrder = o - 1;   // under the rod (and under the hand)
            if (reelLayer != null) reelLayer.SortingOrder = o - 1;
            rodLine.sortingOrder = o - 2;
            dangle.sortingOrder = front ? o + 1 : OrderLine;
            dangleBait.sortingOrder = dangle.sortingOrder + 1;
        }

        public bool ShowDangle { get; set; } = true;

        /// <summary>
        /// Walks along the standing area: glides to the input speed, stops at the ends of <see cref="Range"/> (and never
        /// leaves the pixel view) and remembers the spot for this stage. The 3D figure steps with it (Angler3D.Steps).
        /// </summary>
        void UpdateWalk(float dt)
        {
            var r = Range;
            var pv = PixelView.Current;
            if (pv != null)
            {
                float half = Mathf.Max(0f, pv.ViewSize.x * 0.5f * PixelView.PPU - ViewMarginPx) / P.PixelsPerMetre(Feet);
                r = new Vector2(Mathf.Max(r.x, -half), Mathf.Min(r.y, half));
                if (r.x > r.y) r = new Vector2(0f, 0f);
            }
            float want = Mathf.Clamp(WalkInput, -1f, 1f) * WalkSpeed;
            if ((X <= r.x + 1e-4f && want < 0f) || (X >= r.y - 1e-4f && want > 0f)) want = 0f; // against the end
            walkV = Mathf.Lerp(walkV, want, 1f - Mathf.Exp(-dt / WalkTau));
            if (want == 0f && Mathf.Abs(walkV) < 0.02f) walkV = 0f;
            float free = X + walkV * dt;
            float x = Mathf.Clamp(free, r.x, r.y);
            if (x != free) walkV = 0f; // reached an end
            X = x;
            LastX[stage.Def.id] = X;
        }

        /// <summary>The way he wants to face (degrees, + = right): the walk side while walking, else the target.</summary>
        float FaceAngle()
        {
            if (Walking) return Mathf.Sign(walkV) * WalkFace * Mathf.Clamp01(Mathf.Abs(walkV) / WalkSpeed);
            if (!FaceTarget.HasValue) return 0f;
            var d = FaceTarget.Value - Feet;
            return Mathf.Atan2(d.x, Mathf.Max(0.5f, d.z)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The line's sideways belly in the current (world, horizontal; Tackle.Bow along the line's right-hand normal): the
        /// line's curve bows this far off the straight line at its middle (Docs/time_currents_spec.md 9.2).
        /// </summary>
        public Vector3 LineBow;

        Color tintShown = Color.white, deepShown = Color.clear;

        /// <summary>
        /// The period look's actor tint (StageView.ActorTint, moonlit blue at night) on the 2D parts: the pose sprite, the
        /// painted rod, the reel sprite, the dangling bait and the line (the 3D actors take it in their layer's shader).
        /// </summary>
        void ApplyTint()
        {
            var t = stage.ActorTint;
            var deep = stage.WaterDeep;
            if (t == tintShown && deep == deepShown) return;
            tintShown = t;
            deepShown = deep;
            var solid = new Color(t.r, t.g, t.b, 1f);
            body.color = solid;
            rod.Sr.color = solid;
            reelSr.color = solid;
            dangleBait.color = solid;
            if (lineDef == null) return;
            var lc = lineDef.color * solid;
            lc.a = lineDef.color.a;
            line.startColor = line.endColor = lc;
            dangle.startColor = dangle.endColor = lc;
            rodLine.startColor = rodLine.endColor = lc;
            underLine.startColor = underLine.endColor = stage.UnderwaterLine(lc);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            breathe += dt;
            ApplyTint();
            UpdateWalk(dt);
            // the ocean boat bobs on the swell (whole pixels): everything standing on its deck moves with it
            var bob = stage.DeckBob;
            var feet = Feet;
            PoseAnchor anchor;
            Vector3 baseDir, upright = RodUpright, jerkUp = RodJerkUp;
            float bodyYaw = 0f, jerk = Mathf.Clamp01(RodJerk01);
            // he turns towards where he fishes (faceS); the rod's sideways sweep turns the body and the rod further, within
            // the rod's yaw limits, and brings the rod's top down a little (a rod held low and to the side)
            faceS = Mathf.Lerp(faceS, FaceAngle(), 1f - Mathf.Exp(-dt / FaceTau));
            sweepS = Mathf.Lerp(sweepS, Mathf.Clamp(Sweep, -SweepMax, SweepMax), 1f - Mathf.Exp(-dt / SweepTau));
            if (Sweep == 0f && Mathf.Abs(sweepS) < 0.01f) sweepS = 0f;
            float rodYawDeg = Mathf.Clamp(faceS + sweepS, -RodMax, RodMaxRight);
            SweepEff = rodYawDeg - Mathf.Clamp(faceS, -RodMax, RodMaxRight);
            RodYaw = rodYawDeg;
            float swSign = Mathf.Sign(sweepS);
            SweepLine = sweepS == 0f ? 0f : swSign * Mathf.Min(Mathf.Abs(sweepS), Mathf.Max(0f, swSign * (rodYawDeg - faceS)));
            // (only swept out to his left: swept right, a lower rod would pass closer to the hat seen from behind; there it
            // may instead come up a little, SweepRaiseRight)
            var sweepDrop = Quaternion.AngleAxis((SideLow ? SweepDropLow : SweepDrop) * Mathf.Clamp01(-SweepEff / SweepMax) - SweepRaiseRight * Mathf.Clamp01(SweepEff / SweepMax), Vector3.right);
            if (Uses3D)
            {
                // reel <-> fight <-> idle flicker from frame to frame while circling: only follow a pose that holds
                if (shownPose == null) shownPose = Pose;
                if (Pose != shownPose)
                {
                    holdT += dt;
                    bool quick = Pose == "aim" || Pose == "cast" || shownPose == "aim" || shownPose == "cast";
                    if (quick || holdT >= PoseHold) { shownPose = Pose; holdT = 0f; }
                }
                else holdT = 0f;
                anchor = cd.Get(shownPose);
                // he turns towards where he fishes: the body most of the way, the head and the rod a little further;
                // the rod hand lives in the body frame, so it swings round the feet with the body. A sweep turns the body
                // and the rod as a turn of his face would (the head stays on the rig / fish)
                bodyYaw = Mathf.Clamp((faceS + sweepS) * BodyShare, -BodyMax, BodyMaxRight);
                var rodYaw = Quaternion.Euler(0f, rodYawDeg, 0f);
                upright = rodYaw * RodUpright;
                jerkUp = rodYaw * RodJerkUp;
                // the rod hand and the rod's angle glide to the new pose (the hand relative to the feet, so it never
                // trails behind him while he walks); winding up, they follow the finger (WindUp)
                bool wind = WindUp.HasValue && shownPose == "aim";
                var wa = wind ? WindAngles(WindUp.Value) : Vector2.zero;
                var poseHand = wind ? WindHand(wa) : HoldHand(shownPose) ?? anchor.Hand;
                var wantHand = Quaternion.Euler(0f, bodyYaw > 0f ? bodyYaw * HandRight : bodyYaw, 0f) * poseHand;
                var holdDir = wind ? WindDir(wa)
                    : HoldHand(shownPose) != null && HoldRodLean != 0f ? Quaternion.AngleAxis(HoldRodLean, Vector3.forward) * anchor.RodDir : anchor.RodDir;
                var wantDir = rodYaw * (wind ? holdDir : sweepDrop * holdDir);
                float k = FollowRate(wind, dt);
                handS = Vector3.Lerp(handS, wantHand, k);
                dirS = k >= 1f ? wantDir : Vector3.Slerp(dirS, wantDir, k).normalized;
                // the 톡: the rod fist snaps up and draws back a little with the rod (after the smoothing: it is quick)
                var jerkHand = jerk > 0f ? Quaternion.Euler(0f, bodyYaw, 0f) * new Vector3(0f, JerkHandUp, -JerkHandBack) * jerk : Vector3.zero;
                Hand = feet + handS + jerkHand;
                baseDir = dirS;
                bodyLayer.Offset = bob;
                reelLayer.Offset = bob;
                bodyLayer.RimSpan = (feet, feet + Vector3.up * HatTop);
                a3d.BodyYaw = bodyYaw;
                a3d.HeadYaw = Mathf.Clamp(faceS, -HeadMax, HeadMax);   // (not the sweep: he keeps his eyes on the rig / fish)
                a3d.Velocity = new Vector3(walkV, 0f, 0f);
                a3d.Walk = Mathf.Clamp01(Mathf.Abs(walkV) / WalkSpeed);
            }
            else
            {
                anchor = cd.Get(Pose);
                // no whole-body idle bob: moving the entire figure up and down read as a jump, not as breathing
                // (the sprites walk sideways without turning)
                body.transform.position = P.To2D(feet) + bob;
                HatPos2D = P.To2D(feet) + bob + SpriteHatPx / PixelView.PPU;
                Hand = feet + anchor.Hand;
                // the painted rod glides to the pose's angle like the 3D figure's (and follows the finger winding up); the
                // pose sprites do not turn, so a sweep swings the painted rod alone: to his left as the 3D rod, to his right
                // only SpriteSweepRight of it (the sprite's body does not turn with it, and further the rod crosses its hat)
                bool wind = WindUp.HasValue && Pose == "aim";
                float yaw2d = SweepEff > 0f ? SweepEff * SpriteSweepRight : SweepEff;
                var wantDir = wind ? WindDir(WindAngles(WindUp.Value)) : Quaternion.Euler(0f, yaw2d, 0f) * (sweepDrop * anchor.RodDir);
                float k = FollowRate(wind, dt);
                dirS = k >= 1f ? wantDir : Vector3.Slerp(dirS, wantDir, k).normalized;
                baseDir = dirS;
            }
            // the rod is in front of the body while up / back over his shoulder: in the aim, and coming out of it (the
            // cast swing, or back to the hold) until it has swung forward
            if ((Uses3D ? shownPose : Pose) == "aim") rodFrontOut = true;
            else if (baseDir.z >= RodFrontZ) rodFrontOut = false;
            SetRodFront(rodFrontOut);
            RodAngles = new Vector2(Mathf.Asin(Mathf.Clamp(baseDir.z, -1f, 1f)) * Mathf.Rad2Deg, Mathf.Atan2(baseDir.x, baseDir.y) * Mathf.Rad2Deg);

            var dir = RodLift01 > 0f ? Vector3.Slerp(baseDir, upright, Mathf.Clamp01(RodLift01)).normalized : baseDir;
            // the 톡 jerks the rod up and back towards him (the tip lifts on screen)
            if (jerk > 0f) dir = Vector3.Slerp(dir, jerkUp, jerk).normalized;
            // under load the tip section swings round to follow the line, a soft rod further than a stiff one
            // (full load: bamboo 0.66 of the way, big-game 0.93); the bent rod's tip comes in towards the hand
            var toLineDir = LineTarget.HasValue ? (LineTarget.Value - Hand).normalized : Vector3.zero;
            float bend = LineTarget.HasValue ? Mathf.Min(0.95f, Mathf.Pow(Mathf.Clamp01(Tension01), 0.8f) * (0.55f + rodDef.flex * 0.75f)) : 0f;
            float len = rodDef.length * (1f - 0.22f * bend);
            // where the tip is as the rod is held, unbent and untilted: what the fight places the fish by (RodTipPlan)
            RodTipPlan = Hand + dir * rodDef.length;
            dir = KeepOffHat(dir, bob, jerk, dt);
            rod.ReelS = ReelS;
            var bentDir = LineTarget.HasValue ? Vector3.Slerp(dir, toLineDir, bend).normalized : dir;
            var tip = Hand + bentDir * len;
            RodScreenDir = (P.To2D(Hand + dir * 0.3f) - P.To2D(Hand)).normalized;
            // cubic curve: the butt keeps the pose angle, the tip section lines up with the line
            var c1 = Hand + dir * len * 0.5f;
            var c2 = tip - bentDir * len * 0.3f;
            // close to breaking the rod tip and the line shiver (up to ~1.6 px, flipping every frame)
            float shiver = Strain01 > 0f ? Mathf.Clamp01(Strain01) * 1.6f / PixelView.PPU * ((Time.frameCount & 1) == 0 ? 1f : -1f) : 0f;
            var rodPerp = Perp(P.To2D(tip) - P.To2D(Hand));
            var hand = Hand;
            Vector3 Bez(float t)
            {
                float u = 1f - t;
                return u * u * u * hand + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * tip;
            }
            Vector2 RodAt(float t) => P.To2D(Bez(t)) + rodPerp * (shiver * t * t);
            // painted pixel by pixel along the curve (s metres from the hand; the butt runs back behind the hand)
            float rodLen = len;
            Vector2 At(float s) => (s < 0f ? P.To2D(hand + dir * s) : RodAt(s / rodLen)) + bob;
            rod.Paint(At, rodLen);
            RodTip = tip;

            if (reel3d != null)
            {
                // the 3D reel on the reel seat: foot under the rod, +Z along the rod, hanging in the rod's vertical plane;
                // the crank turns with the reel revolutions
                float t = ReelS / rodLen;
                var seat = Bez(t);
                float u = 1f - t;
                var along = 3f * u * u * (c1 - hand) + 6f * u * t * (c2 - c1) + 3f * t * t * (tip - c2);
                along = along.sqrMagnitude > 1e-8f ? along.normalized : dir;
                // "up" in the rod's vertical plane, rolled by ReelRoll: the reel swings in under the rod towards him and
                // its crank faces up / back, so the turning knob and the right fist on it show beside his torso
                var vert = Vector3.up - along * Vector3.Dot(Vector3.up, along);
                vert = vert.sqrMagnitude > 1e-6f ? vert.normalized : Vector3.back;
                var side = new Vector3(along.z, 0f, -along.x);
                if (side.x < 0f) side = -side;
                side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
                float roll = ReelRoll * Mathf.Deg2Rad;
                var up = (vert * Mathf.Cos(roll) + side * Mathf.Sin(roll)).normalized;
                var foot = seat - up * (ReelUnderRodPx / Mathf.Max(1f, P.PixelsPerMetre(seat)));
                reel3d.Place(foot, along, up, ReelRevs * 360f, ReelScale);
                rodLine.positionCount = 1 + rod.Guides.Count;
                rodLine.SetPosition(0, P.To2D(reel3d.Spool) + bob);
                for (int i = 0; i < rod.Guides.Count; i++) rodLine.SetPosition(i + 1, rod.Guides[i]);
            }
            else
            {
                // reel sprite on the reel seat past the fist, hanging off the rod's underside and tilted a little towards
                // it (handle frames alternate with the reeling pose); the line runs from its spool through the guide rings
                var seatPt = At(ReelS);
                var tan = At(ReelS + 0.05f) - At(ReelS - 0.05f);
                var under = Perp(tan);
                if (under.y > 0f || (Mathf.Abs(under.y) < 0.2f && under.x < 0f)) under = -under; // same side as RodPainter
                var up = Vector2.Lerp(Vector2.up, -under, ReelTilt).normalized;
                float reelDeg = Vector2.SignedAngle(Vector2.up, up);
                var reelPos = seatPt + under * (1.5f / PixelView.PPU) - up * (ReelFootPx / PixelView.PPU);
                reelSr.sprite = reelFrames[Pose == "reel2" || (Uses3D && Mathf.Repeat(ReelRevs, 1f) >= 0.5f) ? 1 : 0];
                reelSr.transform.SetPositionAndRotation(Snap(reelPos), Quaternion.Euler(0, 0, reelDeg));
                rodLine.positionCount = 1 + rod.Guides.Count;
                rodLine.SetPosition(0, reelPos + (Vector2)(Quaternion.Euler(0, 0, reelDeg) * spoolPx) / PixelView.PPU);
                for (int i = 0; i < rod.Guides.Count; i++) rodLine.SetPosition(i + 1, rod.Guides[i]);
            }

            if (Uses3D)
            {
                // left fist on the rod grip; right fist on the crank knob, or on the rod behind the left hand while
                // swinging it (aim / cast), or up in the air (cheer)
                Vector3 rightGrip;
                var turned = Quaternion.Euler(0f, Angler3D.Yaw + bodyYaw, 0f);
                if (shownPose == "aim") rightGrip = Hand + dir * -0.13f;
                else if (shownPose == "cast") rightGrip = Hand + dir * -0.14f;
                else if (shownPose == "cheer") rightGrip = feet + turned * Angler3D.CheerFist;
                else if (reel3d != null) rightGrip = reel3d.Knob;
                else rightGrip = Hand + dir * ReelS + turned * new Vector3(0.06f, -0.06f, 0f);
                a3d.Pose(feet, shownPose, dt, Hand, rightGrip, P.CameraPos);
                HatPos2D = P.To2D(a3d.HeadPos + Vector3.up * HatMid) + bob;
                hatSeen = true;
            }

            if (LineTarget.HasValue)
            {
                line.enabled = true;
                dangle.enabled = false;
                dangleBait.enabled = false;
                var end = LineTarget.Value;
                LineUnderwater = end.y < -0.05f;
                underLine.enabled = LineUnderwater;
                if (LineUnderwater)
                {
                    var sub = end;
                    end = WaterEntry = EntryPoint(tip, sub);
                    underLine.SetPosition(0, P.To2D(end));
                    underLine.SetPosition(1, Fish != null ? Fish.DrawnMouth : P.To2D(P.Apparent(sub)));
                }
                float dist = Vector3.Distance(tip, end);
                // a slack line sags a little; seen in perspective a bigger sag reads as a sharp V, so at most 0.8 m
                // (a 톡 snaps the line taut for a moment)
                var mid = (tip + end) * 0.5f + Vector3.down * (Mathf.Clamp01(Slack01) * (1f - JerkSlack * jerk) * Mathf.Min(dist * 0.05f, 0.8f));
                // bowed by the current: the curve's control point moves 2 x the belly, so its middle sits the belly off
                // the straight line (a slack line still lies on the water: SurfaceUnder below)
                mid += 2f * new Vector3(LineBow.x, 0f, LineBow.z);
                curveTip = tip;
                curveMid = mid;
                curveEnd = end;
                curveBob = bob;
                curveOn = true;
                var linePerp = Perp(P.To2D(end) - P.To2D(tip));
                // resting on a prop's top (a perched rig, this frame's state): only the vertices lying there are tested nearer
                Vector3? rest = Rig != null && Rig.State == Tackle.Mode.Perched ? Rig.PerchAt : (Vector3?)null;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float t = i / (line.positionCount - 1f);
                    var p = (1 - t) * (1 - t) * tip + 2 * (1 - t) * t * mid + t * t * end;
                    p.y = Mathf.Max(p.y, SurfaceUnder(p)); // a slack line lies on the water (or ice), it never sinks through it
                    // the shiver starts with the rod tip's and runs along the line as a standing wave
                    var shake = rodPerp * (shiver * (1f - t)) + linePerp * (shiver * 0.8f * Mathf.Sin(t * Mathf.PI));
                    var p2 = P.To2D(p, out float pd);
                    if (rest.HasValue)
                    {
                        float on = Mathf.InverseLerp(RestReach, RestFull, Vector3.Distance(p, rest.Value));
                        if (on > 0f) pd = Mathf.Max(0.05f, pd - FrontOcclusion.Resting * on);
                    }
                    line.SetPosition(i, FrontOcclusion.Point(p2 + shake + bob * (1f - t), pd));
                }
            }
            else
            {
                line.enabled = false;
                underLine.enabled = false;
                LineUnderwater = false;
                curveOn = false;
                dangle.enabled = ShowDangle;
                dangleBait.enabled = ShowDangle;
                var hang = tip + new Vector3(0, -0.8f + Mathf.Sin(breathe * 1.7f) * 0.03f, 0);
                var tip2 = P.To2D(tip, out float tipD);
                var hang2 = P.To2D(hang, out float hangD);
                dangle.SetPosition(0, FrontOcclusion.Point(tip2 + bob, tipD));
                dangle.SetPosition(1, FrontOcclusion.Point(hang2 + bob, hangD));
                dangleBait.transform.position = Snap(hang2 + bob);
                FrontOcclusion.SetDepth(dangleBait, hangD);
                dangleBait.transform.localScale = Vector3.one * P.ScaleAt(hang);
            }
        }

        /// <summary>
        /// How far (0..1) the rod hand and the rod's angle glide towards the wanted pose this frame: ~0.15 s between poses
        /// (<see cref="HandTau"/>); winding up, once the glide into the aim is done, they follow the (already smoothed)
        /// finger directly.
        /// </summary>
        float FollowRate(bool wind, float dt)
        {
            windIn = wind ? windIn + dt : 0f;
            float k = smoothInit ? 1f - Mathf.Exp(-dt / HandTau) : 1f;
            smoothInit = true;
            return wind ? Mathf.Lerp(k, 1f, Mathf.Clamp01(windIn / WindFollowIn)) : k;
        }

        // ---- keeping the rod off the hat
        // Under load the rod's tip swings round towards the line: with the fish off to his right (or the rod swept across
        // it for side pressure) the bent rod can come to cross the hat seen from behind. In the holding poses (idle / reel /
        // fight; not the wind-up, the cast, a 톡 or the landing lift) the rod then comes up a little, or down when that is
        // the smaller change, in HatStep steps (at most HatSteps), just far enough to clear the hat by HatKeepPx: onto it
        // at once, off it gently (HatTauOff). Everywhere else it never engages.
        // It keeps to the side it is tilted to while that side can clear the hat (the least tilt there); it goes over to the
        // other side only when this one cannot, and not again within HatDwell: two ways that clear by about as much must not
        // take turns from frame to frame (the rod, the line from its tip and the water entry flipping for single frames).
        const float HatKeepPx = 2.5f, HatStep = 4f, HatTauOff = 0.15f;   // (the head is last frame's: a little margin)
        const float HatDwell = 0.3f;        // s a change of side holds before the other side may be taken again
        const int HatSteps = 6, HatSamples = 64;
        const float SpriteHatRPx = 8f;      // the pose sprites' hat around HatPos2D (px)
        float hatTilt, hatSideT = 99f;
        bool hatSeen;
        readonly System.Collections.Generic.List<Vector2> hatPts = new System.Collections.Generic.List<Vector2>(32);
        System.Collections.Generic.List<Vector2> hatHull;
        /// <summary>Degrees the rod is tilted this frame to keep it off the hat (+ = up; for the tests).</summary>
        internal float HatTilt => hatTilt;
        /// <summary>The untilted rod's gap to the hat as the guard estimated it this frame (px; 999 = not checked; for the tests).</summary>
        internal float HatGapNow { get; private set; } = 999f;

        Vector3 KeepOffHat(Vector3 dir, Vector2 bob, float jerk, float dt)
        {
            string pose = Uses3D ? shownPose : Pose;
            bool hold = pose == "idle" || pose == "reel" || pose == "reel2" || pose == "fight";
            float want = 0f;
            HatGapNow = 999f;
            hatSideT += dt;
            if (hold && !WindUp.HasValue && RodLift01 <= 0f && jerk <= 0f && BuildHat(bob))
            {
                var toLine = LineTarget.HasValue ? (LineTarget.Value - Hand).normalized : Vector3.zero;
                float bend = LineTarget.HasValue ? Mathf.Min(0.95f, Mathf.Pow(Mathf.Clamp01(Tension01), 0.8f) * (0.55f + rodDef.flex * 0.75f)) : 0f;
                HatGapNow = HatGap(dir, bob, toLine, bend);
                if (HatGapNow < HatKeepPx)
                {
                    // the least tilt that clears on one side (0: none within HatSteps)
                    float Least(float s)
                    {
                        for (int i = 1; i <= HatSteps; i++)
                            if (HatGap(Tilt(dir, s * i * HatStep), bob, toLine, bend) >= HatKeepPx) return s * i * HatStep;
                        return 0f;
                    }
                    float side = hatTilt > 0f ? 1f : hatTilt < 0f ? -1f : 0f;
                    if (side != 0f) want = Least(side);   // the side it is on, while that side clears
                    if (want == 0f && side == 0f)
                    {
                        // not tilted yet: the least tilt either way, up first at each step
                        for (int i = 1; i <= HatSteps && want == 0f; i++)
                        {
                            if (HatGap(Tilt(dir, i * HatStep), bob, toLine, bend) >= HatKeepPx) want = i * HatStep;
                            else if (HatGap(Tilt(dir, -i * HatStep), bob, toLine, bend) >= HatKeepPx) want = -i * HatStep;
                        }
                    }
                    else if (want == 0f && hatSideT >= HatDwell) want = Least(-side);   // this side cannot: over to the other
                    // neither clears (or the other side is not open yet): as far as it goes on its side
                    if (want == 0f) want = (side != 0f ? side : 1f) * HatStep * HatSteps;
                }
            }
            // onto the needed tilt at once, on its side or over to the other (a rod swinging across the hat would cross it
            // while a smoothed tilt caught up), back off it gently
            bool over = want != 0f && want * hatTilt < 0f;
            if (over) hatSideT = 0f;
            if (want != 0f && (Mathf.Abs(want) > Mathf.Abs(hatTilt) || over)) hatTilt = want;
            else hatTilt = Mathf.Lerp(hatTilt, want, 1f - Mathf.Exp(-dt / HatTauOff));
            if (want == 0f && Mathf.Abs(hatTilt) < 0.05f) hatTilt = 0f;
            return hatTilt != 0f ? Tilt(dir, hatTilt) : dir;
        }

        /// <summary>The rod's direction tilted up towards upright (+ degrees) or down towards level (-).</summary>
        static Vector3 Tilt(Vector3 d, float deg)
        {
            if (deg > 0f) return Vector3.RotateTowards(d, Vector3.up, deg * Mathf.Deg2Rad, 0f).normalized;
            var flat = new Vector3(d.x, 0f, d.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            return Vector3.RotateTowards(d, flat, -deg * Mathf.Deg2Rad, 0f).normalized;
        }

        /// <summary>The hat's outline on screen (px) this frame: the 3D head's brim and crown rings (as the tests measure it), or the sprite's hat.</summary>
        bool BuildHat(Vector2 bob)
        {
            if (a3d == null)
            {
                hatHull = null;
                return true;
            }
            if (!hatSeen) return false;
            var head = a3d.HeadPos;
            hatPts.Clear();
            for (int i = 0; i < 16; i++)
            {
                float t = i * Mathf.PI * 2f / 16f;
                var o = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                hatPts.Add((P.To2D(head + Vector3.up * 0.05f + o * 0.19f) + bob) * PixelView.PPU);
                hatPts.Add((P.To2D(head + Vector3.up * 0.22f + o * 0.11f) + bob) * PixelView.PPU);
            }
            hatHull = ScreenHull.Of(hatPts);
            return true;
        }

        /// <summary>Screen px between the rod bent from direction <paramref name="d"/> (butt to tip, as painted) and the hat; negative = on it.</summary>
        float HatGap(Vector3 d, Vector2 bob, Vector3 toLine, float bend)
        {
            var bent = LineTarget.HasValue ? Vector3.Slerp(d, toLine, bend).normalized : d;
            float l = rodDef.length * (LineTarget.HasValue ? 1f - 0.22f * bend : 1f);
            var hand = Hand;
            var tp = hand + bent * l;
            var k1 = hand + d * l * 0.5f;
            var k2 = tp - bent * l * 0.3f;
            float best = HatDist((P.To2D(hand - d * 0.18f) + bob) * PixelView.PPU);
            // (~2 px apart on screen, as the painter's axis is dense: a coarser sampling misses the closest point)
            for (int i = 0; i <= HatSamples; i++)
            {
                float t = i / (float)HatSamples, u = 1f - t;
                var p = u * u * u * hand + 3f * u * u * t * k1 + 3f * u * t * t * k2 + t * t * t * tp;
                best = Mathf.Min(best, HatDist((P.To2D(p) + bob) * PixelView.PPU));
            }
            return best;
        }

        float HatDist(Vector2 q) => hatHull != null ? ScreenHull.SignedDist(hatHull, q) : (q - HatPos2D * PixelView.PPU).magnitude - SpriteHatRPx;

        /// <summary>Height a sagging line comes to rest on at p: the water, or the ice outside the fishing hole.</summary>
        public float SurfaceUnder(Vector3 p)
        {
            var L = stage.L;
            if (!L.IsIce) return 0f;
            float dx = p.x - L.holeX, dz = p.z - L.holeZ;
            return dx * dx + dz * dz < L.holeR * L.holeR ? 0f : L.iceY;
        }

        static Vector2 Perp(Vector2 d) => d.sqrMagnitude > 1e-8f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;

        // the line above the water as drawn this frame (a quadratic curve rod tip -> mid -> end), for LineAt
        Vector3 curveTip, curveMid, curveEnd;
        Vector2 curveBob;
        bool curveOn;

        /// <summary>
        /// A point of the line above the water as drawn this frame (<paramref name="t"/> = 0 at the rod tip .. 1 at its end:
        /// the float, the line's <see cref="WaterEntry"/>, a fish in the air), in game space, lying on the water (or the ice)
        /// where the drawn line does; <paramref name="bob"/> is the deck bob it is drawn with there (the shiver is left out).
        /// False while no line runs to a target (Tackle: the float riding the line in a fight).
        /// </summary>
        public bool LineAt(float t, out Vector3 p, out Vector2 bob)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            p = u * u * curveTip + 2f * u * t * curveMid + t * t * curveEnd;
            p.y = Mathf.Max(p.y, SurfaceUnder(p));
            bob = curveBob * u;
            return curveOn;
        }

        /// <summary>Surface point where the line from the rod tip to <paramref name="sub"/> enters the water.</summary>
        Vector3 EntryPoint(Vector3 tip, Vector3 sub)
        {
            var L = stage.L;
            if (L.IsIce) return new Vector3(L.holeX, 0, L.holeZ); // always through the hole
            float s = tip.y / Mathf.Max(0.01f, tip.y - sub.y);
            var cross = Vector3.Lerp(tip, sub, s);
            var e = Vector3.Lerp(cross, new Vector3(sub.x, 0, sub.z), SurfaceBend);
            e.y = 0;
            return e;
        }

        static Vector3 Snap(Vector2 p) => new Vector3(Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU, 0);
    }

    /// <summary>A convex outline on screen (the hat seen from behind) and signed distances to it (negative inside).</summary>
    internal static class ScreenHull
    {
        /// <summary>The convex hull (counter-clockwise) of the points; sorts them in place.</summary>
        public static System.Collections.Generic.List<Vector2> Of(System.Collections.Generic.List<Vector2> p)
        {
            p.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            var h = new System.Collections.Generic.List<Vector2>(p.Count + 1);
            float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    var v = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], v) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(v);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        public static float SignedDist(System.Collections.Generic.List<Vector2> hull, Vector2 q)
        {
            bool inside = true;
            float d = float.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                var u = hull[i];
                var v = hull[(i + 1) % hull.Count];
                var e = v - u;
                if (e.x * (q.y - u.y) - e.y * (q.x - u.x) < 0f) inside = false;
                float t = Mathf.Clamp01(Vector2.Dot(q - u, e) / Mathf.Max(1e-6f, e.sqrMagnitude));
                d = Mathf.Min(d, (u + e * t - q).magnitude);
            }
            return inside ? -d : d;
        }
    }
}
