using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The fishing loop: wind up (drag down) and flick up to cast (the flick's speed = distance, its direction at the
    /// release = direction; on the ice the drag sets the depth) → wait for nibbles → tap to set the hook →
    /// fight by drawing circles (reel) while watching the tension → land → sell / keep / release.
    /// </summary>
    public partial class FishingController : MonoBehaviour
    {
        /// <summary>... Snagged: the rig caught on an underwater obstacle (밑걸림, Docs/obstacles_spec.md 6).</summary>
        public enum S { Ready, Aiming, Casting, Waiting, Biting, Fighting, Landing, Result, Retrieving, Encounter, Snagged }

        public S State { get; private set; }
        public StageView Stage { get; private set; }
        public Tackle Tackle { get; private set; }
        public FishSpawner Spawner { get; private set; }
        public Angler Angler { get; private set; }
        public FightModel Fight { get; private set; }
        public FishAgent Hooked { get; private set; }
        public CircleGesture Gesture { get; } = new CircleGesture();
        /// <summary>The lure verbs (wind / flick / pause, and taps), read while the rig is in the water.</summary>
        public LureInput LureIn { get; } = new LureInput();
        /// <summary>How well the equipped lure is being worked (Q, strike windows, feedback words).</summary>
        public LureRhythm Rhythm { get; } = new LureRhythm();
        /// <summary>Lure bite counters since the scene opened (for the test autopilot).</summary>
        public int LureFollows { get; private set; }
        public int LureStrikeRolls { get; private set; }
        public int LureBites { get; private set; }
        public int LureTurned { get; private set; }
        public float FishZMax => Mathf.Min(Stage.L.zFar - 2f, Game.I.Rod.castDist + 14f);
        /// <summary>On the ice: the drag (0..1, sets the depth). Elsewhere: the last throw's flick power (0 while winding up).</summary>
        public float AimPower { get; private set; }
        public float AimDepth { get; private set; }
        /// <summary>The wind-up has been pulled far enough (<see cref="FlickCast.WindUpMin"/>) for a flick to throw.</summary>
        public bool WindUpArmed { get; private set; }
        /// <summary>The last release of a wind-up (not on the ice): what the flick measured and what it made of it.</summary>
        public FlickCast.Result LastFlick { get; private set; }
        /// <summary>Counts every release of a wind-up (not on the ice), thrown or not (for the test autopilot).</summary>
        public int FlickCount { get; private set; }
        /// <summary>The stage legend's presence and trigger (null on stages without a converted legend).</summary>
        public LegendWatch Watch { get; private set; }
        /// <summary>The legend encounter in progress (also kept after it ends, for the test autopilot).</summary>
        public LegendEncounter Encounter { get; private set; }
        public EncounterView EncounterView => encView;
        EncounterView encView;
        FishSpecies encSp;
        float encCm, encDipT;
        bool firstSeen;

        FishingHUD hud;
        PixelView view;
        Vector2 pressPos;
        Vector3 aimTarget;
        float windMax; // the deepest pull of this wind-up, screen heights below the press point
        // the rod follows the finger while winding up (Angler.WindUp): its offset from the press point (screen widths
        // right, screen heights below), smoothed just enough to take out the jitter
        Vector2 windS;
        const float WindTau = 0.05f;
        readonly List<SpriteRenderer> dots = new List<SpriteRenderer>();
        // the wind-up guide: the arrow over his head (throw it up and over) and a faint fan of the throwable range (edge
        // rays + the far arc) on the water
        CastArrow castArrow;
        // the fight's side-pressure arrow over the line's water entry: the way to lean the rod against the fish's run
        SideArrow sideArrow;
        readonly List<SpriteRenderer> fan = new List<SpriteRenderer>();
        const int FanRay = 8, FanArc = 13;
        SpriteRenderer targetRing, biteMark;
        float biteWindow;
        FishAgent biter;
        /// <summary>Tension ratio (of the line's limit, or of a snag's pull) where the line starts to twang (Sfx.LineStrain).</summary>
        const float StrainFrom = 0.6f;
        float fightYaw, fightYawTarget, fishDepthTarget, splashT, tickAcc, jumpTime, lineRingT, dripT;
        // the jump in progress (copied from the fight model when it starts)
        FightModel.JumpKind jumpKind;
        float jumpDur = 1f, trailT;
        int jumpShakes, jumpFlip;
        bool jumpRight;
        Vector3 lastFishPos;
        string lastStateHint;
        float autoRevs; // reel turns of the automatic retrieve (added to the gesture's for the 3D crank)
        // a 톡 (a quick pull down the screen) jerks the rod up and back towards him (Angler.RodJerk01): a quick rise, a
        // short hold, an easy return; its size grows with the flick's strength. No body motion.
        float jerkT = 99f, jerkAmp, jerkFrom;
        const float JerkRise = 0.06f, JerkHold = 0.05f, JerkFall = 0.28f;
        const float JerkMin = 0.35f, JerkGain = 0.5f;   // amplitude = JerkMin + JerkGain * strength (0.35 .. 0.85)
        float followFlashT = -99f, upHintT = -99f;

        // ---- the rod sweep (Waiting / Retrieving: a wound-in rig's path bends towards the rod tip's side, a float at rest
        // is dragged a little) and side pressure (Fighting: the rod leant against the fish's run turns it); off on the ice
        // (the line runs down a hole) and in the legend encounter (its window has its own controls)
        /// <summary>The sideways slide / A-D keys that sweep the rod (sticky).</summary>
        public SideSlide Slide { get; } = new SideSlide();
        /// <summary>
        /// The rod's lean this frame, -1 (full left) .. 1 (full right): the sweep asked for (eased), so side pressure counts
        /// in full even with the fish beyond the rod's drawn yaw limits (the fight strip's rod shows it too).
        /// </summary>
        public float Lean => Mathf.Clamp(Angler.SweepReq / Angler.SweepMax, -1f, 1f);
        /// <summary>Sine of the rod tip's angle off the line to the rig, towards its sweep (+ = right): what bends the rig's path.</summary>
        public float SweepSin => L.IsIce ? 0f : Mathf.Sin(Angler.SweepLine * Mathf.Deg2Rad);
        /// <summary>The hooked fish's sideways run: -1 to his left, +1 to his right, 0 none (resting, jumping, worn out, the ice).</summary>
        public int FishRun { get; private set; }
        /// <summary>Side pressure this frame: + leaning against the run (good), - with it (bad), 0 centred / no run.</summary>
        public float SideNow { get; private set; }
        /// <summary>
        /// Side pressure counts this frame (a run off to one side, not resting / jumping / worn out, not on the ice): the
        /// arrow over the line's entry (<see cref="SideArrow"/>) shows then.
        /// </summary>
        public bool SideActive { get; private set; }
        /// <summary>How long the current run has lasted, and when (into it) side pressure turned the last turned run.</summary>
        public float RunT { get; private set; }
        public float LastTurnT { get; private set; } = -1f;
        /// <summary>Called when a run ends (for the tests): its side, how long it lasted, whether side pressure turned it.</summary>
        public event System.Action<int, float, bool> RunEnded;
        // side pressure: against the run the fish's heading is pulled round towards the rod's side at TurnRate (rad/s at a
        // full lean) and after TurnTime s of full lean (longer at a smaller one) the run is turned: its head comes round
        // towards the rod's side and it runs on that way, the rest of the run cut short (FightModel.TurnCut); with the run its
        // heading is pushed on outwards at PushRate. A lean under SideDead (of the full sweep) is centred.
        const float TurnRate = 0.6f, TurnTime = 0.7f, PushRate = 0.25f, SideDead = 0.15f;
        const float LongRun = 1.6f;          // s: a run this long without side pressure against it brings the one-time hint
        float turnProg;
        bool runTurned;
        FightModel.Phase lastPhase;
        // A / D still held from the rod sweep / side pressure when he is back at the ready walk nothing until let go once
        bool walkLatch;
        System.Random runRnd = new System.Random();
        /// <summary>Test switch (-fkauto steer): no fish engages the rig (the rod sweep's path is measured undisturbed).</summary>
        internal static bool NoBites;

        // ---- the moving water in the fight (Docs/time_currents_spec.md 9.7)
        /// <summary>The run's alignment with the current this step (-1 against .. +1 with it); 0 at rest.</summary>
        public float RunAlign { get; private set; }
        /// <summary>This run rides the stream down past his side (a downstream run).</summary>
        public bool DownstreamRun { get; private set; }
        /// <summary>Downstream runs, with-current runs turned by side pressure ("pulled out of the flow") so far (for the tests).</summary>
        public int DownstreamRuns { get; private set; }
        public int FlowTurns { get; private set; }
        /// <summary>Real seconds left of "pulled out of the flow" (after side pressure turned a with-current run: al 0, load x 0.3).</summary>
        public float OutOfFlowT { get; private set; }
        bool alignTold;
        /// <summary>Mends so far (for the tests).</summary>
        public int Mends { get; private set; }
        /// <summary>Fish that came over to the bait (approaches) since the scene opened, and the rolls for it (for the tests).</summary>
        public int Approaches { get; private set; }
        public int ApproachRolls { get; private set; }
        bool keyWas;

        StageLayout L => Stage.L;
        Persp P => Stage.P;
        // the water under the angler's feet (he walks sideways, see Angler.X) / the ice hole
        Vector3 Anchor => L.IsIce ? new Vector3(L.holeX, 0, L.holeZ) : new Vector3(Angler.X, 0, 0);
        Vector3 ShorePoint => L.IsIce ? new Vector3(L.holeX, 0, L.holeZ) : new Vector3(Angler.X, 0, L.zNear - 0.5f);

        public void Init(StageView stage, PixelView pv)
        {
            Stage = stage;
            view = pv;
            Fx.Ensure();
            // only bottom and vertical lures work through an ice hole: a banned lure still equipped is swapped for paste
            if (L.IsIce && !LureInfo.IceOk(Game.I.Bait))
            {
                var paste = GameDatabase.GetItem<BaitDef>(GameDatabase.StarterBait);
                Toast.Show($"{Game.I.Bait.name}: {LureInfo.IceBanned} → {paste.name}", UIKit.Bad, 2.4f);
                Game.I.Equip(paste);
            }
            Angler = Angler.Create(stage);
            Tackle = Tackle.Create(stage);
            Angler.Rig = Tackle;   // (the line's end resting on a perched rig: read from the rig's state as the line is drawn)
            Tackle.FloatDepth = Mathf.Clamp(2f, 0.5f, L.DepthAt(15f) - 0.3f);
            Spawner = new GameObject("Spawner").AddComponent<FishSpawner>();
            Spawner.Init(this);
            for (int i = 0; i < 14; i++)
            {
                var d = new GameObject("AimDot").AddComponent<SpriteRenderer>();
                d.sprite = Art.Pixel;
                d.sortingOrder = Fx.OrderSparkle;
                d.transform.localScale = new Vector3(2, 2, 1);
                d.enabled = false;
                dots.Add(d);
            }
            for (int i = 0; i < FanRay * 2 + FanArc; i++)
            {
                var d = new GameObject("FanDot").AddComponent<SpriteRenderer>();
                d.sprite = Art.Pixel;
                d.sortingOrder = Fx.OrderRipple + 1;
                d.enabled = false;
                fan.Add(d);
            }
            castArrow = CastArrow.Create(Angler);
            sideArrow = SideArrow.Create(this);
            targetRing = new GameObject("Target").AddComponent<SpriteRenderer>();
            targetRing.sprite = Art.Ring;
            targetRing.sortingOrder = Fx.OrderRipple + 2;
            targetRing.enabled = false;
            biteMark = new GameObject("BiteMark").AddComponent<SpriteRenderer>();
            biteMark.sprite = Art.UI("icon_bite");
            biteMark.sortingOrder = Fx.OrderSparkle + 1;
            biteMark.enabled = false;
            hud = FishingHUD.Create(this);
            Game.I.Changed += OnGameChanged;
            Stage.Water.Rig = Tackle;   // (the float's wake)
            GameClock.PeriodBegan += OnPeriodBegan;
            InitObstacles();
            Watch = LegendWatch.For(this);
            if (Watch != null) Debug.Log($"[ENC] {Stage.Def.id}: legend watch {string.Join(", ", Watch.Legends.Select(f => f.id))} (debug {LegendWatch.DebugMode ?? "off"}{(LegendWatch.DebugLegend != null ? ", " + LegendWatch.DebugLegend : "")})");
            InitZoom();   // (the view zooms in on the rig once it lands: FishingController.Zoom.cs)
            InitMusic();  // (the stage's cue and everything the state does to it: FishingController.Music.cs)
            SetState(S.Ready);
        }

        void OnDestroy()
        {
            Sfx.Rasp(0f);
            LeaveMusic();
            GameClock.PeriodBegan -= OnPeriodBegan;
            GameClock.Stopped();
            if (Game.I != null)
            {
                Game.I.Changed -= OnGameChanged;
                Game.I.Save();   // (the clock)
            }
            Watch?.Destroy();
        }

        /// <summary>
        /// A new period began on the stage (the clock's cross-fade passed its middle): a toast, and the first time ever a hint
        /// that the fish's activity changes with it.
        /// </summary>
        void OnPeriodBegan(Period p)
        {
            Toast.Show(GameClock.BeginText(p), GameClock.TextColor(p), 2f);
            if (!Game.Data.timeHint)
            {
                Game.Data.timeHint = true;
                Game.I.Save();
                hud.Flash($"시간이 흘러 {GameClock.NameI(p)} 됐어요 — 물고기의 활동도 달라져요", UIKit.Sky, 2.8f);
            }
            Debug.Log($"[CLOCK] period {GameClock.Id(p)} began at {GameClock.HHMM(GameClock.Min, false)} day {GameClock.Day}");
        }

        /// <summary>
        /// The game clock runs while the stage is open (Docs/time_currents_spec.md 1.2): not on the catch card, not under a
        /// dialog, not while the app is in the background; a hitch never skips more than 0.1 real s.
        /// </summary>
        void TickClock()
        {
            if (State != S.Result && !Dialog.Open && Application.isFocused) GameClock.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            else GameClock.Stopped();
        }

        void OnGameChanged()
        {
            if (Tackle.Bait.id != Game.I.Bait.id && (State == S.Ready || State == S.Aiming)) Tackle.SetBait(Game.I.Bait);
            Angler.RefreshGear();
            hud.RefreshTackle();
        }

        void SetState(S s)
        {
            if (s == S.Ready && State != S.Ready && State != S.Aiming) walkLatch = true;
            State = s;
            if (s != S.Aiming)
            {
                DrawAim(false);
                DrawFan(false);
                castArrow.Hide();
                Angler.WindUp = null; // the cast swing (or the way back to the hold) carries on from where the rod is
            }
            hud.OnState(s);
        }

        /// <summary>The wind-up's over-the-head arrow (for the test autopilot).</summary>
        public CastArrow Arrow => castArrow;
        /// <summary>The fight's side-pressure arrow (for the test autopilot).</summary>
        public SideArrow PushArrow => sideArrow;

        public bool CanLeave => State == S.Ready || State == S.Aiming || State == S.Waiting || State == S.Retrieving;

        public void FloatDepthChanged(float d) => Tackle.FloatDepth = d;

        public void EquipBait(BaitDef b)
        {
            Game.I.Equip(b);
            Tackle.SetBait(Game.I.Bait);
            Angler.RefreshGear();
            if (State == S.Waiting) Retrieve();
        }

        public void Retrieve()
        {
            // (snagged, the button reads 끊기: it cuts the line)
            if (State == S.Snagged)
            {
                CutLine();
                return;
            }
            if (State != S.Waiting) return;
            retrieveWait = 0f;
            SetState(S.Retrieving);
        }

        // ================================================================== update
        void Update()
        {
            TickClock();
            TickMusic(Time.unscaledDeltaTime);
            Angler.WalkInput = 0f; // he only walks while ready (UpdateReady)
            // the line's belly in the current shows while the rig is in the water
            if (State != S.Waiting && State != S.Retrieving && State != S.Biting) Angler.LineBow = Vector3.zero;
            if (Dialog.Open && State != S.Fighting && State != S.Encounter)
            {
                // a dialog opened over a wind-up (a second finger on the settings / bait button): call the wind-up off
                // quietly, since the finger's release and the tap that closes the dialog are no flick
                if (State == S.Aiming && !L.IsIce)
                {
                    WindUpArmed = false;
                    Angler.SetPose("idle");
                    SetState(S.Ready);
                }
                Gesture.Update(false, Vector2.zero, Time.deltaTime);
                Angler.ReelRevs = Gesture.TotalRevs + autoRevs;
                return;
            }
            float dt = Time.deltaTime;
            bool worldDown = PointerInput.IsDown && !PointerInput.StartedOverUI;
            bool gestureOn = State == S.Waiting || State == S.Fighting || State == S.Biting || State == S.Encounter || State == S.Snagged;
            Gesture.Update(worldDown && gestureOn, PointerInput.Position, dt);
            // (the 톡 also knocks a perched rig off and frees a snag)
            LureIn.Update(Gesture, dt, (State == S.Waiting && (Tackle.State == Tackle.Mode.Water || Tackle.State == Tackle.Mode.Perched))
                                       || State == S.Encounter || State == S.Snagged);
            // the rod sweep / side pressure: a straight sideways slide on the water (or A / D) sweeps the rod, and it stays
            // swept (a bite keeps the rod where it was until the hook set); never on the ice or in the encounter. Snagged,
            // it frees the rig (swept to the free side).
            bool sweepIn = !L.IsIce && ((State == S.Waiting && Tackle.State == Tackle.Mode.Water) || State == S.Retrieving || State == S.Fighting || State == S.Snagged);
            bool sweepKeep = !L.IsIce && State == S.Biting;
            if (sweepKeep) Slide.Freeze();
            else Slide.Update(Gesture, sweepIn);
            Angler.Sweep = sweepIn || sweepKeep ? Slide.Shown * Angler.SweepMax : 0f;
            Angler.SideLow = State == S.Fighting;   // side pressure: the rod held low to the side
            // the legend's lurk point, cues and build-up (the meter runs while a lure soaks)
            if (Watch != null && State != S.Encounter && Watch.Tick(dt, State == S.Waiting && Tackle.State == Tackle.Mode.Water && !NoBites))
            {
                StartEncounter();
                return;
            }
            switch (State)
            {
                case S.Ready: UpdateReady(); break;
                case S.Aiming: UpdateAiming(); break;
                // (until the rig is launched, 0.08 s into the swing, there is no line out: it still dangles from the tip;
                // Tackle.LineEnd would be where the last rig lay, and the line ran there down across the stand for a frame or two)
                case S.Casting: Angler.LineTarget = Tackle.State == Tackle.Mode.Flying ? Tackle.LineEnd : (Vector3?)null; break;
                case S.Waiting: UpdateWaiting(dt); break;
                case S.Biting: UpdateBiting(dt); break;
                case S.Fighting: UpdateFighting(dt); break;
                case S.Retrieving: UpdateRetrieving(dt); break;
                case S.Encounter: UpdateEncounter(dt); break;
                case S.Snagged: UpdateSnagged(dt); break;
            }
            // the 3D reel's crank turns with every revolution, the right hand on its knob
            Angler.ReelRevs = Gesture.TotalRevs + autoRevs;
            Angler.FaceTarget = FaceTarget();
            RodJerk(dt);
            hud.Tick(dt);
        }

        /// <summary>A 톡 of this strength (0..1) jerks the rod (<see cref="RodJerk"/>); a new one mid-jerk rises from where it is.</summary>
        void StartRodJerk(float strength)
        {
            jerkFrom = Angler.RodJerk01;
            jerkAmp = Mathf.Max(jerkFrom, JerkMin + JerkGain * Mathf.Clamp01(strength));
            jerkT = 0f;
        }

        /// <summary>
        /// The rod's jerk after a 톡: up (ease-out over <see cref="JerkRise"/>), held for <see cref="JerkHold"/>, back down
        /// over <see cref="JerkFall"/> (landing lifts the rod itself: none then).
        /// </summary>
        void RodJerk(float dt)
        {
            const float total = JerkRise + JerkHold + JerkFall;
            if (jerkT >= total) return;
            if (State == S.Landing || State == S.Result)
            {
                jerkT = total;
                Angler.RodJerk01 = 0f;
                return;
            }
            jerkT += dt;
            float v;
            if (jerkT < JerkRise)
            {
                float u = 1f - jerkT / JerkRise;
                v = Mathf.Lerp(jerkFrom, jerkAmp, 1f - u * u);
            }
            else if (jerkT < JerkRise + JerkHold) v = jerkAmp;
            else v = jerkAmp * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((jerkT - JerkRise - JerkHold) / JerkFall)));
            Angler.RodJerk01 = jerkT >= total ? 0f : v;
        }

        /// <summary>The rod's jerk this frame (0..1, for the tests).</summary>
        public float RodJerkNow => Angler.RodJerk01;

        /// <summary>
        /// Where the angler turns to: straight ahead while winding up (where the throw goes is only known when the flick is
        /// let go; on the ice: the hole), the throw's target while casting, the float (or the lure's entry point) while
        /// waiting, the hooked fish while fighting (on the ice: the hole the line runs through); else straight ahead.
        /// </summary>
        Vector3? FaceTarget()
        {
            switch (State)
            {
                case S.Aiming: return L.IsIce ? aimTarget : (Vector3?)null;
                case S.Casting: return aimTarget;
                case S.Waiting:
                case S.Biting:
                case S.Encounter:
                case S.Retrieving:
                case S.Snagged: return Tackle.State == Tackle.Mode.Perched ? Tackle.PerchAt : Tackle.Surface;
                case S.Fighting: return L.IsIce || Hooked == null ? Anchor : Hooked.Pos;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ ready / aiming
        void UpdateReady()
        {
            Angler.SetPose("idle");
            Angler.LineTarget = null;
            Angler.ShowDangle = true;
            // walking sideways: the HUD's hold buttons, A / D or the arrow keys (keys still held from the rod sweep / side
            // pressure walk nothing until they have been let go once)
            float keys = PointerInput.WalkKeys;
            if (walkLatch)
            {
                if (keys == 0f) walkLatch = false;
                else keys = 0f;
            }
            Angler.WalkInput = Mathf.Clamp(hud.WalkHeld + keys, -1f, 1f);
            if (PointerInput.WorldPressed)
            {
                pressPos = PointerInput.Position;
                AimPower = 0;
                windMax = 0;
                windS = Vector2.zero;
                WindUpArmed = false;
                SetState(S.Aiming);
            }
        }

        void UpdateAiming()
        {
            if (L.IsIce)
            {
                UpdateAimingIce();
                return;
            }
            // the wind-up: pulling down arms the throw and pulls the rod back; sideways only leans the rod (the flick's
            // direction at the release is the throw's direction)
            float pull = (pressPos.y - PointerInput.Position.y) / Mathf.Max(1f, Screen.height);
            windMax = Mathf.Max(windMax, pull);
            if (!WindUpArmed && windMax >= FlickCast.WindUpMin)
            {
                WindUpArmed = true;
                Sfx.Play(Sfx.Click, 0.3f, 0.8f); // a soft tick: pulled far enough, now flick
            }
            AimPower = 0f;
            bool pulled = WindUpArmed || pull > 0.012f;
            Angler.SetPose(pulled ? "aim" : "idle");
            // the rod follows the finger: pulled down it goes back over his shoulder, coming up it swings over his head,
            // sideways its top leans with it (visual only: the throw is the flick's at the release)
            var off = new Vector2((PointerInput.Position.x - pressPos.x) / Mathf.Max(1f, Screen.width), pull);
            windS = Vector2.Lerp(windS, off, 1f - Mathf.Exp(-Time.deltaTime / WindTau));
            Angler.WindUp = windS;
            castArrow.Set(pulled, WindUpArmed, windMax / FlickCast.WindUpMin);
            DrawFan(pulled);
            if (!PointerInput.IsDown) Throw();
        }

        /// <summary>The ice: pulling down sets the jig's depth in the hole, letting go drops it (the drag, not a flick).</summary>
        void UpdateAimingIce()
        {
            var d = PointerInput.Position - pressPos;
            AimPower = Mathf.Clamp01(-d.y / (Screen.height * 0.3f));
            aimTarget = new Vector3(L.holeX, 0, L.holeZ);
            AimDepth = Mathf.Lerp(0.6f, L.DepthAt(L.holeZ) - 0.3f, AimPower);
            Angler.SetPose(AimPower > 0.04f ? "aim" : "idle");
            DrawAim(AimPower > 0.04f);
            if (!PointerInput.IsDown)
            {
                DrawAim(false);
                if (AimPower < 0.08f) SetState(S.Ready);
                else StartCoroutine(CastRoutine());
            }
        }

        /// <summary>
        /// The pointer was let go after a wind-up: reads the flick (<see cref="FlickCast"/>) and throws, its power setting
        /// the distance and its direction at the release the yaw; without an upward flick it does not cast.
        /// </summary>
        void Throw()
        {
            float H = Mathf.Max(1f, Screen.height);
            var units = FlickCast.UnitsNow(H);
            var r = FlickCast.Analyze(PointerInput.Samples, H, units);
            r.armed = WindUpArmed;
            r.windUp = windMax;
            r.releaseDrop = (pressPos.y - PointerInput.Position.y) / H;
            FlickCount++;
            if (!r.armed || !r.flick)
            {
                // brought back up to (about) the press point without ever flicking: the player called it off, as with the
                // old drag; only a release still down in the wind-up, or a flick that stopped / came too slowly, is a miss
                r.quiet = r.armed && !r.flick && r.releaseDrop <= FlickCast.WindUpMin && r.speedAny < units.min;
                LastFlick = r;
                string why = !r.armed ? (r.flick ? "no wind-up" : "tap") : r.quiet ? "called off" : r.why;
                Debug.Log("[Flick] cancel (" + why + ") " + FlickCast.Describe(r));
                if (r.armed && !r.quiet)
                {
                    hud.Flash("위로 튕기듯 올리며 놓아야 던져져요!", UIKit.Bad, 1.6f);
                    Sfx.Play(Sfx.Error, 0.3f);
                }
                else if (!r.armed && r.flick) hud.Flash("먼저 아래로 당겼다가 위로 튕겨요!", UIKit.Bad, 1.6f);
                Angler.SetPose("idle"); // straight back to the hold from wherever the rod is
                SetState(S.Ready);
                return;
            }
            var rod = Game.I.Rod;
            r.yaw = Mathf.Clamp(YawOnScreenLine(r.aim), -FlickCast.YawMax, FlickCast.YawMax);
            float yaw = r.yaw * Mathf.Deg2Rad;
            float dist = Mathf.Lerp(L.zNear + 2.5f, rod.castDist, r.power);
            var t = new Vector3(Angler.X + Mathf.Sin(yaw) * dist, 0, Mathf.Cos(yaw) * dist);
            t.z = Mathf.Clamp(t.z, L.zNear + 1.2f, L.zFar - 1f);
            t.x = Mathf.Clamp(t.x, -L.xLim + 0.6f, L.xLim - 0.6f);
            // (a weak cast into the water his own breakwater / bow hides goes on out to the first water he can see)
            t = Tackle.PastStand(t, new Vector3(Angler.X, 0f, 0f));
            aimTarget = t; // the angler turns towards it as the cast goes out
            AimPower = r.power;
            r.dist = dist;
            r.target = t;
            LastFlick = r;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[Flick] cast {0} target=({1:0.00},{2:0.00}) angler x={3:0.00}",
                FlickCast.Describe(r), t.x, t.z, Angler.X));
            hud.ShowCastPower(r.power);
            StartCoroutine(CastRoutine());
        }

        /// <summary>
        /// The world yaw (degrees, right +) whose throw lands on the screen line from the water under the angler's feet
        /// going <paramref name="screenDeg"/> from straight up the screen. The camera behind and above him makes a sideways
        /// throw look about twice as wide on screen as its yaw (and skews it once he has walked off-centre), so the flick's
        /// screen angle is carried through the camera: a line on the water stays a straight line on screen, so every cast
        /// distance along that yaw lies on the flicked line.
        /// </summary>
        float YawOnScreenLine(float screenDeg)
        {
            var a = new Vector3(Angler.X, 0f, 0f);
            Vector2 a2 = P.To2D(a);
            float rad = screenDeg * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            if (view != null)
            {
                // screen pixels -> scene units (the pixel view may scale x and y a little differently)
                var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                d = view.ScreenToWorld(c + d * 100f) - view.ScreenToWorld(c);
            }
            if (d.y <= 1e-5f) return screenDeg >= 0f ? 90f : -90f;
            // any point of that line between the feet and the horizon will do; halfway up
            var b = a2 + d * (0.5f * (P.HorizonY - a2.y) / d.y);
            if (!P.ToPlane(b, 0f, out var hit)) return 0f;
            return Mathf.Atan2(hit.x - a.x, hit.z - a.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The flick (degrees from straight up on screen) whose throw goes at this world yaw from where he stands now: the
        /// inverse of <see cref="YawOnScreenLine"/> (for the test autopilot).
        /// </summary>
        public float FlickAngleFor(float yawDeg)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            var a = new Vector3(Angler.X, 0f, 0f);
            var b = a + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * 10f;
            Vector2 d = P.To2D(b) - P.To2D(a);
            if (view != null) d = view.WorldToScreen(P.To2D(b)) - view.WorldToScreen(P.To2D(a));
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The wind-up guide: where and how far is only known when the flick is let go, so instead of an aim point a faint
        /// dotted fan shows the throwable range (the edge rays at +-<see cref="FlickCast.YawMax"/> from the shortest to the
        /// rod's longest cast, and the far arc), a little brighter once the wind-up is armed, with a soft shimmer running
        /// outwards (kept faint: the arrow over his head, <see cref="CastArrow"/>, is the one bright cue).
        /// </summary>
        void DrawFan(bool show)
        {
            // (the underwater obstacles' faint outlines come and go with the fan: Docs/obstacles_spec.md 9)
            if (overlay != null) overlay.Aim(show && !L.IsIce, WindUpArmed, Angler.X, Game.I.Rod.castDist);
            foreach (var f in fan) f.enabled = show;
            if (!show) return;
            float near = L.zNear + 2.5f, far = Mathf.Max(near + 1f, Game.I.Rod.castDist);
            float baseA = WindUpArmed ? 0.32f : 0.22f;
            int k = 0;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < FanRay; i++)
                {
                    float u = i / (FanRay - 1f);
                    float wave = WindUpArmed ? Mathf.Max(0f, Mathf.Sin(Time.time * 5f - u * 3.5f)) : 0f;
                    PlaceFanDot(fan[k++], side * FlickCast.YawMax, Mathf.Lerp(near, far, u), baseA + 0.2f * wave);
                }
            for (int i = 0; i < FanArc; i++)
            {
                float u = i / (FanArc - 1f);
                float wave = WindUpArmed ? 0.14f * Mathf.Max(0f, Mathf.Sin(Time.time * 5f - 3.5f - Mathf.Abs(u - 0.5f) * 2f)) : 0f;
                PlaceFanDot(fan[k++], Mathf.Lerp(-FlickCast.YawMax, FlickCast.YawMax, u), far, baseA + wave);
            }
        }

        void PlaceFanDot(SpriteRenderer d, float yawDeg, float dist, float alpha)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            var p = new Vector3(Angler.X + Mathf.Sin(yaw) * dist, 0, Mathf.Cos(yaw) * dist);
            p.z = Mathf.Clamp(p.z, L.zNear + 1.2f, L.zFar - 1f);
            p.x = Mathf.Clamp(p.x, -L.xLim + 0.6f, L.xLim - 0.6f);
            d.transform.position = Snap(P.To2D(p));
            d.transform.localScale = P.ScaleAt(p) > 0.35f ? new Vector3(2, 2, 1) : Vector3.one;
            d.color = new Color(1f, 0.96f, 0.82f, Mathf.Clamp01(alpha));
        }

        void DrawAim(bool show)
        {
            targetRing.enabled = show;
            foreach (var dd in dots) dd.enabled = show;
            if (!show) return;
            var from = Angler.Feet + new Vector3(0.4f, 2.6f, 0.9f);
            float dist = Vector3.Distance(new Vector3(from.x, 0, from.z), aimTarget);
            float arc = 1.2f + dist * 0.28f;
            for (int i = 0; i < dots.Count; i++)
            {
                float t = (i + 1f) / (dots.Count + 1f);
                var p = Vector3.Lerp(from, aimTarget, t);
                p.y = Mathf.Lerp(from.y, 0, t) + arc * 4f * t * (1 - t);
                if (L.IsIce) p = Vector3.Lerp(from, aimTarget, t);
                dots[i].transform.position = Snap(P.To2D(p));
                dots[i].transform.localScale = P.ScaleAt(p) > 0.6f ? new Vector3(2, 2, 1) : Vector3.one;
                dots[i].color = new Color(1, 1, 1, 0.35f + 0.5f * t);
            }
            var tp = L.IsIce ? aimTarget + Vector3.down * AimDepth : aimTarget;
            targetRing.transform.position = Snap(P.To2D(P.Apparent(tp)));
            float ppm = P.PixelsPerMetre(aimTarget);
            float s = Mathf.Clamp(ppm * 1.4f / 64f, 0.12f, 0.6f);
            targetRing.transform.localScale = new Vector3(s, s * Mathf.Clamp(P.Foreshorten(aimTarget) * 1.6f + 0.2f, 0.3f, 1f), 1);
            targetRing.color = new Color(1f, 0.9f, 0.3f, 0.6f + 0.3f * Mathf.Sin(Time.time * 10f));
        }

        IEnumerator CastRoutine()
        {
            SetState(S.Casting);
            Angler.SetPose("cast");
            if (L.IsIce) Sfx.PlayVar(Sfx.Cast, 0.8f);
            else
            {
                // the harder the flick, the louder and sharper the swish; a strong one whooshes, a top one sparkles
                float p = AimPower;
                if (Sfx.CastSwing != null)   // (the recorded swing is a whoosh already: no synthesized one on top)
                    Sfx.Play(Sfx.CastSwing, Mathf.Lerp(0.5f, 1f, p), Mathf.Lerp(0.92f, 1.08f, p) + Random.Range(-0.03f, 0.03f));
                else
                {
                    Sfx.Play(Sfx.Cast, Mathf.Lerp(0.45f, 1f, p), Mathf.Lerp(0.88f, 1.14f, p) + Random.Range(-0.03f, 0.03f));
                    if (p >= 0.6f) Sfx.Play(Sfx.Whoosh, Mathf.Lerp(0.2f, 0.55f, (p - 0.6f) / 0.4f), Mathf.Lerp(0.95f, 1.2f, p));
                }
                if (p >= 0.95f) Fx.Burst(P.To2D(Angler.RodTip), UIKit.Gold, 8, 2f);
            }
            yield return new WaitForSeconds(0.08f);   // (the bait still dangling from the tip as the rod swings)
            Tackle.SetBait(Game.I.Bait);
            Tackle.Launch(Angler.RodTip, aimTarget, OnLanded);
            Angler.ShowDangle = false;
            Angler.LineTarget = Tackle.LineEnd;
            Angler.Slack01 = 0.1f;
        }

        /// <summary>
        /// The cast came down (Docs/obstacles_spec.md 4.5): perched on a prop or the bank (it waits for a 톡 or the reel), or
        /// into the water where it came down (a bounce drops it: half the splash), on a lily pad, under the branches.
        /// </summary>
        void OnLanded(Landing l)
        {
            if (L.IsIce) l.at = aimTarget;
            var at = l.at;
            Angler.SetPose("idle");
            LureIn.Reset();
            Slide.Reset();
            Rhythm.Reset(Tackle.Bait);
            if (l.Perched)
            {
                LastLanding = l;
                LandPerched(l);
                SetState(S.Waiting);
                Watch?.OnCast();   // (a new cast all the same: the legend's per-cast soak / one-encounter flag start over)
                return;
            }
            var pos2 = P.To2D(at);
            float ppm = P.PixelsPerMetre(at);
            float big = l.contacts > 0 ? 0.5f : 1f;   // (it dropped, it was not thrown)
            Fx.Splash(pos2, Mathf.Clamp(ppm / 25f, 0.35f, 1f) * big, Stage.WaterTint, l.contacts > 0 ? 4 : 8, P.DepthOf(at));
            Fx.Ripple(pos2, Mathf.Clamp(ppm * 1.5f / 64f, 0.15f, 0.8f) * big, P.Foreshorten(at) * 1.6f + 0.15f, new Color(1, 1, 1, 0.8f));
            // the float rig lands with the recorded drop; lures keep the synthesized plop
            if (Tackle.UsesFloat && Sfx.FloatLand != null) Sfx.PlayVar(Sfx.FloatLand, 0.8f * big, 0.06f);
            else Sfx.PlayVar(Sfx.Plop, 0.9f * big);
            Tackle.EnterWater(at);
            if (L.IsIce) Tackle.FloatDepth = AimDepth;
            SetState(S.Waiting);
            Watch?.OnCast();
            var bait = Tackle.Bait;
            // a stage legend's key lures count as liked (the legend comes to them through its encounter)
            if (!GameDatabase.FishOfStage(Stage.Def.id).Any(f => f.Appeal(bait) > 0 || (f.encounter != null && f.encounter.KeyWeight(bait.id) > 0)))
                hud.Flash("이곳 물고기는 이 미끼를 안 좋아하는 것 같아요...", UIKit.Bad, 2.5f);
            // the first rig in the water (not through the ice): once, how the rod sweep works (after the cast's readout)
            else if (!L.IsIce && !Game.Data.sweepHint)
            {
                Game.Data.sweepHint = true;
                Game.I.Save();
                Tween.After(1.2f, () =>
                {
                    if (this != null && State == S.Waiting) hud.Flash("좌우로 밀면 낚시대가 기울어요 — 감으면 그쪽으로 휘어 와요", UIKit.Sky, 3.2f);
                });
                sweepHintNow = true;
            }
            // the first sea visit, after the first cast: once, how the tide works (after the sweep hint if that came now)
            if (L.id == "sea" && !Game.Data.tideHint)
            {
                Game.Data.tideHint = true;
                Game.I.Save();
                Tween.After(sweepHintNow ? 4.6f : 1.2f, () =>
                {
                    if (this != null) hud.Flash("물때: 들물·날물엔 입질이 활발하고, 만조·간조엔 뜸해요", UIKit.Sky, 2.8f);
                });
            }
            sweepHintNow = false;
            LandedObstacles(l);
        }

        bool sweepHintNow;

        // ------------------------------------------------------------------ waiting
        void UpdateWaiting(float dt)
        {
            // perched on a prop / the bank: a 톡 or the reel drops it in
            if (Tackle.State == Tackle.Mode.Perched)
            {
                UpdatePerched(dt, false);
                return;
            }
            Angler.LineTarget = Tackle.LineEnd;
            Angler.Tension01 = 0;
            Angler.Slack01 = 0.45f;
            float revs = Mathf.Max(0, Gesture.FrameRevs);
            WindTackle(revs);
            if (State != S.Waiting) return;
            var bait = Tackle.Bait;
            // a mend: the rod swept against the line's belly flips it upstream (and drags nothing)
            TryMend();
            // a float rig at rest with the rod swept is dragged a little that way
            if (revs <= 0.0001f && !L.IsIce) Tackle.Drag(SweepSin, Anchor, dt);
            if (bait.isLure)
            {
                // the current along the line at the lure: wound against it, it works harder (Docs/time_currents_spec.md 9.4)
                float cAlong = CAlong();
                // a 톡 works the lure (hop / pull / dart) and jerks the rod up
                if (LureIn.FlickNow)
                {
                    Tackle.Twitch(LureIn.FlickStrength, ShorePoint, SweepSin, Mathf.Clamp(1f + cAlong, 0.5f, 1.5f));
                    StartRodJerk(LureIn.FlickStrength);
                }
                // the old 톡 (a quick upward swipe) does nothing now: remind the player the 톡 is pulled down
                else if (LureIn.UpRejectNow && Time.time - upHintT > 6f)
                {
                    upHintT = Time.time;
                    hud.Flash("톡은 아래로 당겨요!", UIKit.Sky, 1.4f);
                }
                Rhythm.Update(bait, LureIn, Tackle, dt, cAlong);
                Tackle.Hanging = Rhythm.Hanging;
                if (Rhythm.Feedback != null) hud.LureFeedback(Rhythm.Feedback, Rhythm.FeedbackGood);
            }
            else Tackle.Hanging = false;
            // the current: the rig drifts and the line bows (not through the ice)
            StepCurrent(dt, revs > 0.0001f);
            // a frog on a lily pad (dropped off its edge, struck through it); the obstacles' snags
            PadTick(dt);
            if (State == S.Waiting) SnagRolls(dt);
            if (State != S.Waiting) return;
            // a tap while a fish is only nibbling scares it away (float baits; the soft worm, which is nibbled too)
            if (LureIn.TapNow && (!bait.isLure || bait.needBottom))
            {
                foreach (var f in Spawner.Fish)
                {
                    if (f.State == FishAgent.St.Nibble)
                    {
                        f.Flee();
                        hud.Flash(bait.isLure ? "너무 일찍 챘어요! 쑥 끌고 갈 때 탭!" : "너무 일찍 챘어요! 찌가 쑥 들어갈 때 탭!", UIKit.Bad);
                        Sfx.Play(Sfx.Error, 0.5f);
                        break;
                    }
                }
            }
        }

        void WindTackle(float revs)
        {
            if (revs > 0.0001f)
            {
                float m = revs * Game.I.Reel.retrieve;
                if (L.IsIce) Tackle.Raise(m);
                else Tackle.Wind(m, ShorePoint, SweepSin);   // (with the rod swept the path bends to its side)
                ReelTicks(revs);
                Angler.SetPose(ReelPose(Gesture.TotalRevs * 4));
            }
            else if (Gesture.Speed < 0.1f) Angler.SetPose("idle");
            bool home = L.IsIce ? Tackle.Depth <= 0.05f : Tackle.Surface.z <= L.zNear + 0.3f;
            if (home && revs > 0.0001f) FinishRetrieve();
        }

        /// <summary>
        /// The reeling pose: the sprite angler alternates two handle frames with the phase; the 3D angler's crank turns
        /// continuously with <see cref="Angler.ReelRevs"/>, so it is one pose.
        /// </summary>
        string ReelPose(float phase) => Angler.Uses3D || ((int)phase) % 2 == 0 ? "reel" : "reel2";

        void ReelTicks(float revs)
        {
            tickAcc += Mathf.Abs(revs);
            if (tickAcc >= 0.25f)
            {
                tickAcc = 0;
                Sfx.ReelClick(0.35f);
            }
        }

        // ------------------------------------------------------------------ the moving water (Docs/time_currents_spec.md 9)
        /// <summary>
        /// The current on the rig this frame (waiting / retrieving, not on the ice): it drifts, its line bows (drawn by the
        /// angler), and the one-time drift / mend hints.
        /// </summary>
        void StepCurrent(float dt, bool winding)
        {
            if (L.IsIce || Tackle.State != Tackle.Mode.Water)
            {
                Angler.LineBow = Vector3.zero;
                return;
            }
            Tackle.StepCurrent(dt, Stage.Current, Anchor, ShorePoint, winding, Game.I.Rod.castDist + 6f);
            Angler.LineBow = Tackle.BowNormal * Tackle.Bow;
            if (State != S.Waiting) return;
            // the stream brought the float in: wind in and cast again (once ever)
            if (!Game.Data.driftHint && Stage.Current.K == CurrentField.Kind.Stream && Tackle.UsesFloat && Tackle.Surface.z <= L.zNear + 1.5f)
            {
                Game.Data.driftHint = true;
                Game.I.Save();
                hud.Flash("찌가 물살에 흘러왔어요 — 감아서 다시 던져요", UIKit.Sky, 2.8f);
            }
            // a big belly drags the float: how to mend it (once ever)
            if (!Game.Data.mendHint && Mathf.Abs(Tackle.Bow) >= 1.5f)
            {
                Game.Data.mendHint = true;
                Game.I.Save();
                hud.Flash("줄이 휘어 찌가 끌려가요 — 휜 반대쪽으로 밀어 줄을 고쳐요", UIKit.Sky, 2.8f);
            }
        }

        /// <summary>
        /// The current along the line at the lure (m/s): + when the water runs away from him past it (he winds against it),
        /// - when it runs towards him; the surface current x exp(-depth / 4), plus half the wind on the film.
        /// </summary>
        float CAlong()
        {
            var cf = Stage.Current;
            if (cf == null || L.IsIce || Tackle.State != Tackle.Mode.Water) return 0f;
            var hook = Tackle.HookPos;
            var outDir = new Vector2(hook.x - ShorePoint.x, hook.z - ShorePoint.z);
            if (outDir.sqrMagnitude < 1e-4f) return 0f;
            outDir.Normalize();
            bool film = !Tackle.UsesFloat && Tackle.Depth <= 0.12f;
            var w = cf.Water(hook.x, hook.z) * CurrentField.Kd(Tackle.Depth) + (film ? 0.5f * cf.Wind : Vector2.zero);
            return Vector2.Dot(w, outDir);
        }

        /// <summary>
        /// A mend (spec 9.2): the frame a sweep is committed (or A / D goes down) while a float rig (or a lure at rest) lies
        /// with a belly of at least 0.5 m, swept at least half way (keys: all the way) against the belly: the belly drops to
        /// 20 % over 0.25 s and the float barely moves (at most 0.15 m of this sweep's drag).
        /// </summary>
        void TryMend()
        {
            bool keyNow = Slide.KeyHeld && !keyWas;
            keyWas = Slide.KeyHeld;
            if (!Slide.SlideNow && !keyNow) return;
            if (L.IsIce || Tackle.State != Tackle.Mode.Water) return;
            if (!(Tackle.UsesFloat || LureIn.PauseT >= 0.3f)) return;
            float bow = Tackle.Bow;
            float v = keyNow ? Slide.Shown : Slide.Value;
            if (Mathf.Abs(bow) < 0.5f || Mathf.Abs(v) < 0.5f || Mathf.Sign(v) != -Mathf.Sign(bow)) return;
            Tackle.Mend(v > 0f ? 1 : -1);
            Mends++;
            hud.LureFeedback("멘딩!", UIKit.Sky, 0.8f);
            Sfx.Play(Sfx.Whoosh, 0.25f);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[CUR] mend bow {0:+0.00;-0.00} sweep {1:+0.00;-0.00}", bow, v));
        }

        /// <summary>
        /// The time of day, the tide and the spot on a fish's appetite (spec 9.5): sqrt(activity) x the sea's tide (0.6 +
        /// 0.75 s) x the stream's spot (a slack pocket 1.3, the fast lane 0.85) x a natural drift (a float riding the moving
        /// water with no drag, 1.15), within 0.25..2. 0 when the species is not about at all now.
        /// </summary>
        public float BiteMult(FishAgent f)
        {
            float a = TimeActivity.Now(f.Sp.id);
            if (a <= 0f) return 0f;
            float m = Mathf.Sqrt(a) * TideMult();
            var cf = Stage.Current;
            var hook = Tackle.HookPos;
            float spot = 1f;
            if (cf != null && cf.K == CurrentField.Kind.Stream)
            {
                float pocket = cf.Pocket(hook.x, hook.z);
                if (pocket <= 0.4f) spot = 1.3f;
                else if (cf.Lane(hook.x, hook.z) >= 0.8f && pocket >= 0.999f) spot = 0.85f;
            }

            if (Tackle.UsesFloat && cf != null && cf.Moving && Tackle.RelSpeed <= 0.08f) m *= 1.15f;
            // near structure (Docs/obstacles_spec.md 8; the stream's pocket and the structure are not stacked: the larger wins)
            m *= StructureBite(f, hook, spot);
            return Mathf.Clamp(m, 0.25f, 2f);
        }

        /// <summary>The sea's tide on bites: 0.6 at slack .. 1.35 at the peak of the stream; 1 elsewhere.</summary>
        public float TideMult() => L.id == "sea" ? 0.6f + 0.75f * Mathf.Clamp01(GameClock.Tide.S) : 1f;

        /// <summary>A lure's strike roll x clamp(sqrt(activity) x the tide, 0.5, 1.5).</summary>
        public float StrikeMult(FishAgent f) => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, TimeActivity.Now(f.Sp.id))) * TideMult(), 0.5f, 1.5f) * StrikeBonus();

        void UpdateRetrieving(float dt)
        {
            Angler.LineTarget = Tackle.LineEnd;
            if (Tackle.State == Tackle.Mode.Perched)
            {
                UpdatePerched(dt, true);
                return;
            }
            if (retrieveWait > 0f)
            {
                // the fish just came off: the float lies where it was for a moment first
                retrieveWait -= dt;
                Angler.Slack01 = 0.45f;
                Angler.SetPose("idle");
                StepCurrent(dt, false);
                return;
            }
            Angler.Slack01 = 0.2f;
            float m = Game.I.Reel.retrieve * 3.2f * dt;
            if (L.IsIce) Tackle.Raise(m * 1.5f);
            else Tackle.Wind(m * 2f, ShorePoint, SweepSin);
            Tackle.Hanging = false;
            StepCurrent(dt, true);
            SnagRolls(dt);
            if (State != S.Retrieving) return;
            Angler.SetPose(ReelPose(Time.time * 8));
            ReelTicks(m / Game.I.Reel.retrieve);
            autoRevs += m / Game.I.Reel.retrieve;
            bool home = L.IsIce ? Tackle.Depth <= 0.05f : Tackle.Surface.z <= L.zNear + 0.3f;
            if (home) FinishRetrieve();
        }

        void FinishRetrieve()
        {
            foreach (var f in Spawner.Fish) if (f.State == FishAgent.St.Approach || f.State == FishAgent.St.Nibble) f.LoseInterest();
            Tackle.Hide();
            retrieveWait = 0f;
            Angler.LineTarget = null;
            Slide.Reset();
            SetState(S.Ready);
        }

        // ------------------------------------------------------------------ legend encounter
        /// <summary>A message in the HUD's flash spot (the encounter's captions while one is on).</summary>
        public void Flash(string text, Color c, float time = 1.5f) => hud.Flash(text, c, time);

        /// <summary>The ordinary fish within <paramref name="radius"/> m of the lure scatter (the legend is near).</summary>
        public void ScatterFish(float radius)
        {
            var hook = Tackle.HookPos;
            foreach (var f in Spawner.Fish)
            {
                if (f.State == FishAgent.St.Hooked || f.State == FishAgent.St.Dead || f.State == FishAgent.St.Flee) continue;
                if (new Vector2(f.Pos.x - hook.x, f.Pos.z - hook.z).magnitude <= radius) f.Flee();
                else if (f.Engaged) f.LoseInterest();
            }
        }

        /// <summary>The line trembles where it cuts the surface (the legend's build-up).</summary>
        public void TrembleLine() => LineRing(Angler.LineUnderwater ? Angler.WaterEntry : Tackle.Surface, 0.45f);

        /// <summary>
        /// The stage legend comes for the lure (Docs/lures_legend_spec.md 2.3): the surface rig is held where it is, no new
        /// fish come and the ones about scatter; the underwater window opens over the dimmed surface.
        /// </summary>
        void StartEncounter()
        {
            var sp = Watch.Triggered ?? Watch.Legend;
            var rec = Game.I.Legend(sp.id);
            bool debug = LegendWatch.DebugMode != null;
            Encounter = new LegendEncounter(sp, debug ? 0 : rec.pity, rec.seen > 0, Game.I.Rod.hookBonus, Random.Range(0, 99999));
            encSp = sp;
            encCm = FishSpawner.RollSize(sp);
            Watch.OnEncounter();
            Slide.Reset();   // (the window has its own controls: the rod comes back to the centre)
            Tackle.Hold();
            Spawner.Paused = true;
            ScatterFish(12f);
            biteMark.enabled = false;
            Angler.LineTarget = Tackle.LineEnd;
            encView = EncounterView.Create(this, Encounter, Tackle.Bait, encCm);
            hud.BeginEncounter(Encounter, encView);
            Encounter.PhaseChanged += OnEncounterPhase;
            Encounter.HookSet += perfect => Sfx.Play(Sfx.Hook, 1f);
            // the fake-out tugs at the line: the tip dips, the line trembles (no gold, no "ting")
            Encounter.FakeOut += () =>
            {
                encDipT = 0.3f;
                Tackle.Nibble();
                TrembleLine();
                Sfx.PlayVar(Sfx.Nibble, 0.7f);
            };
            Encounter.Penalty += (k, text) => Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[ENC] penalty {0} \"{1}\" gauge={2:0} mood={3} tease={4:0.0}", k, text, Encounter.Gauge, LegendEncounter.MoodName(Encounter.Mood), Encounter.TeaseT));
            SetState(S.Encounter);
            hud.Flash("…!", UIKit.Gold, 0.8f);
            Sfx.Play(Sfx.Drone, 0.6f);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[ENC] start {0} {1:0}cm lure {2} depth {3:0.00} z {4:0.0} q {5:0.00} gauge {6:0} (pity {7}, seen {8}, debug {9})",
                sp.id, encCm, Tackle.Bait.id, Tackle.Depth, Tackle.Surface.z, Rhythm.Q, Encounter.Gauge, rec.pity, rec.seen, LegendWatch.DebugMode ?? "off"));
        }

        void OnEncounterPhase(LegendEncounter.Phase p)
        {
            var e = Encounter;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ENC] phase={0} gauge={1:0} mood={2} tease={3:0.0}",
                p, e.Gauge, LegendEncounter.MoodName(e.Mood), e.TeaseT));
            switch (p)
            {
                case LegendEncounter.Phase.Approach:
                {
                    // seen: the first time ever is a find worth XP and a note in the collection
                    var rec = Game.I.Legend(e.Sp.id);
                    firstSeen = rec.seen == 0;
                    rec.seen++;
                    Game.I.Save();
                    break;
                }
                case LegendEncounter.Phase.Surface:
                {
                    // back up to the surface: the legend is on the line where the lure was, a boil on the water
                    var fish = Spawner.SpawnHooked(encSp, e.PerfectLure ? Mathf.Max(encCm, FishSpawner.RollSize(encSp)) : encCm, Tackle.HookPos);
                    Hooked = fish;
                    Angler.Fish = fish;
                    Angler.LineTarget = fish.MouthPos;
                    // (a topwater legend struck at the surface: a bigger boil)
                    JumpSplash(Tackle.Surface, encView != null && encView.TopFlow ? e.Def.top.surfaceBoil : 1.2f);
                    view.Shake(0.2f, 0.3f);
                    Sfx.PlayVar(Sfx.Splash, 1f);
                    break;
                }
            }
        }

        void UpdateEncounter(float dt)
        {
            var e = Encounter;
            Angler.Tension01 = e.Ph >= LegendEncounter.Phase.HookWindow && e.Ph <= LegendEncounter.Phase.Surface ? 0.6f : encDipT > 0f ? 0.35f : 0.05f;
            encDipT -= dt;
            Angler.Slack01 = e.Ph >= LegendEncounter.Phase.Hooked ? 0.05f : 0.3f;
            Angler.LineTarget = Hooked != null ? Hooked.MouthPos : Tackle.LineEnd;
            // the surface echoes every input: the crank turns with the circles, the rod jerks up on a 톡 (no body motion)
            if (Gesture.FrameRevs > 0.0005f)
            {
                Angler.SetPose(ReelPose(Gesture.TotalRevs * 4));
                ReelTicks(Gesture.FrameRevs);
            }
            else if (Gesture.Speed < 0.1f) Angler.SetPose(e.Ph >= LegendEncounter.Phase.Hooked ? "fight" : "idle");
            if (LureIn.FlickNow) StartRodJerk(LureIn.FlickStrength);
            e.Tick(dt, new LegendEncounter.Input
            {
                flick = LureIn.FlickNow, strength = LureIn.FlickStrength, winding = LureIn.Winding, speed = LureIn.Speed,
                windRunT = LureIn.WindRunT, pauseT = LureIn.PauseT, press = PointerInput.WorldPressed,
            });
            if (e.Ph != LegendEncounter.Phase.Done) return;
            if (e.Success) EncounterHooked();
            else EncounterFailed();
        }

        void EndEncounter()
        {
            hud.EndEncounter();
            if (encView != null) Destroy(encView.gameObject);
            encView = null;
            Spawner.Paused = false;
            if (firstSeen)
            {
                // (after the window has gone: a toast at the top would sit over it)
                firstSeen = false;
                Game.I.AddXp(100);
                // after the flash that follows the encounter (same spot)
                Tween.After(1.8f, () => Toast.Show("첫 조우! · 도감에 목격 기록 +100 XP", UIKit.Gold, 2.6f));
            }
        }

        /// <summary>The legend is on: the normal fight from here (a perfect hook set starts it at 85 % stamina).</summary>
        void EncounterHooked()
        {
            var e = Encounter;
            EndEncounter();
            Watch.Cool(e.Sp, e.Def.coolFight);
            // a natural bait (the golden carp's 황금 떡밥 / 옥수수) is eaten, as at an ordinary bite's hook set; a lure stays on
            if (Tackle.UsesFloat) Game.I.ConsumeBait();
            var fish = Hooked;
            Hooked = null;
            if (fish == null) fish = Spawner.SpawnHooked(encSp, encCm, Tackle.HookPos);
            BeginFight(fish, e.Perfect);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ENC] hooked {0} {1:0.0}cm perfect={2} perfectLure={3} penalties={4} -> fight",
                fish.Sp.id, fish.Cm, e.Perfect, e.PerfectLure, e.Penalties));
            hud.Flash(EncounterDef.Named(e.Def.hookedText, fish.Sp.name), UIKit.Gold, 1.6f);
        }

        /// <summary>It turned away: the lure is back in the water where it was, nothing lost; a pity bonus for next time.</summary>
        void EncounterFailed()
        {
            var e = Encounter;
            EndEncounter();
            var rec = Game.I.Legend(e.Sp.id);
            rec.fails++;
            if (LegendWatch.DebugMode == null) rec.pity = Mathf.Min(e.Def.pityMax, rec.pity + e.Def.pityStep);
            Game.I.Save();
            Watch.Cool(e.Sp, e.Def.coolFail);
            Tackle.Resume();
            LureIn.Reset();
            Slide.Reset();
            Angler.SetPose("idle");
            Debug.Log($"[ENC] failed ({e.FailReason}) -> Waiting, lure kept ({Tackle.Bait.id} {Tackle.State}), pity {rec.pity}");
            SetState(S.Waiting);
        }

        /// <summary>
        /// Test hook (-fkauto steer): a <paramref name="cm"/> cm fish of this species takes the rig (moved to
        /// <paramref name="at"/> on the water first) and the fight starts at once with this seed, so the side-pressure
        /// comparison can fight the same fish again. False unless a rig is waiting in the water.
        /// </summary>
        internal bool DebugHook(FishSpecies sp, float cm, int seed, Vector3 at)
        {
            if (State != S.Waiting || Tackle.State != Tackle.Mode.Water || sp == null) return false;
            Tackle.Surface = new Vector3(at.x, 0f, at.z);
            Tackle.Depth = Mathf.Clamp(-at.y, 0.3f, Mathf.Max(0.3f, Tackle.Bottom));
            var fish = Spawner.SpawnHooked(sp, cm, Tackle.HookPos);
            BeginFight(fish, false, seed);
            return true;
        }

        /// <summary>
        /// Test hook (-fkauto current): from the ready, the equipped bait lands at <paramref name="at"/> on the water as a
        /// cast would put it there (the landing's splash, then waiting). False unless ready.
        /// </summary>
        internal bool DebugPlaceRig(Vector3 at)
        {
            if (State != S.Ready) return false;
            aimTarget = new Vector3(Mathf.Clamp(at.x, -L.xLim + 0.6f, L.xLim - 0.6f), 0f, Mathf.Clamp(at.z, L.zNear + 1.2f, L.zFar - 1f));
            Tackle.SetBait(Game.I.Bait);
            Angler.ShowDangle = false;
            // (on the water where asked: a lily pad there counts, the bank check does not)
            OnLanded(Tackle.ResolveLanding(aimTarget, false));
            return true;
        }

        /// <summary>
        /// Test hook (-fkauto occlusion): while set, the hooked fish is held at this point (y &lt; 0: under the water; a jump
        /// still lifts it) instead of on the line's length, e.g. under the pier at the end of a fight. Cleared when the fight ends.
        /// </summary>
        internal Vector3? DebugFishHold;

        /// <summary>Test hook (-fkauto occlusion): the hooked fish jumps now (a hop of <paramref name="dur"/> s, the fight model aside).</summary>
        internal bool DebugJump(float dur = 0.8f)
        {
            if (State != S.Fighting || Hooked == null || jumpTime >= 0) return false;
            jumpTime = 0;
            jumpKind = FightModel.JumpKind.Hop;
            jumpDur = Mathf.Max(0.2f, dur);
            jumpShakes = 0;
            jumpFlip = 0;
            jumpRight = Mathf.Cos(Hooked.Heading) >= 0;
            JumpSplash(Hooked.Pos, 0.8f);
            return true;
        }

        /// <summary>Test hook (-fkauto occlusion): the hooked fish is landed now, from where it is.</summary>
        internal bool DebugLand()
        {
            if (State != S.Fighting || Hooked == null) return false;
            jumpTime = -1;
            Hooked.JumpT = -1;
            DebugFishHold = null;
            StartCoroutine(LandRoutine());
            return true;
        }

        /// <summary>Test hook (-fkauto steer): the hooked fish gets off (ends a comparison fight early).</summary>
        internal void DebugRelease()
        {
            if (State == S.Fighting && Hooked != null) FishEscaped();
        }

        // ------------------------------------------------------------------ fish interest
        public bool CanFishEngage(FishAgent f)
        {
            if (State != S.Waiting && State != S.Biting) return false;
            if (NoBites) return false;
            // the legend is close: the ordinary fish keep away
            if (Watch != null && Watch.Meter >= 0.5f) return false;
            foreach (var o in Spawner.Fish) if (o != f && o.Engaged) return false;
            return true;
        }

        /// <summary>
        /// Whether a wandering fish comes over to the bait (rolled every ~1 s per fish). A lure is sensed from 5 + 4 Q m and
        /// draws 0.15 + 1.25 Q as much (Q = how well it is worked, <see cref="Rhythm"/>); a float bait from 5 m, drawing less
        /// while the rig moves. A glowing bait or lure is seen 2 m further in the cave and under the ice.
        /// </summary>
        public bool WantsToApproach(FishAgent f)
        {
            if (State != S.Waiting || Tackle.State != Tackle.Mode.Water) return false;
            if (!CanFishEngage(f)) return false;
            if (f.Sp.encounter != null) return false; // met through its encounter only
            var hook = Tackle.HookPos;
            float dist = new Vector2(hook.x - f.Pos.x, hook.z - f.Pos.z).magnitude;
            var bait = Tackle.Bait;
            float q = Rhythm.Q;
            float sense = bait.isLure ? 5f + 4f * q : 5f;
            if (bait.glow && (Stage.Def.id == "cave" || L.IsIce)) sense += 2f;
            if (dist > sense) return false;
            if (Mathf.Abs(f.Depth - Tackle.Depth) > 2.5f) return false;
            float appeal = f.Sp.Appeal(bait);
            if (appeal <= 0) return false;
            // (a float drifting with the water is not "moving": its speed through the water counts)
            float activity = bait.isLure ? 0.15f + 1.25f * q : Tackle.RelSpeed < 0.4f ? 1f : 0.35f;
            // the time of day, the tide and the spot (Docs/time_currents_spec.md 9.5)
            float m = BiteMult(f);
            if (m <= 0f) return false;
            float p = Mathf.Min(0.95f, appeal * Game.I.Line.stealth * Stage.Def.biteMult * activity * 0.45f * m);
            ApproachRolls++;
            bool yes = Random.value < p;
            if (yes && bait.isLure) LureFollows++;
            if (yes) Approaches++;
            return yes;
        }

        /// <summary>A fish following the lure gave up on it (the lure was worked badly for 2 s).</summary>
        public void OnFollowerTurned(FishAgent f)
        {
            LureTurned++;
            if (Time.time - followFlashT < 6f || State != S.Waiting) return;
            followFlashT = Time.time;
            hud.Flash("따라오다 돌아섰어요", UIKit.Bad, 1.2f);
        }

        /// <summary>A fish right behind the lure rolled for a strike (logged for the lure test).</summary>
        public void OnStrikeRoll(FishAgent f, float chance)
        {
            LureStrikeRolls++;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[LURE] strike roll {0} chance {1:0.00} q {2:0.00}", f.Sp.id, chance, Rhythm.Q));
        }

        public void OnNibble(FishAgent f)
        {
            Tackle.Nibble();
            Sfx.PlayVar(Sfx.Nibble, 0.6f);
            var p2 = P.To2D(Tackle.Surface);
            Fx.Ripple(p2, Mathf.Clamp(P.PixelsPerMetre(Tackle.Surface) / 64f, 0.1f, 0.5f), P.Foreshorten(Tackle.Surface) * 1.6f + 0.15f,
                new Color(1, 1, 1, 0.6f), 0.6f);
            hud.Flash("톡톡... 입질이다! 기다려요", UIKit.Sky, 0.9f);
        }

        public void OnBite(FishAgent f)
        {
            if (Tackle.Bait.isLure)
            {
                LureBites++;
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[LURE] bite {0} lure {1} q {2:0.00} depth {3:0.00}",
                    f.Sp.id, Tackle.Bait.id, Rhythm.Q, Tackle.Depth));
            }
            biter = f;
            Tackle.ClearPad();
            biteWindow = 0.75f + Game.I.Rod.hookBonus + (Tackle.UsesFloat ? 0.1f : 0f);
            // a bowed line in the current is slow to set the hook (Docs/time_currents_spec.md 9.2)
            float bowLate = 0.12f * Mathf.Max(0f, Mathf.Abs(Tackle.Bow) - 1f);
            if (bowLate > 0f) biteWindow = Mathf.Max(Mathf.Min(biteWindow, 0.45f), biteWindow - bowLate);
            Tackle.BiteDown();
            Sfx.Play(Sfx.Bite, 0.9f);
            var p2 = P.To2D(Tackle.Surface);
            Fx.Ripple(p2, Mathf.Clamp(P.PixelsPerMetre(Tackle.Surface) * 2f / 64f, 0.15f, 0.9f), P.Foreshorten(Tackle.Surface) * 1.6f + 0.15f,
                Color.white, 0.8f);
            Fx.Splash(p2, Mathf.Clamp(0.7f * P.ScaleAt(Tackle.Surface), 0.2f, 0.6f), Stage.WaterTint, 5, P.DepthOf(Tackle.Surface));
            biteMark.enabled = true;
            PlaceBiteMark(); // right away, not only from the next frame's update (it would flash at the origin)
            hud.Flash("쑥! 지금 탭해서 챔질!", UIKit.Gold, 0.9f);
            SetState(S.Biting);
        }

        void UpdateBiting(float dt)
        {
            Angler.LineTarget = Tackle.LineEnd;
            Angler.Slack01 = 0.15f;
            Angler.Tension01 = 0.25f;
            biteWindow -= dt;
            // the fish tugging: rings around the float / line
            lineRingT -= dt;
            if (lineRingT <= 0)
            {
                lineRingT = 0.35f;
                LineRing(Angler.LineUnderwater ? Angler.WaterEntry : Tackle.Surface, 0.5f);
            }
            PlaceBiteMark();
            if (PointerInput.WorldPressed || Gesture.Circling)
            {
                SetHook();
                return;
            }
            if (biteWindow <= 0 || biter == null)
            {
                biteMark.enabled = false;
                Tackle.ResetDip();
                if (biter != null) biter.Flee();
                biter = null;
                if (Tackle.UsesFloat)
                {
                    Game.I.ConsumeBait();
                    hud.Flash("미끼만 떼먹고 도망갔어요...", UIKit.Bad);
                    SetState(S.Retrieving);
                }
                else
                {
                    hud.Flash("놓쳤다! 계속 움직여서 유혹해요", UIKit.Bad);
                    SetState(S.Waiting);
                }
            }
        }

        // ------------------------------------------------------------------ fight
        void SetHook()
        {
            biteMark.enabled = false;
            Sfx.Play(Sfx.Hook, 1f);
            Sfx.Thrash(0.7f, 1.2f);   // the fish splashes as the hook goes home
            view.Shake(0.12f, 0.15f);
            var f = biter;
            biter = null;
            f.SetHooked();
            if (Tackle.UsesFloat) Game.I.ConsumeBait();
            BeginFight(f, false);
            hud.Flash("걸었다! 원을 그려 릴을 감아요!", UIKit.Gold, 1.2f);
        }

        /// <summary>
        /// The fight with a hooked fish (a bite's hook set, or a legend hooked out of its encounter): the fight model from
        /// the tackle and the line out to the fish. A perfect hook set on a legend starts it at 85 % stamina, and the fight
        /// holds still for 0.5 s so a finger can get ready to circle.
        /// </summary>
        void BeginFight(FishAgent fish, bool perfect, int seed = -1)
        {
            Hooked = fish;
            Angler.Fish = Hooked;
            Tackle.Hold();
            Tackle.ResetDip();
            Tackle.FollowFight(Angler);   // (a float rig: the float stays on the line to the fish)
            Angler.LineTarget = Hooked.MouthPos;
            // the fight starts with the rod centred (a sweep while waiting is not carried into it)
            Slide.Reset();
            FishRun = 0;
            SideNow = 0f;
            RunT = 0f;
            turnProg = 0f;
            runTurned = false;
            RunAlign = 0f;
            DownstreamRun = false;
            OutOfFlowT = 0f;
            alignTold = false;
            lastPhase = FightModel.Phase.Run;   // (a new FightModel starts running)
            var tip = Angler.RodTipPlan;        // (the line is measured from the tip as the rod is held, as in UpdateFighting)
            var landing = L.IsIce ? new Vector3(L.holeX, 0, L.holeZ) : new Vector3(Angler.X, 0, L.zNear + 0.4f);
            float land = Vector3.Distance(tip, landing) + 0.3f;
            if (seed < 0) seed = Random.Range(0, 99999);
            Fight = new FightModel(Hooked.Sp, Hooked.Cm, Game.I.Rod, Game.I.Reel, Game.I.Line, Stage.Def.powerMult,
                Vector3.Distance(tip, Hooked.Pos), land, seed);
            runRnd = new System.Random(seed + 1);   // which way each run heads (the same fish fought again runs the same ways)
            Fight.PhaseChanged += OnPhase;
            var rel = Hooked.Pos - Anchor;
            fightYaw = fightYawTarget = Mathf.Atan2(rel.x, Mathf.Max(0.1f, rel.z));
            fishDepthTarget = Hooked.Depth;
            lastFishPos = Hooked.Pos;
            jumpTime = -1;
            if (fish.Sp.encounter != null)
            {
                if (perfect) Fight.Stamina = 0.85f;
                Fight.Hold(0.5f);
            }
            BeginFightObstacles();
            SetState(S.Fighting);
            // the bolt at the hook set is the first run: a cover seeker may head straight for its cover
            TryCoverRun();
        }

        /// <summary>How far round (radians either way) the hooked fish may swim from straight out in front of him.</summary>
        float MaxFightYaw()
        {
            float h = new Vector2(Hooked.Pos.x - Anchor.x, Hooked.Pos.z - Anchor.z).magnitude;
            float maxYaw = h > 0.5f ? Mathf.Asin(Mathf.Clamp01((L.xLim - 0.5f) / h)) : 0.8f;
            return Mathf.Min(maxYaw, L.IsIce ? Mathf.PI : 0.8f);
        }

        /// <summary>The current run (if any) is over: reported to the tests, the side-pressure strip goes back to no run.</summary>
        void EndRun()
        {
            DownstreamRun = false;
            alignTold = false;
            if (Fight != null) Fight.Downstream = false;
            if (FishRun == 0) return;
            RunEnded?.Invoke(FishRun, RunT, runTurned);
            FishRun = 0;
            SideNow = 0f;
            SideActive = false;
            turnProg = 0f;
            runTurned = false;
        }

        void OnPhase(FightModel.Phase ph)
        {
            var sp = Hooked.Sp;
            float maxYaw = MaxFightYaw();
            // a burst always runs on into a run (FightModel.NextPhase): the same run, the same way, with its side pressure
            // (and turn) carried on, not a new one rolled (a lean that was right for the burst stays right)
            bool carryOn = ph == FightModel.Phase.Run && lastPhase == FightModel.Phase.Burst && FishRun != 0;
            lastPhase = ph;
            if (carryOn)
            {
                if (runTurned) Fight.CutRun();   // (turned in the burst: this part is cut short too)
                return;
            }
            EndRun();
            if (Fight.CoverRun || Fight.CoverHold) Fight.EndCover();
            coverTarget = null;
            switch (ph)
            {
                case FightModel.Phase.Run:
                case FightModel.Phase.Burst:
                    if (L.IsIce) fightYawTarget = Mathf.Clamp(fightYaw + Random.Range(-0.7f, 0.7f), -maxYaw, maxYaw);
                    else
                    {
                        // every run heads off to one side (the fight strip shows which, for side pressure): 0.3-0.7 rad from
                        // where it is, the other way if that side has no room
                        float step = (0.3f + 0.4f * (float)runRnd.NextDouble()) * (runRnd.NextDouble() < 0.5 ? -1f : 1f);
                        if (Mathf.Abs(Mathf.Clamp(fightYaw + step, -maxYaw, maxYaw) - fightYaw) < 0.2f) step = -step;
                        fightYawTarget = Mathf.Clamp(fightYaw + step, -maxYaw, maxYaw);
                        // the stream: a third of the runs turn and ride the lane down past his side (to the side with more room)
                        if (ph == FightModel.Phase.Run && Stage.Current.K == CurrentField.Kind.Stream && runRnd.NextDouble() < 0.35)
                        {
                            DownstreamRun = true;
                            DownstreamRuns++;
                            Fight.Downstream = true;
                            fightYawTarget = fightYaw > 0f ? -maxYaw : maxYaw;
                            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[CUR] downstream run {0} yaw {1:+0.00;-0.00} -> {2:+0.00;-0.00}",
                                fightYawTarget > fightYaw ? "R" : "L", fightYaw, fightYawTarget));
                        }
                        FishRun = fightYawTarget > fightYaw + 0.01f ? 1 : fightYawTarget < fightYaw - 0.01f ? -1 : 0;
                        RunT = 0f;
                        turnProg = 0f;
                        runTurned = false;
                    }
                    float bottom = L.DepthAt(Hooked.Pos.z) - 0.3f;
                    fishDepthTarget = Random.Range(Mathf.Min(sp.depthMin, bottom), Mathf.Min(sp.depthMax, bottom));
                    if (DownstreamRun) fishDepthTarget = Mathf.Min(1f, sp.depthMin);   // (up in the fast water)
                    if (ph == FightModel.Phase.Burst) hud.Flash("질주한다! 감지 말고 버텨요!", UIKit.Bad, 0.9f);
                    // a cover seeker may head for its cover instead (Docs/obstacles_spec.md 7.2)
                    TryCoverRun();
                    break;
                case FightModel.Phase.Jump:
                    jumpTime = 0;
                    jumpKind = Fight.Jump;
                    jumpDur = Fight.JumpDuration;
                    jumpShakes = Fight.ShakeAt.Length;
                    jumpFlip = 0;
                    jumpRight = Mathf.Cos(Hooked.Heading) >= 0;
                    if (jumpKind == FightModel.JumpKind.TailWalk)
                    {
                        // skips sideways, towards the side with more room
                        float dir = fightYaw > 0 ? -1f : 1f;
                        fightYawTarget = Mathf.Clamp(fightYaw + dir * 0.45f, -maxYaw, maxYaw);
                        jumpRight = dir > 0;
                    }
                    JumpSplash(Hooked.Pos, 0.8f);
                    Sfx.Play(Sfx.Jump, 0.8f);
                    hud.Flash(jumpKind switch
                    {
                        FightModel.JumpKind.Shake => "공중에서 몸부림친다! 머리 털 때 릴 멈춰요!",
                        FightModel.JumpKind.TailWalk => "꼬리로 수면을 달린다! 머리 털 때 조심!",
                        _ => "점프! 릴을 잠깐 멈춰요!",
                    }, UIKit.Gold, 1f);
                    break;
                default:
                    fightYawTarget = Mathf.Clamp(fightYaw + Random.Range(-0.15f, 0.15f), -maxYaw, maxYaw);
                    fishDepthTarget = Mathf.Max(0.4f, Hooked.Depth - 0.8f);
                    break;
            }
        }

        /// <summary>
        /// Side pressure: while the fish runs off to one side (<see cref="FishRun"/>), the rod leant the other way pulls its
        /// head round: its heading swings back towards the rod's side (<see cref="TurnRate"/>) and after
        /// <see cref="TurnTime"/> s of full lean the run is turned (its head comes round and it runs on that way, the rest of
        /// the run cut short: FightModel.Turn); meanwhile it tires x1.4 and the line takes +10 % (FightModel.SideGood). Leant
        /// with the run it runs on further and longer, tires x0.8, and the line still takes +10 % (SideBad). Centred: as
        /// before. Not on the ice, not in a jump; legend fights too.
        /// </summary>
        void SidePressure(float dt, FightModel f)
        {
            bool running = FishRun != 0 && (f.State == FightModel.Phase.Run || f.State == FightModel.Phase.Burst) && !f.Exhausted && jumpTime < 0f && !L.IsIce;
            SideActive = running;
            f.SideGood = f.SideBad = 0f;
            if (!running)
            {
                if (FishRun != 0 && f.Exhausted) EndRun();
                SideNow = 0f;
                return;
            }
            RunT += dt;
            float lean = Lean;
            if (Mathf.Abs(lean) < SideDead) lean = 0f;
            float good = Mathf.Clamp01(-lean * FishRun), bad = Mathf.Clamp01(lean * FishRun);
            SideNow = good - bad;
            float maxYaw = MaxFightYaw();
            // (dug in at its cover it does not give ground until it is pulled out)
            bool cover = f.CoverRun || f.CoverHold;
            if (good > 0f && !f.CoverHold) fightYawTarget = Mathf.MoveTowards(fightYawTarget, -FishRun * maxYaw, dt * TurnRate * good);
            else if (bad > 0f && !f.CoverHold) fightYawTarget = Mathf.MoveTowards(fightYawTarget, FishRun * maxYaw, dt * PushRate * bad);
            turnProg += good * dt;
            // a run for cover takes longer to turn (x dig): turned, the fish is pulled out of its cover
            if (!runTurned && turnProg >= TurnTime * (cover ? Hooked.Sp.coverDig : 1f))
            {
                runTurned = true;
                LastTurnT = RunT;
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[SIDE] run {0} turned after {1:0.00}s (lean {2:+0.00;-0.00}){3}",
                    FishRun > 0 ? "R" : "L", RunT, Lean, cover ? " out of its cover" : ""));
                f.Turn();   // the rest of the run is cut short (it runs on)
                if (cover)
                {
                    PullOut("side");
                    f.SideGood = good;
                    f.SideBad = bad;
                    return;
                }
                // its head comes round towards the rod's side: it runs on that way (the lean keeps pulling it round)
                float back = FishRun > 0 ? Mathf.Min(fightYawTarget, fightYaw) : Mathf.Max(fightYawTarget, fightYaw);
                fightYawTarget = Mathf.Clamp(back - FishRun * 0.2f, -maxYaw, maxYaw);
                // a run with the current turned: the fish is pulled out of the flow (4 s without the current's help)
                if (RunAlign >= 0.3f || DownstreamRun)
                {
                    OutOfFlowT = 4f;
                    FlowTurns++;
                    DownstreamRun = false;
                    f.Downstream = false;
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[CUR] pulled out of the flow (al {0:+0.00;-0.00}, load {1:0.00})", RunAlign, f.CurrentLoad));
                    hud.Flash("물살에서 빼냈다!", UIKit.Gold, 0.9f);
                }
                else hud.Flash("방향을 꺾었다!", UIKit.Gold, 0.8f);
                Sfx.Play(Sfx.Success, 0.5f);
            }
            f.SideGood = good;
            f.SideBad = bad;
            // the first long run fought without side pressure: once, how to turn it
            if (good < 0.3f && !runTurned && RunT >= LongRun && !Game.Data.sideHint)
            {
                Game.Data.sideHint = true;
                Game.I.Save();
                hud.Flash("물고기가 달리는 반대쪽으로 밀어 방향을 꺾어요!", UIKit.Sky, 2.6f);
            }
        }

        /// <summary>
        /// The current in the fight (Docs/time_currents_spec.md 9.7), each step from where the hooked fish is: the run's
        /// alignment with the current (+1 with it; a downstream run is +1; cut by side pressure against it; 0 for 4 s after
        /// side pressure pulled it out of the flow), the load of the fish holding in the flow (0.25 x its power x the
        /// current / the stage's reference speed; x 0.5 worn out, x 1.5 on a downstream run, x 0.3 pulled out) and the drag
        /// of the current across the line (0.02 x line x c_perp^2); a resting fish swings down-current on the line.
        /// </summary>
        void CurrentFight(float dt, FightModel f)
        {
            var cf = Stage.Current;
            if (cf == null || !cf.Moving || L.IsIce)
            {
                f.CurrentAlign = f.CurrentLoad = f.LineDrag = f.CurrentSpeed = 0f;
                RunAlign = 0f;
                return;
            }
            OutOfFlowT = Mathf.Max(0f, OutOfFlowT - dt);
            var pos = Hooked.Pos;
            var cF = cf.Water(pos.x, pos.z);
            var rr = new Vector2(pos.x - Anchor.x, pos.z - Anchor.z);
            rr = rr.sqrMagnitude > 1e-4f ? rr.normalized : Vector2.up;
            var side = new Vector2(rr.y, -rr.x);   // to his right of the line out to the fish
            bool running = (f.State == FightModel.Phase.Run || f.State == FightModel.Phase.Burst) && !f.Exhausted && jumpTime < 0f;
            float al = 0f;
            if (running)
            {
                var t = FishRun != 0 ? side * FishRun : rr;
                al = DownstreamRun ? 1f : Mathf.Clamp(Vector2.Dot(cF, t) / cf.Ref, -1f, 1f);
                if (al > 0f) al *= 1f - 0.8f * Mathf.Clamp01(f.SideGood);   // side pressure against a with-current run
                if (OutOfFlowT > 0f) al = 0f;
                if (al >= 0.5f && !alignTold)
                {
                    alignTold = true;
                    hud.Flash("물살을 탄다! 반대로 버텨요", UIKit.Sky, 1f);
                }
            }
            else if (f.State == FightModel.Phase.Rest && jumpTime < 0f)
            {
                // resting, it swings down-current on the line
                float maxYaw = MaxFightYaw();
                fightYawTarget = Mathf.Clamp(fightYawTarget + 0.2f * (Vector2.Dot(cF, side) / cf.Ref) * dt, -maxYaw, maxYaw);
            }
            RunAlign = al;
            float load = 0.25f * f.Power * cF.magnitude / cf.Ref;
            if (f.Exhausted) load *= 0.5f;
            if (DownstreamRun && running) load *= 1.5f;
            if (OutOfFlowT > 0f) load *= 0.3f;
            var mid = new Vector2((Anchor.x + pos.x) * 0.5f, (Anchor.z + pos.z) * 0.5f);
            float cPerp = Vector2.Dot(cf.Water(mid.x, mid.y), side);
            f.CurrentAlign = al;
            f.CurrentLoad = load;
            f.LineDrag = 0.02f * f.Line * cPerp * cPerp;
            f.CurrentSpeed = cF.magnitude;
        }

        void UpdateFighting(float dt)
        {
            var f = Fight;
            float revs = Gesture.Speed;
            SidePressure(dt, f);
            CurrentFight(dt, f);
            f.Step(dt, revs, Hooked.Depth < 1.6f);
            bool running = (f.State == FightModel.Phase.Run || f.State == FightModel.Phase.Burst) && !f.Exhausted;

            // --- place the fish so its distance from the rod tip equals the line length
            bool tailWalking = jumpTime >= 0 && jumpKind == FightModel.JumpKind.TailWalk;
            fightYaw = Mathf.MoveTowards(fightYaw, fightYawTarget, dt * (running ? 0.45f : tailWalking ? 0.35f : 0.15f));
            float depth = Hooked.Depth;
            float dTarget = f.Exhausted ? 0.25f : fishDepthTarget;
            depth = Mathf.MoveTowards(depth, dTarget, dt * (running ? 1.2f : 0.5f));
            // (from the tip as the rod is held, not the drawn one: the drawn rod bends towards the line and tilts off the hat,
            // both following the fish; measured from it, the fish's place fed back into itself through the line's direction,
            // and close in the rod, the line, the water entry and the fish swapped between two places every frame)
            var tip = Angler.RodTipPlan;
            var u = new Vector3(Mathf.Sin(fightYaw), 0, Mathf.Cos(fightYaw));
            // the point at height y out along u whose distance from the tip is the line's length
            Vector3 OnLine(float y)
            {
                var a = Anchor - tip + new Vector3(0, y, 0);
                float au = Vector3.Dot(a, u);
                float disc = au * au - a.sqrMagnitude + f.Line * f.Line;
                float h = disc >= 0 ? Mathf.Max(0, -au + Mathf.Sqrt(disc)) : Mathf.Max(0, -au);
                var p = Anchor + u * h + new Vector3(0, y, 0);
                if (!L.IsIce)
                {
                    p.z = Mathf.Clamp(p.z, L.zNear + 0.2f, L.zFar - 1f);
                    p.x = Mathf.Clamp(p.x, -L.xLim + 0.3f, L.xLim - 0.3f);
                }
                return p;
            }
            var pos = OnLine(-depth);
            depth = Mathf.Min(depth, L.DepthAt(pos.z) - 0.2f);
            pos.y = -Mathf.Max(0.15f, depth);

            if (DebugFishHold.HasValue)
            {
                pos = DebugFishHold.Value;
                depth = Mathf.Max(0.15f, -pos.y);
                pos.y = -depth;
            }
            if (jumpTime >= 0)
            {
                UpdateJump(dt, ref pos);
                // in the air: on the line's length from where it is this frame (placed from last frame's height, the first
                // frame of a jump was placed as if still down in the water, and the fish, the line and the rod tip jumped out
                // the frame after)
                if (!DebugFishHold.HasValue && jumpTime >= 0f)
                {
                    float y = pos.y;
                    pos = OnLine(y);
                    pos.y = y;
                }
            }
            var moved = pos - lastFishPos;
            if (new Vector2(moved.x, moved.z).sqrMagnitude > 0.00002f)
            {
                float want = running ? Mathf.Atan2(pos.z - Anchor.z, pos.x - Anchor.x) : Mathf.Atan2(moved.z, moved.x);
                want += Mathf.Sin(Time.time * (running ? 14f : 5f)) * 0.3f;
                Hooked.Heading = Mathf.LerpAngle(Hooked.Heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg, dt * 6f) * Mathf.Deg2Rad;
            }
            lastFishPos = pos;
            Hooked.Pos = pos;
            Hooked.Frantic = running || f.Exhausted;
            // the cover (the run's arrival, the hold) and the line rubbing on structure
            FightObstacles(dt, f, pos);

            // thrashing near the surface (its sound: louder while it runs)
            if (depth < 0.7f && jumpTime < 0) Sfx.Thrash(running ? 0.75f : 0.45f);
            splashT -= dt;
            if (splashT <= 0 && depth < 0.7f && jumpTime < 0)
            {
                splashT = running ? 0.25f : 0.6f;
                var sp2 = P.To2D(new Vector3(pos.x, 0, pos.z), out float spD);
                Fx.Splash(sp2, Mathf.Clamp(P.PixelsPerMetre(pos) / 30f, 0.3f, 1f), Stage.WaterTint, 4, spD);
                Fx.Ripple(sp2, Mathf.Clamp(P.PixelsPerMetre(pos) * 1.4f / 64f, 0.1f, 0.8f), P.Foreshorten(pos) * 1.6f + 0.15f, new Color(1, 1, 1, 0.6f), 0.6f);
            }

            // rod, line, pose: under water the line runs to the fish's mouth and cuts the surface on the way
            Angler.LineTarget = jumpTime >= 0 ? AirMouth(pos) : Hooked.MouthPos;
            lineRingT -= dt;
            if (jumpTime < 0 && Angler.LineUnderwater && lineRingT <= 0)
            {
                lineRingT = running ? 0.3f : Mathf.Lerp(0.9f, 0.45f, f.TensionRatio);
                LineRing(Angler.WaterEntry, 0.55f);
            }
            Angler.Tension01 = Mathf.Clamp01(f.TensionRatio * 1.1f);
            Angler.Slack01 = 1f - Mathf.Clamp01(f.TensionRatio * 2.5f);
            Angler.Strain01 = Mathf.Max(Mathf.InverseLerp(0.8f, 1f, f.TensionRatio), Mathf.Clamp01(f.BreakRatio));
            // the float on the line (float rigs): how tight the line is (against the line's strength, as the line's sag, or
            // against the fish's own pull: a small fish on a strong line still pulls it under), and the hard pulls that tug it
            Tackle.FightTaut = Mathf.Clamp01(Mathf.Max(f.TensionRatio * 2.5f, f.Tension / Mathf.Max(0.2f, f.Power * 0.6f)));
            Tackle.FightHard = f.State == FightModel.Phase.Burst || f.TensionRatio > 0.8f;
            if (Gesture.FrameRevs > 0.0005f)
            {
                Angler.SetPose(ReelPose(Gesture.TotalRevs * 4));
                ReelTicks(Gesture.FrameRevs);
            }
            else Angler.SetPose("fight");

            // audio cues: the line twanging faster and higher as the tension climbs (from StrainFrom of its limit, full at the break)
            Sfx.LineStrain(Mathf.InverseLerp(StrainFrom, 1f, f.TensionRatio));
            // the drag slipping: its loop, louder the further the pull is past the drag (fades out by itself once it stops)
            float dragMax = Game.I.Reel.dragMax;
            if (f.Tension >= dragMax * 0.97f) Sfx.Drag((f.Tension - dragMax * 0.97f) / (dragMax * 0.15f));
            hud.UpdateFight(f, Hooked);

            switch (f.Result)
            {
                case FightModel.Outcome.Landed: StartCoroutine(LandRoutine()); break;
                case FightModel.Outcome.Snapped:
                case FightModel.Outcome.Spooled: LineBroke(f.Result == FightModel.Outcome.Spooled); break;
                case FightModel.Outcome.Escaped: FishEscaped(); break;
            }
        }

        /// <summary>The fight is over. <paramref name="keepFloat"/>: a float rig's float is not taken away (landed: it hangs on
        /// the line; the fish off: it stays on the water to be wound in).</summary>
        void EndFightCommon(bool keepFloat = false)
        {
            EndFightObstacles();
            EndRun();
            Slide.Reset();
            if (Fight != null) Fight.PhaseChanged -= OnPhase;
            Fight = null;
            DebugFishHold = null;
            if (!keepFloat) Tackle.Hide();
            Angler.LineTarget = null;
            Angler.Fish = null;
            Angler.Tension01 = 0;
            Angler.Strain01 = 0;
            Gesture.Reset();
            jumpTime = -1;
        }

        /// <summary>The "!" just above the float's top (which shrinks with distance), bouncing.</summary>
        void PlaceBiteMark()
        {
            var p2 = P.To2D(Tackle.Surface) + new Vector2(0, Tackle.FloatScale * 0.95f + 0.3f + Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.25f);
            biteMark.transform.position = Snap(p2);
        }

        /// <summary>A small ring on the water where the line cuts the surface.</summary>
        void LineRing(Vector3 at, float alpha)
        {
            Fx.Ripple(P.To2D(at), Mathf.Clamp(P.PixelsPerMetre(at) / 100f, 0.06f, 0.4f), P.Foreshorten(at) * 1.6f + 0.15f,
                new Color(1, 1, 1, alpha), 0.8f);
        }

        /// <summary>
        /// Moves the hooked fish through its jump and sprays water. A hop is one arc. A shaker hangs in the air and
        /// whips round once per head shake (k = i + 0.5, where the fight model rolls for a thrown hook). A tail-walker
        /// stands on its tail skipping sideways over the surface, wagging its head, then topples over flat.
        /// </summary>
        void UpdateJump(float dt, ref Vector3 pos)
        {
            var fish = Hooked;
            jumpTime += dt / jumpDur;
            float t = Mathf.Clamp01(jumpTime);
            fish.JumpT = t;
            float len = fish.VisLen, height = 1.4f * Mathf.Clamp(len, 0.7f, 2.2f);
            float size = Mathf.Clamp(P.PixelsPerMetre(pos) / 25f, 0.4f, 1.2f);
            float tilt, bend = 1f;
            bool right = jumpRight;
            switch (jumpKind)
            {
                case FightModel.JumpKind.Shake:
                {
                    float a = 0.3f / jumpDur, b = 1f - a;
                    if (t < a)
                    {
                        pos.y = height * Mathf.Sin(t / a * Mathf.PI * 0.5f);
                        tilt = 45f;
                    }
                    else if (t > b)
                    {
                        float r = (t - b) / (1f - b);
                        pos.y = height * Mathf.Cos(r * Mathf.PI * 0.5f);
                        tilt = Mathf.Lerp(10f, -50f, r);
                    }
                    else
                    {
                        float k = (t - a) / (b - a) * jumpShakes;
                        float w = Mathf.Sin(k * Mathf.PI * 2f);
                        pos.y = height * (1f - 0.06f * Mathf.Abs(w));
                        tilt = 15f + 35f * w;
                        bend = 1f - 0.25f * Mathf.Abs(w);   // curled into a C at the ends of each swing
                        int shake = Mathf.FloorToInt(k + 0.5f);
                        right = shake % 2 == 0 ? jumpRight : !jumpRight;
                        if (shake != jumpFlip)
                        {
                            jumpFlip = shake;
                            Fx.Fling(P.To2D(pos), Stage.WaterTint, size, 7, P.DepthOf(pos));
                        }
                    }
                    break;
                }
                case FightModel.JumpKind.TailWalk:
                {
                    const float a = 0.12f, b = 0.88f;
                    float stand = len * 0.45f;   // tail just in the water with the body at ~72 degrees
                    float k = Mathf.Clamp01((t - a) / (b - a)) * jumpShakes;
                    float wag = Mathf.Sin(k * Mathf.PI * 2f);
                    if (t < a)
                    {
                        float r = t / a;
                        pos.y = stand * r + height * 0.3f * Mathf.Sin(r * Mathf.PI);
                        tilt = Mathf.Lerp(40f, 72f, r);
                    }
                    else if (t > b)
                    {
                        float r = (t - b) / (1f - b);
                        pos.y = stand * (1f - r) + height * 0.15f * Mathf.Sin(r * Mathf.PI);
                        tilt = Mathf.Lerp(72f, 0f, r);
                    }
                    else
                    {
                        pos.y = stand + len * 0.08f * Mathf.Abs(Mathf.Sin(t * jumpDur * 14f));
                        tilt = 72f + 14f * wag;
                        bend = 1f - 0.12f * Mathf.Abs(wag);
                        int shake = Mathf.FloorToInt(k + 0.5f);
                        if (shake != jumpFlip)
                        {
                            jumpFlip = shake;
                            Fx.Fling(P.To2D(pos), Stage.WaterTint, size, 5, P.DepthOf(pos));
                        }
                    }
                    // the tail slapping the water leaves a trail of spray and rings
                    trailT -= dt;
                    if (trailT <= 0 && t > a * 0.5f && t < b)
                    {
                        trailT = 0.06f;
                        var s = new Vector3(pos.x, 0, pos.z);
                        Fx.Splash(P.To2D(s), size * 0.6f, Stage.WaterTint, 3, P.DepthOf(s));
                        if (Random.value < 0.35f) LineRing(s, 0.5f);
                    }
                    break;
                }
                default:
                    pos.y = height * 0.85f * Mathf.Sin(Mathf.PI * t);
                    tilt = Mathf.Lerp(40f, -40f, t);
                    break;
            }
            fish.AirTilt = tilt;
            fish.AirRight = right;
            fish.AirBend = bend;

            // water dripping off the fish in the air
            dripT -= dt;
            if (dripT <= 0 && pos.y > 0.2f)
            {
                dripT = 0.05f;
                Fx.Drip(P.To2D(pos), Stage.WaterTint, size, P.DepthOf(pos));
            }
            if (jumpTime >= 1f)
            {
                jumpTime = -1;
                fish.JumpT = -1;
                JumpSplash(pos, jumpKind == FightModel.JumpKind.TailWalk ? 1.4f : 1.2f);
                Sfx.PlayVar(Sfx.Splash, 0.9f);
            }
        }

        /// <summary>The mouth of the fish in the air, from its drawn pose (the line hangs from it).</summary>
        Vector3 AirMouth(Vector3 pos)
        {
            var f = Hooked;
            float a = f.AirTilt * Mathf.Deg2Rad, r = f.VisLen * 0.45f;
            return pos + new Vector3(Mathf.Cos(a) * r * f.AirBend * (f.AirRight ? 1 : -1), Mathf.Sin(a) * r, 0);
        }

        /// <summary>A fish that gets off in mid-air falls back into the water.</summary>
        void DropFromAir()
        {
            if (jumpTime < 0 || Hooked == null) return;
            JumpSplash(Hooked.Pos, 1f);
            Hooked.Pos = new Vector3(Hooked.Pos.x, -0.3f, Hooked.Pos.z);
        }

        /// <summary>Spray and rings where a jumping fish leaves (power ~0.8) or falls back into (~1.2) the water.</summary>
        void JumpSplash(Vector3 at, float power)
        {
            var s = L.IsIce ? new Vector3(L.holeX, 0, L.holeZ) : new Vector3(at.x, 0, at.z);
            var s2 = P.To2D(s);
            float ppm = P.PixelsPerMetre(s);
            float squash = P.Foreshorten(s) * 1.6f + 0.15f;
            Fx.Splash(s2, Mathf.Clamp(ppm / 20f, 0.4f, 1.3f) * power, Stage.WaterTint, Mathf.RoundToInt(14 * power), P.DepthOf(s));
            Fx.Ripple(s2, Mathf.Clamp(ppm * 1.8f / 64f, 0.12f, 0.9f) * power, squash, new Color(1, 1, 1, 0.85f), 0.9f);
            Fx.Ripple(s2, Mathf.Clamp(ppm * 1.1f / 64f, 0.08f, 0.6f) * power, squash, new Color(1, 1, 1, 0.6f), 0.6f);
        }

        void LineBroke(bool spooled)
        {
            Sfx.Play(Sfx.Snap, 1f);
            view.Shake(0.3f, 0.35f);
            string msg = BreakText(spooled);
            bool lure = Game.I.LoseTackle();
            if (Fight != null && Fight.Outclassed) msg += "\n이 녀석에겐 장비가 약해요. 더 튼튼한 줄과 릴이 필요해요!";
            if (lure) msg += "  (루어를 잃었다)";
            hud.Flash(msg, UIKit.Bad, 2f);
            DropFromAir();
            Hooked.JumpT = -1;
            Hooked.Flee();
            Hooked = null;
            FishOff();
        }

        void FishEscaped()
        {
            Sfx.Play(Sfx.Escape, 0.9f);
            hud.Flash("물고기가 바늘을 털고 도망갔다...", UIKit.Bad, 2f);
            DropFromAir();
            Hooked.JumpT = -1;
            Hooked.Flee();
            Hooked = null;
            FishOff();
        }

        /// <summary>
        /// The fish is off (it got away, the line broke): a lure rig is gone at once and he is ready again; a float rig's
        /// float stays where it was, back up on the water, and after a moment is wound in (the retrieve).
        /// </summary>
        void FishOff()
        {
            FishOffMusic();
            bool keep = Tackle.FloatFight == Tackle.FightFloat.Line;
            EndFightCommon(keep);
            Angler.SetPose("idle");
            if (keep && Tackle.LetGo())
            {
                Angler.LineTarget = Tackle.LineEnd;
                Angler.Slack01 = 0.45f;
                retrieveWait = RetrieveWait;
                SetState(S.Retrieving);
            }
            else
            {
                Tackle.Hide();
                SetState(S.Ready);
            }
        }

        // after the fish is off, the float lies where it was this long before it is wound in
        const float RetrieveWait = 0.7f;
        float retrieveWait;

        IEnumerator LandRoutine()
        {
            SetState(S.Landing);
            var fish = Hooked;
            // a float rig's float comes out on the line with the fish and hangs there (over it, or at the rod tip)
            bool hangFloat = Tackle.FloatFight == Tackle.FightFloat.Line;
            EndFightCommon(hangFloat);
            if (hangFloat) Tackle.HangFromRod();
            fish.JumpT = -1;
            fish.Lift();
            var start = new Vector3(fish.Pos.x, 0, fish.Pos.z);
            Fx.Splash(P.To2D(start), Mathf.Clamp(P.PixelsPerMetre(start) / 20f, 0.4f, 1.3f), Stage.WaterTint, 16, P.DepthOf(start));
            Sfx.PlayVar(Sfx.Splash, 1f);
            // hands stay at the chest on rod and reel (no arms overhead); the rod alone is raised near upright and the fish
            // comes out on the line, swinging up to hang nose-up under the tip, the rod bowing a little under it
            Angler.SetPose("reel");
            fish.AirRight = Mathf.Cos(fish.Heading) >= 0;
            fish.AirBend = 1f;
            Angler.Fish = null;
            Angler.Tension01 = 0.25f;
            Angler.Slack01 = 0f;
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.8f;
                Angler.RodLift01 = Mathf.SmoothStep(0, 1, Mathf.Min(1f, t * 1.6f)); // the rod is up before the fish arrives
                float k = Mathf.SmoothStep(0, 1, t);
                var p = Vector3.Lerp(start, HangPoint(fish), k);
                p.y += Mathf.Sin(k * Mathf.PI) * 0.8f;
                fish.Pos = p;
                fish.AirTilt = Mathf.Lerp(20f, 80f, k);
                Angler.LineTarget = HungMouth(fish);
                // (swung up out of the water it shows only where the stand does not hide it: it keeps its depth all the
                // way up and while it hangs, so nothing nearer ever shows it in front of the rail / the pier's edge)
                yield return null;
            }
            Sfx.Play(Sfx.Catch, 0.9f);
            CatchMusic(fish.Sp);
            Fx.Burst(P.To2D(fish.Pos), UIKit.Gold, 16, 4f);
            var cf = Game.I.MakeCatch(fish.Sp, fish.Cm, Stage.Def.id);
            var rep = Game.I.RegisterCatch(cf);
            if (fish.Sp.encounter != null)
            {
                // a legend landed: its pity bonus starts over
                Game.I.Legend(fish.Sp.id).pity = 0;
                Game.I.Save();
                Debug.Log($"[ENC] landed {fish.Sp.id} {cf.sizeCm:0.0}cm");
            }
            SetState(S.Result);
            // it hangs on the line, swaying gently, until the catch card is closed; the card comes a second later so
            // the catch is seen hanging first
            StartCoroutine(HangSway(fish));
            yield return new WaitForSeconds(CardDelay);
            CatchPopup.Show(hud.Canvas, cf, rep, choice =>
            {
                switch (choice)
                {
                    case CatchPopup.Choice.Sell:
                        Game.I.Sell(cf);
                        Sfx.Play(Sfx.Coin);
                        hud.Flash($"+{UIKit.Num(cf.value)} 코인", UIKit.Gold, 1.2f);
                        break;
                    case CatchPopup.Choice.Keep:
                        Game.I.AddToAquarium(cf);
                        Sfx.Play(Sfx.Keep, 0.7f);
                        hud.Flash($"수조에 넣었어요 ({Game.I.UsedSpace}/{Game.I.Capacity}칸)", UIKit.Sky, 1.4f);
                        break;
                    default:
                        Sfx.Play(Sfx.Release, 0.7f);
                        hud.Flash("잘 가~ 다음에 또 만나자!", UIKit.Cream, 1.2f);
                        break;
                }
                if (fish != null) fish.Kill();
                Tackle.Hide();   // (the float that hung on the line with the fish)
                Angler.LineTarget = null;
                Angler.Tension01 = 0;
                Angler.RodLift01 = 0;
                Angler.SetPose("idle");
                SetState(S.Ready);
            });
        }

        const float CardDelay = 1f; // seconds the landed fish hangs in view before the catch card comes up

        IEnumerator HangSway(FishAgent fish)
        {
            float sway = 0;
            while (State == S.Result && fish != null)
            {
                sway += Time.deltaTime;
                fish.Pos = HangPoint(fish) + new Vector3(Mathf.Sin(sway * 2.2f) * 0.12f, 0, 0);
                fish.AirTilt = 80f + Mathf.Sin(sway * 2.2f + 0.8f) * 6f;
                Angler.LineTarget = HungMouth(fish);
                yield return null;
            }
        }

        /// <summary>Where a landed fish hangs: its mouth a little under the rod tip.</summary>
        Vector3 HangPoint(FishAgent f) => Angler.RodTip - new Vector3(0, 0.35f + f.VisLen * 0.45f, 0);

        /// <summary>The mouth of a fish hanging nose-up in the air (the line's end).</summary>
        static Vector3 HungMouth(FishAgent f)
        {
            float a = f.AirTilt * Mathf.Deg2Rad, r = f.VisLen * 0.45f;
            return f.Pos + new Vector3(Mathf.Cos(a) * r * (f.AirRight ? 1 : -1), Mathf.Sin(a) * r, 0);
        }

        static Vector3 Snap(Vector2 p) => new Vector3(Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU, 0);
    }
}
