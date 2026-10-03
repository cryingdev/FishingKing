using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto stallhunt (with -fkscene Fishing -fkstage sea, -fkbait (default 새우), -fkhunt &lt;tries&gt; (default 200),
    /// -fkhuntseed &lt;n&gt; (default: the clock, logged)): hunts the rig wound in that never comes home. Each try lays the
    /// float on or by a tetrapod field (a random point along a "tet" snag zone, out to 1.5 m past its edge) at a random tide
    /// (slack / the flood's peak / the ebb's, the old rules or the reach's, with or without the tetrapods' slack), no
    /// bites; then either waits for it to snag (forced with DebugSnag after 8 s) and cuts it (끊기: the spent float is wound
    /// in) or winds it straight in (회수: a snag on the way cut too). Caught on a prop on the way (a "prop" snag) it is cut,
    /// or with -fkhuntsweep first swept to the strip's free side for up to 2.5 s (held there while it is wound on once it
    /// slides off). The rig must be home (ready) without the stall watch (FishingController.RetrieveStalls, its [BREAK]
    /// retrieve stalled line) within 40 game s. On the fixed 1/60 s step as fast as it draws. [HUNT] lines; CHECK at the end.
    /// </summary>
    public partial class AutoPilot
    {
        IEnumerator StallHunt()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("[HUNT] CHECK FAIL no FishingController (use -fkscene Fishing -fkstage sea)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.4f);
            }
            var obs = ctl.Stage.Obstacles;
            var tets = obs == null ? new List<Obstacle>() : obs.Snags.Where(o => o.Has("tet")).ToList();
            if (tets.Count == 0 && obs != null) tets = obs.Snags.ToList();
            // the props standing in the water (rocks, posts, the boat ...): the rig is also laid round them (half the tries
            // where there are snag zones too)
            var props = obs == null ? new List<Obstacle>() : obs.Solids.Where(o => o.Standing && !o.Near).ToList();
            if (tets.Count == 0 && props.Count == 0)
            {
                Log($"[HUNT] CHECK FAIL {ctl.Stage.Def.id} has no snag zones or props to cast at");
                Application.Quit();
                yield break;
            }
            int tries = Mathf.Max(1, Mathf.RoundToInt(ArgF("-fkhunt") ?? 200f));
            int seed = Mathf.RoundToInt(ArgF("-fkhuntseed") ?? System.Environment.TickCount % 100000);
            var rnd = new System.Random(seed);
            string baitId = Arg("-fkbait") ?? "bait_shrimp";
            SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
            EquipTest(baitId, ctl);
            FishingController.NoBites = true;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            // -fkhuntsweep: a rig that stops coming home is swept as a player would (the rod held left, then right) before
            // the stall watch (given 8 s) takes it in
            bool sweepTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fkhuntsweep") >= 0;
            if (sweepTest) FishingController.DebugStallTime = 8f;
            int catches = 0, freedSweep = 0, cutCatch = 0;
            Log($"[HUNT] listener volume {AudioListener.volume}, sweep test {sweepTest}");
            Log($"[HUNT] {ctl.Stage.Def.id}: {tries} tries at {tets.Count} zones and {props.Count} props, bait {baitId}, seed {seed}");
            // -fkhuntat <x>,<z>: every try lays the float there (a spot found jamming), the tide still random
            Vector2? at = null;
            var atArg = Arg("-fkhuntat");
            if (atArg != null)
            {
                var xz = atArg.Split(',');
                if (xz.Length == 2 && float.TryParse(xz[0], System.Globalization.NumberStyles.Float, CIc, out float ax)
                    && float.TryParse(xz[1], System.Globalization.NumberStyles.Float, CIc, out float az)) at = new Vector2(ax, az);
            }
            Log($"[HUNT] listener volume {AudioListener.volume}, mute {AudioMix.Muted}, at {(at.HasValue ? at.Value.ToString("0.00") : "random")}");
            float[] phases = { 0.5f, 0.25f, 0.75f };
            int cuts = 0, winds = 0, stalls0 = ctl.RetrieveStalls, stuck = 0, perched = 0, forced = 0;
            // -fkhuntsecs <s>: stop after this long (real time), however many tries are left
            float limit = ArgF("-fkhuntsecs") ?? 0f, start = Time.realtimeSinceStartup;
            int done = 0;
            for (int k = 0; k < tries; k++)
            {
                if (limit > 0f && Time.realtimeSinceStartup - start > limit) break;
                done++;
                Obstacle z;
                Vector2 p;
                if (props.Count > 0 && (tets.Count == 0 || rnd.Next(2) == 0))
                {
                    // round a prop: 0.2-2 m off its footprint's broad circle, any side
                    z = props[rnd.Next(props.Count)];
                    float ang = (float)(rnd.NextDouble() * Mathf.PI * 2f), r = z.R * 0.7f + 0.2f + (float)rnd.NextDouble() * 1.8f;
                    p = at ?? z.C + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                }
                else
                {
                    z = tets[rnd.Next(tets.Count)];
                    float t = (float)rnd.NextDouble();
                    var a = new Vector2(z.x0, z.z0);
                    var b = new Vector2(z.x1, z.z1);
                    var on = Vector2.Lerp(a, b, t);
                    var dir = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector2.right;
                    var side = new Vector2(-dir.y, dir.x);
                    p = at ?? on + side * (((float)rnd.NextDouble() * 2f - 1f) * (z.rad + 1.5f));
                }
                float phase = phases[rnd.Next(phases.Length)];
                bool old = rnd.Next(2) == 0, bare = rnd.Next(2) == 0, cut = rnd.Next(3) != 0;
                GameClock.TidePhase = phase;
                FishingController.DebugOldTide = old;
                CurrentField.DebugNoCushion = bare;
                yield return ToReady(ctl);
                if (Game.I.Bait == null || Game.I.Bait.id != baitId) EquipTest(baitId, ctl);
                var tk = ctl.Tackle;
                if (!ctl.DebugPlaceRig(new Vector3(p.x, 0f, p.y))) continue;
                yield return null;
                if (tk.State == Tackle.Mode.Perched) perched++;
                string how;
                if (cut)
                {
                    // wait for it to catch on the tetrapods (the water may carry it on); else forced where it lies
                    float w = 0f;
                    while (w < 8f && ctl.State == FishingController.S.Waiting)
                    {
                        w += Time.deltaTime;
                        yield return null;
                    }
                    if (ctl.State == FishingController.S.Waiting && !ctl.DebugSnag())
                    {
                        // (out of the water still: perched, say) wound in instead
                        ctl.Retrieve();
                        how = "wound (no snag)";
                        winds++;
                    }
                    else
                    {
                        if (ctl.State == FishingController.S.Snagged && w >= 8f) forced++;
                        ctl.Retrieve();   // (snagged: 끊기)
                        how = "cut";
                        cuts++;
                    }
                }
                else
                {
                    ctl.Retrieve();
                    how = "wound";
                    winds++;
                }
                // home within 40 game s: a snag on the way cut; caught on a prop, cut too, or (-fkhuntsweep) the rod held to
                // the shown free side for up to 2.5 s first, and held there while it is wound on
                int s0 = ctl.RetrieveStalls;
                float g = 0f, sweepT = 0f;
                bool sweeping = false;
                SnagInfo caught = null;
                while (g < 40f && ctl.State != FishingController.S.Ready)
                {
                    var sn = tk.Snag;
                    if (ctl.State == FishingController.S.Snagged && sn != null && sn.kind == "prop" && sn != caught)
                    {
                        caught = sn;
                        catches++;
                        if (sweepTest)
                        {
                            sweeping = true;
                            sweepT = 0f;
                            PointerInput.SimRight = sn.freeSide > 0;
                            PointerInput.SimLeft = sn.freeSide <= 0;
                        }
                    }
                    if (ctl.State == FishingController.S.Snagged)
                    {
                        if (!sweeping || sweepT >= 2.5f)
                        {
                            if (sn != null && sn.kind == "prop") cutCatch++;
                            sweeping = false;
                            PointerInput.SimLeft = PointerInput.SimRight = false;
                            ctl.Retrieve();   // (끊기)
                        }
                    }
                    else if (ctl.State == FishingController.S.Waiting)
                    {
                        if (caught != null && sweeping && sweepT < 2.5f)
                        {
                            freedSweep++;
                            sweepT = 2.5f;   // (freed: wound on with the rod still held that way)
                        }
                        ctl.Retrieve();
                    }
                    if (sweeping) sweepT += Time.deltaTime;
                    if (sweeping && sweepT >= 4f) PointerInput.SimLeft = PointerInput.SimRight = false;
                    g += Time.deltaTime;
                    yield return null;
                }
                PointerInput.SimLeft = PointerInput.SimRight = false;
                bool stalled = ctl.RetrieveStalls > s0, home = ctl.State == FishingController.S.Ready;
                if (!home) stuck++;
                if (stalled || !home || caught != null || k % 20 == 0)
                    Log(string.Format(CIc, "[HUNT] try {0}: {1} at ({2:0.00}, {3:0.00}) by {4}, tide {5:0.00} {6}{7}: {8} after {9:0.0} s{10}{11}",
                        k, how, p.x, p.y, z.id, phase, old ? "old" : "reach", bare ? " no cushion" : "",
                        home ? "home" : $"NOT home ({ctl.State}, tackle {tk.State} at ({tk.Surface.x:0.00}, {tk.Surface.z:0.00}))", g,
                        stalled ? " (STALLED: taken in by the stall watch)" : "",
                        caught != null ? string.Format(CIc, "; caught on {0} at ({1:0.00}, {2:0.00}) free side {3:+0;-0}", caught.zone != null ? caught.zone.id : "?", caught.at.x, caught.at.z, caught.freeSide) : ""));
            }
            FishingController.DebugOldTide = false;
            CurrentField.DebugNoCushion = false;
            FishingController.NoBites = false;
            int stalls = ctl.RetrieveStalls - stalls0;
            FishingController.DebugStallTime = 0f;
            Log($"[HUNT] caught on a prop {catches} (prop catches {ctl.PropCatches}): {freedSweep} slid off with the rod held to the free side, {cutCatch} cut");
            Log($"[HUNT] {done} of {tries} tries: {cuts} cut ({forced} forced snags), {winds} wound, {perched} laid perched; stalls {stalls}, not home {stuck}");
            Log($"[HUNT] CHECK {(stalls == 0 && stuck == 0 ? "PASS" : "FAIL")} every rig wound in by the tetrapods came home (stalls {stalls}, not home {stuck})");
            Log("[HUNT] stall hunt done");
            Application.Quit();
        }
    }
}
