using System;
using UnityEngine;

namespace FishingKing
{
    /// <summary>Where and how a cast came down (Docs/obstacles_spec.md 4.5): the point, and what it met on the way.</summary>
    public struct Landing
    {
        public Vector3 at;
        /// <summary>The last solid the flying rig hit (null: none), how many contacts in all.</summary>
        public Obstacle struck;
        public int contacts;
        /// <summary>Landed on a lily pad (null: no).</summary>
        public Obstacle pad;
        /// <summary>Came to rest on a prop's top (null: no; see <see cref="bank"/>).</summary>
        public Obstacle perch;
        /// <summary>Came to rest on the bank (not a valid water point).</summary>
        public bool bank;
        /// <summary>Slid down a prop's face into the water after a side contact.</summary>
        public bool slid;
        /// <summary>Entered the water under an overhang's shade (null: no).</summary>
        public Obstacle overhang;
        public bool Perched => perch != null || bank;
    }

    /// <summary>A snagged rig (Docs/obstacles_spec.md 6.4): the zone, where it holds, the way a sweep frees it, the tension ratio.</summary>
    public class SnagInfo
    {
        public Obstacle zone;
        public Vector3 at;
        public int freeSide;
        /// <summary>Weed, reed or a pad: a sweep either way frees it.</summary>
        public bool soft;
        /// <summary>"hard", "weed", "reed" or "pad".</summary>
        public string kind;
        /// <summary>0..1+: the snag tension ratio (winding against it builds it; 1 held = the line breaks).</summary>
        public float r;
    }

    /// <summary>
    /// Float + bait (natural baits) or lure. Lives in game space: <see cref="Surface"/> is the point
    /// on the water (y = 0) where the float sits / the line enters, <see cref="Depth"/> is the hook depth.
    /// <para>A lure at rest follows its buoyancy (Docs/lures_legend_spec.md 1.3): a sinking one goes down to the bottom (through
    /// the ice: to the depth the drag let it down to, <see cref="Floor"/>) at its sink speed (its fall speed after a flick or
    /// a stop), a suspending one only to its swim depth, a floating one back up to the surface. Winding moves the rig to the shore and changes its depth by the lure's wind lift per metre
    /// (a negative lift dives it: the crankbait); a flick (<see cref="Twitch"/>) hops it, pulls it to the shore and darts it
    /// sideways, and starts the fall (<see cref="Falling"/> / <see cref="FallT"/>).</para>
    /// <para>With the rod swept to one side (the controller's rod sweep) a rig being wound in comes along a path bent towards
    /// the rod tip's side (<see cref="SweepLateral"/>), and a float rig at rest is dragged a little that way
    /// (<see cref="Drag"/>).</para>
    /// <para>With a fish on, a float rig's float stays fixed on the line <see cref="FloatDepth"/> up from the hook
    /// (<see cref="FloatFight"/>: riding the surface at the line's entry, pulled under along the line, skipping in a jump,
    /// hanging from the rod with the landed fish); it is placed in LateUpdate, after the Angler has drawn this frame's line
    /// (the execution order below) and before the side-pressure arrow reads it (100).</para>
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class Tackle : MonoBehaviour
    {
        public enum Mode { Hidden, Flying, Water, Held, Perched }

        public Mode State { get; private set; } = Mode.Hidden;
        public Vector3 Surface;
        public float Depth;
        /// <summary>The float's depth; through the ice the drag's depth, which a lure also stops at (<see cref="Floor"/>).</summary>
        public float FloatDepth = 2f;
        public BaitDef Bait { get; private set; }
        public bool UsesFloat => Bait != null && !Bait.isLure;
        public Vector3 FlyPos { get; private set; }
        public float MoveSpeed { get; private set; }  // m/s, smoothed (float baits: a moving rig scares the fish)
        public Vector3 HookPos => new Vector3(Surface.x, -Depth, Surface.z);

        // ---- lure signals (read by the lure rhythm, the legend watch and the HUD)
        /// <summary>The lure is on (or within 0.3 m of) the bottom (through the ice: of the depth it was let down to, <see cref="Floor"/>).</summary>
        public bool OnBottom { get; private set; }
        /// <summary>True for the one frame the lure touches down on the bottom (through the ice: comes down to <see cref="Floor"/>).</summary>
        public bool BottomTouch { get; private set; }
        /// <summary>Seconds since the lure last touched down on the bottom.</summary>
        public float SinceTouch { get; private set; } = 99f;
        /// <summary>Moving down after a flick (or a stop of the reel) and not yet at its rest depth.</summary>
        public bool Falling { get; private set; }
        /// <summary>How long the current fall has lasted.</summary>
        public float FallT { get; private set; }
        /// <summary>The lure is still being lifted / pulled by the last flick.</summary>
        public bool Hopping => twitchLeft > 0f;
        /// <summary>The bottom under the rig (hook depth limit).</summary>
        public float Bottom => stage.L.DepthAt(Surface.z) - 0.25f;
        /// <summary>
        /// Where a sinking lure comes to rest: the bottom; through the ice the depth the drag let it down to
        /// (<see cref="FloatDepth"/>), no deeper than the bottom, and on it within <see cref="IceFloorSnap"/> (the full drag
        /// stops 5 cm short of it). Its lifts and falls and the bottom signals work around it.
        /// </summary>
        public float Floor
        {
            get
            {
                float b = Bottom;
                if (!stage.L.IsIce || FloatDepth >= b - IceFloorSnap) return b;
                return Mathf.Max(0.05f, FloatDepth);
            }
        }
        const float IceFloorSnap = 0.1f;

        /// <summary>
        /// Point the fishing line is drawn to: the float, or a sunk lure itself (the angler's line then cuts the
        /// surface on the way, see <see cref="Angler.WaterEntry"/>).
        /// </summary>
        public Vector3 LineEnd => State == Mode.Flying ? FlyPos : State == Mode.Perched ? PerchAt : !UsesFloat && Depth > 0.05f ? HookPos : Surface;

        /// <summary>Perspective scale of the float at <see cref="Surface"/> (1 = native size at the angler's feet).</summary>
        public float FloatScale { get; private set; } = 1f;

        // smallest on-screen sizes, so a far float can still show a bite and the bait stays visible
        const float FloatMinPx = 8f, BaitMinPx = 4f;
        const float TwitchTime = 0.14f;   // a flick's hop / pull / dart plays out over this long
        const float WoundGrace = 0.15f;   // winding within this long holds the lure's depth (no sinking / rising)
        const int OrderUnder = 13;        // lure glints and silt, just above the bait sprite (12)

        StageView stage;
        Persp P;
        SpriteRenderer floatSr, baitSr, underLine, haloSr;
        Vector3 flyFrom, flyTo;
        float flyT, flyDur, flyArc;
        Action<Landing> onLand;
        // ---- the flight after a contact with a solid (Docs/obstacles_spec.md 4.2-4.4): ballistic with the arc's own
        // gravity, or sliding down the face into the water; at most 3 contacts
        bool ballistic, sliding;
        Vector3 bVel, slideFrom, slideTo;
        float bGrav, slideT, bT;
        int contacts;
        Obstacle struck;
        /// <summary>A contact with a solid in flight (for the controller's word, sound and FX): the hit and the normal speed (m/s).</summary>
        public event Action<ObstacleHit, float> Contact;

        // ---- surfaces and snags (Docs/obstacles_spec.md 5, 6)
        /// <summary>Perched: where it rests (on a prop's top, or on the bank) and the prop (null: the bank).</summary>
        public Vector3 PerchAt { get; private set; }
        public Obstacle PerchO { get; private set; }
        /// <summary>The lure sits on this lily pad (the frog, the popper): no drift, no snag; winding crawls it across.</summary>
        public Obstacle OnPad { get; private set; }
        /// <summary>The rig is snagged (held fixed at the snag point; null: free).</summary>
        public SnagInfo Snag { get; private set; }
        Obstacle padLeft;
        Obstacles Obst => stage.Obstacles;
        float bobT, dip, dipTarget;
        Vector3 lastHook;
        // lure motion
        float windT = 99f;                // since the last wind
        float twitchLeft, twitchHop;      // hop metres (+ = up) spread over TwitchTime
        Vector3 twitchMove;
        bool fallArmed, touched;
        int dartSide = 1;
        float kick, fxT, lookT;

        public static Tackle Create(StageView s)
        {
            var t = new GameObject("Tackle").AddComponent<Tackle>();
            t.stage = s;
            t.P = s.P;
            t.floatSr = t.Sprite("Float", Fx.OrderRipple + 1);
            t.baitSr = t.Sprite("Bait", 12);
            t.haloSr = t.Sprite("Halo", 11);
            t.haloSr.sprite = Halo;
            t.underLine = t.Sprite("UnderLine", 11);
            t.underLine.sprite = Art.Pixel;
            t.chemiSr = t.Sprite("Chemi", Fx.OrderRipple + 2);
            t.chemiSr.sprite = Chemi;
            // over the front layer (the bait in flight / perched, the float on the line in the air) they are hidden where
            // the front layer is nearer (FrontOcclusion); on / under the water they are drawn under it anyway (no depth)
            FrontOcclusion.Use(t.floatSr);
            FrontOcclusion.Use(t.baitSr);
            FrontOcclusion.Use(t.chemiSr);
            t.SetBait(Game.I.Bait);
            t.Hide();
            return t;
        }

        // ---- the moving water (Docs/time_currents_spec.md 9.1-9.3): the rig drifts, the line bows, a mend takes the bow out
        /// <summary>The line's belly (m, + = to his right of the straight line from him to the rig).</summary>
        public float Bow { get; private set; }
        /// <summary>The right-hand normal of the line (world, horizontal): <see cref="Bow"/> is measured along it.</summary>
        public Vector3 BowNormal { get; private set; } = Vector3.right;
        /// <summary>The hook's velocity through the water (m/s, (x, z)): its own motion minus the water's drift, smoothed.</summary>
        public Vector2 RelVel { get; private set; }
        /// <summary>|<see cref="RelVel"/>|: 0 for a float riding the water freely, 0.12 |Bow| for one dragged by its line.</summary>
        public float RelSpeed { get; private set; }
        /// <summary>Set by the controller: a steady lure hanging in the current (it does not drift and keeps its depth).</summary>
        public bool Hanging;
        /// <summary>The drift velocity this frame without the bow's drag (m/s, (x, z)).</summary>
        public Vector2 FreeDrift { get; private set; }
        Vector2 prevFlat;
        int currentFrame = -9;
        // the side a drifting rig is stepping round a prop by (StepCurrent)
        int driftRound;
        Obstacle driftRoundO;
        float mendT = -1f, mendFrom;
        SpriteRenderer chemiSr;
        /// <summary>The float, the bait and the 케미 light as drawn (the occlusion watch).</summary>
        internal SpriteRenderer FloatR => floatSr;
        internal SpriteRenderer BaitR => baitSr;
        internal SpriteRenderer ChemiR => chemiSr;

        /// <summary>
        /// One frame of the current on the rig in the water (called by the controller while waiting / retrieving, not on the
        /// ice): the line bows with the current across it (dBow/dt = 0.9 c_perp - Bow / 4 at rest, decaying over 0.6 s while
        /// wound in; a float rig and a lure at rest for 0.3 s), and the rig drifts: a float with 0.85 x the water plus the
        /// wind plus the bow's drag (0.12 Bow along the normal), a lure on the film with 0.7 x the water plus half the wind,
        /// a lure under it with 0.7 x the water x exp(-depth / 4) (x 0.1 on the bottom, none while hanging); then it is kept
        /// in the water, within the line's reach, and never drifts into the shallows at his feet. Pressed onto the sea's
        /// tetrapods, in the slack against them, it lodges there.
        /// </summary>
        public void StepCurrent(float dt, CurrentField cf, Vector3 anchor, Vector3 shore, bool winding, float maxDist)
        {
            currentFrame = Time.frameCount;
            if (State != Mode.Water || stage.L.IsIce || cf == null || dt <= 0f || OnPad != null || Snag != null)
            {
                FreeDrift = Vector2.zero;
                return;
            }
            var L = stage.L;
            var flat = new Vector2(Surface.x - anchor.x, Surface.z - anchor.z);
            float len = flat.magnitude;
            var u = len > 0.01f ? flat / len : Vector2.up;
            var n = new Vector2(u.y, -u.x);
            BowNormal = new Vector3(n.x, 0f, n.y);
            // the bow
            bool rest = UsesFloat || windT >= 0.3f;
            if (winding || !rest) Bow *= Mathf.Exp(-dt / 0.6f);
            else
            {
                var mid = (new Vector2(shore.x, shore.z) + new Vector2(Surface.x, Surface.z)) * 0.5f;
                float cPerp = Vector2.Dot(cf.Water(mid.x, mid.y) + cf.Wind, n);
                Bow += (0.9f * cPerp - Bow / 4f) * dt;
            }
            if (mendT >= 0f)
            {
                // a mend: the belly flipped upstream, 80 % of it gone over 0.25 s (ease-out)
                mendT += dt;
                float k = Mathf.Clamp01(mendT / 0.25f);
                k = 1f - (1f - k) * (1f - k);
                Bow = Mathf.Lerp(mendFrom, 0.2f * mendFrom, k);
                if (mendT >= 0.25f) mendT = -1f;
            }
            float bowMax = Mathf.Min(0.25f * len, 4f);
            Bow = Mathf.Clamp(Bow, -bowMax, bowMax);
            // the drift
            var w = cf.Water(Surface.x, Surface.z);
            bool film = !UsesFloat && (Bait.buoyancy == Buoyancy.Float || Depth <= 0.12f);
            Vector2 v;
            if (UsesFloat) v = 0.85f * w + cf.Wind;
            else if (film) v = 0.7f * w + 0.5f * cf.Wind;
            else v = 0.7f * CurrentField.Kd(Depth) * w * (OnBottom ? 0.1f : 1f);
            if (Hanging) v = Vector2.zero;
            FreeDrift = v;
            var total = v + (UsesFloat ? 0.12f * Bow * n : Vector2.zero);
            if (total.sqrMagnitude < 1e-10f) return;
            float z0 = Surface.z;
            var from = Surface;
            Surface += new Vector3(total.x, 0f, total.y) * dt;
            Surface.x = Mathf.Clamp(Surface.x, -L.xLim + 0.6f, L.xLim - 0.6f);
            Surface.z = Mathf.Min(Surface.z, L.zFar - 1f);
            // the line holds it: never further than its reach (it swings round instead)
            var off = new Vector2(Surface.x - anchor.x, Surface.z - anchor.z);
            if (off.magnitude > maxDist)
            {
                off = off.normalized * maxDist;
                Surface = new Vector3(anchor.x + off.x, 0f, anchor.z + off.y);
            }
            // in the shallows at his feet the drift stops (that is no retrieve)
            float floor = Mathf.Min(z0, L.zNear + 1f);
            if (Surface.z < floor) Surface.z = floor;
            Surface.y = 0f;
            // the water never carries it out of view or in behind the front layer (the sea's xLim is 90 m, the ocean's 120:
            // the tide would take a float off the screen in seconds): it stops against the edge and slides along it. A rig
            // already outside (a cast at the edge) may only come back in. A float goes on in behind a midstream rock's
            // painted top as long as it still shows over it (Open), up to the rock itself.
            var wf = stage.Water;
            if (wf != null)
            {
                bool fromOpen = Open(from);
                // (and never into a prop standing in the water: it stops against it and slides along it)
                bool Ok(Vector3 p) => (Open(p) || (!fromOpen && Mathf.Abs(p.x) <= Mathf.Abs(from.x))) && Mathf.Abs(p.x) <= L.xLim - 0.6f + 1e-3f
                                      && new Vector2(p.x - anchor.x, p.z - anchor.z).magnitude <= maxDist + 1e-3f && !Blocked(p);
                // the sea's tide runs slack against the tetrapods (CurrentField.Cushion): a rig that the last of it or the
                // line's belly presses onto them lodges in that slack where it touches, instead of sliding along their face
                // (which took a float left alone at the flood's peak into the shallows at his feet)
                if (!Ok(Surface) && cf.Slack(from.x, from.z))
                {
                    Surface = new Vector3(from.x, 0f, from.z);
                    return;
                }
                // against a prop's face (or a midstream rock's painted top that would hide the float): the water carries it
                // on round the prop's side, not pinned there for good (spec 4.9 "stops at it and swings round")
                if (!Ok(Surface) && Obst != null && !Obst.Empty)
                {
                    var s2 = new Vector2(Surface.x, Surface.z);
                    var f2 = new Vector2(from.x, from.z);
                    var step = s2 - f2;
                    float sl = step.magnitude;
                    var rock = UsesFloat && !Obst.BlockedAtSurface(s2, 0.05f) ? RockOver(Surface) : null;
                    if (rock != null && sl > 1e-6f)
                    {
                        // held off by a midstream rock's painted top (on it would hide the float): it works out sideways at
                        // its drift speed towards the rock's nearer side (kept while it goes round this rock, else the other),
                        // on down with the water where it still shows
                        int side = rock == driftRoundO && driftRound != 0 ? driftRound : from.x >= rock.C.x ? 1 : -1;
                        for (int k = 0; k < 4; k++)
                        {
                            if (k == 2) side = -side;
                            var round = new Vector3(from.x + side * sl, 0f, k % 2 == 0 ? Surface.z : from.z);
                            if (!Ok(round)) continue;
                            Surface = round;
                            driftRound = side;
                            driftRoundO = rock;
                            break;
                        }
                    }
                    else if (Obst.BlockedAtSurface(s2, 0.05f, out var po) && sl > 1e-6f)
                    {
                        // it slides along the face at half its drift speed until the way on is clear: towards the side it is
                        // already off the prop's middle (kept while it works its way round this prop), else the other
                        var lat = new Vector2(-step.y, step.x) / sl;
                        Obstacles.EdgeDist(po.Poly, f2, out _, out var en);
                        var tg = new Vector2(-en.y, en.x);
                        int side = po == driftRoundO && driftRound != 0 ? driftRound : Vector2.Dot(f2 - po.C, lat) >= 0f ? 1 : -1;
                        // (along the face that way, else straight across the drift that way; then the other side)
                        for (int k = 0; k < 4; k++)
                        {
                            if (k == 2) side = -side;
                            var mv = (k % 2 == 0 ? (Vector2.Dot(tg, lat) * side >= 0f ? tg : -tg) : lat * side) * (0.5f * sl);
                            var round = new Vector3(from.x + mv.x, 0f, from.z + mv.y);
                            if (!Ok(round)) continue;
                            Surface = round;
                            driftRound = side;
                            driftRoundO = po;
                            break;
                        }
                    }
                }
                if (!Ok(Surface))
                {
                    var a = new Vector3(Surface.x, 0f, from.z);
                    var b = new Vector3(from.x, 0f, Surface.z);
                    if (Mathf.Abs(Surface.z - from.z) > Mathf.Abs(Surface.x - from.x)) (a, b) = (b, a);
                    Surface = Ok(a) ? a : Ok(b) ? b : new Vector3(from.x, 0f, from.z);
                }
            }
        }

        /// <summary>
        /// A mend (the rod swept against the line's belly): the bow drops to 20 % over 0.25 s, and this sweep's float drag
        /// (<see cref="Drag"/>) is used up but for 0.15 m. <paramref name="side"/>: the side the rod was swept to.
        /// </summary>
        public void Mend(int side)
        {
            mendFrom = Bow;
            mendT = 0f;
            dragSide = side;
            dragUsed = Mathf.Max(dragUsed, SweepDragMax - 0.15f);
        }

        static Sprite chemi;

        /// <summary>The float's night light (케미): a 2x2 px #c8ff70 core with a 1 px halo at 0.35.</summary>
        static Sprite Chemi
        {
            get
            {
                if (chemi != null) return chemi;
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var core = new Color32(0xc8, 0xff, 0x70, 0xff);
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    bool inner = x >= 1 && x <= 2 && y >= 1 && y <= 2;
                    bool corner = (x == 0 || x == 3) && (y == 0 || y == 3);
                    t.SetPixel(x, y, inner ? (Color)core : corner ? Color.clear : new Color(core.r / 255f, core.g / 255f, core.b / 255f, 0.35f));
                }
                t.Apply();
                return chemi = UnityEngine.Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 16);
            }
        }

        SpriteRenderer Sprite(string name, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sortingOrder = order;
            return sr;
        }

        public void SetBait(BaitDef b)
        {
            Bait = b;
            baitSr.sprite = BareHook ? Art.Get(BareHookSprite) : Art.WorldBait(b.id);
            floatSr.sprite = Art.Get(FloatSprite);
        }

        /// <summary>The float this stage's float rigs use ("World/float_ball" at sea, "World/float_stick" elsewhere).</summary>
        public string FloatSprite => stage.L.id == "sea" || stage.L.id == "ocean" ? "World/float_ball" : "World/float_stick";
        /// <summary>The float's item icon (Items/float_ball / float_stick: the line-break loss toast).</summary>
        public string FloatItemId => stage.L.id == "sea" || stage.L.id == "ocean" ? "float_ball" : "float_stick";

        // ---- the bait gone (a float rig: eaten at a bite whose fish got off, stolen, torn off on a pad, lost at a snag)
        public const string BareHookSprite = "World/hook_bare_w";
        /// <summary>The natural bait is off the hook: the rig is drawn with the bare hook until it is cast again (re-baited).</summary>
        public bool BaitGone { get; private set; }
        /// <summary>A float rig with its bait gone: drawn with the bare hook, no fish takes it.</summary>
        public bool BareHook => BaitGone && UsesFloat;

        /// <summary>The bait is off the hook (a float rig): the bare hook is drawn in its place until the next cast.</summary>
        public void TakeBait()
        {
            BaitGone = true;
            if (UsesFloat) baitSr.sprite = Art.Get(BareHookSprite);
        }

        /// <summary>A new cast re-baits the hook.</summary>
        void Rebait()
        {
            if (!BaitGone) return;
            BaitGone = false;
            if (Bait != null) baitSr.sprite = Art.WorldBait(Bait.id);
        }

        /// <summary>
        /// Set while the line-snap's free end flies back (LineSnap): the hook is drawn there, not under the float, and the
        /// straight line from the float to the hook is left to the snap's curl. Null otherwise.
        /// </summary>
        internal Vector3? HookOverride;

        /// <summary>A plan point inside a prop standing in the water (+5 cm): no rig in the water goes there (spec 4.9).</summary>
        bool Blocked(Vector3 p) => !stage.L.IsIce && Obst != null && !Obst.Empty && Obst.BlockedAtSurface(new Vector2(p.x, p.z), 0.05f);

        /// <summary>Pixel rows of a float's top that must still show over a midstream rock's painted top it drifts in behind.</summary>
        const int RockPeekPx = 5;   // (4 rows and the float's 1 px bob)
        /// <summary>Front-layer pixels this near a midstream rock's footprint (m) are its painted base and foam.</summary>
        const float RockPaintM = 0.6f;

        /// <summary>
        /// Visible water the current or the rod sweep may take the rig to: in view and not behind the front layer
        /// (<see cref="WaterFx.DriftOpen"/>); a float also on in behind a midstream rock's painted top, which hides the water
        /// just beyond the rock, as long as <see cref="RockPeekPx"/> of it still show over the rock (so it drifts on up to
        /// the rock itself, spec 4.9, and never out of sight behind it).
        /// </summary>
        bool Open(Vector3 p)
        {
            var wf = stage.Water;
            if (wf == null || wf.DriftOpen(p.x, p.z)) return true;
            return UsesFloat && RockOver(p) != null && Peeks(p);
        }

        /// <summary>
        /// The midstream rock (an exported prop, Data/obstacles_stream.json) painted over the water at <paramref name="p"/>
        /// (null: none, or out of view): within <see cref="RockPaintM"/> of its footprint (its painted base and foam), or
        /// hidden from the camera by it (its 1 px outline reaches ~0.2 m past what the geometry hides, so the points 0.25 m
        /// nearer the camera and 0.2 m nearer the rock count too).
        /// </summary>
        Obstacle RockOver(Vector3 p)
        {
            var wf = stage.Water;
            if (wf == null || Obst == null || Obst.Empty || !wf.InView(p.x, p.z)) return null;
            var xz = new Vector2(p.x, p.z);
            var cam = P.CameraPos;
            var q = new Vector3(p.x, 0f, p.z);
            var toCam = new Vector3(cam.x - p.x, 0f, cam.z - p.z).normalized;
            foreach (var s in Obst.Solids)
            {
                if (!s.Standing || !s.Has("midstream")) continue;
                float d = Obstacles.Dist(s, xz);
                if (d <= RockPaintM) return s;
                if (d > 5f) continue;   // (the water a rock's top hides reaches ~3 m past it)
                var toC = s.C - xz;
                var toRock = new Vector3(toC.x, 0f, toC.y).normalized;
                if (Obst.Occluded(q, cam, out var o) || Obst.Occluded(q + toCam * 0.25f, cam, out o) || Obst.Occluded(q + toRock * 0.2f, cam, out o))
                    if (o == s) return s;
            }
            return null;
        }

        /// <summary>A float on the water at <paramref name="p"/> shows its top <see cref="RockPeekPx"/> pixel rows (drawn as <see cref="Render"/> places it).</summary>
        bool Peeks(Vector3 p)
        {
            float h = floatSr.sprite.rect.height;
            float s = P.ScaleAt(p, h, FloatMinPx);
            var at = P.To2D(p);
            float top = (0.25f * PixelView.PPU + 0.5f * h) * s;
            for (int k = 0; k < RockPeekPx; k++)
                if (stage.Water.FrontAt(at + new Vector2(0f, (top - 0.5f - k) / PixelView.PPU))) return false;
            return true;
        }

        int bendSide;

        /// <summary>
        /// A rig wound <paramref name="metres"/> into a prop standing in the water from <paramref name="from"/>: it slides
        /// that far along the prop's nearest edge, the way that brings it nearer <paramref name="goal"/> (straight behind
        /// the prop: the side it last went round, else its right), then out of the prop.
        /// </summary>
        Vector3 BendRound(Vector3 from, float metres, Vector3 goal)
        {
            var xz = new Vector2(from.x, from.z);
            var ahead = from + new Vector3(goal.x - from.x, 0f, goal.z - from.z).normalized * metres;
            if (!Obst.BlockedAtSurface(new Vector2(ahead.x, ahead.z), 0.05f, out var o)) return PushOut(ahead);
            Obstacles.EdgeDist(o.Poly, xz, out _, out var n);
            var t = new Vector2(-n.y, n.x);
            var g = new Vector2(goal.x - from.x, goal.z - from.z);
            float along = Vector2.Dot(t, g.normalized);
            if (Mathf.Abs(along) < 0.05f) t *= bendSide != 0 ? bendSide : 1;
            else
            {
                if (along < 0f) t = -t;
                bendSide = Vector2.Dot(t, new Vector2(-n.y, n.x)) >= 0f ? 1 : -1;
            }
            var np = xz + t * metres;
            return PushOut(new Vector3(np.x, 0f, np.y));
        }

        /// <summary>Out of any prop standing in the water, along its nearest edge's normal.</summary>
        Vector3 PushOut(Vector3 p)
        {
            if (!Blocked(p)) return p;
            var q = Obst.PushOut(new Vector2(p.x, p.z), 0.05f);
            return new Vector3(q.x, 0f, q.y);
        }

        public void Hide()
        {
            State = Mode.Hidden;
            OnPad = padLeft = padSlid = null;   // (a pad left while retrieved must not drop the next cast "off" it)
            padSlideT = -1f;
            Snag = null;
            PerchO = null;
            sliding = ballistic = false;
            FloatFight = FightFloat.Off;
            FloatRiding = FloatInAir = false;
            HookOverride = null;
            floatSr.enabled = baitSr.enabled = underLine.enabled = haloSr.enabled = false;
            if (chemiSr != null) chemiSr.enabled = false;
        }

        public void Launch(Vector3 from, Vector3 to, Action<Landing> landed)
        {
            flyFrom = from;
            flyTo = to;
            float dist = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
            flyDur = 0.45f + dist * 0.022f;
            flyArc = 1.2f + dist * 0.28f;
            flyT = 0;
            onLand = landed;
            Rebait();
            HookOverride = null;
            ballistic = sliding = false;
            contacts = 0;
            struck = null;
            bT = 0f;
            bGrav = Mathf.Clamp(-8f * flyArc / (flyDur * flyDur), -90f, -25f);
            OnPad = null;
            Snag = null;
            PerchO = null;
            State = Mode.Flying;
            FlyPos = from;
            floatSr.enabled = false;
            baitSr.enabled = true;
            var tint = stage.ActorTint;
            baitSr.color = new Color(tint.r, tint.g, tint.b, 1f);
            baitSr.transform.rotation = Quaternion.identity;
            // (placed, ordered and given its depth now: launched after this frame's Update, it would otherwise show for a
            // frame where and as it was last drawn)
            DrawFlying(from);
        }

        public void EnterWater(Vector3 at)
        {
            Rebait();
            HookOverride = null;
            Surface = PushOut(new Vector3(at.x, 0, at.z));
            Depth = 0;
            State = Mode.Water;
            OnPad = padLeft = padSlid = null;
            padSlideT = -1f;
            Snag = null;
            PerchO = null;
            lastHook = HookPos;
            dip = dipTarget = 0;
            floatSr.enabled = UsesFloat;
            windT = 99f;
            twitchLeft = 0f;
            fallArmed = touched = Falling = false;
            FallT = 0f;
            SinceTouch = 99f;
            OnBottom = BottomTouch = false;
            kick = 0f;
            dragUsed = 0f;
            dragSide = 0;
            SweepDrift = 0f;
            Bow = 0f;
            mendT = -1f;
            Hanging = false;
            FreeDrift = RelVel = Vector2.zero;
            RelSpeed = 0f;
            prevFlat = new Vector2(Surface.x, Surface.z);
            if (Bait.isLure && Bait.buoyancy == Buoyancy.Float) Depth = 0.05f;
        }

        public void Hold() => State = Mode.Held;

        /// <summary>Back in the water where it was (after a legend encounter that came to nothing).</summary>
        public void Resume()
        {
            if (State != Mode.Held) return;
            State = Mode.Water;
            lastHook = HookPos;
            windT = 99f;
        }

        // ---- the rod sweep (FishingController: the rod swept to one side while the rig is in the water)
        /// <summary>
        /// Winding in (or a 톡's pull) with the rod swept: for every metre the rig comes in it also drifts this much times the
        /// sine of the sweep sideways, towards the rod tip's side (seen from the angler), so its path bends that way.
        /// </summary>
        public const float SweepLateral = 0.6f;
        /// <summary>A float rig at rest (not wound) is dragged sideways at most this far per sweep (m)...</summary>
        public const float SweepDragMax = 1.5f;
        /// <summary>...at this speed (m/s) at a full sweep (sin 30 deg), slower at a smaller one; the line does not lengthen.</summary>
        public const float SweepDragSpeed = 0.3f;
        float dragUsed;
        int dragSide;
        /// <summary>Metres the rig has drifted sideways from the sweep since it went in the water (+ = to his right; for the tests).</summary>
        public float SweepDrift { get; private set; }

        /// <summary>
        /// Winding in: moves the rig towards <paramref name="to"/>; a lure's depth changes by its wind lift per metre. With
        /// the rod swept (<paramref name="sweepSin"/> = sine of the sweep, + = right) the path bends towards the rod tip's
        /// side (<see cref="SweepLateral"/>).
        /// </summary>
        public void Wind(float metres, Vector3 to, float sweepSin = 0f)
        {
            if (State != Mode.Water || metres <= 0 || Snag != null || PadSliding) return;
            var flat = new Vector3(to.x - Surface.x, 0, to.z - Surface.z);
            float d = flat.magnitude;
            if (OnPad != null)
            {
                // on a lily pad it crawls across it towards him (pad friction: 0.7 x the wound distance)
                if (d > 0.01f) Surface += flat / d * Mathf.Min(d, metres * 0.7f);
                windT = 0f;
                CheckPadLeave();
                return;
            }
            if (d > 0.01f)
            {
                float m = Mathf.Min(d, metres);
                var before = Surface;
                Surface += flat / d * m;
                // wound against a prop in the water, it bends round it (along its edge, the way to him)
                if (Blocked(Surface)) Surface = BendRound(before, m, to);
                if (sweepSin != 0f) Sideways(flat / d, m * SweepLateral * sweepSin);
            }
            if (UsesFloat) return;
            windT = 0f;
            Depth = ClampLure(Depth - metres * Bait.windLift);
            // stopping the reel lets it fall (the spoon flutters down)
            fallArmed = true;
            Falling = false;
            FallT = 0f;
        }

        /// <summary>
        /// Moves the rig <paramref name="metres"/> sideways (+ = to the angler's right as he looks at it) across
        /// <paramref name="toShore"/> (the unit direction from the rig to him), inside the stage's visible water.
        /// </summary>
        void Sideways(Vector3 toShore, float metres)
        {
            var right = new Vector3(-toShore.z, 0f, toShore.x);
            float x0 = Surface.x, z0 = Surface.z;
            var from = new Vector3(x0, 0f, z0);
            Surface += right * metres;
            var L = stage.L;
            Surface.x = Mathf.Clamp(Surface.x, -L.xLim + 0.6f, L.xLim - 0.6f);
            // inside the water: short of the far edge, and never in towards the bank past SweepNearZ (a sideways move alone
            // never takes it onto the shore or brings it home; winding in may still take it lower)
            Surface.z = Mathf.Clamp(Surface.z, Mathf.Min(z0, L.zNear + SweepNearZ), Mathf.Max(z0, L.zFar - 1f));
            Surface.y = 0f;
            // in the visible water, as the current's drift (StepCurrent): never out of view or in behind the front layer. A
            // rig already out of view may only come back in; one behind the front layer (wound in under the pier) may go
            // either way
            var wf = stage.Water;
            bool fromOpen = Open(from), fromInView = wf == null || wf.InView(x0, z0);
            bool Ok(Vector3 p) => !Blocked(p) && (Open(p) || (!fromOpen && (fromInView || Mathf.Abs(p.x) <= Mathf.Abs(x0))));
            if (!Ok(Surface))
            {
                // against a prop in the water or the edge of the visible water: it stops there and slides along it
                var a = new Vector3(Surface.x, 0f, z0);
                var b = new Vector3(x0, 0f, Surface.z);
                Surface = Ok(a) ? a : Ok(b) ? b : from;
            }
            SweepDrift += Surface.x - x0;
        }

        /// <summary>The rod sweep moves a rig sideways no nearer the bank than this far out from the waterline (m; home is 0.3).</summary>
        public const float SweepNearZ = 0.4f;

        /// <summary>
        /// A float rig at rest with the rod swept (<paramref name="sweepSin"/>, + = right): dragged slowly sideways towards
        /// the rod tip's side, at most <see cref="SweepDragMax"/> m per sweep (a sweep to the other side, or back through the
        /// centre, starts a new one), keeping its distance from <paramref name="anchor"/> (the line does not lengthen).
        /// </summary>
        public void Drag(float sweepSin, Vector3 anchor, float dt)
        {
            if (State != Mode.Water || !UsesFloat || Snag != null) return;
            int side = sweepSin > 0.05f ? 1 : sweepSin < -0.05f ? -1 : 0;
            if (side != dragSide)
            {
                dragSide = side;
                dragUsed = 0f;
            }
            if (side == 0 || dragUsed >= SweepDragMax) return;
            var flat = new Vector3(anchor.x - Surface.x, 0f, anchor.z - Surface.z);
            float r = flat.magnitude;
            if (r < 0.5f) return;
            float step = Mathf.Min(SweepDragMax - dragUsed, SweepDragSpeed * Mathf.Abs(sweepSin) / 0.5f * dt);
            dragUsed += step;
            var before = Surface;
            float drift0 = SweepDrift;
            Sideways(flat / r, side * step);
            // back onto the circle round him: the line holds it at the same distance
            var back = new Vector3(Surface.x - anchor.x, 0f, Surface.z - anchor.z);
            if (back.magnitude > r)
            {
                float x1 = Surface.x;
                Surface = new Vector3(anchor.x, 0f, anchor.z) + back.normalized * r;
                SweepDrift += Surface.x - x1;
            }
            // round the circle towards the bank it goes no further in than SweepNearZ from the waterline, nor back out of the
            // visible water: this sweep's drag ends there (it stays in the water, in sight)
            float floor = stage.L.zNear + SweepNearZ;
            if ((Surface.z < floor && Surface.z < before.z) || (!Open(Surface) && Open(before)))
            {
                Surface = before;
                SweepDrift = drift0;
                dragUsed = SweepDragMax;
            }
        }

        /// <summary>The depths a lure may take: floats up to the film, others just under it; down to its running depth / the bottom.</summary>
        float ClampLure(float depth)
        {
            float bottom = Mathf.Max(0.05f, Bottom);
            float lo = Bait.buoyancy == Buoyancy.Float ? 0.05f : 0.15f;
            float hi = Bait.maxDepth > 0f ? Mathf.Min(Bait.maxDepth, bottom) : bottom;
            return Mathf.Clamp(depth, Mathf.Min(lo, bottom), Mathf.Max(Mathf.Min(lo, bottom), hi));
        }

        /// <summary>Ice fishing: winding only raises the rig.</summary>
        public void Raise(float metres)
        {
            if (State != Mode.Water || Snag != null) return;
            Depth = Mathf.Max(0f, Depth - metres);
            windT = 0f;
            if (!UsesFloat)
            {
                fallArmed = true;
                Falling = false;
                FallT = 0f;
            }
        }

        /// <summary>
        /// A flick (strength 0..1) on a lure: it hops by hopM * (hopBase + (1 - hopBase) * strength) (negative dives), moves
        /// pullM towards <paramref name="shore"/> and darts dartM sideways (alternating), over <see cref="TwitchTime"/>; then
        /// it falls. Topwater lures splash ("퐁"), jerk and vertical lures flash.
        /// </summary>
        public void Twitch(float strength, Vector3 shore, float sweepSin = 0f, float currentMult = 1f)
        {
            if (State != Mode.Water || UsesFloat || Snag != null || PadSliding) return;
            var b = Bait;
            if (OnPad != null)
            {
                // on a lily pad a 톡 hops it 0.35 m towards him
                var toShore = new Vector3(shore.x - Surface.x, 0, shore.z - Surface.z);
                if (toShore.magnitude > 0.05f) Surface += toShore.normalized * Mathf.Min(0.35f, toShore.magnitude);
                dartSide = -dartSide;
                kick = dartSide * 0.8f;
                CheckPadLeave();
                return;
            }
            // (against the current the lure works harder, with it weaker: currentMult = clamp(1 + c_along, 0.5, 1.5))
            twitchHop = b.hopM * (b.hopBase + (1f - b.hopBase) * Mathf.Clamp01(strength)) * currentMult;
            var move = Vector3.zero;
            var flat = new Vector3(shore.x - Surface.x, 0, shore.z - Surface.z);
            float d = flat.magnitude;
            if (d > 0.05f)
            {
                // (with the rod swept the pull bends towards the rod tip's side, like winding in)
                var u = flat / d;
                float m = Mathf.Min(d, b.pullM * currentMult);
                move += u * m;
                if (sweepSin != 0f && !stage.L.IsIce) move += new Vector3(-u.z, 0f, u.x) * (m * SweepLateral * sweepSin);
            }
            if (b.dartM > 0f && !stage.L.IsIce)
            {
                dartSide = -dartSide;
                float x = Mathf.Clamp(Surface.x + dartSide * b.dartM * currentMult, -stage.L.xLim + 0.3f, stage.L.xLim - 0.3f);
                move.x += x - Surface.x;
            }
            twitchMove = move;
            twitchLeft = TwitchTime;
            fallArmed = true;
            Falling = false;
            FallT = 0f;
            kick = dartSide * (0.6f + 0.4f * strength);
            var hp = HookPos;
            switch (b.action)
            {
                case LureAction.Topwater:
                {
                    var s = new Vector3(Surface.x, 0, Surface.z);
                    var s2 = P.To2D(s);
                    float ppm = P.PixelsPerMetre(s);
                    Fx.Splash(s2, Mathf.Clamp(ppm / 40f, 0.3f, 0.7f), stage.WaterTint, 5, P.DepthOf(s));
                    Fx.Ripple(s2, Mathf.Clamp(ppm * 0.9f / 64f, 0.08f, 0.5f), P.Foreshorten(s) * 1.6f + 0.15f, new Color(1, 1, 1, 0.75f), 0.7f);
                    Sfx.PlayVar(Sfx.Plop, 0.55f, 0.12f);
                    break;
                }
                case LureAction.Twitch:
                case LureAction.Vertical:
                    Fx.Sparkle(Snap(P.To2D(P.Apparent(hp))), new Color(1f, 1f, 0.95f, 0.9f), 0.2f, 2f, OrderUnder);
                    break;
            }
        }

        public void Nibble() => dip = -2.5f;
        public void BiteDown() => dipTarget = -7f;
        public void ResetDip() { dipTarget = 0; }

        // ------------------------------------------------------------------ the cast's flight (Docs/obstacles_spec.md 4)
        Vector3 FlyAt(float t)
        {
            var p = Vector3.Lerp(flyFrom, flyTo, t);
            p.y = Mathf.Lerp(flyFrom.y, flyTo.y, t) + flyArc * 4f * t * (1 - t);
            return p;
        }

        /// <summary>The arc's velocity at t (spec 4.2).</summary>
        Vector3 FlyVel(float t) => new Vector3((flyTo.x - flyFrom.x) / flyDur, ((flyTo.y - flyFrom.y) + flyArc * 4f * (1f - 2f * t)) / flyDur, (flyTo.z - flyFrom.z) / flyDur);

        void DrawFlying(Vector3 p)
        {
            baitSr.transform.position = Snap(P.To2D(p, out float pd));
            baitSr.transform.localScale = Vector3.one * BaitScale(p);
            baitSr.sortingOrder = 45;
            FrontOcclusion.SetDepth(baitSr, pd);
        }

        /// <summary>
        /// One frame of the flight: along the arc until the first contact with a solid (not on the ice), then ballistic; at a
        /// contact the rig bounces off (the normal speed x e, the tangential x keep; half e and 0.7 keep on a top; at most 8
        /// m/s), stops below 1.5 m/s or on the third contact: on a top it perches, on a side it slides down the face into
        /// the water. Where it comes down through y = 0 it lands (<see cref="ResolveLanding"/>).
        /// </summary>
        void UpdateFlight(float dt)
        {
            var prev = FlyPos;
            if (sliding)
            {
                slideT += dt / 0.25f;
                var q = Vector3.Lerp(slideFrom, slideTo, Mathf.Clamp01(slideT));
                FlyPos = q;
                DrawFlying(q);
                if (slideT >= 1f) Land(new Vector3(slideTo.x, 0f, slideTo.z), true);
                return;
            }
            float tPrev = Mathf.Clamp01(flyT), t = tPrev;
            Vector3 p;
            if (!ballistic)
            {
                flyT += dt / flyDur;
                t = Mathf.Clamp01(flyT);
                p = FlyAt(t);
            }
            else
            {
                bT += dt;
                bVel.y += bGrav * dt;
                p = prev + bVel * dt;
            }
            var obs = Obst;
            if (obs != null && !obs.Empty && !stage.L.IsIce && contacts < 3 && obs.SweepSolid(prev, p, out var hit))
            {
                var v = ballistic ? bVel : FlyVel(Mathf.Lerp(tPrev, t, hit.t));
                var n = hit.normal;
                float vnS = Vector3.Dot(v, n);
                var vt = v - vnS * n;
                float e = hit.o.Mat.e, keep = hit.o.Mat.keep;
                if (hit.top)
                {
                    e *= 0.5f;
                    keep *= 0.7f;
                }
                var v2 = keep * vt + (vnS < 0f ? -e * vnS : vnS) * n;
                if (v2.magnitude > 8f) v2 = v2.normalized * 8f;
                contacts++;
                struck = hit.o;
                Obstacles.Say(string.Format(System.Globalization.CultureInfo.InvariantCulture, "contact {0} {1} v {2:0.0} -> {3:0.0} at ({4:0.00}, {5:0.00}, {6:0.00}) #{7}",
                    hit.o.id, hit.top ? "top" : "side", v.magnitude, v2.magnitude, hit.at.x, hit.at.y, hit.at.z, contacts));
                Contact?.Invoke(hit, Mathf.Max(0f, -vnS));
                var restart = hit.at + n * 0.02f;
                if (v2.magnitude < 1.5f || contacts >= 3)
                {
                    FlyPos = restart;
                    DrawFlying(restart);
                    if (hit.top) Finish(new Landing { at = restart, perch = hit.o, struck = struck, contacts = contacts });
                    else StartSlide(hit, restart);
                    return;
                }
                ballistic = true;
                bVel = v2;
                p = restart;
            }
            FlyPos = p;
            DrawFlying(p);
            if (!ballistic && flyT >= 1f) Land(flyTo, false);
            else if (ballistic && (p.y <= 0f || bT > 4f))
            {
                // where it came down through the surface
                float k = prev.y > 0f && prev.y > p.y ? prev.y / (prev.y - p.y) : 1f;
                var w = Vector3.Lerp(prev, p, Mathf.Clamp01(k));
                Land(new Vector3(w.x, 0f, w.z), false);
            }
        }

        /// <summary>Stopped on a face: it slides down it, just outside it, into the water over 0.25 s (a natural entry).</summary>
        void StartSlide(ObstacleHit hit, Vector3 from)
        {
            var o = hit.o;
            var n2 = new Vector2(hit.normal.x, hit.normal.z);
            var off = new Vector2(from.x, from.z) - o.C;
            if (n2.sqrMagnitude < 1e-6f) n2 = off.sqrMagnitude > 1e-6f ? off : Vector2.down;
            n2.Normalize();
            var q = new Vector2(from.x, from.z);
            for (int i = 0; i < 80; i++)
            {
                bool inside = Obst.Inside(o, q, 0.02f);
                if (!inside && o.TierPoly != null)
                    foreach (var tp in o.TierPoly) inside |= Obstacles.InPoly(tp, q);
                if (!inside) break;
                q += n2 * 0.05f;
            }
            slideFrom = from;
            slideTo = new Vector3(q.x, 0f, q.y);
            slideT = 0f;
            sliding = true;
        }

        void Land(Vector3 at, bool slid)
        {
            var l = ResolveLanding(at, true);
            l.contacts = contacts;
            l.struck = struck;
            l.slid = slid;
            Finish(l);
        }

        void Finish(Landing l)
        {
            sliding = ballistic = false;
            var cb = onLand;
            onLand = null;
            cb?.Invoke(l);
        }

        /// <summary>
        /// What a rig coming down at <paramref name="at"/> meets (spec 4.5): a lily pad, the bank (with
        /// <paramref name="bankCheck"/>: out of the water box, behind the painted front layer, inside a standing prop), an
        /// overhang's shade; else plain water.
        /// </summary>
        public Landing ResolveLanding(Vector3 at, bool bankCheck)
        {
            var l = new Landing { at = new Vector3(at.x, 0f, at.z) };
            var L = stage.L;
            if (L.IsIce) return l;
            var xz = new Vector2(at.x, at.z);
            var obs = Obst;
            if (obs != null && !obs.Empty)
            {
                l.pad = obs.PadAt(xz);
                l.overhang = obs.UnderOverhang(xz);
            }
            // (the bank is part of the obstacles: without their data the rig lands where it was thrown, as before)
            if (l.pad == null && bankCheck && obs != null && !obs.Empty && !ValidWater(l.at)) l.bank = true;
            return l;
        }

        /// <summary>A water point a rig may enter (spec 4.5): in the fishable box, not behind the front layer, not in a prop.</summary>
        public bool ValidWater(Vector3 at)
        {
            var L = stage.L;
            if (Mathf.Abs(at.x) > L.xLim - 0.6f + 1e-3f || at.z < L.zNear + 0.3f || at.z > L.zFar - 1f + 1e-3f) return false;
            var xz = new Vector2(at.x, at.z);
            if (Obst != null && !Obst.Empty && Obst.BlockedAtSurface(xz, 0f)) return false;
            // behind the painted front layer: the bank; but water hidden behind a prop (a rock between it and the camera)
            // and reeds or pads painted over the water are water
            if (stage.Water != null && stage.Water.BehindFront(at.x, at.z))
                return Obst != null && !Obst.Empty && (Obst.Occluded(new Vector3(at.x, 0f, at.z), P.CameraPos)
                    || Obst.ZoneAt(new Vector3(at.x, -0.05f, at.z), false, true, true) != null || Obst.PadAt(xz) != null);
            return true;
        }

        /// <summary>
        /// Water the angler's own ground hides from the camera (the sea's 3 m breakwater, the ocean's bow: spec 1.3, not an
        /// obstacle): in the box, not a valid water point (behind the front layer, no prop's shadow, reeds or pad), and the
        /// sight line from it to the camera passes under the ground's top where the water begins (zNear; the top taken 1 m
        /// over his feet for the bulwark / the boulder's crown). It is no bank.
        /// </summary>
        public bool HiddenByStand(Vector3 at)
        {
            var L = stage.L;
            if (L.IsIce || stage.Water == null || Obst == null || Obst.Empty) return false;
            if (Mathf.Abs(at.x) > L.xLim - 0.6f + 1e-3f || at.z < L.zNear + 0.3f || at.z > L.zFar - 1f + 1e-3f) return false;
            var cam = P.CameraPos;
            if (cam.z >= L.zNear || at.z <= L.zNear) return false;
            float s = (at.z - L.zNear) / (at.z - cam.z);
            if (s * cam.y >= L.standH + 1f) return false;
            return !Obst.BlockedAtSurface(new Vector2(at.x, at.z), 0f) && !ValidWater(at);
        }

        /// <summary>
        /// A cast's target in water hidden by the angler's own ground (a weak cast off the breakwater or the bow) goes on
        /// out along the cast (from <paramref name="from"/>) to the first water he can see, so it never comes down on a
        /// "bank" in open water or out of sight under the deck; any other target is kept.
        /// </summary>
        public Vector3 PastStand(Vector3 target, Vector3 from)
        {
            if (!HiddenByStand(target)) return target;
            var L = stage.L;
            var dir = new Vector3(target.x - from.x, 0f, target.z - from.z);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();
            for (float d = 0.25f; d <= 20f; d += 0.25f)
            {
                var q = target + dir * d;
                q.x = Mathf.Clamp(q.x, -L.xLim + 0.6f, L.xLim - 0.6f);
                q.z = Mathf.Clamp(q.z, L.zNear + 1.2f, L.zFar - 1f);
                if (!HiddenByStand(q)) return q;
            }
            return target;
        }

        // ------------------------------------------------------------------ perches, pads, snags (spec 5, 6)
        /// <summary>Comes to rest on a prop's top (<paramref name="o"/>) or on the bank (null): the fish ignore it.</summary>
        public void Perch(Vector3 at, Obstacle o)
        {
            State = Mode.Perched;
            PerchAt = at;
            PerchO = o;
            Surface = new Vector3(at.x, 0f, at.z);
            Depth = 0f;
            OnPad = null;
            Snag = null;
            Hanging = false;
            floatSr.enabled = false;
            baitSr.enabled = true;
        }

        /// <summary>The lure sits on a lily pad (the frog, the popper): on the film, no drift, no snag.</summary>
        public void SitOnPad(Obstacle pad)
        {
            OnPad = pad;
            Depth = 0.05f;
        }

        /// <summary>It left the pad since the last call (winding it across, a 톡): the pad, else null.</summary>
        public Obstacle TakePadLeft()
        {
            var p = padLeft;
            padLeft = null;
            return p;
        }

        public void ClearPad() => OnPad = null;

        // ---- a lure sliding off a pad (spec 5.2)
        /// <summary>A lure that does not sit on a pad slides off it over this long (s).</summary>
        public const float PadSlideTime = 0.25f;
        float padSlideT = -1f;
        Vector3 padSlideFrom, padSlideTo;
        Obstacle padSlid;
        /// <summary>Sliding off a pad now (drawn on it; no drift, no winding, no sinking).</summary>
        public bool PadSliding => padSlideT >= 0f;

        /// <summary>
        /// A lure that landed on a lily pad and does not sit on it: it slides from where it came down to
        /// <paramref name="edge"/> (the edge towards him) over <see cref="PadSlideTime"/>, gathering speed, drawn on the pad;
        /// then it is in the water there (<see cref="TakePadSlid"/>).
        /// </summary>
        public void SlideOffPad(Obstacle pad, Vector3 edge)
        {
            OnPad = pad;
            padSlideFrom = Surface;
            padSlideTo = new Vector3(edge.x, 0f, edge.z);
            padSlideT = 0f;
            padSlid = null;
            Depth = 0.05f;
        }

        /// <summary>It came off the pad's edge into the water since the last call: the pad, else null.</summary>
        public Obstacle TakePadSlid()
        {
            var p = padSlid;
            padSlid = null;
            return p;
        }

        void UpdatePadSlide(float dt)
        {
            padSlideT += dt / PadSlideTime;
            float k = Mathf.Clamp01(padSlideT);
            Surface = Vector3.Lerp(padSlideFrom, padSlideTo, k * k);
            Depth = 0.05f;
            if (k < 1f) return;
            padSlideT = -1f;
            padSlid = OnPad;
            OnPad = null;
            Depth = 0f;   // (in the water from the film, as a cast comes in)
        }

        void CheckPadLeave()
        {
            if (OnPad == null || Obst == null) return;
            if (!Obst.Inside(OnPad, new Vector2(Surface.x, Surface.z)))
            {
                padLeft = OnPad;
                OnPad = null;
            }
        }

        /// <summary>Snagged: the rig is held at the snag point (no drift, no sinking); a float lies pulled down.</summary>
        public void SetSnag(SnagInfo s)
        {
            Snag = s;
            OnPad = null;
            Hanging = false;
            Surface = new Vector3(s.at.x, 0f, s.at.z);
            Depth = Mathf.Max(0f, -s.at.y);
            dipTarget = -3f;
            twitchLeft = 0f;
        }

        public void ClearSnag()
        {
            Snag = null;
            dipTarget = 0f;
        }

        /// <summary>Freed: the rig pops <paramref name="toShore"/> m towards him and <paramref name="up"/> m up (never into a prop).</summary>
        public void PopFree(Vector3 shore, float toShore, float up)
        {
            var flat = new Vector3(shore.x - Surface.x, 0f, shore.z - Surface.z);
            if (flat.magnitude > 0.05f && !stage.L.IsIce) Surface = PushOut(Surface + flat.normalized * Mathf.Min(toShore, flat.magnitude));
            Depth = stage.L.IsIce ? Mathf.Max(0f, Depth - up) : Mathf.Max(UsesFloat ? 0f : 0.05f, Depth - up);
            lastHook = HookPos;
            windT = 99f;
            fallArmed = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            BottomTouch = false;
            if (State == Mode.Flying)
            {
                UpdateFlight(dt);
                return;
            }
            if (State == Mode.Water && Snag == null)
            {
                float bottom = Bottom;
                if (UsesFloat)
                {
                    float target = Mathf.Min(FloatDepth, bottom);
                    if (Depth < target) Depth = Mathf.Min(target, Depth + Bait.sinkSpeed * dt);
                    else if (Depth > target) Depth = Mathf.Max(target, Depth - dt);
                }
                else if (padSlideT >= 0f) UpdatePadSlide(dt);
                else UpdateLure(dt, Floor);   // (through the ice it rests at the drag's depth)
            }
            if (Time.frameCount - currentFrame > 1) FreeDrift = Vector2.zero;   // (the current is not stepped outside waiting / retrieving)
            if (State == Mode.Water || State == Mode.Held)
            {
                var hook = HookPos;
                float sp = (hook - lastHook).magnitude / Mathf.Max(dt, 1e-4f);
                float k = 1f - Mathf.Exp(-dt / 0.25f);
                MoveSpeed = Mathf.Lerp(MoveSpeed, sp, k);
                // through the water: the rig's own motion without the water's drift (a float riding the stream: ~0)
                var flat = new Vector2(Surface.x, Surface.z);
                var vel = (flat - prevFlat) / Mathf.Max(dt, 1e-4f);
                if (State != Mode.Water) vel = Vector2.zero;
                RelVel = Vector2.Lerp(RelVel, vel - FreeDrift, k);
                RelSpeed = RelVel.magnitude;
                prevFlat = flat;
                lastHook = hook;
            }
            Render(dt);
        }

        void UpdateLure(float dt, float bottom)
        {
            var b = Bait;
            windT += dt;
            SinceTouch += dt;
            // (hanging in the current, the line holds it as winding would: it keeps its depth)
            bool wound = windT < WoundGrace || Hanging;
            if (twitchLeft > 0f)
            {
                // the flick in progress: hop, pull and dart
                float k = Mathf.Min(dt, twitchLeft) / TwitchTime;
                twitchLeft -= dt;
                Depth = ClampLure(Depth - twitchHop * k);
                var before = Surface;
                Surface += twitchMove * k;
                if (!stage.L.IsIce) Surface.x = Mathf.Clamp(Surface.x, -stage.L.xLim + 0.3f, stage.L.xLim - 0.3f);
                Surface.y = 0f;
                if (Blocked(Surface)) Surface = before;   // (a dart into a prop stops against it)
                if (twitchHop > 0.3f && b.action == LureAction.Vertical && (fxT -= dt) <= 0f)
                {
                    // a streak of light on the rise
                    fxT = 0.03f;
                    Fx.Sparkle(Snap(P.To2D(P.Apparent(HookPos))), new Color(0.9f, 0.95f, 1f, 0.7f), 0.18f, 1f, OrderUnder);
                }
                Falling = false;
            }
            else
            {
                float rest;
                switch (b.buoyancy)
                {
                    case Buoyancy.Float: rest = 0.05f; break;
                    case Buoyancy.Suspend: rest = Mathf.Max(Depth, Mathf.Min(b.swimDepth, bottom)); break; // hangs where it is once down
                    default: rest = bottom; break;
                }
                if (!wound)
                {
                    if (Depth < rest - 0.001f)
                    {
                        float v = fallArmed && b.fallSpeed > 0f ? b.fallSpeed : b.sinkSpeed;
                        Depth = Mathf.Min(rest, Depth + v * dt);
                        Falling = fallArmed;
                    }
                    else if (Depth > rest + 0.001f && b.buoyancy == Buoyancy.Float)
                    {
                        Depth = Mathf.Max(rest, Depth - b.riseSpeed * dt);
                        Falling = false;
                    }
                    else
                    {
                        Falling = false;
                        fallArmed = false;
                    }
                }
                else Falling = false;
                if (Falling) FallT += dt;
            }
            Depth = Mathf.Min(Depth, Mathf.Max(0.05f, bottom));
            OnBottom = Depth >= bottom - 0.3f;
            if (!touched && Depth >= bottom - 0.02f && b.buoyancy == Buoyancy.Sink)
            {
                touched = true;
                BottomTouch = true;
                SinceTouch = 0f;
                // a puff of silt (none where the ice rig stops in open water) and a soft "톡"
                if (bottom >= Bottom - 0.01f) Fx.Puff(Snap(P.To2D(P.Apparent(HookPos))), stage.UnderwaterTint(Depth, 0.8f), 4, 0.5f, OrderUnder, 0.25f);
                Sfx.Play(Sfx.Nibble, 0.22f, 1.4f);
            }
            else if (Depth < bottom - 0.3f) touched = false;
        }

        void Render(float dt)
        {
            // the period look's tint (moonlit at night) on the float and the bait out of the water
            var tint = stage.ActorTint;
            if (State == Mode.Perched)
            {
                // resting on a prop's top / the bank, over the front layer
                floatSr.enabled = underLine.enabled = haloSr.enabled = chemiSr.enabled = false;
                baitSr.enabled = true;
                baitSr.sortingOrder = 45;
                baitSr.transform.position = Snap(P.To2D(PerchAt, out float perchD) + stage.DeckBob);
                baitSr.transform.localScale = Vector3.one * BaitScale(PerchAt);
                // (tested a little nearer than where it rests: the top it sits on must not hide its lower half)
                FrontOcclusion.SetDepth(baitSr, Mathf.Max(0.05f, perchD - FrontOcclusion.Resting));
                baitSr.transform.rotation = Quaternion.identity;
                baitSr.color = new Color(tint.r, tint.g, tint.b, 1f);
                return;
            }
            if (State != Mode.Water && State != Mode.Held)
            {
                if (State == Mode.Hidden) floatSr.enabled = baitSr.enabled = underLine.enabled = haloSr.enabled = chemiSr.enabled = false;
                else
                {
                    chemiSr.enabled = false;
                    baitSr.color = new Color(tint.r, tint.g, tint.b, 1f);   // (flying)
                }
                return;
            }
            bobT += dt;
            dip = Mathf.MoveTowards(dip, dipTarget, dt * 30f);
            bool showFloat = UsesFloat && State == Mode.Water;
            floatSr.enabled = showFloat;
            if (showFloat)
            {
                FrontOcclusion.SetDepth(floatSr, 0f);   // (on the water: under the front layer, or on a pad over it)
                FrontOcclusion.SetDepth(chemiSr, 0f);
            }
            FloatScale = P.ScaleAt(Surface, floatSr.sprite.rect.height, FloatMinPx);
            float night = stage.Now.Night;
            chemiSr.enabled = showFloat && night > 0.01f;
            if (showFloat)
            {
                // bob, dip and the lift above the waterline are sprite pixels, so they shrink with the float
                float s = FloatScale;
                var fp = P.To2D(Surface);
                float bob = Mathf.Round(Mathf.Sin(bobT * 2.3f) * 0.8f);
                floatSr.transform.position = Snap(fp + new Vector2(0, ((bob + dip) / PixelView.PPU + 0.25f) * s));
                floatSr.transform.localScale = Vector3.one * s;
                floatSr.transform.rotation = Quaternion.identity;   // (upright again after a fight)
                // (caught on a lily pad: over the painted pad, which is in the front layer)
                bool onPad = Snag != null && Snag.kind == "pad";
                floatSr.sortingOrder = onPad ? StageView.OrderLight : Fx.OrderRipple + 1;
                chemiSr.sortingOrder = onPad ? StageView.OrderLight + 1 : Fx.OrderRipple + 2;
                float a = Snag != null ? 0.7f : dip < -4f ? 0.45f : 1f;
                floatSr.color = new Color(tint.r, tint.g, tint.b, a);
                if (chemiSr.enabled)
                {
                    // the 케미 light on the float's top pixel: it dips with the float, so a bite still reads
                    float top = (floatSr.sprite.rect.height * 0.5f - 1f) * s / PixelView.PPU;
                    chemiSr.transform.position = Snap((Vector2)floatSr.transform.position + new Vector2(0f, top));
                    chemiSr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(night) * (dip < -4f ? 0.5f : 1f));
                }
            }
            // bait / lure under water (or the bare hook; on the line-snap's free end while that flies back)
            var hookPos = HookOverride ?? HookPos;
            baitSr.enabled = State == Mode.Water;
            FrontOcclusion.SetDepth(baitSr, 0f);
            bool film = !UsesFloat && Depth <= 0.12f;   // a topwater lure riding the surface
            // (sitting on / caught on a lily pad: over the painted pad, which is in the front layer)
            bool padTop = OnPad != null || (Snag != null && Snag.kind == "pad");
            baitSr.sortingOrder = film ? (padTop ? StageView.OrderLight : Fx.OrderRipple + 1) : 12;
            baitSr.transform.position = Snap(P.To2D(film ? new Vector3(hookPos.x, 0f, hookPos.z) : P.Apparent(hookPos)));
            baitSr.transform.localScale = Vector3.one * BaitScale(hookPos);
            baitSr.color = film ? new Color(tint.r, tint.g, tint.b, 1f) : stage.UnderwaterTint(Mathf.Max(0f, -hookPos.y), UsesFloat ? 0.7f : 0.9f);
            baitSr.transform.rotation = Quaternion.Euler(0, 0, UsesFloat || State != Mode.Water ? 0f : LureLook(dt, hookPos, film));
            // a glowing lure's halo, seen from the surface
            bool halo = Bait.isLure && Bait.glow && State == Mode.Water;
            haloSr.enabled = halo;
            if (halo)
            {
                haloSr.transform.position = baitSr.transform.position;
                float pulse = 1f + 0.06f * Mathf.Sin(bobT * Mathf.PI * 2f * 1.5f);
                haloSr.transform.localScale = Vector3.one * Mathf.Max(1f, baitSr.transform.localScale.x) * pulse;
                haloSr.color = new Color(0.6f, 1f, 0.92f, Mathf.Lerp(0.75f, 0.5f, Mathf.Clamp01(Depth / 8f)));
            }
            // line from the float straight down to the hook (a lure's line is drawn by the angler)
            var a2 = P.To2D(Surface);
            var b2 = P.To2D(P.Apparent(hookPos));
            underLine.enabled = State == Mode.Water && UsesFloat && HookOverride == null && (a2 - b2).sqrMagnitude > 0.01f;
            if (underLine.enabled)
            {
                var mid = (a2 + b2) * 0.5f;
                underLine.transform.position = mid;
                var d = b2 - a2;
                underLine.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                underLine.transform.localScale = new Vector3(d.magnitude * PixelView.PPU, 1, 1);
                underLine.color = stage.UnderwaterLine(Game.I.Line.color);
            }
        }

        // ------------------------------------------------------------------ the float in a fight (float rigs)
        /// <summary>
        /// What a float rig's float does while a fish is on: nothing (a lure, no fight), riding the line out to the hooked
        /// fish (<see cref="FollowFight"/>), or hanging on the line with the landed fish (<see cref="HangFromRod"/>).
        /// </summary>
        public enum FightFloat { Off, Line, Hang }
        public FightFloat FloatFight { get; private set; }
        /// <summary>Set every frame in a fight: how tight the line is (0 slack .. 1 taut); the float goes under from <see cref="UnderFrom"/>.</summary>
        public float FightTaut;
        /// <summary>Set every frame in a fight: a hard pull (a burst, the line near its limit) tugs the float under for a moment.</summary>
        public bool FightHard;
        /// <summary>The float rides the surface on the line in the fight this frame: the side-pressure arrow floats over it.</summary>
        public bool FloatRiding { get; private set; }
        /// <summary>The float is drawn in the air this frame (a jump, the landing).</summary>
        public bool FloatInAir { get; private set; }
        /// <summary>The float's point in game space this frame (fight / landing): on the surface, under it or in the air.</summary>
        public Vector3 FloatAt { get; private set; }
        /// <summary>The top of the riding float's sprite without its bob and dip (pixel scene).</summary>
        public Vector2 FloatTop2D { get; private set; }
        /// <summary>Where the float's sprite is drawn (pixel scene; for the tests).</summary>
        public Vector2 FloatShown2D => floatSr != null ? (Vector2)floatSr.transform.position : Vector2.zero;
        /// <summary>How deep the float is pulled under along the line (m; 0 = riding the surface).</summary>
        public float FloatUnder => under;
        /// <summary>Seconds since the float last popped back up to the surface (for the tests).</summary>
        public float SincePop { get; private set; } = 99f;
        /// <summary>The riding float's speed through the water (m/s): its wake.</summary>
        public float FightRelSpeed => fightRel.magnitude;

        public const float UnderFrom = 0.3f, UnderFull = 0.6f;   // FightTaut from which the float is pulled under / all the way
        const float SinkTau = 0.08f, RiseSpeed = 1.6f;          // yanked under fast (s); back up at this speed (m/s)
        const float Sunk = 0.18f, Up = 0.03f;                   // m: counts as pulled under (a ring) / back up (the pop)
        const float TugTime = 0.3f;                             // s a hard pull holds it under
        Angler fightAngler;
        float under, tugT, tugCool, skipCool;
        bool sunk, rideInit;
        int lastKind = -1;
        Vector2 blend, lastShown, fightRel;
        Vector3 lastRide;
        static readonly Vector3[] linePts = new Vector3[33];
        static readonly float[] lineCum = new float[33];

        /// <summary>A fish is on (called after <see cref="Hold"/>): a float rig's float stays on the line to it.</summary>
        public void FollowFight(Angler a)
        {
            fightAngler = a;
            FloatFight = UsesFloat ? FightFloat.Line : FightFloat.Off;
            under = 0f;
            sunk = rideInit = false;
            tugT = skipCool = 0f;
            tugCool = 0.6f;
            // (from where the waiting float was drawn: it glides onto the line over a few frames)
            lastKind = floatSr.enabled ? 99 : -1;
            lastShown = floatSr.transform.position;
            blend = fightRel = Vector2.zero;
            FightTaut = 0f;
            FightHard = false;
            SincePop = 99f;
        }

        /// <summary>Landed: the float hangs on the line over the fish (or at the rod tip) until <see cref="Hide"/> (the catch card).</summary>
        public void HangFromRod()
        {
            if (FloatFight == FightFloat.Off) return;
            State = Mode.Held;
            FloatFight = FightFloat.Hang;
            FightTaut = 0f;
            FightHard = false;
            tugT = 0f;
        }

        /// <summary>
        /// The fish is off with a float rig (it got away, or the line parted on the hook's side of the float): the float
        /// stays where it was, back up on the water there (with a ring if it was under or in the air) and in the water
        /// again to be wound in, with the bare hook (the bait was eaten at the bite: <see cref="TakeBait"/>). False when
        /// no float rides the line (a lure).
        /// </summary>
        public bool LetGo()
        {
            if (FloatFight != FightFloat.Line || !UsesFloat)
            {
                FloatFight = FightFloat.Off;
                return false;
            }
            var L = stage.L;
            var at = L.IsIce ? new Vector3(L.holeX, 0f, L.holeZ) : new Vector3(FloatAt.x, 0f, FloatAt.z);
            if (!L.IsIce)
            {
                at.x = Mathf.Clamp(at.x, -L.xLim + 0.6f, L.xLim - 0.6f);
                at.z = Mathf.Clamp(at.z, L.zNear + 0.4f, L.zFar - 1f);
            }
            bool pop = under > Up || FloatInAir;
            at = PushOut(at);
            float hook = Mathf.Clamp(under + 0.5f, 0f, Mathf.Min(FloatDepth, Mathf.Max(0f, Bottom)));
            FloatFight = FightFloat.Off;
            FloatRiding = FloatInAir = false;
            under = 0f;
            BackInWater(at, hook);   // (the bare hook sinks back to its depth under the float)
            TakeBait();
            if (pop) PopRing(at, 1f);
            return true;
        }

        /// <summary>
        /// The line parted at a snag (forced or cut with 끊기) with a float rig: it parts on the hook's side, so the float
        /// stays where it lies and is wound in; the bait is gone with the hook's hold, the bare hook springs up off the
        /// snag to <paramref name="hookUp"/> of its depth and sinks back under the float. The caller clears the snag first.
        /// </summary>
        public void LetGoSnag(float hookUp = 0.4f)
        {
            if (!UsesFloat) return;
            var at = new Vector3(Surface.x, 0f, Surface.z);
            float hook = Mathf.Clamp(Depth * hookUp, 0.2f, Mathf.Max(0.2f, Mathf.Min(FloatDepth, Bottom)));
            BackInWater(at, hook);
            TakeBait();
            PopRing(at, 0.8f);
        }

        /// <summary>
        /// A fish shook the lure off (no break): the lure stays on the line where it came out of the fish's mouth
        /// (<paramref name="at"/>, game space; through the ice the hole, at that depth) and is wound in from there. False
        /// for a float rig (its float is let go by <see cref="LetGo"/>).
        /// </summary>
        public bool LetGoLure(Vector3 at)
        {
            FloatFight = FightFloat.Off;
            FloatRiding = FloatInAir = false;
            if (UsesFloat || Bait == null) return false;
            var L = stage.L;
            var s = L.IsIce ? new Vector3(L.holeX, 0f, L.holeZ) : new Vector3(at.x, 0f, at.z);
            if (!L.IsIce)
            {
                s.x = Mathf.Clamp(s.x, -L.xLim + 0.6f, L.xLim - 0.6f);
                s.z = Mathf.Clamp(s.z, L.zNear + 0.4f, L.zFar - 1f);
                s = PushOut(s);
            }
            Surface = s;
            BackInWater(s, ClampLure(Mathf.Max(0.05f, -at.y)));
            OnPad = padLeft = padSlid = null;
            padSlideT = -1f;
            twitchLeft = 0f;
            fallArmed = true;   // (let go, it sinks / rises the way its buoyancy takes it)
            Falling = false;
            FallT = 0f;
            touched = OnBottom = BottomTouch = false;
            SinceTouch = 99f;
            kick = 0f;
            return true;
        }

        /// <summary>In the water again at <paramref name="at"/> (on the surface) with the hook <paramref name="hook"/> m down, at rest.</summary>
        void BackInWater(Vector3 at, float hook)
        {
            Surface = new Vector3(at.x, 0f, at.z);
            Depth = hook;
            State = Mode.Water;
            Snag = null;
            HookOverride = null;
            lastHook = HookPos;
            prevFlat = new Vector2(Surface.x, Surface.z);
            RelVel = FreeDrift = Vector2.zero;
            RelSpeed = 0f;
            dip = dipTarget = 0f;
            windT = 99f;
            Bow = 0f;
            mendT = -1f;
            Hanging = false;
        }

        /// <summary>The float bobbing back up (or skipping on the water): a ring, a little spray and a soft plop.</summary>
        void PopRing(Vector3 at, float power)
        {
            var s = new Vector3(at.x, 0f, at.z);
            var s2 = P.To2D(s);
            float ppm = P.PixelsPerMetre(s);
            Fx.Ripple(s2, Mathf.Clamp(ppm * 0.8f / 64f, 0.08f, 0.45f) * power, P.Foreshorten(s) * 1.6f + 0.15f, new Color(1, 1, 1, 0.75f), 0.7f);
            Fx.Splash(s2, Mathf.Clamp(ppm / 40f, 0.25f, 0.6f) * power, stage.WaterTint, Mathf.Max(2, Mathf.RoundToInt(4 * power)), P.DepthOf(s));
            Sfx.PlayVar(Sfx.Plop, 0.35f * power, 0.15f);
        }

        /// <summary>A ring where the float is yanked under.</summary>
        void SinkRing(Vector3 at)
        {
            var s = new Vector3(at.x, 0f, at.z);
            Fx.Ripple(P.To2D(s), Mathf.Clamp(P.PixelsPerMetre(s) * 0.6f / 64f, 0.06f, 0.35f), P.Foreshorten(s) * 1.6f + 0.15f, new Color(1, 1, 1, 0.6f), 0.55f);
        }

        /// <summary>
        /// The float's wake this frame (WaterFx): where it rides and its velocity through the water (m/s, (x, z)). A float rig
        /// in the water moving through it, or the float riding the line in a fight (dragged across by a run, held against
        /// the stream).
        /// </summary>
        public bool WakeNow(out Vector3 at, out Vector2 rel)
        {
            at = Surface;
            rel = RelVel;
            if (!UsesFloat) return false;
            if (State == Mode.Water) return RelSpeed >= 0.08f && RelVel.sqrMagnitude >= 1e-6f;
            if (FloatFight != FightFloat.Line || !FloatRiding) return false;
            at = new Vector3(FloatAt.x, 0f, FloatAt.z);
            rel = fightRel;
            return fightRel.magnitude >= 0.15f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            FloatRiding = FloatInAir = false;
            SincePop += dt;
            if (FloatFight == FightFloat.Off || State != Mode.Held || fightAngler == null || !UsesFloat) return;
            if (!fightAngler.LineTarget.HasValue || !fightAngler.LineAt(1f, out _, out _))
            {
                floatSr.enabled = chemiSr.enabled = false;
                return;
            }
            RenderFightFloat(dt);
        }

        /// <summary>
        /// The float fixed on the line <see cref="FloatDepth"/> up from the hook. With the line under water it rides the
        /// surface at the line's entry (<see cref="Angler.WaterEntry"/>: bobbing, tilted towards the pull, a wake as it is
        /// dragged across) while the line from there down to the fish's mouth is shorter than that; while it is longer and
        /// the line is tight (<see cref="FightTaut"/>) it is pulled under to that point of the line (refracted onto the drawn
        /// underwater line, dimmed like it, smaller and fainter the deeper it goes), and comes back up with a ring when the
        /// fish comes up or the line goes slack; a hard pull (<see cref="FightHard"/>) tugs it under for a moment even at the
        /// surface. With the line out of the water (a jump, the landing) it sits on the drawn line that far back from its
        /// end (<see cref="Angler.LineAt"/>): on the water where the line lies on it (skipping), else in the air along the
        /// line, never past the rod tip. Scaled with the distance (<see cref="FloatMinPx"/> at least) as when waiting.
        /// </summary>
        void RenderFightFloat(float dt)
        {
            var a = fightAngler;
            var L = stage.L;
            var tint = stage.ActorTint;
            var solid = new Color(tint.r, tint.g, tint.b, 1f);
            float h = floatSr.sprite.rect.height;
            float night = stage.Now.Night;
            // a hard pull tugs it under for a moment (the bite's dip)
            tugCool -= dt;
            tugT -= dt;
            skipCool -= dt;
            if (FloatFight == FightFloat.Line && FightHard && tugCool <= 0f)
            {
                tugT = TugTime;
                tugCool = UnityEngine.Random.Range(1.1f, 2.2f);
            }
            dipTarget = tugT > 0f ? -7f : 0f;
            int kind;              // 0 riding the surface, 1 pulled under, 2 in the air (or lying on the ice)
            Vector3 at, entry = a.WaterEntry;
            Vector2 p2, axis = Vector2.up;
            float s, alpha = 1f;
            var col = solid;
            int order;
            if (FloatFight == FightFloat.Line && a.LineUnderwater)
            {
                var mouth = a.LineTarget.Value;
                float D = Mathf.Max(0.05f, -mouth.y);
                float lu = Vector3.Distance(entry, mouth);
                // the point FloatDepth up the line from the hook is this deep; a slack line lets the float bob up
                float geo = lu > FloatDepth ? D * (1f - FloatDepth / lu) : 0f;
                float want = geo * Mathf.InverseLerp(UnderFrom, UnderFull, FightTaut);
                under = want > under ? Mathf.Lerp(under, want, 1f - Mathf.Exp(-dt / SinkTau)) : Mathf.MoveTowards(under, want, RiseSpeed * dt);
                under = Mathf.Clamp(under, 0f, D * 0.97f);
                var e2 = P.To2D(entry);
                var m2 = a.Fish != null ? a.Fish.DrawnMouth : P.To2D(P.Apparent(mouth));
                var seg = m2 - e2;
                if (under > 0.01f)
                {
                    kind = 1;
                    at = Vector3.Lerp(entry, mouth, under / D);
                    // refracted, then onto the drawn underwater line (entry -> the fish's drawn mouth)
                    var app = P.To2D(P.Apparent(at));
                    float k = seg.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(app - e2, seg) / seg.sqrMagnitude) : 0f;
                    p2 = e2 + seg * k;
                    float deep = Mathf.Clamp01(under / 3f);
                    s = Mathf.Max(P.ScaleAt(at) * Mathf.Lerp(1f, 0.8f, deep), FloatMinPx / Mathf.Max(1f, h));
                    // lying along the line (its top towards the entry), upright while only just under
                    var along = seg.sqrMagnitude > 1e-8f ? -seg.normalized : Vector2.up;
                    if (along.y < 0f) along = -along;
                    axis = Vector2.Lerp(Vector2.up, along, Mathf.Clamp01(under / 0.4f)).normalized;
                    col = Color.Lerp(solid, stage.WaterDeep, 0.4f + 0.35f * deep);
                    alpha = Mathf.Lerp(0.85f, 0.4f, deep) * (L.IsIce ? 0.8f : 1f);
                    order = Angler.OrderUnderLine + 1;   // on the underwater line (20), over the fish shadows, under the waves
                }
                else
                {
                    kind = 0;
                    at = entry;
                    s = P.ScaleAt(entry, h, FloatMinPx);
                    p2 = e2;
                    // tilted towards the pull: its foot towards the fish, its top towards the rod (the more, the tighter)
                    var toFish = seg.sqrMagnitude > 1e-8f ? seg.normalized : Vector2.zero;
                    var toRod = P.To2D(a.RodTip) - e2;
                    toRod = toRod.sqrMagnitude > 1e-8f ? toRod.normalized : Vector2.zero;
                    float deg = Mathf.Clamp((toRod.x - toFish.x) * 0.5f, -1f, 1f) * 28f * (0.35f + 0.65f * Mathf.Clamp01(FightTaut));
                    axis = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));
                    order = Fx.OrderRipple + 1;
                }
            }
            else if (FloatFight == FightFloat.Line && a.LineTarget.Value.y <= 0.05f)
            {
                // the line's end on the water (the frame the hook is set: it still runs to where the float was)
                var e = a.LineTarget.Value;
                kind = 0;
                at = entry = new Vector3(e.x, 0f, e.z);
                s = P.ScaleAt(at, h, FloatMinPx);
                p2 = P.To2D(at);
                order = Fx.OrderRipple + 1;
            }
            else
            {
                // the line out of the water (a jump: the fish in the air; the landing: the fish hanging): on the drawn line
                // FloatDepth back from its end, never past the rod tip
                float t = LineBack(FloatDepth, h, out at, out var bob2);
                under = 0f;
                s = P.ScaleAt(at, h, FloatMinPx);
                if (at.y <= 0.02f)
                {
                    kind = 0;
                    at.y = 0f;
                    p2 = P.To2D(at);
                    order = Fx.OrderRipple + 1;
                }
                else
                {
                    kind = 2;
                    p2 = P.To2D(at) + bob2;
                    // hanging along the line, its top towards the rod tip
                    a.LineAt(Mathf.Max(0f, t - 0.03f), out var q, out var qb);
                    var d = P.To2D(q) + qb - p2;
                    axis = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
                    order = Angler.OrderLine - 1;   // over the fish in the air (42) and the spray (44), under the line (46)
                }
            }
            // drawn: riding, it sits 4 px (at its scale) up out of the water along its tilt, bobbing (faster on a tight line)
            // and dipping with a tug; under the water and in the air it hangs centred on the line
            var draw = p2;
            if (kind == 0)
            {
                float bob = Mathf.Round(Mathf.Sin(bobT * (2.3f + 3f * FightTaut)) * (0.8f + 0.7f * FightTaut));
                draw += axis * (((bob + dip) / PixelView.PPU + 0.25f) * s);
                if (dip < -4f) alpha = 0.45f;
            }
            // a change of place (going under, popping up, lifted out by a jump) glides over a few frames
            if (lastKind >= 0 && kind != lastKind)
            {
                var by = lastShown - draw;
                blend = by.sqrMagnitude < 9f ? by : Vector2.zero;
            }
            blend *= Mathf.Exp(-dt / 0.07f);
            if (blend.sqrMagnitude < 1e-6f) blend = Vector2.zero;
            var shown = draw + blend;
            // yanked under / back up; a jump lifting it off the water or dropping it back on (a skip)
            var ride = kind == 0 ? at : rideInit ? lastRide : new Vector3(entry.x, 0f, entry.z);
            if (kind == 1 && under > Sunk && !sunk)
            {
                sunk = true;
                SinkRing(entry);
            }
            else if (sunk && under < Up)
            {
                sunk = false;
                SincePop = 0f;
                PopRing(ride, 1f);
            }
            else if (lastKind >= 0 && kind != lastKind && (kind == 2 || lastKind == 2) && skipCool <= 0f && L.IsIce == false)
            {
                skipCool = 0.15f;
                PopRing(ride, 0.55f);
            }
            // the wake: its speed through the water while riding (the water's own drift taken out)
            if (kind == 0)
            {
                var flat = new Vector2(at.x, at.z);
                if (rideInit)
                {
                    var v = (flat - new Vector2(lastRide.x, lastRide.z)) / Mathf.Max(dt, 1e-4f);
                    var cf = stage.Current;
                    if (cf != null && !L.IsIce) v -= cf.Water(at.x, at.z);
                    fightRel = Vector2.Lerp(fightRel, v, 1f - Mathf.Exp(-dt / 0.2f));
                }
                rideInit = true;
                lastRide = at;
            }
            else
            {
                rideInit = false;
                fightRel = Vector2.zero;
            }
            lastKind = kind;
            lastShown = shown;
            FloatAt = at;
            FloatRiding = kind == 0 && FloatFight == FightFloat.Line;
            FloatInAir = kind == 2;
            FloatTop2D = p2 + Vector2.up * ((0.25f + h * 0.5f / PixelView.PPU) * s);
            floatSr.enabled = true;
            floatSr.sortingOrder = order;
            floatSr.transform.position = Snap(shown);
            // in the air (over the front layer): hidden where the pier / rocks / boat are nearer, like the line it hangs on
            float airD = kind == 2 ? P.DepthOf(at) : 0f;
            FrontOcclusion.SetDepth(floatSr, airD);
            FrontOcclusion.SetDepth(chemiSr, airD);
            floatSr.transform.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, axis));
            floatSr.transform.localScale = Vector3.one * s;
            floatSr.color = new Color(col.r, col.g, col.b, alpha);
            // the 케미 light on its top pixel
            chemiSr.enabled = night > 0.01f;
            if (chemiSr.enabled)
            {
                float top = (h * 0.5f - 1f) * s / PixelView.PPU;
                chemiSr.transform.position = Snap(shown + axis * top);
                chemiSr.sortingOrder = order + 1;
                chemiSr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(night) * alpha * (kind == 1 ? 0.7f : 1f));
            }
        }

        /// <summary>
        /// The point of the drawn line (<see cref="Angler.LineAt"/>) <paramref name="d"/> m back along it from its end, but
        /// no nearer the rod tip than half a float <paramref name="spritePx"/> tall (it hangs just under the tip ring then);
        /// returns its t (0 = the rod tip).
        /// </summary>
        float LineBack(float d, float spritePx, out Vector3 p, out Vector2 bob)
        {
            var a = fightAngler;
            const int n = 32;
            for (int i = 0; i <= n; i++)
            {
                a.LineAt(1f - i / (float)n, out linePts[i], out _);
                lineCum[i] = i == 0 ? 0f : lineCum[i - 1] + Vector3.Distance(linePts[i - 1], linePts[i]);
            }
            float total = lineCum[n];
            var tip = linePts[n];
            float half = spritePx * 0.5f * P.ScaleAt(tip, spritePx, FloatMinPx) / Mathf.Max(1f, P.PixelsPerMetre(tip)) + 0.03f;
            float want = Mathf.Clamp(d, 0f, Mathf.Max(0f, total - half));
            int j = 1;
            while (j < n && lineCum[j] < want) j++;
            float seg = lineCum[j] - lineCum[j - 1];
            float f = seg > 1e-6f ? Mathf.Clamp01((want - lineCum[j - 1]) / seg) : 0f;
            float t = 1f - (j - 1 + f) / n;
            a.LineAt(t, out p, out bob);
            return t;
        }

        /// <summary>
        /// How each lure moves and glints in the water (1.2 "Look"); returns the sprite's tilt (degrees, + = nose up).
        /// </summary>
        float LureLook(float dt, Vector3 hookPos, bool film)
        {
            var b = Bait;
            bool wound = windT < WoundGrace;
            lookT += dt;
            fxT -= dt;
            kick = Mathf.MoveTowards(kick, 0f, dt * 5f);
            Vector2 at = Snap(P.To2D(film ? new Vector3(hookPos.x, 0f, hookPos.z) : P.Apparent(hookPos)));
            var glint = new Color(1f, 0.97f, 0.8f, 0.85f);
            float tilt = kick * 25f;
            switch (b.id)
            {
                case "bait_spinner":
                    if (wound && fxT <= 0f)
                    {
                        fxT = 0.2f; // the blade's glint
                        Fx.Sparkle(at + new Vector2(0.2f * BaitScale(hookPos), 0.1f), glint, 0.12f, 1f, OrderUnder);
                    }
                    break;
                case "bait_spoon":
                    if (wound)
                    {
                        tilt += 12f * Mathf.Sin(lookT * Mathf.PI * 2f * 5f);
                        if (fxT <= 0f) { fxT = 0.3f; Fx.Sparkle(at, glint, 0.15f, 2f, OrderUnder); }
                    }
                    else if (Falling)
                    {
                        tilt += -15f + 25f * Mathf.Sin(lookT * Mathf.PI * 2f * 3f); // flutters down
                        if (fxT <= 0f) { fxT = 0.4f; Fx.Sparkle(at, glint, 0.12f, 1f, OrderUnder); }
                    }
                    break;
                case "bait_crank":
                    if (wound) tilt += 8f * Mathf.Sin(lookT * Mathf.PI * 2f * 11f); // tight, fast wobble
                    break;
                case "bait_kona":
                    if (wound)
                    {
                        tilt += 4f * Mathf.Sin(lookT * Mathf.PI * 2f * 6f);
                        if (fxT <= 0f)
                        {
                            fxT = 0.1f; // bubble trail off the skirt
                            Fx.Puff(at, new Color(1f, 1f, 1f, 0.55f), 1, 0.2f, OrderUnder, 0.35f);
                        }
                    }
                    break;
                case "bait_popper":
                    tilt += 5f * Mathf.Sin(lookT * Mathf.PI * 2f * 1.5f);
                    break;
                case "bait_frog":
                    if (wound)
                    {
                        tilt += 8f * Mathf.Sin(lookT * Mathf.PI * 2f * 4f); // kicking legs
                        if (fxT <= 0f)
                        {
                            fxT = 0.22f; // V-wake
                            var s = new Vector3(hookPos.x, 0f, hookPos.z + 0.25f);
                            Fx.Ripple(P.To2D(s), Mathf.Clamp(P.PixelsPerMetre(s) * 0.6f / 64f, 0.05f, 0.35f), P.Foreshorten(s) * 1.6f + 0.15f,
                                new Color(1, 1, 1, 0.55f), 0.6f);
                        }
                    }
                    break;
                case "bait_softworm":
                    if (wound || Falling || Hopping) tilt += 6f * Mathf.Sin(lookT * Mathf.PI * 2f * 3f); // curly tail
                    break;
                case "bait_jig":
                    if (Falling)
                    {
                        tilt += -25f + 15f * Mathf.Sin(lookT * Mathf.PI * 2f * 4f);
                        if (fxT <= 0f) { fxT = 0.3f; Fx.Sparkle(at, glint, 0.15f, 1f, OrderUnder); }
                    }
                    else if (Hopping) tilt += 30f;
                    break;
                case "bait_egi":
                    if (Falling) tilt += -20f + 5f * Mathf.Sin(lookT * Mathf.PI * 2f * 2f); // a slow nose-down glide
                    else if (Hopping) tilt += 25f;
                    break;
                case "bait_minnow":
                    tilt += 3f * Mathf.Sin(lookT * Mathf.PI * 2f * 1.2f);
                    break;
            }
            return tilt;
        }

        float BaitScale(Vector3 p) => P.ScaleAt(p, baitSr.sprite != null ? baitSr.sprite.rect.height : 11f, BaitMinPx);

        static Vector3 Snap(Vector2 p) => new Vector3(Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU, 0);

        static Sprite halo;

        /// <summary>A 16 px pixel halo (hard, dithered rings) for glowing lures, tinted in code.</summary>
        static Sprite Halo
        {
            get
            {
                if (halo != null) return halo;
                const int d = 16;
                var t = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dx = x + 0.5f - d / 2f, dy = y + 0.5f - d / 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool checker = ((x + y) & 1) == 0;
                    float a = r < 3f ? 0.55f : r < 5f ? 0.35f : r < 6.5f ? (checker ? 0.3f : 0f) : r < 8f ? (checker && (x & 1) == 0 ? 0.25f : 0f) : 0f;
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                t.Apply();
                return halo = UnityEngine.Sprite.Create(t, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f), 16);
            }
        }
    }
}
