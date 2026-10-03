using System.Globalization;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The obstacles in play (Docs/obstacles_spec.md): the cast's contacts and landings (bank shots, overhangs, perches, lily
    /// pads), snags (밑걸림: the S.Snagged state, freeing by the rod sweep or a 톡, forcing it breaks the line, 끊기), the
    /// cover-seeking fights (the run for cover, the hold, the line rubbing and wearing, side pressure pulling it out) and the
    /// bites near structure. Everything reads <see cref="StageView.Obstacles"/>; an empty set changes nothing.
    /// </summary>
    public partial class FishingController
    {
        static readonly CultureInfo CIo = CultureInfo.InvariantCulture;
        // line wear per second on structure before roughness / tension / speed / toughness (0.22 cut PE 3호 on tetrapods in 2-10 s)
        const float RubRate = 0.11f;
        /// <summary>
        /// A rub counts (wear, warning, rasp) only on a line this taut: tension >= this x the lesser of the line's limit and
        /// the fish's pull. A slack line (a resting fish, line given, its water entry dropping by the bow) does not press on
        /// structure. (The fish's pull too, not the line alone: PE 3호 holding a 감성돔 dug into the tetrapods sits at ~0.15 of
        /// its limit yet is loaded.)
        /// </summary>
        public const float RubTaut = 0.3f;

        /// <summary>The line is taut enough to rub (see <see cref="RubTaut"/>).</summary>
        public static bool RubTautNow(float tension, float lineLimit, float power) =>
            tension >= RubTaut * Mathf.Max(0.01f, Mathf.Min(lineLimit, power));

        /// <summary>Test hook (-fkauto obstacles): the next run of the hooked fish heads for this cover (its id), no roll.</summary>
        internal static string DebugCover;
        /// <summary>Test hook (-fkauto breaks): the line wears this many times as fast on structure (1: normal).</summary>
        internal static float DebugRubMult = 1f;

        Obstacles Obst => Stage.Obstacles;
        ObstacleOverlay overlay;
        public ObstacleOverlay Overlay => overlay;
        PadWobble padWobble;
        /// <summary>The lily pads' wobble (for the tests).</summary>
        public PadWobble PadWobbleFx => padWobble;
        float padWobbleT = -99f;

        // ---- counters for the test autopilot
        public int ObstContacts { get; private set; }
        public int BankShots { get; private set; }
        public int Perches { get; private set; }
        public int PadSits { get; private set; }
        public int PadDrops { get; private set; }
        public int PadStrikes { get; private set; }
        public int SnagCount { get; private set; }
        public int SnagFrees { get; private set; }
        public int SnagBreaks { get; private set; }
        public int PadTears { get; private set; }
        /// <summary>Snag checks made on a moving rig (SnagRolls past its guards; for the tests).</summary>
        public int SnagChecks { get; private set; }
        /// <summary>The snag checks have a last hook point to measure the next step from (for the tests: false after a fight).</summary>
        internal bool SnagRefSet => snagPrevOk;
        public int CoverRuns { get; private set; }
        public int CoverHolds { get; private set; }
        public int PullOuts { get; private set; }
        public int CrankBumps { get; private set; }
        public string LastFreeWay { get; private set; } = "";
        public Obstacle LastContact { get; private set; }
        public Landing LastLanding { get; private set; }
        public Obstacle LastCover { get; private set; }
        public FightModel.Cause LastSnapCause { get; private set; }
        public string LastBreakName { get; private set; } = "";
        public float LastAbrasion { get; private set; }
        /// <summary>The time the pad-drop strike window (x1.5 for followers) is open until.</summary>
        public float PadDropUntil => padDropUntil;

        // ---- the natural entry (8.3): a bank shot, a drop under an overhang, a slide down a face, a perch knocked off
        Vector3 natAt;
        float natT = -99f;
        // ---- lure windows
        float padDropUntil = -99f, crankUntil = -99f, crankBumpT = -99f, padStrikeT;
        // ---- a perched rig: wound so far
        float perchWound;
        // ---- snags
        Vector3 snagPrevHook;
        bool snagPrevOk, snagArrow;
        float snagCoolUntil = -99f, hookSpeed, lastSideMove;
        float snagRightT, snagWrongT, snagSoftT, snagBreakT, snagLastWindT, snagGiveT = -99f, snagCurT, snagPadWound, snagRingT;
        // ---- the cover fight
        Obstacle coverTarget;
        float coverYaw, coverCoolUntil = -99f, horseT, rimTarget, sparkT, rubLogT;
        bool firstRunDone, rubTold, abrTold;
        Obstacle rubO;
        Vector3 rubPrevFish;

        /// <summary>The line rubs on structure this fight frame.</summary>
        public bool Rubbing { get; private set; }
        /// <summary>The line has rubbed in this fight (the abrasion meter shows from then on).</summary>
        public bool RubbedThisFight { get; private set; }
        /// <summary>What the line rubs on (its name in the texts).</summary>
        public string RubName => rubO != null ? rubO.Name : "";
        /// <summary>Snagged: the arrow shows a free way (after a wrong sweep or a failed 톡; a prop catch settled on slack: its next move).</summary>
        public bool SnagArrowOn => SnagArrowDir != Vector2Int.zero;
        public int SnagFreeSide => Tackle.Snag != null ? Tackle.Snag.freeSide : 0;
        /// <summary>The rig's plan distance from him (the snag strip's distance slot).</summary>
        public float RigDistance => new Vector2(Tackle.Surface.x - Anchor.x, Tackle.Surface.z - Anchor.z).magnitude;

        // ================================================================== the line out (Docs/obstacles_spec.md 4.9)
        /// <summary>
        /// The line out (m), measured as <see cref="LineChord"/>: the cast pays out to where the rig lands, the current's
        /// drift takes more off the reel, winding takes it back. Where the rig cannot follow the line (held against a prop,
        /// snagged) the line out falls short of the chord and the stretch is the tension: <see cref="StretchPerR"/> m of it
        /// is a snag tension of 1.
        /// </summary>
        public float LineOut { get; private set; }
        /// <summary>From the angler to the rig over the water (through the ice: the hook's depth under the hole).</summary>
        public float LineChord => L.IsIce ? Tackle.Depth : RigDistance;
        /// <summary>Metres of stretch to a snag tension of 1 (0.9 per rev of winding, as before the line out).</summary>
        float StretchPerR => Mathf.Max(0.05f, Game.I.Reel.retrieve) / 0.9f;
        /// <summary>A rig held against a prop is caught on it once the line it could not follow comes to this (m).</summary>
        const float CatchStretch = 0.05f;
        /// <summary>Rigs caught on a prop while wound in (not counted in <see cref="SnagCount"/>; for the tests).</summary>
        public int PropCatches { get; private set; }

        /// <summary>The line taut to the rig where it lies (after a cast, a let-go, a freed snag).</summary>
        void TautLine() => LineOut = LineChord;

        /// <summary>
        /// After a wind: a rig that followed the line keeps it taut; one a prop held (Tackle.HeldBy) leaves the wound line
        /// as stretch, and past <see cref="CatchStretch"/> it is caught on that prop (a snag of kind "prop").
        /// </summary>
        void AfterWind()
        {
            var by = Tackle.HeldBy;
            if (by == null)
            {
                TautLine();
                return;
            }
            LineOut = Mathf.Min(LineOut, LineChord) - Tackle.HeldM;
            if (LineChord - LineOut >= CatchStretch) CatchOnProp(by);
        }

        /// <summary>
        /// Caught on a prop the line pulls the rig into (spec 4.9): the snag strip as for 밑걸림, its tension the stretch;
        /// the free side the way the swept rod would slide it off (none frees a rig wedged in a notch: a 톡, 끊기 or the
        /// break); a break or a cut parts the line at the prop, above the float, so the whole rig goes.
        /// </summary>
        void CatchOnProp(Obstacle o)
        {
            var tk = Tackle;
            float stretch = LineChord - LineOut;
            SnagAt(o, new Vector3(tk.Surface.x, -tk.Depth, tk.Surface.z), false, "prop");
            var sn = tk.Snag;
            if (sn == null) return;
            sn.r = stretch / StretchPerR;
            LineOut = LineChord - stretch;
            // (the free way from the physics: the rod move that would slide it out; none: wedged)
            tk.PropFaces(propFaces);
            SnagPhysics.Exits(propFaces, out bool up, out bool down, out _);
            PropGuideNow(sn, PropGrabNow(), up, down);
            var sb = new System.Text.StringBuilder();
            foreach (var f in propFaces) sb.Append(string.Format(CIo, " {0} n ({1:0.00}, {2:0.00}) lean {3:+0.00;-0.00} mu {4:0.00}", f.id, f.n.x, f.n.y, f.slope, f.mu));
            Obstacles.Say(string.Format(CIo, "prop catch {0}: guide side {1:+0;-0;0} pitch {2:+0;-0;0}{3}, exits up {4} down {5};{6}", o.id, propGuide.x, propGuide.y,
                propWedged ? " (wedged)" : "", up, down, sb));
            PropCatches++;
        }
        /// <summary>A frog sitting on a pad is not struck the usual way (only through the pad).</summary>
        public bool PadBlocksStrike => Tackle.OnPad != null;

        void InitObstacles()
        {
            overlay = ObstacleOverlay.Create(this);
            padWobble = PadWobble.Create(Stage);
            Tackle.Contact += OnContact;
        }

        /// <summary>The pad wobbles (spec 5.1): a lure landing on it, crossing it, dropping off it, a strike through it.</summary>
        void WobblePad(Obstacle pad)
        {
            padWobbleT = Time.time;
            padWobble.Wobble(pad);
        }

        static float Plan(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        static float Plan(Vector3 a, Vector2 b) => new Vector2(a.x - b.x, a.z - b.y).magnitude;

        // ================================================================== casts (spec 4, 5)
        /// <summary>A contact in flight: the material's sound and word, a hit flash and chips (a crystal sparkles, leaves fall).</summary>
        void OnContact(ObstacleHit hit, float vn)
        {
            ObstContacts++;
            LastContact = hit.o;
            var m = hit.o.Mat;
            var clip = m.Sound;
            if (clip != null) Sfx.PlayVar(clip, Mathf.Clamp(vn / 12f, 0.3f, 1f), 0.06f);
            if (!string.IsNullOrEmpty(m.word)) hud.LureFeedback(m.word, UIKit.Cream, 0.6f);
            var p2 = (Vector2)Snap(P.To2D(hit.at, out float hitD) + Stage.DeckBob);
            // (hidden behind a nearer front-layer prop; tested a little nearer than the face it hits, which must not hide it)
            hitD = Mathf.Max(0.05f, hitD - FrontOcclusion.Resting);
            bool small = P.ScaleAt(hit.at) < 0.35f;
            if (m.id == "leaf")
            {
                Fx.Puff(p2, m.fx, 3, 0.35f, Fx.OrderSplash, -0.7f, hitD);
                return;
            }
            Fx.Frames(Fx.Load(small ? "fx_hit_s_f" : "fx_hit_f", small ? 3 : 4), p2, 0.05f, Fx.OrderSplash + 1, m.id == "crystal" ? m.fx : Color.white, depth: hitD);
            if (m.id == "crystal")
            {
                for (int i = 0; i < 3; i++) Fx.Sparkle(p2 + Random.insideUnitCircle * 0.25f, m.fx, 0.4f, 1f, Fx.OrderSplash + 1, hitD);
                return;
            }
            Fx.Frames(Fx.Load(small ? "fx_chip_s_f" : "fx_chip_f", small ? 3 : 4), p2, 0.06f, Fx.OrderSplash, m.fx, Random.value < 0.5f, depth: hitD);
            Fx.Puff(p2, m.fx, Random.Range(3, 6), 0.5f, Fx.OrderSplash, 0.15f, hitD);
        }

        /// <summary>
        /// The rig came to rest out of the water (spec 5.4): on a prop's top or on the bank. The fish ignore it until a 톡 or
        /// the reel drops it in.
        /// </summary>
        void LandPerched(Landing l)
        {
            Tackle.Perch(l.at, l.perch);
            Perches++;
            perchWound = 0f;
            if (l.perch == null) Sfx.PlayVar(Sfx.Knock, 0.3f);
            string name = l.perch == null ? null : l.perch.Mat.id == "hull" ? "보트" : l.perch.Name;
            hud.Flash(l.perch != null ? $"{name} 위에 걸쳐졌어요 — 톡 당겨 떨어뜨려요" : "물가에 떨어졌어요 — 톡 당기거나 감아요", UIKit.Sky, 1.8f);
            Obstacles.Say(string.Format(CIo, "perch {0} at ({1:0.00}, {2:0.00}, {3:0.00})", l.perch != null ? l.perch.id : "bank", l.at.x, l.at.y, l.at.z));
        }

        /// <summary>
        /// After the rig entered the water (spec 4.7, 4.8, 5.1-5.3): a bank shot / a drop under the branches / a slide down a
        /// face is a natural entry; on a lily pad the frog and the popper sit, other lures slide off the edge (20 % catch the
        /// pad), a float rig catches it.
        /// </summary>
        void LandedObstacles(Landing l)
        {
            LastLanding = l;
            if (Obst == null || Obst.Empty) return;
            var at = l.at;
            var xz = new Vector2(at.x, at.z);
            bool natural = false;
            if (l.contacts > 0 && l.struck != null && !l.struck.Overhang && Obstacles.Dist(l.struck, xz) <= 2f)
            {
                BankShots++;
                natural = true;
                hud.Flash("뱅크샷!", UIKit.Gold, 1f);
                Sfx.Play(Sfx.Success, 0.5f);
                Obstacles.Say(string.Format(CIo, "bank shot off {0}: in at ({1:0.00}, {2:0.00}), {3:0.00} m from it", l.struck.id, at.x, at.z, Obstacles.Dist(l.struck, xz)));
            }
            else if (l.overhang != null)
            {
                natural = true;
                hud.Flash("가지 아래로 쏙!", UIKit.Gold, 1f);
                Sfx.Play(Sfx.Success, 0.5f);
                Obstacles.Say($"under the overhang {l.overhang.id}");
            }
            else if (l.slid) natural = true;
            if (natural) NaturalEntry(at);
            if (l.pad != null) LandOnPad(l);
        }

        /// <summary>A natural entry: for 8 s fish within 5 m of it bite x1.3, a lure starts +0.15 Q.</summary>
        void NaturalEntry(Vector3 at)
        {
            natAt = at;
            natT = Time.time;
            if (Tackle.Bait != null && Tackle.Bait.isLure) Rhythm.Boost(0.15f);
        }

        void LandOnPad(Landing l)
        {
            var pad = l.pad;
            var b = Tackle.Bait;
            var p2 = (Vector2)Snap(P.To2D(l.at));
            WobblePad(pad);
            if (!b.isLure)
            {
                Tackle.Depth = 0f;
                SnagPad(pad);
                return;
            }
            if (b.id == "bait_frog" || b.id == "bait_popper")
            {
                Tackle.SitOnPad(pad);
                PadSits++;
                padStrikeT = 0.5f;
                Fx.Frames(Fx.Load("fx_padland_f", 4), p2, 0.06f, Fx.OrderRipple + 2, Stage.ActorTint);
                hud.Flash(b.id == "bait_frog" ? "연잎 위에 앉았다!" : "연잎 위에 올라갔다", b.id == "bait_frog" ? UIKit.Gold : UIKit.Sky, 1f);
                Obstacles.Say($"pad {b.id} sits on {pad.id}");
                return;
            }
            // the others slide off the edge nearest him over 0.25 s (PadSlid: "툭" as it drops in)
            Tackle.SlideOffPad(pad, PadEdge(pad, l.at));
            Obstacles.Say($"pad {b.id} slides on {pad.id}");
        }

        /// <summary>A lure slid off a pad's edge into the water ("툭", a 2 px ripple): 20 % catch the pad (the soft worm never).</summary>
        void PadSlid(Obstacle pad)
        {
            var b = Tackle.Bait;
            var edge = Tackle.Surface;
            hud.LureFeedback("툭", UIKit.Cream, 0.6f);
            var e2 = P.To2D(edge);
            Fx.Ripple(e2, Mathf.Clamp(P.PixelsPerMetre(edge) * 0.4f / 64f, 0.05f, 0.25f), P.Foreshorten(edge) * 1.6f + 0.15f, new Color(1, 1, 1, 0.6f), 0.5f);
            float p = b.id == "bait_softworm" ? 0f : 0.2f;
            bool caught = Obstacles.Roll() < p;
            Obstacles.Say(string.Format(CIo, "pad {0} slides off {1} (catch {2:0.00}: {3})", b.id, pad.id, p, caught ? "yes" : "no"));
            if (caught) SnagPad(pad);
        }

        /// <summary>The point of the pad's edge towards him (5 cm outside it) from <paramref name="from"/>.</summary>
        Vector3 PadEdge(Obstacle pad, Vector3 from)
        {
            var dir = new Vector2(ShorePoint.x - from.x, ShorePoint.z - from.z);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
            dir.Normalize();
            var q = new Vector2(from.x, from.z);
            for (int i = 0; i < 80 && Obst.Inside(pad, q, 0.05f); i++) q += dir * 0.05f;
            return new Vector3(q.x, 0f, q.y);
        }

        /// <summary>A pad snag (spec 5.3): the rig caught on the pad (soft; the line never breaks on it).</summary>
        void SnagPad(Obstacle pad)
        {
            var s = Tackle.Surface;
            SnagAt(pad, new Vector3(s.x, -Mathf.Max(0f, Tackle.Depth), s.z), true, "pad");
        }

        /// <summary>
        /// The frog on a pad: dropped off the edge (퐁, the strike window), the pad strike after 1.5 s still; a lure that slid
        /// off a pad; the pad's wobble under a 톡's hop and the crawl.
        /// </summary>
        void PadTick(float dt)
        {
            var tk = Tackle;
            var slid = tk.TakePadSlid();
            if (slid != null)
            {
                PadSlid(slid);
                return;
            }
            var left = tk.TakePadLeft();
            if (left != null)
            {
                WobblePad(left);
                PadDrops++;
                var s = tk.Surface;
                var s2 = P.To2D(s);
                Fx.Splash(s2, Mathf.Clamp(P.PixelsPerMetre(s) / 40f, 0.25f, 0.6f), Stage.WaterTint, 4, P.DepthOf(s));
                Fx.Ripple(s2, Mathf.Clamp(P.PixelsPerMetre(s) * 0.8f / 64f, 0.06f, 0.4f), P.Foreshorten(s) * 1.6f + 0.15f, new Color(1, 1, 1, 0.7f), 0.6f);
                Sfx.PlayVar(Sfx.Plop, 0.5f);
                hud.LureFeedback("퐁", UIKit.Cream, 0.6f);
                if (tk.Bait.id == "bait_frog") padDropUntil = Time.time + 1.2f;
                Obstacles.Say($"pad drop {tk.Bait.id} off {left.id}");
                if (tk.Bait.id == "bait_popper" && Obstacles.Roll() < 0.3f) SnagPad(left);
                return;
            }
            if (tk.OnPad != null && !tk.PadSliding && (LureIn.FlickNow || (LureIn.Winding && Time.time - padWobbleT >= PadWobble.CrawlGap)))
                WobblePad(tk.OnPad);
            if (tk.OnPad == null || tk.Bait.id != "bait_frog" || LureIn.PauseT < 1.5f) return;
            padStrikeT -= dt;
            if (padStrikeT > 0f) return;
            padStrikeT = 0.5f;
            // a topwater-liking fish close by may strike through the pad (0.35 x its strike chance)
            var hook = tk.HookPos;
            foreach (var f in Spawner.Fish)
            {
                if (f.State != FishAgent.St.Wander && f.State != FishAgent.St.Approach) continue;
                if (!f.Sp.actionPrefs.TryGetValue(LureAction.Topwater, out float tp) || tp < 0.5f) continue;
                if (Plan(f.Pos, hook) > 4f || !CanFishEngage(f) || BiteMult(f) <= 0f) continue;
                float chance = (0.35f + 0.5f * f.Sp.aggression) * (0.5f + 0.7f * Rhythm.Q) * StrikeMult(f) * 0.35f;
                OnStrikeRoll(f, chance);
                if (Random.value >= chance) continue;
                PadStrikes++;
                var s2 = P.To2D(tk.Surface);
                WobblePad(tk.OnPad);
                f.ForceBite();
                Fx.Splash(s2, Mathf.Clamp(P.PixelsPerMetre(tk.Surface) / 25f, 0.4f, 1.1f), Stage.WaterTint, 12, P.DepthOf(tk.Surface));
                Fx.Ripple(s2, Mathf.Clamp(P.PixelsPerMetre(tk.Surface) * 1.6f / 64f, 0.12f, 0.8f), P.Foreshorten(tk.Surface) * 1.6f + 0.15f, Color.white, 0.8f);
                hud.Flash("연잎을 뚫고 덮쳤다! 쑥! 지금 탭해서 챔질!", UIKit.Gold, 1f);
                Obstacles.Say($"pad strike {f.Sp.id}");
                return;
            }
        }

        /// <summary>Strike rolls of following fish: x1.5 in the pad-drop window, x1.3 after a crank's bump.</summary>
        float StrikeBonus() => (Time.time < padDropUntil ? 1.5f : 1f) * (Time.time < crankUntil ? 1.3f : 1f);

        // ------------------------------------------------------------------ perched
        /// <summary>A perched rig: a 톡 knocks it off into the water (a natural entry); 0.4 m of winding slides it off.</summary>
        void UpdatePerched(float dt, bool auto)
        {
            Angler.LineTarget = Tackle.LineEnd;   // (its end lies on the prop's top: Angler.Rig, not hidden by it)
            Angler.Tension01 = 0f;
            Angler.Slack01 = 0.3f;
            Angler.LineBow = Vector3.zero;
            if (!auto && LureIn.FlickNow)
            {
                StartRodJerk(LureIn.FlickStrength);
                KnockOff(true);
                return;
            }
            float m = auto ? Game.I.Reel.retrieve * 3.2f * dt * 2f : Mathf.Max(0f, Gesture.FrameRevs) * Game.I.Reel.retrieve;
            if (m > 0f)
            {
                perchWound += m;
                ReelTicks(m / Mathf.Max(0.05f, Game.I.Reel.retrieve));
                if (auto) autoRevs += m / Mathf.Max(0.05f, Game.I.Reel.retrieve);
                Angler.SetPose(ReelPose(auto ? Time.time * 8 : Gesture.TotalRevs * 4));
                if (perchWound >= 0.4f) KnockOff(false);
            }
            else if (!auto && Gesture.Speed < 0.1f) Angler.SetPose("idle");
        }

        /// <summary>
        /// Off the perch into the water 0.3 m outside the prop (the nearest valid water point on the line to him). A 톡 is a
        /// natural entry; wound off a prop, a treble / hook lure catches its lip (25 %; natural baits 10 %; frog, soft worm 0).
        /// </summary>
        void KnockOff(bool tok)
        {
            var o = Tackle.PerchO;
            var from = Tackle.PerchAt;
            var drop = DropPoint(from, o);
            var b = Tackle.Bait;
            float lipP = o == null || tok ? 0f : !b.isLure ? 0.10f : b.id == "bait_frog" || b.id == "bait_softworm" ? 0f : 0.25f;
            bool lip = lipP > 0f && Obstacles.Roll() < lipP;
            Tackle.EnterWater(drop);
            var s2 = P.To2D(drop);
            Fx.Splash(s2, Mathf.Clamp(P.PixelsPerMetre(drop) / 40f, 0.25f, 0.6f), Stage.WaterTint, 4, P.DepthOf(drop));
            Fx.Ripple(s2, Mathf.Clamp(P.PixelsPerMetre(drop) * 0.8f / 64f, 0.06f, 0.4f), P.Foreshorten(drop) * 1.6f + 0.15f, new Color(1, 1, 1, 0.7f), 0.6f);
            Sfx.PlayVar(Sfx.Plop, 0.45f);
            perchWound = 0f;
            snagPrevOk = false;
            if (tok) NaturalEntry(drop);
            Watch?.OnRigLanded(drop);   // (a perched cast knocked in: it came down here)
            Obstacles.Say(string.Format(CIo, "perch off {0} ({1}) into ({2:0.00}, {3:0.00}){4}", o != null ? o.id : "bank", tok ? "tok" : "wind", drop.x, drop.z,
                lipP > 0f ? $" lip roll {lipP:0.00}: {(lip ? "snag" : "no")}" : ""));
            if (lip)
            {
                var zone = Obst.Get(o.id + ".skirt") ?? o;
                SnagAt(zone, new Vector3(drop.x, -0.2f, drop.z), false, "hard");
            }
        }

        Vector3 DropPoint(Vector3 from, Obstacle o)
        {
            var f = new Vector3(from.x, 0f, from.z);
            var dir = new Vector3(ShorePoint.x - from.x, 0f, ShorePoint.z - from.z);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.back;
            dir.Normalize();
            bool Ok(Vector3 q) => Tackle.ValidWater(q) && !(o != null && Obst.Inside(o, new Vector2(q.x, q.z), 0.3f));
            for (float d = 0f; d <= 30f; d += 0.1f)
                if (Ok(f + dir * d)) return f + dir * d;
            for (float d = 0.1f; d <= 30f; d += 0.1f)
                if (Ok(f - dir * d)) return f - dir * d;
            return new Vector3(Mathf.Clamp(from.x, -L.xLim + 0.6f, L.xLim - 0.6f), 0f, Mathf.Clamp(from.z, L.zNear + 1.2f, L.zFar - 1f));
        }

        // ================================================================== snags (spec 6)
        /// <summary>The snag rates per metre in a hard / weed zone and per bottom touch in a hard zone (table 6.2).</summary>
        static void SnagBase(BaitDef b, out float hard, out float weed, out float touch)
        {
            hard = 0.08f;
            weed = 0.12f;
            touch = 0f;
            if (b == null || !b.isLure) return;
            switch (b.id)
            {
                case "bait_spinner": hard = 0.20f; weed = 0.25f; touch = 0.10f; break;
                case "bait_spoon": hard = 0.22f; weed = 0.25f; touch = 0.15f; break;
                case "bait_crank": hard = 0.06f; weed = 0.30f; touch = 0.10f; break;
                case "bait_kona": hard = 0.10f; weed = 0.20f; touch = 0.10f; break;
                case "bait_minnow": hard = 0.18f; weed = 0.25f; touch = 0.10f; break;
                case "bait_popper": hard = 0f; weed = 0.20f; touch = 0f; break;
                case "bait_frog": hard = 0f; weed = 0f; touch = 0f; break;        // weedless
                case "bait_softworm": hard = 0.05f; weed = 0.05f; touch = 0.04f; break;   // Texas-rigged
                case "bait_jig": hard = 0.30f; weed = 0.20f; touch = 0.20f; break;
                case "bait_egi": hard = 0.30f; weed = 0.25f; touch = 0.20f; break;
                default: hard = 0.15f; weed = 0.20f; touch = 0.10f; break;
            }
        }

        /// <summary>
        /// The snag rolls of a moving rig (Waiting / Retrieving, spec 6.2-6.3): for every metre the hook travels inside a
        /// snag / weed zone p = 1 - exp(-rate d), rate = base x grab x grabK x speed x depth x bottom x SnagMult; per bottom
        /// touch in a hard zone its own roll. None 1 s after a snag came free, within 0.5 m of home, while hanging in the
        /// current. A crank that is not caught bumps off a hard zone (딱!, a strike window).
        /// </summary>
        void SnagRolls(float dt)
        {
            var tk = Tackle;
            var obs = Obst;
            if (obs == null || obs.Empty || tk.State != Tackle.Mode.Water || tk.Snag != null || tk.OnPad != null)
            {
                snagPrevOk = false;
                return;
            }
            SnagChecks++;
            var h = tk.HookPos;
            float d = 0f;
            if (snagPrevOk)
            {
                var dv = new Vector2(h.x - snagPrevHook.x, h.z - snagPrevHook.z);
                d = dv.magnitude;
                if (Mathf.Abs(dv.x) > 1e-4f) lastSideMove = Mathf.Sign(dv.x);
            }
            snagPrevHook = h;
            snagPrevOk = true;
            hookSpeed = Mathf.Lerp(hookSpeed, d / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.25f));
            if (Obstacles.SnagMult <= 0f || Time.time < snagCoolUntil || tk.Hanging) return;
            if (L.IsIce ? tk.Depth <= 0.55f : tk.Surface.z <= L.zNear + 0.8f) return;
            var b = tk.Bait;
            SnagBase(b, out float hardB, out float weedB, out float touchB);
            // (a natural bait: the weedless hook's guard snags less: HookDef.snagK)
            if (RigHook != null)
            {
                hardB *= RigHook.snagK;
                weedB *= RigHook.snagK;
                touchB *= RigHook.snagK;
            }
            bool film = !tk.UsesFloat && tk.Depth <= 0.12f;
            var hz = hardB > 0f || touchB > 0f ? obs.ZoneAt(h, true, false, film) : null;
            if (tk.BottomTouch && hz != null && touchB > 0f)
            {
                float p = touchB * hz.Mat.grab * hz.grabK * Obstacles.SnagMult;
                bool yes = Obstacles.Roll() < p;
                Obstacles.Say(string.Format(CIo, "snag roll {0} touch {1} p {2:0.00}: {3}", hz.id, b.id, p, yes ? "snag" : "no"));
                if (yes)
                {
                    SnagAt(hz, h, false, "hard");
                    return;
                }
            }
            if (d <= 1e-5f) return;
            float speedF = hookSpeed < 0.3f ? 1.3f : hookSpeed > 1.2f ? 0.6f : 1f;
            if (hz != null && hardB > 0f)
            {
                float depthF = h.y >= hz.top - 0.3f ? 0.5f : 1f;
                float bottomF = tk.OnBottom ? 1.5f : 1f;
                float rate = hardB * hz.Mat.grab * hz.grabK * speedF * depthF * bottomF * Obstacles.SnagMult;
                if (Obstacles.Roll() < 1f - Mathf.Exp(-rate * d))
                {
                    Obstacles.Say(string.Format(CIo, "snag {0} {1} d {2:0.000} rate {3:0.00}", hz.id, b.id, d, rate));
                    SnagAt(hz, h, false, "hard");
                    return;
                }
                // the crank's lip bumps it off the hard stuff: a strike window for its followers
                if (b.id == "bait_crank" && Time.time - crankBumpT >= 0.8f)
                {
                    crankBumpT = Time.time;
                    crankUntil = Time.time + 1f;
                    CrankBumps++;
                    hud.LureFeedback("딱!", UIKit.Sky, 0.6f);
                    Fx.Puff(Snap(P.To2D(P.Apparent(h))), Stage.UnderwaterTint(tk.Depth, 0.8f), 2, 0.4f, 13, 0.2f);
                    Obstacles.Say($"crank bump {hz.id}");
                }
            }
            if (weedB > 0f)
            {
                var wz = obs.ZoneAt(h, false, true, film);
                if (wz != null && !(b.id == "bait_popper" && wz.top < -0.1f))
                {
                    float depthF = h.y >= wz.top - 0.3f ? 0.5f : 1f;
                    float rate = weedB * wz.Mat.grab * wz.grabK * speedF * depthF * Obstacles.SnagMult;
                    if (Obstacles.Roll() < 1f - Mathf.Exp(-rate * d))
                    {
                        Obstacles.Say(string.Format(CIo, "snag {0} {1} d {2:0.000} rate {3:0.00}", wz.id, b.id, d, rate));
                        SnagAt(wz, h, true, wz.mat == "reed" ? "reed" : "weed");
                    }
                }
            }
        }

        /// <summary>
        /// The hook catches (spec 6.4): the rig held at the snag point, the fight strip in snag mode, the free side worked out
        /// (pull from the other angle: away from the side of him it lies on; near straight out, against its last sideways
        /// motion), a following fish turns away.
        /// </summary>
        void SnagAt(Obstacle zone, Vector3 h, bool soft, string kind)
        {
            float dx = h.x - Anchor.x;
            int side = Mathf.Abs(dx) >= 1f ? -(int)Mathf.Sign(dx) : lastSideMove != 0f ? -(int)Mathf.Sign(lastSideMove) : (Obstacles.Roll() < 0.5f ? -1 : 1);
            var sn = new SnagInfo { zone = zone, at = h, freeSide = side, soft = soft, kind = kind, r = 0f };
            Tackle.SetSnag(sn);
            TautLine();   // (no stretch yet)
            if (kind != "prop") SnagCount++;
            snagArrow = false;
            snagRightT = snagWrongT = snagSoftT = snagBreakT = snagCurT = snagPadWound = 0f;
            snagRingT = 0f;
            snagLastWindT = Time.time;
            ResetPropWork();
            foreach (var f in Spawner.Fish) if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble) f.LoseInterest();
            SetState(S.Snagged);
            if (kind == "pad") hud.Flash("연잎에 걸렸어요 — 톡 당기거나 좌우로 밀어요", UIKit.Bad, 1.6f);
            else if (kind == "prop") hud.Flash($"{PropName(zone)}에 걸렸다!", UIKit.Bad, 1.2f);
            else hud.Flash(kind == "weed" ? "수초에 걸렸다!" : kind == "reed" ? "갈대에 걸렸다!" : "밑걸림!", UIKit.Bad, 1.2f);
            Sfx.Play(kind == "hard" || kind == "prop" ? Sfx.Knock : Sfx.Tear, 0.6f, 0.8f);
            if (!Tackle.UsesFloat && kind != "pad") Fx.Puff(Snap(P.To2D(P.Apparent(h))), Stage.UnderwaterTint(Tackle.Depth, 0.8f), 5, 0.5f, 13, 0.25f);
            Obstacles.Say(string.Format(CIo, "snagged {0} ({1}) {2} at ({3:0.00}, {4:0.00}, {5:0.00}) free side {6:+0;-0}", zone.id, kind, Tackle.Bait.id, h.x, h.y, h.z, side));
        }

        /// <summary>
        /// Snagged (spec 6.4-6.6): winding builds the snag tension (0.9 x rev/s) until the line breaks (held at 1 for the
        /// rod's break grace), giving line or leaving the reel lets it off; a 톡 frees it by chance (x1.5 on a slack line),
        /// the rod held swept to the free side for 0.6 s frees it (soft snags: either side, 0.5 s), a wrong sweep shows the
        /// arrow (and still frees with 15 % when committed); the stream nudges a float loose now and then. A pad snag frees
        /// after 0.8 m of winding (a natural bait torn off).
        /// </summary>
        void UpdateSnagged(float dt)
        {
            var tk = Tackle;
            var sn = tk.Snag;
            if (sn == null)
            {
                ClearRodPitch();
                SetState(S.Waiting);
                return;
            }
            Angler.LineTarget = tk.LineEnd;
            Angler.Slack01 = 0f;
            Angler.LineBow = Vector3.zero;
            Angler.Tension01 = Mathf.Clamp01(0.3f + 0.6f * sn.r);
            Angler.Strain01 = Mathf.InverseLerp(0.8f, 1.1f, sn.r);
            snagRingT -= dt;
            if (snagRingT <= 0f)
            {
                snagRingT = 0.8f;
                LineRing(Angler.LineUnderwater ? Angler.WaterEntry : tk.Surface, 0.4f);
            }
            float revs = Gesture.Speed;
            bool winding = revs >= LureInput.WindMin, giving = revs <= -LureInput.WindMin;
            if (winding)
            {
                Angler.SetPose(ReelPose(Gesture.TotalRevs * 4));
                ReelTicks(Mathf.Abs(Gesture.FrameRevs));
                snagLastWindT = Time.time;
            }
            else Angler.SetPose("fight");
            if (giving) snagGiveT = Time.time;
            if (sn.kind == "pad")
            {
                if (winding)
                {
                    snagPadWound += revs * Game.I.Reel.retrieve * dt;
                    if (snagPadWound >= 0.8f)
                    {
                        PadTear();
                        return;
                    }
                }
            }
            else
            {
                // the tension is the line's stretch: winding takes line the held rig cannot give, giving line eases it, the
                // rod eases it a little when left (the current keeps a floor)
                float floor = Stage.Current != null && Stage.Current.Moving && !L.IsIce ? 0.12f : 0.05f;
                float k = StretchPerR, chord = LineChord;
                // (caught on a prop, giving line pays out slack past the chord: PropLine)
                if (sn.kind == "prop") PropLine(dt, winding, giving, revs, floor);
                else if (winding) LineOut -= revs * Game.I.Reel.retrieve * dt;
                else if (giving) LineOut = Mathf.Min(chord, LineOut + 1.5f * k * dt);
                else LineOut = Mathf.Min(chord - floor * k, LineOut + 1.2f * k * dt);
                sn.r = Mathf.Max(0f, (chord - LineOut) / k);
                // the line twanging as winding loads the snag (as in a fight)
                Sfx.LineStrain(Mathf.InverseLerp(StrainFrom, 1f, sn.r));
                if (sn.r >= 1f)
                {
                    snagBreakT += dt;
                    if (snagBreakT >= 0.3f + 0.6f * Game.I.Rod.flex)
                    {
                        SnagBreak(false);
                        return;
                    }
                }
                else snagBreakT = 0f;
            }
            // a 톡
            if (LureIn.FlickNow && sn.kind == "prop" && !L.IsIce)
            {
                // (a prop catch: a short pull along the snapped-up line, worked by the physics: PropStep)
                StartRodJerk(LureIn.FlickStrength);
                PropTok(LureIn.FlickStrength);
            }
            else if (LureIn.FlickNow)
            {
                StartRodJerk(LureIn.FlickStrength);
                float s = LureIn.FlickStrength, p;
                bool slack = false;
                if (sn.kind == "pad") p = 0.6f + 0.3f * s;
                else
                {
                    slack = Time.time - snagLastWindT >= 1.5f || Time.time - snagGiveT < 1.5f;
                    p = (0.25f + 0.35f * s) * sn.zone.Mat.freeK * (slack ? 1.5f : 1f);
                }
                bool ok = Obstacles.Roll() < p;
                Obstacles.Say(string.Format(CIo, "free tok s {0:0.00} p {1:0.00}{2}: {3}", s, p, slack ? " (slack)" : "", ok ? "free" : "held"));
                if (ok)
                {
                    FreeSnag("tok");
                    return;
                }
                if (!sn.soft) snagArrow = true;
            }
            // a prop catch: the line's pull against its faces (Docs/obstacles_spec.md 6.10; the sweep, the pitch, the
            // slack and the 톡 all act through it)
            if (sn.kind == "prop" && !L.IsIce)
            {
                RodPitch(dt);
                if (PropStep(dt, sn)) return;
            }
            // the rod sweep (not through the ice)
            else if (!L.IsIce)
            {
                float lean = LeanReq;   // (the sweep asked for: a snag far out to one side frees the same)
                bool held = Mathf.Abs(lean) >= 0.5f;
                if (sn.soft)
                {
                    snagSoftT = held ? snagSoftT + dt : 0f;
                    if (snagSoftT >= 0.5f)
                    {
                        FreeSnag("sweep");
                        return;
                    }
                }
                else
                {
                    int side = lean > 0f ? 1 : -1;
                    if (held && side == sn.freeSide)
                    {
                        snagRightT += dt;
                        snagWrongT = 0f;
                        if (snagRightT >= 0.6f)
                        {
                            FreeSnag("sweep");
                            return;
                        }
                    }
                    else if (held)
                    {
                        snagWrongT += dt;
                        snagRightT = 0f;
                        if (snagWrongT >= 1f) snagArrow = true;
                    }
                    else snagRightT = snagWrongT = 0f;
                    if (Slide.SlideNow && Mathf.Abs(Slide.Value) >= 0.5f && (int)Mathf.Sign(Slide.Value) == -sn.freeSide)
                    {
                        bool ok = Obstacles.Roll() < 0.15f;
                        Obstacles.Say($"free sweep wrong way: {(ok ? "free" : "held")}");
                        snagArrow = true;
                        if (ok)
                        {
                            FreeSnag("sweep-wrong");
                            return;
                        }
                    }
                }
            }
            // the stream nudges a float rig loose
            if (tk.UsesFloat && sn.kind != "prop" && Stage.Current != null && Stage.Current.Moving && !L.IsIce)
            {
                snagCurT += dt;
                if (snagCurT >= 3f)
                {
                    snagCurT = 0f;
                    if (Obstacles.Roll() < 0.08f)
                    {
                        FreeSnag("current");
                        return;
                    }
                }
            }
            hud.UpdateSnag(sn);
        }

        /// <summary>Freed (빠졌다!): the rig pops 0.3 m towards him and 0.3 m up (a pad: off its edge), back to waiting.</summary>
        void FreeSnag(string how)
        {
            var tk = Tackle;
            var sn = tk.Snag;
            SnagFrees++;
            LastFreeWay = how;
            hud.Flash("빠졌다!", UIKit.Gold, 0.9f);
            Sfx.Play(Sfx.Success, 0.5f);
            var s = tk.Surface;
            var s2 = P.To2D(s);
            Fx.Splash(s2, Mathf.Clamp(P.PixelsPerMetre(s) / 45f, 0.2f, 0.5f), Stage.WaterTint, 3, P.DepthOf(s));
            if (!tk.UsesFloat) Fx.Puff(Snap(P.To2D(P.Apparent(tk.HookPos))), Stage.UnderwaterTint(tk.Depth, 0.8f), 4, 0.5f, 13, 0.3f);
            tk.ClearSnag();
            if (sn != null && sn.kind == "pad")
            {
                Sfx.PlayVar(Sfx.Tear, 0.5f);
                tk.Surface = PadEdge(sn.zone, s);
            }
            // (off a prop: out from its faces, a hair when the sweep slid it off, so winding on slides it along them)
            else if (sn != null && sn.kind == "prop") tk.PopOff(how == "tok" ? 0.3f : 0.03f);
            else tk.PopFree(ShorePoint, 0.3f, 0.3f);
            TautLine();
            snagCoolUntil = Time.time + 1f;
            snagPrevOk = false;
            snagArrow = false;
            ClearRodPitch();
            Angler.Tension01 = 0f;
            Angler.Strain01 = 0f;
            Obstacles.Say($"free {how} {(sn != null ? sn.zone.id : "?")}");
            SetState(S.Waiting);
        }

        /// <summary>
        /// Forced (or cut with 끊기): the line parts at the snag, on the hook's side. A lure is lost (the line's end whips
        /// back to the tip, ready); a float rig keeps its float, which is wound in spent with the bare hook (the bait, if it
        /// was still on, used up once); the loss toast lists what went.
        /// </summary>
        void SnagBreak(bool cut)
        {
            var sn = Tackle.Snag;
            bool prop = sn != null && sn.kind == "prop";
            float lineOut = Mathf.Max(LineOut, LineChord);
            var loss = SnagLoss(cut);
            if (cut) Sfx.Play(Sfx.Snap, 0.5f);
            else
            {
                Sfx.Play(Sfx.Snap, 1f);
                view.Shake(0.3f, 0.35f);
                SnagBreakMusic();
            }
            hud.Flash(cut ? "줄을 끊었어요" : "밑걸림으로 줄이 끊어졌다!", UIKit.Bad, cut ? 1.6f : 2f);
            SnagBreaks++;
            Obstacles.Say($"{(cut ? "cut" : "break")} at {(sn != null ? sn.zone.id : "?")} lure lost {loss.lure != null}");
            var hook = Tackle.HookPos;
            var lineEnd = Angler.LineUnderwater ? Angler.WaterEntry : Tackle.LineEnd;
            Tackle.ClearSnag();
            foreach (var f in Spawner.Fish) if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble) f.LoseInterest();
            Angler.Tension01 = 0f;
            Angler.Strain01 = 0f;
            Slide.Reset();
            ClearRodPitch();
            snagArrow = false;
            snagPrevOk = false;
            if (Tackle.UsesFloat && !prop)
            {
                // (caught on a pad: the float lies off its edge)
                if (sn != null && sn.kind == "pad") Tackle.Surface = PadEdge(sn.zone, Tackle.Surface);
                Tackle.LetGoSnag();
                if (!Tackle.Bait.isLure) Tackle.LoseHookOff();   // (wound in with nothing under the float: the hook stayed in the snag)
                snap.Recoil(hook);
                Angler.LineTarget = Tackle.LineEnd;
                Angler.Slack01 = 0.45f;
                retrieveWait = RetrieveWait;
                spentRig = true;
                SetState(S.Retrieving);
            }
            else
            {
                Tackle.Hide();
                Angler.LineTarget = null;
                snap.Home(lineEnd);
                SetState(S.Ready);
            }
            LoseLine(loss, lineOut);
            ShowLoss(loss);
        }

        /// <summary>Test hook (-fkauto music): the rig's line breaks at a snag now, forced (cut = false) or with 끊기 (cut = true).</summary>
        internal void DebugSnagBreak(bool cut)
        {
            if (State == S.Waiting || State == S.Snagged) SnagBreak(cut);
        }

        /// <summary>The 끊기 button (the 회수 button while snagged).</summary>
        void CutLine()
        {
            if (State == S.Snagged) SnagBreak(true);
        }

        float propSayT;

        /// <summary>The prop's name in the texts (테트라포드, 바위, 말뚝 ...).</summary>
        static string PropName(Obstacle o) => o == null ? "장애물" : !string.IsNullOrEmpty(o.Name) ? o.Name : o.Mat.name;

        /// <summary>Wound 0.8 m against a pad: it tears free; a natural bait is torn off (the rig is wound in), a lure loses nothing.</summary>
        void PadTear()
        {
            var tk = Tackle;
            if (tk.UsesFloat)
            {
                var sn = tk.Snag;
                // (a bare hook has nothing left to tear off)
                if (!tk.BaitGone) Game.I.ConsumeBait();
                tk.TakeBait();
                PadTears++;
                Sfx.PlayVar(Sfx.Tear, 0.7f);
                hud.Flash("미끼가 연잎에 뜯겼어요", UIKit.Bad, 1.6f);
                tk.ClearSnag();
                if (sn != null) tk.Surface = PadEdge(sn.zone, tk.Surface);
                snagCoolUntil = Time.time + 1f;
                Obstacles.Say("pad tear: the bait torn off");
                retrieveWait = 0f;
                SetState(S.Retrieving);
                return;
            }
            FreeSnag("wind");
        }

        // ================================================================== fights (spec 7)
        void BeginFightObstacles()
        {
            snagPrevOk = false;   // (the rig's last snag-check point is from before the bite)
            rubAtOk = false;
            coverTarget = null;
            coverCoolUntil = -99f;
            firstRunDone = false;
            rubTold = abrTold = false;
            Rubbing = RubbedThisFight = false;
            rubO = null;
            horseT = sparkT = rubLogT = 0f;
            rubPrevFish = Hooked != null ? Hooked.Pos : Vector3.zero;
            Sfx.Rasp(0f);
            if (Fight != null) Fight.HoldEnded += OnHoldEnded;
        }

        void EndFightObstacles()
        {
            if (Fight != null)
            {
                Fight.HoldEnded -= OnHoldEnded;
                LastAbrasion = Fight.Abrasion;
            }
            Sfx.Rasp(0f);
            Rubbing = false;
            coverTarget = null;
        }

        void OnHoldEnded()
        {
            coverCoolUntil = Time.time + 5f;
            Obstacles.Say($"hold ended {(coverTarget != null ? coverTarget.id : "")}");
            coverTarget = null;
        }

        /// <summary>How far round the fight lets a fish swim at this plan distance from him.</summary>
        float MaxYawAt(float h)
        {
            float maxYaw = h > 0.5f ? Mathf.Asin(Mathf.Clamp01((L.xLim - 0.5f) / h)) : 0.8f;
            return Mathf.Min(maxYaw, L.IsIce ? Mathf.PI : 0.8f);
        }

        /// <summary>
        /// At a run's start (spec 7.2): a cover seeker may head for the nearest cover of its types within its reach (a hold
        /// point the fight can reach), p = seek (x1.5 on the first run, x0.5 worn down to under 30 %), capped 0.9; never
        /// within 5 s of a pull-out (or the end of a hold), never on a downstream run. The ice: a rim run.
        /// </summary>
        void TryCoverRun()
        {
            bool first = !firstRunDone;
            firstRunDone = true;
            var f = Fight;
            var sp = Hooked.Sp;
            if (f == null || Hooked == null || Obst == null || Obst.Empty || DownstreamRun || sp.coverSeek <= 0f) return;
            // (the test hook's forced cover ignores the cool-down)
            if (Time.time < coverCoolUntil && DebugCover == null) return;
            float p = Mathf.Min(0.9f, sp.coverSeek * (first ? 1.5f : 1f) * (f.Stamina < 0.3f ? 0.5f : 1f));
            if (L.IsIce)
            {
                if (Obst.Rims.Count == 0 || System.Array.IndexOf(sp.coverFor ?? new string[0], "rim") < 0) return;
                if (Obstacles.Roll() >= sp.coverSeek) return;
                coverTarget = Obst.Rims[0];
                rimTarget = L.holeR + 0.6f * Hooked.Depth + 2f;
                CoverRuns++;
                LastCover = coverTarget;
                f.BeginCoverRun();
                // (no side pressure through the ice: steady winding in its hold is what drags it out, 7.6)
                hud.Flash("얼음 밑으로 파고든다! 계속 감아 끌어내요!", UIKit.Bad, 1.4f);
                Obstacles.Say(string.Format(CIo, "cover run {0} -> rim (wide to {1:0.0} m)", sp.id, rimTarget));
                return;
            }
            Obstacle c;
            bool forced = false;
            if (DebugCover != null)
            {
                c = Obst.Get(DebugCover);
                DebugCover = null;
                forced = c != null;
            }
            else c = Obst.NearestCover(Hooked.Pos, sp, sp.coverReach, hold =>
            {
                float h = new Vector2(hold.x - Anchor.x, hold.y - Anchor.z).magnitude;
                float yaw = Mathf.Atan2(hold.x - Anchor.x, hold.y - Anchor.z);
                // (on the bed: a hold with water enough for it; a big carp does not run into the reeds' 1 m)
                return Mathf.Abs(yaw) <= MaxYawAt(h) + 1e-3f && h <= f.Line + 8f
                       && (!L.Terrain || L.DepthAt(hold.x, hold.y) >= FishHabitat.MinWater(Hooked.Cm) + 0.2f);
            });
            if (c == null) return;
            if (!forced && Obstacles.Roll() >= p)
            {
                Obstacles.Say(string.Format(CIo, "cover roll {0} -> {1} p {2:0.00}: no", sp.id, c.id, p));
                return;
            }
            StartCoverRun(c);
        }

        void StartCoverRun(Obstacle c)
        {
            var sp = Hooked.Sp;
            coverTarget = c;
            LastCover = c;
            CoverRuns++;
            var hold = c.Hold;
            float h = new Vector2(hold.x - Anchor.x, hold.y - Anchor.z).magnitude;
            float lim = MaxYawAt(h);
            coverYaw = Mathf.Clamp(Mathf.Atan2(hold.x - Anchor.x, hold.y - Anchor.z), -lim, lim);
            fightYawTarget = coverYaw;
            float dy = coverYaw - fightYaw;
            FishRun = Mathf.Abs(dy) >= 0.05f ? (dy > 0f ? 1 : -1) : (hold.x >= Hooked.Pos.x ? 1 : -1);
            RunT = 0f;
            turnProg = 0f;
            runTurned = false;
            float bed = L.DepthAt(hold.x, hold.y) - 0.3f;
            fishDepthTarget = Mathf.Clamp(bed - 0.2f, Mathf.Min(sp.depthMin, bed), Mathf.Min(sp.depthMax, bed));
            Fight.BeginCoverRun();
            horseT = 0f;
            hud.Flash("커버로 파고든다! 반대로 밀어요!", UIKit.Bad, 1.1f);
            Obstacles.Say(string.Format(CIo, "cover run {0} -> {1} hold ({2:0.00}, {3:0.00}) yaw {4:+0.00;-0.00} run {5:+0;-0}", sp.id, c.id, hold.x, hold.y, coverYaw, FishRun));
        }

        /// <summary>
        /// Pulled out of the cover (spec 7.6): by side pressure (the run turned) or by horsing it out of its hold; its head
        /// comes 0.35 rad away from the cover, no cover run for 5 s.
        /// </summary>
        void PullOut(string how)
        {
            var f = Fight;
            PullOuts++;
            f.EndCover();
            float maxYaw = MaxFightYaw();
            fightYawTarget = Mathf.Clamp(fightYaw - FishRun * 0.35f, -maxYaw, maxYaw);
            coverCoolUntil = Time.time + 5f;
            Obstacles.Say(string.Format(CIo, "pullout {0} from {1} A {2:0.00}", how, coverTarget != null ? coverTarget.id : "?", f.Abrasion));
            coverTarget = null;
            horseT = 0f;
            // (a with-current run turned this way is pulled out of the flow as well)
            if (RunAlign >= 0.3f || DownstreamRun)
            {
                OutOfFlowT = 4f;
                FlowTurns++;
                DownstreamRun = false;
                f.Downstream = false;
            }
            hud.Flash("커버에서 끌어냈다!", UIKit.Gold, 0.9f);
            Sfx.Play(Sfx.Success, 0.5f);
        }

        /// <summary>
        /// Each fight frame, the fish placed: the cover run's arrival (the hold), horsing it out of the hold, and the line
        /// rubbing on structure (the abrasion meter: dA/dt = 0.22 x rough x (0.4 + 0.6 tension) x (0.5 + 0.5 speed / 1.5) /
        /// tough, half while giving line; sparks, the rasp, the warnings).
        /// </summary>
        void FightObstacles(float dt, FightModel f, Vector3 pos)
        {
            if (Obst == null || Obst.Empty)
            {
                f.Rubbing = false;
                return;
            }
            if (f.CoverRun && coverTarget != null)
            {
                bool arrived;
                if (coverTarget.kind == "rim") arrived = Plan(pos, new Vector2(L.holeX, L.holeZ)) >= rimTarget;
                else
                {
                    var hold = coverTarget.Hold;
                    float fishH = Plan(pos, new Vector2(Anchor.x, Anchor.z)), holdH = new Vector2(hold.x - Anchor.x, hold.y - Anchor.z).magnitude;
                    arrived = Plan(pos, hold) <= 0.8f || (Mathf.Abs(fightYaw - coverYaw) <= 0.08f && fishH >= holdH - 0.8f);
                }
                if (arrived)
                {
                    f.BeginHold(Hooked.Sp.coverDig);
                    CoverHolds++;
                    Obstacles.Say(string.Format(CIo, "hold {0} at ({1:0.00}, {2:0.00}) for {3:0.0}s", coverTarget.id, pos.x, pos.z, f.CoverHoldLeft));
                }
            }
            if (f.CoverHold && coverTarget != null)
            {
                if (coverTarget.kind != "rim") fightYawTarget = coverYaw;
                // steady winding at a good tension drags it out ("horsing it": it costs abrasion)
                if (Gesture.Speed >= 0.5f && f.TensionRatio >= 0.5f) horseT += dt;
                else horseT = 0f;
                if (horseT >= 2.5f * Hooked.Sp.coverDig) PullOut("horse");
            }
            // the line rubbing
            bool rub = false;
            Obstacle o = null;
            Vector3 at = Vector3.zero;
            if (jumpTime < 0f)
            {
                if (L.IsIce)
                {
                    var hole = new Vector2(L.holeX, L.holeZ);
                    float hh = Plan(pos, hole), dF = Mathf.Max(0f, -pos.y);
                    // (the rim wears the line only on a rim run / hold, the pike's "cover": through the ice there is no
                    // side pressure to steer a fish, so every fish that swam a little wide would otherwise wear the line
                    // through with no answer; measured: pike and char fights broke on the rim almost every time)
                    bool rimRun = coverTarget != null && coverTarget.kind == "rim" && (f.CoverRun || f.CoverHold);
                    if (rimRun && Obst.Rims.Count > 0 && hh > L.holeR + 0.6f * dF)
                    {
                        o = Obst.Rims[0];
                        var dir = (new Vector2(pos.x, pos.z) - hole).normalized;
                        at = new Vector3(hole.x + dir.x * L.holeR, -0.1f, hole.y + dir.y * L.holeR);
                        rub = true;
                    }
                    else rub = Obst.Rub(hole, pos, out o, out at);
                }
                else
                {
                    var e = Angler.LineUnderwater ? Angler.WaterEntry : Angler.RodTip;
                    rub = Obst.Rub(new Vector2(e.x, e.z), pos, out o, out at);
                }
            }
            // (a slack line does not press on the structure)
            if (rub && !RubTautNow(f.Tension, f.LineLimit, f.Power)) rub = false;
            f.Rubbing = rub;
            Rubbing = rub;
            float fishSpeed = Plan(pos, rubPrevFish) / Mathf.Max(dt, 1e-4f);
            rubPrevFish = pos;
            if (!rub)
            {
                Sfx.Rasp(0f);
                return;
            }
            rubO = o;
            rubAt = at;   // (where it wears: above or below the float decides what a break there takes)
            rubAtOk = true;
            float rough = o.Mat.rough * o.roughK;
            float rate = RubRate * rough * (0.4f + 0.6f * Mathf.Clamp01(f.TensionRatio)) * (0.5f + 0.5f * Mathf.Clamp01(fishSpeed / 1.5f))
                         / Mathf.Max(0.1f, Game.I.Line.tough) * (f.Giving ? 0.5f : 1f) * DebugRubMult;
            f.Abrade(rate * dt);
            if (!rubTold)
            {
                rubTold = true;
                RubbedThisFight = true;
                hud.Flash($"줄이 {o.Name}에 쓸린다!", UIKit.Bad, 1.2f);
            }
            if (!abrTold && f.Abrasion >= 0.6f)
            {
                abrTold = true;
                hud.Flash("줄이 버티지 못해요! 빨리 빼내요!", UIKit.Bad, 1.2f);
            }
            sparkT -= dt;
            if (sparkT <= 0f)
            {
                sparkT = 0.12f;
                var s2 = (Vector2)Snap(P.To2D(P.Apparent(at), out float rubD));
                rubD = Mathf.Max(0.05f, rubD - FrontOcclusion.Resting);   // (on the structure's face: that face must not hide it)
                var frames = Fx.Load("fx_rub_f", 4);
                if (frames != null) Fx.Frames(frames, s2, 0.03f, Fx.OrderSplash, Color.white, depth: rubD);
                else Fx.Sparkle(s2, Art.Hex("#ffd080"), 0.15f, 1f, Fx.OrderSplash, rubD);
            }
            Sfx.Rasp(0.15f + 0.3f * Mathf.Min(1f, rate / 0.25f));
            rubLogT -= dt;
            if (rubLogT <= 0f)
            {
                rubLogT = 0.5f;
                Obstacles.Say(string.Format(CIo, "rub {0} A {1:0.00} rate {2:0.000}/s tension {3:0.00} limit {4:0.00}", o.id, f.Abrasion, rate, f.TensionRatio, f.LineLimit));
            }
        }

        /// <summary>The break message: worn through on structure, or the usual.</summary>
        string BreakText(bool spooled)
        {
            var f = Fight;
            LastSnapCause = f != null ? f.SnapCause : FightModel.Cause.None;
            LastBreakName = rubO != null ? rubO.Name : "";
            if (!spooled && f != null && f.SnapCause == FightModel.Cause.Abrasion && rubO != null) return $"줄이 {rubO.Name}에 쓸려 끊어졌다!";
            return spooled ? "줄이 다 풀려서 끊어졌다!" : "줄이 끊어졌다!";
        }

        // ================================================================== bites (spec 8)
        /// <summary>Structure and a natural entry on a fish's appetite (in BiteMult).</summary>
        float StructureBite(FishAgent f, Vector3 hook, float spot)
        {
            float st = Obst != null ? Obst.StructureMult(hook, f.Sp) : 1f;
            // (the stream's pocket x1.3 and this are not stacked: the larger wins; the fast lane's 0.85 stays)
            float m = spot < 1f ? spot * st : Mathf.Max(spot, st);
            if (Time.time - natT < 8f && Plan(f.Pos, natAt) <= 5f) m *= 1.3f;
            return m;
        }

        // ================================================================== test hooks
        /// <summary>
        /// Test hook (-fkauto obstacles): from the ready, a cast straight at <paramref name="target"/> (the real flight, its
        /// contacts and landing), as a flick would throw it. False unless ready.
        /// </summary>
        internal bool DebugCastTo(Vector3 target, float power = 0.8f)
        {
            if (State != S.Ready || L.IsIce) return false;
            var t = target;
            t.y = 0f;
            t.z = Mathf.Clamp(t.z, L.zNear + 1.2f, L.zFar - 1f);
            t.x = Mathf.Clamp(t.x, -L.xLim + 0.6f, L.xLim - 0.6f);
            aimTarget = Tackle.PastStand(t, new Vector3(Angler.X, 0f, 0f));
            AimPower = power;
            StartCoroutine(CastRoutine());
            return true;
        }
    }
}
