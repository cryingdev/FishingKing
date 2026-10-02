using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// One fish in the water. Game position is in metres (y = -depth). Drawn as a top-down shadow
    /// sprite under the surface (tinted by depth, foreshortened by the perspective) and as the
    /// side-view sprite when it jumps or is lifted out of the water.
    /// </summary>
    public class FishAgent : MonoBehaviour
    {
        public enum St { Wander, Approach, Nibble, Bite, Hooked, Flee, Lifted, Dead }

        public FishSpecies Sp { get; private set; }
        public float Cm { get; private set; }
        public St State { get; private set; }
        public Vector3 Pos;
        public float Heading;            // radians in the water plane, 0 = +x, PI/2 = away from the angler
        public float VisLen { get; private set; }
        public float Depth => -Pos.y;
        public bool Engaged => State == St.Approach || State == St.Nibble || State == St.Bite || State == St.Hooked;
        public bool Frantic;             // fast tail beat while hooked
        public float JumpT = -1f;        // 0..1 while airborne

        // pose in the air, set by the fight every frame while JumpT >= 0
        public float AirTilt;            // degrees, nose up
        public bool AirRight = true;     // facing right on screen
        public float AirBend = 1f;       // body length factor (a curled body looks shorter)
        public Vector3 MouthPos => Pos + new Vector3(Mathf.Cos(Heading), 0, Mathf.Sin(Heading)) * VisLen * 0.45f;

        /// <summary>The mouth of the shadow sprite as drawn (2D scene); the shadow is squashed a bit less than the true perspective.</summary>
        public Vector2 DrawnMouth { get; private set; }

        /// <summary>The side-view sprite (in the air: over the front layer; the occlusion watch reads it as drawn).</summary>
        internal SpriteRenderer SideR => side;

        FishingController ctl;
        StageView stage;
        Persp P;
        Transform shadowRoot;
        SpriteRenderer shadow, side;
        Sprite[] top, sideSpr;
        Vector3 target;
        float speed, stateT, thinkT, animT, nibbleT;
        int nibblesLeft;
        bool nibbleIn;
        float fleeTimer;

        /// <summary>
        /// Drawn length in metres: the true length plus half of the old readability boost
        /// (20 cm bluegill 0.68 m, 60 cm carp 1.18 m, 4 m marlin 5.25 m).
        /// </summary>
        public static float VisualLength(float cm)
        {
            float real = cm / 100f;
            float boosted = Mathf.Clamp(0.85f + real * 1.5f, 0.9f, 6.5f);
            return (real + boosted) * 0.5f;
        }

        public void Init(FishingController c, FishSpecies sp, float cm, Vector3 pos)
        {
            ctl = c;
            stage = c.Stage;
            P = stage.P;
            Sp = sp;
            Cm = cm;
            VisLen = VisualLength(cm);
            Pos = pos;
            Heading = Random.Range(0f, Mathf.PI * 2);
            top = new[] { Art.Get($"Fish/{sp.id}_t0"), Art.Get($"Fish/{sp.id}_t1") };
            sideSpr = new[] { Art.Fish(sp.id, 0), Art.Fish(sp.id, 1) };
            shadowRoot = new GameObject("Shadow").transform;
            shadowRoot.SetParent(transform, false);
            shadow = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(shadowRoot, false);
            shadow.sprite = top[0];
            side = new GameObject("Side").AddComponent<SpriteRenderer>();
            side.transform.SetParent(transform, false);
            side.sprite = sideSpr[0];
            side.sortingOrder = 42;
            side.enabled = false;
            FrontOcclusion.Use(side);   // (a jump beside the pier / rocks / boat shows only where they do not hide it)
            speed = sp.speed * Random.Range(0.18f, 0.3f);
            PickTarget();
            State = St.Wander;
            thinkT = Random.Range(0.5f, 1.5f);
        }

        // ------------------------------------------------------------------ wandering
        /// <summary>
        /// Stream holders (Docs/time_currents_spec.md 9.6): how likely a wander target lies inside a slack pocket behind a
        /// mid-stream rock (so a float led into a pocket meets them).
        /// </summary>
        static float HoldChance(string id) => id switch
        {
            "mandarin_fish" => 0.8f,
            "lenok" => 0.5f,
            "rainbow_trout" => 0.5f,
            "cherry_salmon" => 0.4f,
            "pale_chub" => 0.1f,
            _ => 0f,
        };

        /// <summary>A period began in which this species is not about (a = 0): it leaves at this real time (-1 = no).</summary>
        public float LeaveAt = -1f;

        void PickTarget()
        {
            // not its time of day (engaged when the period began, or stocked late in the cross-fade): it swims off soon,
            // as the others did when the period began (it would never bite, and would hold a place in the stock all period)
            if (LeaveAt < 0f && TimeActivity.A(Sp.id, GameClock.Now) <= 0f) LeaveAt = Time.unscaledTime + Random.Range(2f, 20f);
            var L = stage.L;
            float zMax = Mathf.Min(L.zFar - 2f, ctl.FishZMax);
            var hab = ctl.Habitat;
            // on a generated bed (the lake): a node drawn by its habitat (Docs/terrain_depth_spec.md 7.4); else uniform
            if (!(L.Terrain && hab != null && hab.Target(this, zMax, out target)))
            {
                float z = Random.Range(L.zNear + 1.5f, zMax);
                float half = Mathf.Min(L.xLim - 0.5f, P.VisibleHalfWidth(z, 600));
                float bottom = L.ProfileDepth(z) - 0.3f;
                float d = Random.Range(Mathf.Min(Sp.depthMin, bottom), Mathf.Min(Sp.depthMax, bottom));
                target = new Vector3(Random.Range(-half, half), -Mathf.Max(0.3f, d), z);
            }
            var cur = stage.Current;
            bool heldInPocket = false;
            if (cur != null && cur.K == CurrentField.Kind.Stream && Random.value < HoldChance(Sp.id))
            {
                heldInPocket = true;
                // hold in the slack behind a rock (a pocket within reach, uniformly in its ellipse)
                var pk = CurrentField.Pockets[Random.Range(0, CurrentField.Pockets.Length)];
                if (pk.z - pk.az < zMax)
                {
                    var o = Random.insideUnitCircle;
                    float pz = Mathf.Clamp(pk.z + o.y * pk.az, L.zNear + 1.5f, zMax);
                    float px = Mathf.Clamp(pk.x + o.x * pk.ax, -L.xLim + 0.5f, L.xLim - 0.5f);
                    float pb = L.DepthAt(px, pz) - 0.3f;
                    float pd = Random.Range(Mathf.Min(Sp.depthMin, pb), Mathf.Min(Sp.depthMax, pb));
                    target = new Vector3(px, -Mathf.Max(0.3f, pd), pz);
                }
            }
            if (L.IsIce)
            {
                // under the ice fish gather loosely around the hole
                var h = new Vector3(L.holeX, 0, L.holeZ);
                var off = Random.insideUnitCircle * 9f;
                target = new Vector3(h.x + off.x, target.y, Mathf.Clamp(h.z + Mathf.Abs(off.y) * 1.5f, L.zNear + 0.5f, 22f));
            }
            // a cover seeker wanders near its covers (Docs/obstacles_spec.md 8.2): 0.6 x seek of its targets in one of them;
            // the stream's pocket holders pick a pocket or a rock cover 50 / 50
            var obs = stage.Obstacles;
            if (obs != null && !obs.Empty && Sp.coverSeek > 0f)
            {
                bool holder = cur != null && cur.K == CurrentField.Kind.Stream && HoldChance(Sp.id) > 0f;
                if (holder ? heldInPocket && Random.value < 0.5f : Random.value < 0.6f * Sp.coverSeek)
                {
                    // (on the bed: only a cover whose hold has water enough for it)
                    float need = HabitatModel.MinWater(Cm) + 0.2f;
                    var c = L.Terrain ? obs.RandomCover(Sp, zMax, cv => L.DepthAt(cv.hx, cv.hz) >= need) : obs.RandomCover(Sp, zMax);
                    if (c != null)
                    {
                        var q = Obstacles.RandomPoint(c);
                        float cz = Mathf.Clamp(q.y, L.zNear + 1.5f, zMax);
                        float cx = Mathf.Clamp(q.x, -L.xLim + 0.5f, L.xLim - 0.5f);
                        float cb = L.DepthAt(cx, cz) - 0.3f;
                        float cd = L.Terrain && hab != null ? hab.SwimDepth(Sp, FishHabitat.DrawPeriod(), L.DepthAt(cx, cz))
                            : Random.Range(Mathf.Min(Sp.depthMin, cb), Mathf.Min(Sp.depthMax, cb));
                        target = new Vector3(cx, -Mathf.Max(0.3f, cd), cz);
                    }
                }
            }
        }

        /// <summary>
        /// Strikes at once (a frog sitting on a lily pad struck through it, Docs/obstacles_spec.md 5.1): it comes up under
        /// the lure and takes it.
        /// </summary>
        public void ForceBite()
        {
            var hook = ctl.Tackle.HookPos;
            var toHook = new Vector3(hook.x - Pos.x, 0f, hook.z - Pos.z);
            if (toHook.sqrMagnitude > 1e-6f) Heading = Mathf.Atan2(toHook.z, toHook.x);
            var fwd = new Vector3(Mathf.Cos(Heading), 0f, Mathf.Sin(Heading));
            Pos = new Vector3(hook.x, -Mathf.Max(0.3f, Depth), hook.z) - fwd * VisLen * 0.45f;
            StartBite();
        }

        void Steer(Vector3 goal, float spd, float dt, float turnRate = 2.5f)
        {
            var d = goal - Pos;
            var flat = new Vector2(d.x, d.z);
            if (flat.sqrMagnitude > 0.0004f)
            {
                float want = Mathf.Atan2(flat.y, flat.x);
                Heading = Mathf.MoveTowardsAngle(Heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg, turnRate * Mathf.Rad2Deg * dt) * Mathf.Deg2Rad;
            }
            var fwd = new Vector3(Mathf.Cos(Heading), 0, Mathf.Sin(Heading));
            float align = Mathf.Clamp01(Vector3.Dot(fwd, new Vector3(d.x, 0, d.z).normalized) * 0.8f + 0.2f);
            Pos += fwd * spd * align * dt;
            Pos.y = Mathf.MoveTowards(Pos.y, goal.y, spd * 0.5f * dt);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            stateT += dt;
            switch (State)
            {
                case St.Wander: UpdateWander(dt); break;
                case St.Approach: UpdateApproach(dt); break;
                case St.Nibble: UpdateNibble(dt); break;
                case St.Bite: UpdateBite(dt); break;
                case St.Flee: UpdateFlee(dt); break;
            }
            CountBed();
        }

        void LateUpdate() => Render(Time.deltaTime);

        void UpdateWander(float dt)
        {
            if (LeaveAt >= 0f && Time.unscaledTime >= LeaveAt)
            {
                // not its time of day: it swims off
                LeaveAt = -1f;
                Flee();
                return;
            }
            var before = Pos;
            Steer(target, speed, dt);
            // the current carries it a little (it holds station against most of it)
            var cur = stage.Current;
            if (cur != null && cur.Moving && !stage.L.IsIce)
            {
                var w = cur.Water(Pos.x, Pos.z) * (0.25f * CurrentField.Kd(Depth));
                Pos += new Vector3(w.x, 0f, w.y) * dt;
            }
            if (KeepInWater(before)) return;
            if (new Vector2(target.x - Pos.x, target.z - Pos.z).magnitude < 0.6f || stateT > 12f)
            {
                PickTarget();
                stateT = 0;
            }
            thinkT -= dt;
            if (thinkT <= 0)
            {
                thinkT = Random.Range(0.8f, 1.3f);
                if (ctl.WantsToApproach(this)) StartApproach();
            }
        }

        void StartApproach()
        {
            State = St.Approach;
            stateT = 0;
            lowQT = 0;
            rolledWindow = ctl.Rhythm.WindowId - 1;
        }

        float lowQT;       // following a lure worked badly (Q < 0.25) this long
        int rolledWindow;  // the lure's strike window this fish last rolled for

        void UpdateApproach(float dt)
        {
            var tk = ctl.Tackle;
            if (tk.State != Tackle.Mode.Water || !ctl.CanFishEngage(this))
            {
                LoseInterest();
                return;
            }
            var hook = tk.HookPos;
            var toHook = new Vector3(hook.x - Pos.x, 0, hook.z - Pos.z);
            var goal = hook - toHook.normalized * VisLen * 0.45f;
            var before = Pos;
            Steer(goal, Sp.speed * 0.45f, dt, 3.5f);
            if (KeepInWater(before)) return;
            float dist = Vector3.Distance(MouthPos, hook);
            // (a float drifting with the water is not "moving": its speed through the water counts)
            if (tk.UsesFloat && tk.RelSpeed > 1.3f && Random.value < dt * 0.8f)
            {
                LoseInterest();
                return;
            }
            var rh = ctl.Rhythm;
            if (!tk.UsesFloat)
            {
                // a lure worked badly: it follows for a bit, then turns away
                lowQT = rh.Q < 0.25f ? lowQT + dt : 0f;
                if (lowQT >= 2f)
                {
                    ctl.OnFollowerTurned(this);
                    LoseInterest();
                    return;
                }
            }
            if (dist < 0.3f)
            {
                bool nibbler = tk.UsesFloat || tk.Bait.needBottom;
                if (!nibbler)
                {
                    // right behind the lure: one strike roll each time the lure's strike window comes due (1.4 / 1.5)
                    // (a frog sitting on a lily pad is only taken through the pad: FishingController.PadTick)
                    if (rh.StrikeOpen && rh.WindowId != rolledWindow && !ctl.PadBlocksStrike)
                    {
                        rolledWindow = rh.WindowId;
                        // x the time of day and the tide (Docs/time_currents_spec.md 9.5)
                        float chance = (0.35f + 0.5f * Sp.aggression) * (0.5f + 0.7f * rh.Q) * ctl.StrikeMult(this);
                        ctl.OnStrikeRoll(this, chance);
                        if (Random.value < chance)
                        {
                            StartBite();
                            return;
                        }
                    }
                }
                else if (tk.UsesFloat || rh.StrikeOpen)
                {
                    // natural baits and the soft worm (on the fall / just after touching the bottom) are nibbled first
                    State = St.Nibble;
                    stateT = 0;
                    nibblesLeft = Random.Range(1, 4) - (Sp.aggression > 0.6f ? 1 : 0);
                    nibbleT = 0;
                    nibbleIn = false;
                    return;
                }
            }
            if (stateT > 14f) LoseInterest();
        }

        void UpdateNibble(float dt)
        {
            var tk = ctl.Tackle;
            if (tk.State != Tackle.Mode.Water)
            {
                LoseInterest();
                return;
            }
            var hook = tk.HookPos;
            var fwd = new Vector3(Mathf.Cos(Heading), 0, Mathf.Sin(Heading));
            nibbleT -= dt;
            var rest = hook - fwd * (VisLen * 0.45f + (nibbleIn ? 0f : 0.25f));
            var next = Vector3.MoveTowards(Pos, rest, dt * 1.6f);
            // on a generated bed its body does not back into water shallower than it swims in (it noses in from where it is)
            if (stage.L.Terrain && ShallowStep(next.x, next.z))
            {
                next.x = Pos.x;
                next.z = Pos.z;
            }
            Pos = next;
            if (nibbleT <= 0)
            {
                nibbleIn = !nibbleIn;
                nibbleT = nibbleIn ? 0.25f : Random.Range(0.45f, 0.9f);
                if (nibbleIn)
                {
                    ctl.OnNibble(this);
                    nibblesLeft--;
                }
                else if (nibblesLeft <= 0)
                {
                    if (Random.value < 0.85f) StartBite();
                    else LoseInterest();
                }
            }
        }

        void StartBite()
        {
            State = St.Bite;
            stateT = 0;
            ctl.OnBite(this);
        }

        void UpdateBite(float dt)
        {
            // swims off with the bait, dragging the float under
            var away = new Vector3(Mathf.Cos(Heading), 0, Mathf.Sin(Heading));
            var step = away * Sp.speed * 0.25f * dt;
            // on a generated bed it is not carried into water shallower than it swims in (it holds there; deeper is fine)
            if (stage.L.Terrain && ShallowStep(Pos.x + step.x, Pos.z + step.z)) step = Vector3.zero;
            Pos += step;
            Pos.y = Mathf.MoveTowards(Pos.y, -Mathf.Min(Sp.depthMax, stage.L.DepthAt(Pos.x, Pos.z) - 0.3f), dt * 0.4f);
            var tk = ctl.Tackle;
            tk.Surface = new Vector3(MouthPos.x, 0, MouthPos.z);
            tk.Depth = Depth;
        }

        public void SetHooked()
        {
            State = St.Hooked;
            stateT = 0;
        }

        public void LoseInterest()
        {
            if (State == St.Hooked || State == St.Dead) return;
            State = St.Wander;
            stateT = 0;
            thinkT = Random.Range(3f, 6f);
            PickTarget();
        }

        public void Flee()
        {
            State = St.Flee;
            stateT = 0;
            fleeTimer = 0;
            var L = stage.L;
            float fx = Pos.x + Random.Range(-15f, 15f);
            float fz = Mathf.Min(L.zFar, Pos.z + 40f);
            // (on the bed: the shallower of the way out and the end)
            float bed = L.Terrain ? Mathf.Min(L.DepthAt((Pos.x + fx) * 0.5f, Pos.z + 20f), L.DepthAt(fx, fz)) : L.ProfileDepth(Pos.z + 20);
            target = new Vector3(fx, -Mathf.Min(Sp.depthMax, bed - 0.3f), fz);
        }

        // ------------------------------------------------------------------ the bed (Docs/terrain_depth_spec.md 9.1)
        /// <summary>Test counters (-fkauto depth): frames a free fish stood in water shallower than it swims in / below the bed.</summary>
        internal static int ShallowFrames, UnderBedFrames;

        /// <summary>
        /// On a generated bed, after a wander / approach / flee step from <paramref name="before"/>: a fish never swims into
        /// water shallower than <see cref="FishHabitat.MinWater"/> of its size (it stays where it was and wanders elsewhere,
        /// gives up the bait, or flees down the slope instead), and never under the bed (lifted to 0.3 m over it). True when
        /// the step was refused.
        /// </summary>
        bool KeepInWater(Vector3 before)
        {
            var L = stage.L;
            if (!L.Terrain) return false;
            bool refused = false;
            float now = L.DepthAt(Pos.x, Pos.z);
            // (a fish already in too little water, dragged there in a fight, may still make for deeper water)
            if (now < HabitatModel.MinWater(Cm) && now < L.DepthAt(before.x, before.z))
            {
                refused = true;
                Pos.x = before.x;
                Pos.z = before.z;
                switch (State)
                {
                    case St.Wander:
                        PickTarget();
                        stateT = 0f;
                        break;
                    case St.Approach:
                        LoseInterest();
                        break;
                    case St.Flee:
                        var dd = L.Bathy.DeeperDir(Pos.x, Pos.z);
                        if (dd == Vector2.zero) dd = new Vector2(0f, 1f);
                        target.x = Pos.x + dd.x * 15f;
                        target.z = Pos.z + dd.y * 15f;
                        break;
                }
            }
            KeepOffBed();
            return refused;
        }

        /// <summary>A step to (x, z) would take it into water shallower than it swims in, and shallower than where it is.</summary>
        bool ShallowStep(float x, float z)
        {
            var L = stage.L;
            float next = L.DepthAt(x, z);
            return next < HabitatModel.MinWater(Cm) && next < L.DepthAt(Pos.x, Pos.z);
        }

        /// <summary>Lifted to at least 0.3 m over the bed (a bed only lifts).</summary>
        void KeepOffBed()
        {
            var L = stage.L;
            if (!L.Terrain) return;
            Pos.y = Mathf.Max(Pos.y, -Mathf.Max(0.3f, L.DepthAt(Pos.x, Pos.z) - 0.3f));
        }

        /// <summary>
        /// For the tests: this free fish (wandering, coming, nibbling, biting or fleeing) is in water shallower than it swims
        /// in / (wandering, coming or fleeing: one at the bait has its mouth on it, maybe on the bed) under the bed now.
        /// </summary>
        void CountBed()
        {
            var L = stage.L;
            if (!L.Terrain) return;
            bool atBait = State == St.Nibble || State == St.Bite;
            if (!atBait && State != St.Wander && State != St.Approach && State != St.Flee) return;
            float w = L.DepthAt(Pos.x, Pos.z);
            if (w < HabitatModel.MinWater(Cm) - 1e-3f) ShallowFrames++;
            if (!atBait && Pos.y < -Mathf.Max(0.3f, w - 0.3f) - 1e-3f) UnderBedFrames++;
        }

        void UpdateFlee(float dt)
        {
            var before = Pos;
            Steer(target, Sp.speed * 1.4f, dt, 5f);
            KeepInWater(before);
            fleeTimer += dt;
            if (fleeTimer > 3.5f) Kill();
        }

        public void Lift()
        {
            State = St.Lifted;
            ClearOfFront = false;
        }

        /// <summary>
        /// Landed (FishingController.LandRoutine) and drawn clear of the stand: nothing of the front layer under its sprite is
        /// nearer than it (this frame; for the tests). The lifted fish keeps its depth the whole time, swung up out of the
        /// water beside / under the pier, the rocks, the quay or the boat and hanging at the rod tip: it shows only where they
        /// do not hide it (<see cref="FrontOcclusion"/>), so it never pops in front of a rail it is still behind.
        /// </summary>
        public bool ClearOfFront { get; private set; }

        /// <summary>The lowest point of the side sprite as posed in the air (game y): its tilt, bend and outline.</summary>
        public float AirBottom
        {
            get
            {
                var s = side != null ? side.sprite : null;
                float aspect = s != null && s.rect.width > 0f ? s.rect.height / s.rect.width : 0.4f;
                float a = AirTilt * Mathf.Deg2Rad;
                return Pos.y - 0.5f * VisLen * (AirBend * Mathf.Abs(Mathf.Sin(a)) + aspect * Mathf.Abs(Mathf.Cos(a)));
            }
        }

        public void Kill()
        {
            State = St.Dead;
            ctl.Spawner.Remove(this);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ rendering
        // sprites follow the perspective (VisLen metres at the fish's distance) down to these on-screen lengths
        const float MinShadowPx = 8f, MinJumpPx = 10f;

        static readonly System.Collections.Generic.Dictionary<Sprite, float> lumCache = new System.Collections.Generic.Dictionary<Sprite, float>();

        /// <summary>Average brightness of a sprite's opaque pixels (fish sprites are imported readable), cached.</summary>
        static float SpriteLum(Sprite s)
        {
            if (s == null) return 0.5f;
            if (lumCache.TryGetValue(s, out float l)) return l;
            l = 0.5f;
            var tex = s.texture;
            if (tex != null && tex.isReadable)
            {
                var r = s.rect;
                double sum = 0;
                int n = 0;
                foreach (var c in tex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height))
                    if (c.a > 0.5f) { sum += StageView.Lum(c); n++; }
                if (n > 0) l = (float)(sum / n);
            }
            lumCache[s] = l;
            return l;
        }

        void Render(float dt)
        {
            float beat = State == St.Hooked || State == St.Flee || Frantic ? 0.09f : State == St.Wander ? 0.32f : 0.2f;
            animT += dt;
            int frame = (int)(animT / beat) % 2;
            bool airborne = JumpT >= 0f || State == St.Lifted;
            if (airborne)
            {
                shadow.enabled = false;
                side.enabled = true;
                side.sprite = sideSpr[frame];
                var air = new Vector3(Pos.x, Mathf.Max(0, Pos.y), Pos.z);
                var p2 = P.To2D(air, out float airDepth);
                // hidden where the front layer is nearer: in a jump, swung up by the landing and hanging at the rod tip alike
                FrontOcclusion.SetDepth(side, airDepth);
                float ppm = P.PixelsPerMetre(Pos);
                float s = Mathf.Max(MinJumpPx, ppm * VisLen) / Mathf.Max(8f, side.sprite.rect.width);
                side.transform.position = p2;
                bool jumping = JumpT >= 0 || State == St.Lifted; // a landed fish hangs posed by the fight code too
                bool right = jumping ? AirRight : Mathf.Cos(Heading) >= 0;
                side.flipX = !right;
                float tilt = jumping ? AirTilt * (right ? 1 : -1) : 0f;
                side.transform.rotation = Quaternion.Euler(0, 0, tilt);
                side.transform.localScale = new Vector3(s * (jumping ? AirBend : 1f), s, 1);
                if (State == St.Lifted)
                {
                    var b = side.bounds;
                    ClearOfFront = !FrontOcclusion.AnyNearer(b.min, b.max, airDepth);
                }
                return;
            }
            side.enabled = false;
            shadow.enabled = true;
            shadow.sprite = top[frame] != null ? top[frame] : sideSpr[frame];
            var seen = P.Apparent(Pos);
            var pos2 = P.To2D(seen);
            float ppm2 = P.PixelsPerMetre(seen);
            float scale = Mathf.Max(MinShadowPx, ppm2 * VisLen) / Mathf.Max(8f, shadow.sprite.rect.width);
            float squash = P.ShadowSquash(seen);
            shadowRoot.position = new Vector3(Mathf.Round(pos2.x * 16) / 16, Mathf.Round(pos2.y * 16) / 16, 0);
            shadowRoot.localScale = new Vector3(scale, scale * squash, 1);
            shadow.transform.localRotation = Quaternion.Euler(0, 0, Heading * Mathf.Rad2Deg);
            float half = scale * shadow.sprite.rect.width * 0.45f / PixelView.PPU;
            DrawnMouth = (Vector2)shadowRoot.position + new Vector2(Mathf.Cos(Heading) * half, Mathf.Sin(Heading) * half * squash);
            shadow.color = stage.FishShadowTint(Depth, SpriteLum(shadow.sprite), State == St.Flee ? Mathf.Clamp01(1.2f - fleeTimer * 0.35f) : 1f);
            shadow.sortingOrder = 10 + Mathf.Clamp(9 - (int)(Pos.z / 6f), 0, 9);
        }
    }
}
