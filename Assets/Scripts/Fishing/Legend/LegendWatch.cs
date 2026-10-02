using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The stage's legends on the surface and their trigger (Docs/lures_legend_spec.md 2.2, Docs/legends_rollout.md 1.3,
    /// 5), ticked by the controller. While a legend is off cooldown a lurk point sits somewhere in the deep water (it
    /// appears appear.x..y s after the stage opens or a cooldown ends and moves every relocate.x..y s), cued every 15-25 s
    /// by bubble rings on the surface above it (on the ice: inside the hole) and its shadow gliding by. Every legend of the
    /// stage (the ocean has two) shares that lurk point and its cues, but has its own meter, cooldown and pity: with one
    /// of its key lures worked the way it likes near the lurk point, its meter fills; the tells (a flash at 0.4, the
    /// ordinary fish scattering at 0.5, the line trembling at 0.7) follow the fullest meter, and the first meter to
    /// reach 1.0 rolls to start that legend's encounter. Without a line strong enough a legend's meter stops at 0.7.
    /// <para>The spot (Docs/lures_legend_spec.md 2.2.1): with each cue the water splashes at a spot near the lurk point (in
    /// the home view, within his cast; on the ice the hole) for spotWindow s, its bursts quickening at the end. Only a new cast that
    /// comes down within spotRadius of it in time, with a rig some legend here wants, claims it, and the meters run only
    /// while it is claimed (the rig in the water within spotHold, default nearLurk, of it); every other condition still
    /// applies. A miss (the window out, a landing outside it, a wrong rig) or a claim lost brings the next spot spotRetry
    /// s later, with no cooldown and no pity. Not in the -fkencounter now test.</para>
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
        /// <summary>The legend whose meter is fullest (the first on a tie): the one the tells and the shadow are about.</summary>
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
        SpriteRenderer shade;
        float shadeT = -1f, shadeDir, nextDir;   // the cue's shadow: s into its glide (-1 none); its heading / the next cue's
        bool wasAway;

        // ---- the spot (Docs/lures_legend_spec.md 2.2.1)
        /// <summary>A spot splashes now, waiting for a cast (its window running).</summary>
        public bool SpotOn { get; private set; }
        /// <summary>A new cast came down in the spot in time and the rig is still near it: the meters may run.</summary>
        public bool SpotClaimed { get; private set; }
        /// <summary>The spot's surface point (the last one offered, kept after it ends for the log / test).</summary>
        public Vector3 Spot { get; private set; }
        public float SpotRadius { get; private set; }
        /// <summary>Seconds the current spot has been up / its window.</summary>
        public float SpotT { get; private set; }
        public float SpotWindow { get; private set; }
        /// <summary>How the last spot ended: "", "claim", "timeout", "outside", "rig", "lost", "away", "busy".</summary>
        public string SpotEnd { get; private set; } = "";
        /// <summary>Spots offered / claimed / missed this visit (the test).</summary>
        public int SpotOffers { get; private set; }
        public int SpotClaims { get; private set; }
        public int SpotMisses { get; private set; }
        /// <summary>Time.time the last spot ended (the retry test).</summary>
        public float SpotEndedAt { get; private set; } = -1f;
        /// <summary>The newest burst's spray is in the air now (the test's shots).</summary>
        public bool SpotSplashing { get; private set; }
        /// <summary>Bursts the current spot has thrown so far (the test: they quicken over its last 4 s).</summary>
        public int SpotBursts { get; private set; }
        /// <summary>Seconds from the newest burst to the next (1.5 s, down to 0.55 s at the window's end).</summary>
        public float SpotBurstGap { get; private set; }
        public Vector2 Spot2D => P.To2D(new Vector3(Spot.x, 0f, Spot.z));
        /// <summary>Test hook (-fkauto zoom's cue cases): this watch's cues offer no spot.</summary>
        bool spotsOff;
        float burstIn, swirlPh, fadeT = -1f, fadeLen = 0.6f, hintAt = -99f;
        float burst0 = 99f, burst1 = 99f, burst2 = 99f;   // s since the spot's last three bursts, newest first
        int seed0, seed1, seed2;
        bool fadeClaim, flyingAtEnd;

        /// <summary>Seconds a cue plays: three rings 0.25 s apart, 0.8 s each, and the shadow's glide (ShadeLen, the longer).</summary>
        public const float CueLength = ShadeLen;

        /// <summary>A cue plays now.</summary>
        public bool CuePlaying => HasLurk && !Away && cueAge < CueLength;

        /// <summary>A cue plays now or starts within <paramref name="lead"/> s (the zoomed view keeps it in frame: FishingController.Zoom.cs).</summary>
        public bool CueSoon(float lead) => CuePlaying || SpotOn || (HasLurk && !Away && cueT <= lead);

        /// <summary>Where a cue's rings come up in the pixel scene (over the lurk point; on the ice: the hole).</summary>
        public Vector2 CueRings2D => L.IsIce ? P.To2D(new Vector3(L.holeX, 0f, L.holeZ)) : P.To2D(new Vector3(Lurk.x, 0f, Lurk.z));

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
            shade = Shade();
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
            EndSpot("away", false);
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

        /// <summary>A new cast landed: a fresh soak and a new chance.</summary>
        public void OnCast()
        {
            CastUsed = false;
            Soak = 0f;
            debugT = 0f;
            foreach (var e in entries) e.meter = 0f;
            told4 = told5 = told7 = false;
            CrawlQ = 0f;
            // (a new cast: the rig that held the spot has left it)
            if (SpotClaimed) EndSpot("lost", false);
        }

        /// <summary>
        /// The new cast's rig came down in the water at <paramref name="at"/> (a landing, a bounce that settled, a perched
        /// rig knocked in): inside the splashing spot in time with a rig some legend here wants, it claims the spot; anywhere
        /// else, or with a wrong rig, the spot is missed. A rig already in the water when the spot came up never counts.
        /// </summary>
        public void OnRigLanded(Vector3 at)
        {
            if (!SpotOn) return;
            float d = L.IsIce ? 0f : new Vector2(at.x - Spot.x, at.z - Spot.z).magnitude;
            var bait = ctl.Tackle.Bait;
            bool keyed = bait != null && !ctl.Tackle.BareHook && entries.Any(e => !AwayOf(e.sp) && e.sp.encounter.KeyWeight(bait.id) > 0f);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[SPOT] landed ({0:0.00}, {1:0.00}) {2:0.00} m from the spot (radius {3:0.0}) at {4:0.0}/{5:0.0}s, rig {6}{7}",
                at.x, at.z, d, SpotRadius, SpotT, SpotWindow, bait != null ? bait.id : "-", keyed ? " (a key)" : " (no key)"));
            if (d > SpotRadius)
            {
                ctl.Flash("빗나갔다… 물보라가 이는 곳 안에 던져야 해요", UIKit.Cream, 1.8f);
                EndSpot("outside", true);
                return;
            }
            if (!keyed)
            {
                ctl.Flash(Lead.sp.encounter.tipWrongLure, UIKit.Sky, 2.6f);
                EndSpot("rig", true);
                return;
            }
            SpotOn = false;
            SpotClaimed = true;
            SpotClaims++;
            SpotEnd = "claim";
            fadeT = 0f;
            fadeLen = 1f;
            fadeClaim = true;
            Sfx.Play(Sfx.Drone, 0.25f);
            ctl.Flash("바로 그 자리! 가만히 기다려 봐요…", new Color32(0xb8, 0xff, 0x8a, 0xff), 2.0f);
        }

        /// <summary>
        /// The spot ends: missed (a miss: <paramref name="miss"/>) or a claim lost. Its marker fades; the next spot comes
        /// with the cue after spotRetry s. No cooldown, no pity.
        /// </summary>
        void EndSpot(string why, bool miss)
        {
            if (!SpotOn && !SpotClaimed) return;
            bool was = SpotOn;
            SpotOn = SpotClaimed = false;
            SpotEnd = why;
            SpotEndedAt = Time.time;
            if (miss) SpotMisses++;
            if (was)
            {
                // (no new bursts: the ripples spread on and die out)
                fadeT = 0f;
                fadeLen = 1.2f;
                fadeClaim = false;
            }
            if (HasLurk && why != "away") cueT = Mathf.Max(0.5f, Primary.spotRetry);
            Debug.Log($"[SPOT] ended: {why}{(miss ? " (a miss)" : "")}, next in {cueT:0.0}s");
        }

        /// <summary>
        /// A spot near the lurk point for this cue: open water in the home view within his cast, deep enough for the
        /// legend's bottom rule, clear of standing props, pads and overhangs by its radius, within nearLurk of the lurk
        /// point; the nearer the lurk point the better, and better still beside the legend's own cover (coverFor). On the
        /// ice: the hole.
        /// </summary>
        bool ChooseSpot(out Vector3 spot, out float radius) => ChooseSpotFor(new Vector2(Lurk.x, Lurk.z), true, out spot, out radius);

        /// <summary>
        /// <see cref="ChooseSpot"/> round any lurk point. On the generated bed (Docs/terrain_depth_spec.md 10) every node
        /// within nearLurk - 1 of it and in his reach is tried (the depth over a disc of 0.6 x the radius), one drawn
        /// among those scoring within 1 of the best; <paramref name="pick"/> false: only whether one exists (no draw).
        /// </summary>
        bool ChooseSpotFor(Vector2 lurk, bool pick, out Vector3 spot, out float radius)
        {
            var d = Primary;
            radius = d.spotRadius;
            if (L.IsIce)
            {
                spot = new Vector3(L.holeX, 0f, L.holeZ);
                radius = L.holeR;
                return true;
            }
            var water = ctl.Stage.Water;
            var obs = ctl.Stage.Obstacles;
            var sp = (entries.FirstOrDefault(e => !AwayOf(e.sp)) ?? entries[0]).sp;
            float reach = Game.I.Rod.castDist - 0.5f;
            var a = new Vector2(ctl.Angler.X, 0f);
            float maxOff = Mathf.Max(1.5f, d.nearLurk - 2f);
            float depthNeed = entries.Where(e => !AwayOf(e.sp)).Select(e => e.sp.encounter.depthMin).DefaultIfEmpty(0f).Min();
            float r = radius;
            bool Ok(Vector2 c)
            {
                if (c.y < L.zNear + 3f || Mathf.Abs(c.x) > L.xLim - r) return false;
                if (depthNeed > 0f && (L.Terrain ? L.Bathy.MinDepthDisc(c.x, c.y, 0.6f * r) : L.DepthAt(c.x, c.y)) < depthNeed + 0.3f) return false;
                if (water != null && (!water.OpenWater(c.x, c.y) || !water.OpenWater(c.x - r, c.y) || !water.OpenWater(c.x + r, c.y)
                                      || !water.OpenWater(c.x, c.y + r * 0.6f) || !water.OpenWater(c.x, c.y - r * 0.6f))) return false;
                if (obs != null && !obs.Empty)
                {
                    if (obs.BlockedAtSurface(c, r)) return false;
                    foreach (var o in obs.Pads) if (obs.Inside(o, c, r * 0.6f)) return false;
                    foreach (var o in obs.All) if (o.Overhang && obs.Inside(o, c, r * 0.6f)) return false;
                }
                return true;
            }
            float Score(Vector2 c)
            {
                float score = -(c - lurk).magnitude * 0.3f;
                if (obs != null && sp.coverFor != null)
                    foreach (var cv in obs.Covers)
                        if (Obstacles.CoverMatch(cv, sp) && !obs.Inside(cv, c) && Obstacles.Dist(cv, c) <= 2.5f)
                        {
                            score += 2f;
                            break;
                        }
                return score;
            }
            spot = default;
            if (L.Terrain)
            {
                var b = L.Bathy;
                float near = d.nearLurk - 1f;
                var cands = new List<(Vector2 c, float score)>();
                float top = float.MinValue;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((lurk.x - near - b.X0) / Bathymetry.Cell)), i1 = Mathf.Min(b.Nx - 1, Mathf.CeilToInt((lurk.x + near - b.X0) / Bathymetry.Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((lurk.y - near - b.Z0) / Bathymetry.Cell)), j1 = Mathf.Min(b.Nz - 1, Mathf.CeilToInt((lurk.y + near - b.Z0) / Bathymetry.Cell));
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    var c = b.NodePos(j * b.Nx + i);
                    if ((c - lurk).magnitude > near || (c - a).magnitude > reach || !Ok(c)) continue;
                    if (!pick)
                    {
                        spot = new Vector3(c.x, 0f, c.y);
                        return true;
                    }
                    float sc = Score(c);
                    cands.Add((c, sc));
                    top = Mathf.Max(top, sc);
                }
                if (cands.Count == 0) return false;
                var good = cands.Where(t => t.score >= top - 1f).ToList();
                var pickC = good[Random.Range(0, good.Count)].c;
                spot = new Vector3(pickC.x, 0f, pickC.y);
                return true;
            }
            bool found = false;
            float best = float.MinValue;
            for (int i = 0; i < 48; i++)
            {
                // (first close to the lurk point, then farther out)
                var c = i == 0 ? lurk : lurk + Random.insideUnitCircle * (i < 20 ? 3f : maxOff);
                var off = c - a;
                if (off.magnitude > reach) c = a + off.normalized * reach;
                if ((c - lurk).magnitude > d.nearLurk - 1f || !Ok(c)) continue;
                float score = Score(c);
                if (score > best)
                {
                    best = score;
                    spot = new Vector3(c.x, 0f, c.y);
                    found = true;
                }
            }
            return found;
        }

        /// <summary>A spot comes up with this cue (the first ever: the hint after the stage's announcement).</summary>
        void OfferSpot()
        {
            if (!ChooseSpot(out var at, out float radius))
            {
                Debug.Log($"[SPOT] no open water for a spot near the lurk point {Lurk:F1}: none this cue");
                // (the generated bed: two cues in a row without a spot move the lurk point)
                if (L.Terrain && ++spotFails >= 2)
                {
                    spotFails = 0;
                    Place(default, false);
                }
                return;
            }
            spotFails = 0;
            var d = Primary;
            Spot = at;
            SpotRadius = radius;
            SpotOn = true;
            SpotClaimed = false;
            SpotT = 0f;
            SpotWindow = d.spotWindow;
            SpotEnd = "";
            SpotOffers++;
            burstIn = 0f;
            swirlPh = Random.value * Mathf.PI * 2f;
            SpotBursts = 0;
            fadeT = -1f;
            flyingAtEnd = false;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[SPOT] offer {0} at ({1:0.00}, {2:0.00}) radius {3:0.0} window {4:0}s, lurk {5}, {6:0.0} m from it, angler x {7:0.00}",
                SpotOffers, at.x, at.z, radius, SpotWindow, Lurk.ToString("F1"), new Vector2(at.x - Lurk.x, at.z - Lurk.z).magnitude, ctl.Angler.X));
            if (!Game.Data.spotHint)
            {
                Game.Data.spotHint = true;
                Game.I.Save();
                // (after the announcement, which shows 2.2 s)
                float wait = Mathf.Max(0f, hintAt + 2.3f - Time.time);
                if (wait <= 0f) ctl.Flash("물보라가 이는 곳으로 던져 보세요", UIKit.Sky, 2.8f);
                else Tween.After(wait, () =>
                {
                    if (ctl != null && SpotOn) ctl.Flash("물보라가 이는 곳으로 던져 보세요", UIKit.Sky, 2.8f);
                });
            }
        }

        /// <summary>
        /// The spot's frame: its window (a cast still in the air when it runs out may still land in it), the claim held
        /// while the rig stays in the water within spotHold (default nearLurk) of it, and its marker (WaterFx.ShowSpot).
        /// </summary>
        void TickSpot(float dt)
        {
            var st = ctl.State;
            if (SpotOn)
            {
                SpotT += dt;
                if (SpotT >= SpotWindow)
                {
                    // (thrown in time and still in the air as it runs out: it may still come down in it)
                    if (SpotT - dt < SpotWindow && st == FishingController.S.Casting) flyingAtEnd = true;
                    if (!(flyingAtEnd && st == FishingController.S.Casting))
                    {
                        ctl.Flash("물보라가 잦아들었다…", UIKit.Cream, 1.6f);
                        EndSpot("timeout", true);
                    }
                }
            }
            else if (SpotClaimed)
            {
                var tk = ctl.Tackle;
                float hold = Primary.spotHold > 0f ? Primary.spotHold : Primary.nearLurk;
                float d = L.IsIce ? 0f : new Vector2(tk.HookPos.x - Spot.x, tk.HookPos.z - Spot.z).magnitude;
                bool inWater = st == FishingController.S.Waiting && tk.State == Tackle.Mode.Water;
                if (!inWater || d > hold)
                {
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[SPOT] claim lost: {0} ({1:0.0} m from the spot, hold {2:0.0})",
                        !inWater ? "rig out of the water (" + st + ")" : "moved off", d, hold));
                    EndSpot("lost", false);
                }
            }
            // the marker
            var water = ctl.Stage.Water;
            if (water == null) return;
            var look = new WaterFx.SpotLook { x = Spot.x, z = Spot.z, radius = L.IsIce ? SpotRadius * 0.8f : SpotRadius, pulse = -1f, swirl = -1f };
            // (the bursts' ripples spread on through the fade, as they would)
            burst0 += dt;
            burst1 += dt;
            burst2 += dt;
            if (SpotOn)
            {
                // a burst of spray every 1.5 s, quickening over the last 4 s to one every 0.55 s (the countdown), the
                // swirl under it turning faster with them
                float hurry = Mathf.Clamp01((SpotWindow - SpotT) / 4f);
                if ((burstIn -= dt) <= 0f)
                {
                    SpotBurstGap = Mathf.Lerp(0.55f, 1.5f, hurry);
                    burstIn += SpotBurstGap;
                    burst2 = burst1;
                    seed2 = seed1;
                    burst1 = burst0;
                    seed1 = seed0;
                    burst0 = 0f;
                    seed0 = SpotOffers * 97 + SpotBursts;
                    SpotBursts++;
                }
                swirlPh += dt * Mathf.Lerp(4.5f, 1.6f, hurry);
                SpotSplashing = burst0 < WaterFx.SpraySecs;
                look.alpha = Mathf.Clamp01(SpotT / 0.3f);
                look.swirl = Mathf.Repeat(swirlPh, Mathf.PI * 2f);
                Bursts(ref look);
                water.ShowSpot(look);
                return;
            }
            SpotSplashing = false;
            if (fadeT >= 0f)
            {
                fadeT += dt;
                float k = fadeT / fadeLen;
                if (k >= 1f)
                {
                    fadeT = -1f;
                    return;
                }
                look.claim = fadeClaim;
                // (a miss: the last ripples die out; a claim: its splash near full strength, then gone)
                look.alpha = fadeClaim ? 1f - k * k : 1f - k;
                look.pulse = fadeClaim ? k : -1f;
                Bursts(ref look);
                water.ShowSpot(look);
            }
        }

        void Bursts(ref WaterFx.SpotLook look)
        {
            look.b0 = burst0;
            look.b1 = burst1;
            look.b2 = burst2;
            look.s0 = seed0;
            look.s1 = seed1;
            look.s2 = seed2;
        }

        /// <summary>The encounter this cast started (win or lose, one per cast): every meter starts over.</summary>
        public void OnEncounter()
        {
            CastUsed = true;
            foreach (var e in entries) e.meter = 0f;
            // (the claimed spot did its work)
            SpotOn = SpotClaimed = false;
            SpotEnd = "encounter";
            fadeT = -1f;
        }

        /// <summary>-fkencounter natural: the lurk point in front of him (the ice: beside the hole; the ocean: 24 m out).</summary>
        void PlaceNatural()
        {
            if (L.IsIce) Place(new Vector3(L.holeX, 0f, L.holeZ + 4f), true);
            else
            {
                var want = new Vector2(ctl.Angler.X, L.mode == "boat" ? 24f : 16f);
                // (the generated bed: on the drop-off beside the weed nearest that, within 4 m)
                if (L.Terrain && Primary.lurkWeedEdge)
                {
                    float bd = 4f;
                    foreach (var q in LurkCandidates())
                        if ((q - want).magnitude <= bd)
                        {
                            bd = (q - want).magnitude;
                            want = q;
                        }
                }
                Place(new Vector3(want.x, 0f, want.y), true);
            }
        }

        /// <summary>
        /// Test hook (-fkauto zoom): the lurk point put here, its next cue in <paramref name="cueIn"/> s; null: no lurk point
        /// (and none appearing for a long while).
        /// </summary>
        internal void DebugLurk(Vector3? at, float cueIn = 1.5f)
        {
            cueRings = 0;
            shadeT = -1f;
            cueAge = 99f;
            // (these cue checks want the cue alone: no spot with it)
            spotsOff = true;
            SpotOn = SpotClaimed = false;
            if (at == null)
            {
                HasLurk = false;
                appearT = 1e6f;
                return;
            }
            Place(at.Value, true);
            cueT = cueIn;
        }

        /// <summary>Test hook (-fkauto legendspot, the look's cases): the splashing spot, if any, goes (no miss), and the next cue with its spot plays on the next frame.</summary>
        internal void DebugSpotNow()
        {
            if (SpotOn || SpotClaimed)
            {
                SpotOn = SpotClaimed = false;
                SpotEnd = "test";
                SpotEndedAt = Time.time;
                fadeT = -1f;
            }
            cueT = 0f;
        }

        // ---- the generated bed (Docs/terrain_depth_spec.md 10)
        List<Vector2> lurkCands;
        float lurkCandsCast = -1f;
        int spotFails;

        /// <summary>
        /// The lurk candidates on the generated bed: WeedEdge nodes (a drop-off beside weed) at least depthMin + 0.5 deep,
        /// z from lurkZMin out to where a spot within his cast can still sit over them, in the view (rebuilt when the
        /// rod changes).
        /// </summary>
        List<Vector2> LurkCandidates()
        {
            float cd = Game.I.Rod.castDist;
            if (lurkCands != null && Mathf.Abs(cd - lurkCandsCast) < 1e-3f) return lurkCands;
            lurkCandsCast = cd;
            lurkCands = new List<Vector2>();
            var b = L.Bathy;
            if (b == null) return lurkCands;
            var d = Primary;
            float zMax = Mathf.Min(Mathf.Min(ctl.FishZMax, d.lurkZMax), cd + d.nearLurk - 2f);
            float viewW = PixelView.Current != null && PixelView.Current.Target != null ? PixelView.Current.Target.width : PixelView.BaseWidth;
            for (int k = 0; k < b.NodeCount; k++)
            {
                if ((b.NodeFlags(k) & BedFlag.WeedEdge) == 0 || b.NodeDepth(k) < d.depthMin + 0.5f) continue;
                var q = b.NodePos(k);
                if (q.y < d.lurkZMin || q.y > zMax) continue;
                float half = Mathf.Min(L.xLim - 1f, P.VisibleHalfWidth(q.y, viewW) - 1f);
                if (Mathf.Abs(q.x) > half) continue;
                lurkCands.Add(q);
            }
            Debug.Log($"[BATHY] lurk candidates for cast {cd:0}: {lurkCands.Count}");
            return lurkCands;
        }

        /// <summary>Test hook (-fkauto depth): the lurk point placed as a cue would place it; where it went.</summary>
        internal Vector3 DebugPlace()
        {
            Place(Vector3.zero, false);
            return Lurk;
        }

        /// <summary>Test hook (-fkauto depth): a spot for the current lurk point (as a cue would choose it).</summary>
        internal bool DebugChooseSpot(out Vector3 s) => ChooseSpot(out s, out _);

        /// <summary>Test hook: the lurk candidates for the rod in hand.</summary>
        internal IReadOnlyList<Vector2> DebugLurkCandidates => LurkCandidates();

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
                // (no farther out than a spot within his cast can sit over it: within nearLurk of the cast's reach)
                float zMax = Mathf.Min(Mathf.Min(ctl.FishZMax, d.lurkZMax), Game.I.Rod.castDist + d.nearLurk - 2f);
                // (in open water he can see: not in or by a painted trunk / rock standing in the water, nor behind the
                // front layer, where the cue rings and the eye would not show; Docs/obstacles_spec.md)
                var obs = ctl.Stage.Obstacles;
                var water = ctl.Stage.Water;
                x = z = 0f;
                bool onEdge = false;
                // the generated bed (Docs/terrain_depth_spec.md 10): a drop-off node beside the weed, in open water he can
                // see, with a spot within his cast
                if (!exact && L.Terrain && d.lurkWeedEdge)
                {
                    var cands = LurkCandidates();
                    for (int tries = 0; tries < 12 && cands.Count > 0; tries++)
                    {
                        var q = cands[Random.Range(0, cands.Count)];
                        if ((obs != null && obs.BlockedAtSurface(q, 1f)) || (water != null && water.BehindFront(q.x, q.y))) continue;
                        if (!ChooseSpotFor(q, false, out _, out _)) continue;
                        x = q.x;
                        z = q.y;
                        onEdge = true;
                        break;
                    }
                }
                for (int tries = 0; tries < 12 && !onEdge; tries++)
                {
                    z = exact ? near.z : Random.Range(d.lurkZMin, Mathf.Max(d.lurkZMin + 0.5f, zMax));
                    float half = Mathf.Min(L.xLim - 1f, P.VisibleHalfWidth(z, PixelView.Current != null && PixelView.Current.Target != null
                        ? PixelView.Current.Target.width : PixelView.BaseWidth) - 1f);
                    x = exact ? near.x : Random.Range(-half, half);
                    if (exact || ((obs == null || !obs.BlockedAtSurface(new Vector2(x, z), 1f)) && (water == null || !water.BehindFront(x, z)))) break;
                }
            }
            float bed = L.DepthAt(x, z);
            float depth = d.lurkDepth > 0f ? Mathf.Min(d.lurkDepth, bed - 0.5f) : bed - 0.5f;
            Lurk = new Vector3(x, -depth, z);
            HasLurk = true;
            relocateT = Random.Range(d.relocate.x, d.relocate.y);
            cueT = exact ? 1.5f : Random.Range(2f, 6f);
            if (!announced)
            {
                announced = true;
                hintAt = Time.time;
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
                shadeT = -1f;
                TickShade(dt);
                EndSpot("busy", false);
                TickSpot(dt);
                foreach (var e in entries) e.meter = Mathf.MoveTowards(e.meter, 0f, dt * 0.5f / e.sp.encounter.fillTime);
                return false;
            }
            TickShade(dt);
            if (Away)
            {
                wasAway = true;
                EndSpot("away", false);
                TickSpot(dt);
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
                // (not while a spot splashes or is held: it lies over the lurk point)
                if (DebugMode == null && (relocateT -= dt) <= 0f && Meter < 0.4f && !SpotOn && !SpotClaimed) Place(Vector3.zero, false);
                cueAge += dt;
                if ((cueT -= dt) <= 0f)
                {
                    cueT = Random.Range(15f, 25f);
                    cueRings = 3;
                    cueRingT = 0f;
                    shadeT = 0f;
                    shadeDir = nextDir;
                    nextDir = NewShadeDir();
                    cueAge = 0f;
                    // with the cue a spot near it splashes, unless one is out or held (never in the -fkencounter now test)
                    if (!SpotOn && !SpotClaimed && !spotsOff && DebugMode != "now") OfferSpot();
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
            TickSpot(dt);
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
                // (the spot first: no build-up without a new cast that came down in it in time and stays near it)
                e.blocked = key <= 0f ? "lure" : AwayOf(e.sp) ? "away" : !HasLurk ? "lurk" : !SpotClaimed ? "spot" : Soak < (natural ? 2f : d.minSoak) ? "soak"
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

        /// <summary>Seconds a cue's shadow takes to glide by / metres it glides (on the ice: under the hole, less).</summary>
        public const float ShadeLen = 2.2f, ShadeGlide = 3f;

        /// <summary>The cue's shadow is drawn now (the test).</summary>
        public bool ShadeShown => shade != null && shade.enabled;
        /// <summary>How far through its glide the cue's shadow is (0..1; -1 none).</summary>
        public float ShadeK => shadeT < 0f ? -1f : Mathf.Clamp01(shadeT / ShadeLen);

        /// <summary>A heading for a cue's shadow: across the view more than towards or away from him (±35° off the sides).</summary>
        static float NewShadeDir() => (Random.value < 0.5f ? 0f : Mathf.PI) + Random.Range(-0.6f, 0.6f);

        /// <summary>The middle of the shadow's glide (the lurk point; on the ice: the hole) and its depth under the surface.</summary>
        Vector3 ShadeMid
        {
            get
            {
                var c = L.IsIce ? new Vector3(L.holeX, 0f, L.holeZ) : new Vector3(Lurk.x, 0f, Lurk.z);
                c.y = -(L.IsIce ? 1f : Mathf.Clamp(-Lurk.y, 0.5f, 1.2f));
                return c;
            }
        }

        /// <summary>A point of the shadow's glide, <paramref name="k"/> 0..1 along heading <paramref name="dir"/> (in the water: the depth).</summary>
        Vector3 ShadeAt(float k, float dir)
        {
            float len = L.IsIce ? Mathf.Min(ShadeGlide, L.holeR * 2f + 1f) : ShadeGlide;
            float s = (k - 0.5f) * len;
            var m = ShadeMid;
            return new Vector3(m.x + Mathf.Cos(dir) * s, m.y, m.z + Mathf.Sin(dir) * s);
        }

        float CueDir => shadeT >= 0f ? shadeDir : nextDir;

        /// <summary>Where the cue's shadow is now in the pixel scene (seen through the surface; between cues: the middle of the next glide).</summary>
        public Vector2 CueShade2D => P.To2D(P.Apparent(ShadeAt(shadeT >= 0f ? ShadeK : 0.5f, CueDir)));
        /// <summary>Where the cue's shadow glides from / to (the zoomed view keeps both in frame).</summary>
        public Vector2 CueShadeFrom2D => P.To2D(P.Apparent(ShadeAt(0f, CueDir)));
        public Vector2 CueShadeTo2D => P.To2D(P.Apparent(ShadeAt(1f, CueDir)));

        SpriteRenderer Shade()
        {
            var root = new GameObject("LegendShade").transform;
            var sr = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            sr.sortingOrder = 19; // the fish-shadow band
            sr.enabled = false;
            nextDir = NewShadeDir();
            return sr;
        }

        /// <summary>
        /// A cue's shadow: the legend's own top-down silhouette (its size, as an ordinary fish's shadow is drawn: FishAgent)
        /// gliding <see cref="ShadeGlide"/> m across the lurk point just under the surface over <see cref="ShadeLen"/> s,
        /// fading in and out. Dark: the fish-shadow tint of the water (the period look's) taken well down, so it reads as a
        /// shape in the water and never as a light.
        /// </summary>
        void TickShade(float dt)
        {
            bool on = shadeT >= 0f && HasLurk && !Away;
            if (on)
            {
                shadeT += dt;
                if (shadeT > ShadeLen) shadeT = -1f;
            }
            on &= shadeT >= 0f;
            shade.enabled = on;
            if (!on) return;
            var sp = Lead.sp;
            int frame = (int)(shadeT / 0.36f) % 2;
            var spr = Art.Get($"Fish/{sp.id}_t{frame}") ?? Art.Fish(sp.id, frame);
            if (spr == null)
            {
                shade.enabled = false;
                return;
            }
            float k = ShadeK;
            var at = ShadeAt(k, shadeDir);
            var seen = P.Apparent(at);
            var pos2 = P.To2D(seen);
            float ppm = P.PixelsPerMetre(seen);
            float vis = FishAgent.VisualLength(Mathf.Lerp(sp.minCm, sp.maxCm, 0.6f));
            float scale = Mathf.Max(10f, ppm * vis) / Mathf.Max(8f, spr.rect.width);
            float squash = P.ShadowSquash(seen);
            var root = shade.transform.parent;
            root.position = new Vector3(Mathf.Round(pos2.x * 16) / 16, Mathf.Round(pos2.y * 16) / 16, 0);
            root.localScale = new Vector3(scale, scale * squash, 1);
            shade.transform.localRotation = Quaternion.Euler(0, 0, shadeDir * Mathf.Rad2Deg);
            shade.sprite = spr;
            // in over its first third, out over its last
            float fade = Mathf.SmoothStep(0f, 1f, k / 0.33f) * Mathf.SmoothStep(0f, 1f, (1f - k) / 0.4f);
            var c = ctl.Stage.FishShadowTint(-at.y, 0.5f, fade);
            c.r *= 0.3f;
            c.g *= 0.3f;
            c.b *= 0.3f;
            c.a *= L.IsIce ? 0.6f : 0.75f;
            shade.color = c;
        }

        public void Destroy()
        {
            if (shade != null) Object.Destroy(shade.transform.parent.gameObject);
        }
    }
}
