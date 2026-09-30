using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The stage's legends on the surface and their trigger (Docs/lures_legend_spec.md 2.2, Docs/legends_rollout.md 1.3,
    /// 5), ticked by the controller. While a legend is off cooldown a lurk point sits somewhere in the deep water (it
    /// appears appear.x..y s after the stage opens or a cooldown ends and moves every relocate.x..y s), cued every 15-25 s
    /// by bubble rings on the surface above it (on the ice: inside the hole) and a glint of two eyes. Every legend of the
    /// stage (the ocean has two) shares that lurk point and its cues, but has its own meter, cooldown and pity: with one
    /// of its key lures worked the way it likes near the lurk point, its meter fills; the tells (a flash at 0.4, the
    /// ordinary fish scattering at 0.5, the line trembling at 0.7) follow the fullest meter, and the first meter to
    /// reach 1.0 rolls to start that legend's encounter. Without a line strong enough a legend's meter stops at 0.7.
    /// </summary>
    public class LegendWatch
    {
        /// <summary>-fkencounter: null (normal), "now" (1 s after the lure lands, no conditions) or "natural" (see Game).</summary>
        public static string DebugMode;
        /// <summary>Test hook (-fkencplay coolsave): cooldowns are set even under -fkencounter (normally it has none).</summary>
        public static bool DebugKeepCool;
        /// <summary>-fklegend: the legend the -fkencounter test is for (null = the stage's first).</summary>
        public static string DebugLegend;
        /// <summary>Which legend's wrong-lure tip a stage shows next (the ocean alternates, one per visit).</summary>
        static readonly Dictionary<string, int> tipTurn = new Dictionary<string, int>();

        /// <summary>One legend on this stage: its meter and what it has told this visit.</summary>
        class Entry
        {
            public FishSpecies sp;
            public float meter;
            public bool gearTold;
            public string blocked = "";
        }

        readonly List<Entry> entries = new List<Entry>();
        /// <summary>The stage's encounter legends (the ocean: the marlin, then the great white).</summary>
        public IReadOnlyList<FishSpecies> Legends { get; }
        /// <summary>The legend whose meter is fullest (the first on a tie): the one the tells and the glint are about.</summary>
        public FishSpecies Legend => Lead.sp;
        public EncounterDef Def => Legend.encounter;
        /// <summary>The legend whose roll succeeded (set when <see cref="Tick"/> returns true).</summary>
        public FishSpecies Triggered { get; private set; }
        public bool HasLurk { get; private set; }
        public Vector3 Lurk { get; private set; }
        /// <summary>0..1: the fullest build-up towards a roll.</summary>
        public float Meter => entries.Max(e => e.meter);
        public float MeterOf(string id) => entries.FirstOrDefault(e => e.sp.id == id)?.meter ?? 0f;
        /// <summary>Why the meters are not filling this frame (for the log / test): "reason" or "id:reason id:reason".</summary>
        public string Blocked => entries.Count == 1 ? entries[0].blocked : string.Join(" ", entries.Select(e => e.sp.id + ":" + e.blocked));
        /// <summary>This cast already had its encounter.</summary>
        public bool CastUsed { get; private set; }
        public float Soak { get; private set; }
        /// <summary>The natural baits' stillness (0..1 over 3 s since the last wind or flick) and the great white's crawl.</summary>
        public float StillQ { get; private set; }
        public float CrawlQ { get; private set; }

        readonly FishingController ctl;
        float appearT, relocateT, cueT, ringT, droneT, wrongT, mixT, debugT;
        bool announced, wrongTold, mixTold, told4, told5, told7;
        int cueRings;
        float cueRingT, cueAge = 99f;
        SpriteRenderer glintA, glintB;
        float glintT = -1f;
        bool wasAway;

        /// <summary>Seconds a cue plays: three rings 0.25 s apart, 0.8 s each (the glint: its first 0.5 s).</summary>
        public const float CueLength = 1.3f;

        /// <summary>A cue plays now.</summary>
        public bool CuePlaying => HasLurk && !Away && cueAge < CueLength;

        /// <summary>A cue plays now or starts within <paramref name="lead"/> s (the zoomed view keeps it in frame: FishingController.Zoom.cs).</summary>
        public bool CueSoon(float lead) => CuePlaying || (HasLurk && !Away && cueT <= lead);

        /// <summary>Where a cue's rings come up in the pixel scene (over the lurk point; on the ice: the hole).</summary>
        public Vector2 CueRings2D => L.IsIce ? P.To2D(new Vector3(L.holeX, 0f, L.holeZ)) : P.To2D(new Vector3(Lurk.x, 0f, Lurk.z));

        /// <summary>Where its eye glint shows (the lurk point seen through the surface; on the ice: the hole).</summary>
        public Vector2 CueEyes2D => L.IsIce ? P.To2D(new Vector3(L.holeX, 0f, L.holeZ)) : P.To2D(P.Apparent(Lurk));

        StageLayout L => ctl.Stage.L;
        Persp P => ctl.Stage.P;

        Entry Lead
        {
            get
            {
                var best = entries[0];
                foreach (var e in entries)
                    if (e.meter > best.meter + 1e-4f) best = e;
                return best;
            }
        }

        /// <summary>The first legend not away (whose timings the shared lurk point follows).</summary>
        EncounterDef Primary => (entries.FirstOrDefault(e => !AwayOf(e.sp)) ?? entries[0]).sp.encounter;

        LegendWatch(FishingController c, List<FishSpecies> legends)
        {
            ctl = c;
            Legends = legends;
            foreach (var sp in legends) entries.Add(new Entry { sp = sp });
            appearT = Random.Range(Primary.appear.x, Primary.appear.y);
            glintA = Glint("LegendGlintA");
            glintB = Glint("LegendGlintB");
            if (DebugMode == "natural") PlaceNatural();
        }

        /// <summary>The watch for this stage's encounter legends, or null when it has none.</summary>
        public static LegendWatch For(FishingController c)
        {
            var legends = GameDatabase.FishOfStage(c.Stage.Def.id).Where(f => f.encounter != null).ToList();
            return legends.Count > 0 ? new LegendWatch(c, legends) : null;
        }

        /// <summary>
        /// Seconds a legend (by species id) is still away. Cooldowns are saved in its <see cref="LegendRecord"/> as a wall
        /// clock end (unix seconds), so they keep running while the app is closed; a device clock set back is capped at the
        /// cooldown's own length (the end moved up to now + that length).
        /// </summary>
        public static long Remaining(string id)
        {
            var rec = Game.I != null ? Game.I.FindLegend(id) : null;
            if (rec == null || rec.coolUntil <= 0) return 0;
            long now = SaveSystem.Now, left = rec.coolUntil - now;
            if (left > rec.coolLen)
            {
                left = rec.coolLen;
                rec.coolUntil = now + left;
            }
            return left > 0 ? left : 0;
        }

        /// <summary>Every saved cooldown off (the -fkencounter tests).</summary>
        public static void ClearCooldowns()
        {
            if (Game.I == null) return;
            foreach (var r in Game.Data.legends) r.coolUntil = r.coolLen = 0;
        }

        public bool AwayOf(FishSpecies sp) => Remaining(sp.id) > 0;

        /// <summary>Every legend of the stage is away.</summary>
        public bool Away => entries.All(e => AwayOf(e.sp));

        /// <summary>Sends a legend away for this long, saved (no cooldowns in the -fkencounter test).</summary>
        public void Cool(FishSpecies sp, float seconds)
        {
            foreach (var e in entries) e.meter = 0f;
            if (DebugMode != null && !DebugKeepCool)
            {
                HasLurk = false;
                return;
            }
            var rec = Game.I.Legend(sp.id);
            rec.coolLen = Mathf.Max(0, Mathf.CeilToInt(seconds));
            rec.coolUntil = SaveSystem.Now + rec.coolLen;
            Game.I.Save();
            if (Away) HasLurk = false;
        }

        SpriteRenderer Glint(string name)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.sprite = Art.Pixel;
            sr.sortingOrder = 19; // the fish-shadow band
            sr.enabled = false;
            return sr;
        }

        /// <summary>A new cast landed: a fresh soak and a new chance.</summary>
        public void OnCast()
        {
            CastUsed = false;
            Soak = 0f;
            debugT = 0f;
            foreach (var e in entries) e.meter = 0f;
            told4 = told5 = told7 = false;
            CrawlQ = 0f;
        }

        /// <summary>The encounter this cast started (win or lose, one per cast): every meter starts over.</summary>
        public void OnEncounter()
        {
            CastUsed = true;
            foreach (var e in entries) e.meter = 0f;
        }

        /// <summary>-fkencounter natural: the lurk point in front of him (the ice: beside the hole; the ocean: 24 m out).</summary>
        void PlaceNatural()
        {
            if (L.IsIce) Place(new Vector3(L.holeX, 0f, L.holeZ + 4f), true);
            else Place(new Vector3(ctl.Angler.X, 0f, L.mode == "boat" ? 24f : 16f), true);
        }

        /// <summary>
        /// Test hook (-fkauto zoom): the lurk point put here, its next cue in <paramref name="cueIn"/> s; null: no lurk point
        /// (and none appearing for a long while).
        /// </summary>
        internal void DebugLurk(Vector3? at, float cueIn = 1.5f)
        {
            cueRings = 0;
            glintT = -1f;
            cueAge = 99f;
            if (at == null)
            {
                HasLurk = false;
                appearT = 1e6f;
                return;
            }
            Place(at.Value, true);
            cueT = cueIn;
        }

        void Place(Vector3 near, bool exact)
        {
            var d = Primary;
            float x, z;
            if (L.IsIce)
            {
                // under the ice beside the hole: the lure only lives in the hole
                x = exact ? near.x : L.holeX + Random.Range(-5f, 5f);
                z = exact ? near.z : L.holeZ + Random.Range(3f, 11f);
            }
            else
            {
                float zMax = Mathf.Min(ctl.FishZMax, d.lurkZMax);
                // (in open water he can see: not in or by a painted trunk / rock standing in the water, nor behind the
                // front layer, where the cue rings and the eye would not show; Docs/obstacles_spec.md)
                var obs = ctl.Stage.Obstacles;
                var water = ctl.Stage.Water;
                x = z = 0f;
                for (int tries = 0; tries < 12; tries++)
                {
                    z = exact ? near.z : Random.Range(d.lurkZMin, Mathf.Max(d.lurkZMin + 0.5f, zMax));
                    float half = Mathf.Min(L.xLim - 1f, P.VisibleHalfWidth(z, PixelView.Current != null && PixelView.Current.Target != null
                        ? PixelView.Current.Target.width : PixelView.BaseWidth) - 1f);
                    x = exact ? near.x : Random.Range(-half, half);
                    if (exact || ((obs == null || !obs.BlockedAtSurface(new Vector2(x, z), 1f)) && (water == null || !water.BehindFront(x, z)))) break;
                }
            }
            float depth = d.lurkDepth > 0f ? Mathf.Min(d.lurkDepth, L.DepthAt(z) - 0.5f) : L.DepthAt(z) - 0.5f;
            Lurk = new Vector3(x, -depth, z);
            HasLurk = true;
            relocateT = Random.Range(d.relocate.x, d.relocate.y);
            cueT = exact ? 1.5f : Random.Range(2f, 6f);
            if (!announced)
            {
                announced = true;
                ctl.Flash(d.lurkText, new Color32(0xb8, 0xff, 0x8a, 0xff), 2.2f);
            }
        }

        /// <summary>
        /// One frame. <paramref name="soaking"/>: waiting with the lure in the water (the meters run). Returns true when an
        /// encounter should start now (<see cref="Triggered"/> says whose).
        /// </summary>
        public bool Tick(float dt, bool soaking)
        {
            // only between fish (ready, casting, waiting, retrieving): no lurk placing, cues or its announcement while he
            // plays a fish, lands it or looks at the catch card (the legend can't be lurking while he fights it)
            var st = ctl.State;
            bool calm = st == FishingController.S.Ready || st == FishingController.S.Aiming || st == FishingController.S.Casting
                        || st == FishingController.S.Waiting || st == FishingController.S.Retrieving;
            if (!calm)
            {
                cueRings = 0;
                cueAge = 99f;
                glintT = -1f;
                TickGlint(dt);
                foreach (var e in entries) e.meter = Mathf.MoveTowards(e.meter, 0f, dt * 0.5f / e.sp.encounter.fillTime);
                return false;
            }
            TickGlint(dt);
            if (Away)
            {
                wasAway = true;
                HasLurk = false;
                foreach (var e in entries) e.meter = 0f;
                return false;
            }
            if (wasAway)
            {
                wasAway = false;
                appearT = Random.Range(Primary.appear.x, Primary.appear.y);
            }
            // the lurk point: appears, moves on now and then, cues where it is
            if (!HasLurk)
            {
                appearT -= dt;
                if (DebugMode == "natural") PlaceNatural();
                // (the -fkencounter now test: at once, so its announcement comes before the first cast, not mid-fight)
                else if (appearT <= 0f || DebugMode == "now") Place(Vector3.zero, false);
            }
            else
            {
                if (DebugMode == null && (relocateT -= dt) <= 0f && Meter < 0.4f) Place(Vector3.zero, false);
                cueAge += dt;
                if ((cueT -= dt) <= 0f)
                {
                    cueT = Random.Range(15f, 25f);
                    cueRings = 3;
                    cueRingT = 0f;
                    glintT = 0f;
                    cueAge = 0f;
                }
                if (cueRings > 0 && (cueRingT -= dt) <= 0f)
                {
                    cueRings--;
                    cueRingT = 0.25f;
                    Vector3 s;
                    float r;
                    if (L.IsIce)
                    {
                        // under the ice the rings come up inside the hole
                        s = new Vector3(L.holeX + Random.Range(-0.25f, 0.25f) * L.holeR, 0f, L.holeZ + Random.Range(-0.25f, 0.25f) * L.holeR);
                        r = Mathf.Clamp(P.PixelsPerMetre(s) * L.holeR * 0.6f / 64f, 0.04f, 0.2f);
                    }
                    else
                    {
                        s = new Vector3(Lurk.x + Random.Range(-0.3f, 0.3f), 0f, Lurk.z + Random.Range(-0.3f, 0.3f));
                        r = Mathf.Clamp(P.PixelsPerMetre(s) * 0.5f / 64f, 0.05f, 0.3f);
                    }
                    Fx.Ripple(P.To2D(s), r, P.Foreshorten(s) * 1.6f + 0.15f, new Color(1f, 1f, 1f, 0.5f), 0.8f);
                }
            }
            if (!soaking || CastUsed)
            {
                if (!soaking)
                    foreach (var e in entries) e.meter = Mathf.MoveTowards(e.meter, 0f, dt * 0.5f / e.sp.encounter.fillTime);
                return false;
            }
            Soak += dt;
            TickQuality(dt);
            if (DebugMode == "now")
            {
                debugT += dt;
                if (debugT < 1f) return false;
                // the forced legend if the bait is one of its keys, else the first legend it is a key of
                var bait = Game.I.Bait;
                var want = entries.FirstOrDefault(e => e.sp.id == DebugLegend && !AwayOf(e.sp) && e.sp.encounter.KeyWeight(bait.id) > 0f)
                           ?? entries.FirstOrDefault(e => !AwayOf(e.sp) && e.sp.encounter.KeyWeight(bait.id) > 0f);
                if (want == null) return false;
                Triggered = want.sp;
                return true;
            }
            return TickMeters(dt);
        }

        /// <summary>The natural baits' stillness and the crawl (Docs/legends_rollout.md 1.1).</summary>
        void TickQuality(float dt)
        {
            var li = ctl.LureIn;
            StillQ = Mathf.Clamp01(li.PauseT / 3f);
            float s = li.Winding ? (li.Speed >= 0.4f && li.Speed <= 1.2f ? 1f : 0f) : li.PauseT <= 1.5f ? 0.5f : 0f;
            CrawlQ += (s - CrawlQ) * (1f - Mathf.Exp(-dt / 2f));
            if (li.FlickNow) CrawlQ -= 0.2f;
            CrawlQ = Mathf.Clamp01(CrawlQ);
        }

        float QualityOf(MeterQ q) => q == MeterQ.Still ? StillQ : q == MeterQ.Crawl ? CrawlQ : ctl.Rhythm.Q;

        bool TickMeters(float dt)
        {
            var tk = ctl.Tackle;
            var bait = tk.Bait;
            float near = !HasLurk ? 99f : L.IsIce ? 0f : new Vector2(tk.HookPos.x - Lurk.x, tk.HookPos.z - Lurk.z).magnitude;
            bool natural = DebugMode == "natural";
            bool anyKey = false, anyFill = false;
            foreach (var e in entries)
            {
                var d = e.sp.encounter;
                float key = d.KeyWeight(bait.id);
                anyKey |= key > 0f;
                var rule = d.Rule(bait.id);
                float dMin = rule != null && !float.IsNaN(rule.depthMin) ? rule.depthMin : d.depthMin;
                float dMax = rule != null && !float.IsNaN(rule.depthMax) ? rule.depthMax : d.depthMax;
                float band = rule != null && !float.IsNaN(rule.bottomBand) ? rule.bottomBand : d.bottomBand;
                float q = QualityOf(rule?.meterQ ?? d.meterQ);
                e.blocked = key <= 0f ? "lure" : AwayOf(e.sp) ? "away" : !HasLurk ? "lurk" : Soak < (natural ? 2f : d.minSoak) ? "soak"
                    : tk.Depth < dMin ? "shallow" : dMax > 0f && tk.Depth > dMax ? "deep" : band < 90f && tk.Depth < tk.Bottom - band ? "off bottom"
                    : near > d.nearLurk ? "far" : q < d.minQ ? "q" : "";
                // (x its activity at this time of day: Docs/time_currents_spec.md 6.1)
                if (e.blocked == "") e.meter += dt * key * q / d.fillTime * (natural ? 8f : 1f) * TimeActivity.Now(e.sp.id);
                else e.meter -= 0.5f * dt / d.fillTime;
                // without a line that can hold it the build-up stops short
                bool weak = Game.I.Line.strength < d.minLine;
                e.meter = Mathf.Clamp(e.meter, 0f, weak ? 0.7f : 1f);
                anyFill |= e.blocked == "";
                if (weak && e.meter >= 0.7f && !e.gearTold)
                {
                    e.gearTold = true;
                    ctl.Flash($"이 녀석을 상대하려면 {d.gearLine}{(HasFinal(d.gearLine) ? "이" : "가")} 필요할 것 같다… ({d.minLine:0}kg 이상)", UIKit.Bad, 2.6f);
                    told7 = true;
                }
            }
            Tips(dt, bait, near, anyKey, anyFill);
            float meter = Meter;
            if (meter < 0.3f) told4 = false;
            if (meter < 0.4f) told5 = false;
            if (meter < 0.6f) told7 = false;
            if (!told4 && meter >= 0.4f)
            {
                told4 = true;
                ctl.Flash("…깊은 곳에서 무언가 움직인다", UIKit.Sky, 1.8f);
            }
            if (!told5 && meter >= 0.5f)
            {
                told5 = true;
                ctl.ScatterFish(12f);
                ctl.Flash("물고기들이 흩어졌다…!", UIKit.Sky, 1.6f);
            }
            if (meter >= 0.7f)
            {
                if (!told7)
                {
                    told7 = true;
                    ringT = 0f;
                    droneT = 0f;
                    ctl.Flash("줄이 미세하게 떨린다…", UIKit.Sky, 1.6f);
                }
                if ((ringT -= dt) <= 0f)
                {
                    ringT = 0.6f;
                    ctl.TrembleLine();
                }
                if ((droneT -= dt) <= 0f)
                {
                    droneT = 1.6f;
                    Sfx.Play(Sfx.Drone, 0.35f);
                }
            }
            // the roll: the first full meter
            foreach (var e in entries)
            {
                if (e.meter < 1f) continue;
                float p = Mathf.Min(0.95f, e.sp.encounter.chance * Game.I.Rod.luck);
                if (natural || Random.value < p)
                {
                    Triggered = e.sp;
                    return true;
                }
                e.meter = 0.5f;
                told7 = false;
                ctl.Flash("기척이 멀어졌다…", UIKit.Cream, 1.6f);
            }
            return false;
        }

        static bool HasFinal(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            char c = w[w.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0;
        }

        /// <summary>
        /// The hints near the lurk point: a lure no legend here wants (or, for a bait-eater, a wrong natural bait) for 30 s
        /// gives a legend's tipWrongLure (the ocean takes turns, one per visit); on a two-legend stage a key lure that meets
        /// neither pattern for 30 s says what each one chases.
        /// </summary>
        void Tips(float dt, BaitDef bait, float near, bool anyKey, bool anyFill)
        {
            if (!HasLurk) return;
            bool baitEater = entries.Any(e => e.sp.encounter.BaitEater);
            float nearLurk = Primary.nearLurk;
            if (!anyKey && (bait.isLure || baitEater) && near <= nearLurk && !wrongTold)
            {
                wrongT += dt;
                if (wrongT >= 30f)
                {
                    wrongTold = true;
                    string sid = ctl.Stage.Def.id;
                    tipTurn.TryGetValue(sid, out int turn);
                    tipTurn[sid] = turn + 1;
                    ctl.Flash(entries[turn % entries.Count].sp.encounter.tipWrongLure, UIKit.Sky, 2.6f);
                }
            }
            if (entries.Count > 1 && anyKey && !anyFill && near <= nearLurk && !mixTold && Soak >= Primary.minSoak)
            {
                mixT += dt;
                if (mixT >= 30f)
                {
                    mixTold = true;
                    ctl.Flash("청새치는 빠르게 달리는 먹이를, 백상아리는 천천히 비틀거리는 먹이를 쫓는다…", UIKit.Sky, 3.0f);
                }
            }
        }

        /// <summary>The eye glint of a cue: two pixels 2 px apart over the lurk point (on the ice: the hole's centre) for 0.5 s.</summary>
        void TickGlint(float dt)
        {
            bool on = glintT >= 0f && HasLurk && !Away;
            if (on)
            {
                glintT += dt;
                if (glintT > 0.5f) glintT = -1f;
            }
            on &= glintT >= 0f;
            glintA.enabled = glintB.enabled = on;
            if (!on) return;
            var p = L.IsIce ? P.To2D(new Vector3(L.holeX, 0f, L.holeZ)) : P.To2D(P.Apparent(Lurk));
            const float px = 1f / PixelView.PPU;
            float x = Mathf.Round(p.x * PixelView.PPU) / PixelView.PPU, y = Mathf.Round(p.y * PixelView.PPU) / PixelView.PPU;
            // blink out halfway through
            float a = glintT > 0.22f && glintT < 0.3f ? 0f : L.IsIce ? 0.4f : 0.7f;
            // the eye colour of the legend whose meter is higher (the first on a tie)
            var c = Art.Hex(Lead.sp.encounter.eyeCore, a);
            glintA.transform.position = new Vector3(x - px + px * 0.5f, y + px * 0.5f, 0f);
            glintB.transform.position = new Vector3(x + px + px * 0.5f, y + px * 0.5f, 0f);
            glintA.color = glintB.color = c;
        }

        public void Destroy()
        {
            if (glintA != null) Object.Destroy(glintA.gameObject);
            if (glintB != null) Object.Destroy(glintB.gameObject);
        }
    }
}
