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
    /// in) or winds it straight in (회수: a snag on the way cut too). The rig must be home (ready) without the stall
    /// watch (FishingController.RetrieveStalls, its [BREAK] retrieve stalled line) within 40 game s. On the fixed 1/60 s
    /// step as fast as it draws. [HUNT] lines; CHECK at the end.
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
            if (tets.Count == 0)
            {
                Log($"[HUNT] CHECK FAIL {ctl.Stage.Def.id} has no snag zones to cast at");
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
            int jams = 0, outLeft = 0, outRight = 0, outNone = 0;
            Log($"[HUNT] listener volume {AudioListener.volume}, sweep test {sweepTest}");
            Log($"[HUNT] {ctl.Stage.Def.id}: {tries} tries at {tets.Count} zones ({string.Join(", ", tets.Select(o => o.id))}), bait {baitId}, seed {seed}");
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
            for (int k = 0; k < tries; k++)
            {
                var z = tets[rnd.Next(tets.Count)];
                float t = (float)rnd.NextDouble();
                var a = new Vector2(z.x0, z.z0);
                var b = new Vector2(z.x1, z.z1);
                var on = Vector2.Lerp(a, b, t);
                var dir = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector2.right;
                var side = new Vector2(-dir.y, dir.x);
                var p = at ?? on + side * (((float)rnd.NextDouble() * 2f - 1f) * (z.rad + 1.5f));
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
                // home within 40 game s, a snag on the way cut too
                int s0 = ctl.RetrieveStalls;
                float g = 0f, still = 0f, sweepT = 0f, refZ = tk.LineEnd.z;
                int sweep = 0;
                var jamAt = Vector3.zero;
                while (g < 40f && ctl.State != FishingController.S.Ready)
                {
                    if (ctl.State == FishingController.S.Snagged || ctl.State == FishingController.S.Waiting) ctl.Retrieve();
                    if (sweepTest && ctl.State == FishingController.S.Retrieving && tk.State == Tackle.Mode.Water)
                    {
                        // no 0.1 m nearer home in 1 s: jammed; the rod held left for 2.5 s, then right
                        float zNow = tk.LineEnd.z;
                        if (zNow <= refZ - 0.1f)
                        {
                            refZ = zNow;
                            still = 0f;
                        }
                        else still += Time.deltaTime;
                        if (sweep > 0) sweepT += Time.deltaTime;
                        if (sweep == 0 && still >= 1f)
                        {
                            sweep = 1;
                            jamAt = tk.Surface;
                            PointerInput.SimLeft = true;
                            still = sweepT = 0f;
                        }
                        else if (sweep == 1 && sweepT >= 2.5f && still >= 1f)
                        {
                            sweep = 2;
                            PointerInput.SimLeft = false;
                            PointerInput.SimRight = true;
                            still = sweepT = 0f;
                        }
                    }
                    g += Time.deltaTime;
                    yield return null;
                }
                PointerInput.SimLeft = PointerInput.SimRight = false;
                bool stalled = ctl.RetrieveStalls > s0, home = ctl.State == FishingController.S.Ready;
                if (sweep > 0)
                {
                    jams++;
                    string res = !stalled && home ? sweep == 1 ? "out with the rod held left" : "out with the rod held right" : "not out by sweeping";
                    if (!stalled && home)
                    {
                        if (sweep == 1) outLeft++;
                        else outRight++;
                    }
                    else outNone++;
                    Log(string.Format(CIc, "[HUNT] try {0}: jammed at ({1:0.00}, {2:0.00}): {3}", k, jamAt.x, jamAt.z, res));
                }
                if (!home) stuck++;
                if (stalled || !home || k % 20 == 0)
                    Log(string.Format(CIc, "[HUNT] try {0}: {1} at ({2:0.00}, {3:0.00}) by {4}, tide {5:0.00} {6}{7}: {8} after {9:0.0} s{10}",
                        k, how, p.x, p.y, z.id, phase, old ? "old" : "reach", bare ? " no cushion" : "",
                        home ? "home" : $"NOT home ({ctl.State}, tackle {tk.State} at ({tk.Surface.x:0.00}, {tk.Surface.z:0.00}))", g,
                        stalled ? " (STALLED: taken in by the stall watch)" : ""));
            }
            FishingController.DebugOldTide = false;
            CurrentField.DebugNoCushion = false;
            FishingController.NoBites = false;
            int stalls = ctl.RetrieveStalls - stalls0;
            FishingController.DebugStallTime = 0f;
            if (sweepTest) Log($"[HUNT] sweep test: {jams} jammed, {outLeft} out with the rod held left, {outRight} held right, {outNone} not out");
            Log($"[HUNT] {tries} tries: {cuts} cut ({forced} forced snags), {winds} wound, {perched} laid perched; stalls {stalls}, not home {stuck}");
            Log($"[HUNT] CHECK {(stalls == 0 && stuck == 0 ? "PASS" : "FAIL")} every rig wound in by the tetrapods came home (stalls {stalls}, not home {stuck})");
            Log("[HUNT] stall hunt done");
            Application.Quit();
        }
    }
}
