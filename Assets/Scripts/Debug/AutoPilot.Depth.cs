using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FishingKing
{
    /// <summary>
    /// -fkauto depth (Docs/terrain_depth_spec.md 14.1, Docs/lake_phase2_spec.md A8): the lake's generated bed, its fish and
    /// its economy. Run on the lake (-fkscene Fishing -fkstage lake; -fkauto implies world seed 1). [DEPTH] CHECK lines,
    /// then "depth test done: N failed": D1 determinism and the golden hash; D2 seeds 1..50 (V1-V11, V12 over the sweep,
    /// the attempts, the characters, the fallback, the pooled depths); D3 every other stage (and the lake with the grid off,
    /// off the grid: its derived profile) bit-identical; D3b the derived profile = the rows' medians; D4 the queries and the
    /// lake's statistics; D5 the save's world seed; D6 the lying float (tilt, lie, label, hint, standing up, a fight from
    /// it; shots); D7' where the targets go on three lakes (relative bands); D8 the body rule (wander / approach / bite /
    /// flee and fights never in water too shallow, never under the bed); D9 the deep runs; D10 the golden carp's lurk on the
    /// weed edge and its spot; D11' the economy over 50 seeds x 3 rods (the feeding chance, catches and income per minute
    /// within 0.8..1.25 of today's, the best spots); D11b the derived spawn weights; D12 the obstacle bands over the new
    /// bed; D13 the dump and the contour overlay (two seeds); D14 the population and the feeding roll.
    /// </summary>
    public partial class AutoPilot
    {
        int depthFails;
        /// <summary>The golden hash per recipe version (D1: seed 12345); 0 = record it (print and pass with a note).</summary>
        static readonly Dictionary<int, uint> GoldenHash = new Dictionary<int, uint> { [1] = 0x5866ac8fu, [2] = 0xc778575cu };   // (v1, v2: recorded by the Mono player)
        /// <summary>D2's Q50 per seed (D7' picks the shallowest and the deepest lake).</summary>
        readonly Dictionary<int, float> sweepQ50 = new Dictionary<int, float>();

        /// <summary>The CHECK lines' tag: [DEPTH], or [NEWSP] when -fkauto newspecies runs the same gates for one species.</summary>
        string depthTag = "[DEPTH]";

        void DCheck(string what, bool ok)
        {
            if (!ok) depthFails++;
            Log($"{depthTag} CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        IEnumerator DepthTest()
        {
            // the simulated pointer from the start, never pressed: a real mouse on a shared desktop must not touch the rig
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage.Def.id != "lake")
            {
                Log("[DEPTH] needs the lake (-fkscene Fishing -fkstage lake)");
                Application.Quit();
                yield break;
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.4f);
            }
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            var L = ctl.Stage.L;
            DCheck($"the lake has its bed (world seed {(L.Bathy != null ? L.Bathy.WorldSeed : -1)}, the test's 1; save's {Game.I.WorldSeed})",
                L.Terrain && L.Bathy.WorldSeed == 1 && ctl.Habitat != null);
            if (!L.Terrain)
            {
                Log($"[DEPTH] depth test done: {depthFails} failed");
                Application.Quit();
                yield break;
            }
            var recipe = TerrainRecipes.For("lake");
            var raw = Obstacles.ReadSet("lake");
            Log(L.Bathy.Summary());

            DeterminismCheck(L, recipe, raw);
            yield return null;
            yield return SweepCheck(L, recipe, raw);
            LegacyCheck(L);
            QueriesCheck(L);
            SaveSeedCheck(L);
            yield return null;
            yield return HabitatCheck(ctl, recipe, raw);
            BandsCheck(ctl);
            yield return LegendCheck(ctl);
            yield return EconomyCheck(ctl, recipe, raw);
            yield return FloatCheck(ctl);
            yield return BodyCheck(ctl);
            yield return FeedCheck(ctl);
            yield return DeepRunCheck(ctl);
            yield return DepthShots(ctl, recipe, raw);
            Log($"[DEPTH] depth test done: {depthFails} failed");
            Application.Quit();
        }

        // ------------------------------------------------------------------ D1, D2: the generator
        void DeterminismCheck(StageLayout L, TerrainRecipe r, ObstacleSet raw)
        {
            Bathymetry.ClearCache();
            var a = BathyGen.Build(L, r, 1, raw);
            var b = BathyGen.Build(L, r, 1, raw);
            DCheck($"D1 seed 1 built twice: hash 0x{a.Hash:x8} / 0x{b.Hash:x8}, the grid in use 0x{L.Bathy.Hash:x8}", a.Hash == b.Hash && a.Hash == L.Bathy.Hash);
            var g = BathyGen.Build(L, r, 12345, raw);
            GoldenHash.TryGetValue(r.version, out uint gold);
            if (gold == 0u) DCheck($"D1 seed 12345 hash 0x{g.Hash:x8} (recipe v{r.version}: no golden hash yet, recorded: GoldenHash[{r.version}] = 0x{g.Hash:x8})", true);
            else DCheck($"D1 seed 12345 hash 0x{g.Hash:x8} = golden 0x{gold:x8}", g.Hash == gold);
        }

        IEnumerator SweepCheck(StageLayout L, TerrainRecipe r, ObstacleSet raw)
        {
            var hist = new int[r.attempts + 1];
            int fallbacks = 0, failed = 0, first1011 = 0, firstAny = 0;
            var hashes = new HashSet<uint>();
            float msMax = 0f, msSum = 0f, rowOff = 0f;
            int rowOffSeed = 0;
            string firstFail = "";
            var v8 = new List<string>();
            var q50 = new List<float>();
            var weed = new List<float>();
            var gravel = new List<float>();
            // the pooled depths of the 50 lakes' R_ref (every lake and row the same weight), whole cm
            var pool = new double[1001];
            double poolW = 0;
            sweepQ50.Clear();
            Log("[DEPTH] D2 characters: seed attempt label u(depth weed rock side) Dm Q10/Q50/Q90 weed/gravel/sand/mud % flat+shoal/drop-off/hole+channel/hump % holes humps weed-flats");
            for (int s = 1; s <= 50; s++)
            {
                var b = BathyGen.Build(L, r, s, raw);
                hist[Mathf.Min(r.attempts, b.Attempt)]++;
                if (b.Fallback) fallbacks++;
                hashes.Add(b.Hash);
                msMax = Mathf.Max(msMax, b.BuildMs);
                msSum += b.BuildMs;
                var lines = b.Checks.Split('\n');
                // (the first attempt's report: V10 / V11 there are the character ranges' tuning target, at most 10 %)
                int a0 = Array.FindIndex(lines, l => l.StartsWith("attempt 0"));
                int a1 = a0 < 0 ? -1 : Array.FindIndex(lines, a0 + 1, l => l.StartsWith("attempt") || l.StartsWith("fallback"));
                if (a1 < 0) a1 = lines.Length;
                bool f1011 = false, fAny = false;
                for (int i = Mathf.Max(0, a0 + 1); i < a1; i++)
                    if (lines[i].Contains(" FAIL "))
                    {
                        fAny = true;
                        if (lines[i].StartsWith("V10") || lines[i].StartsWith("V11")) f1011 = true;
                    }
                if (f1011) first1011++;
                if (fAny) firstAny++;
                // (the accepted attempt's report: the last block)
                int start = Array.FindLastIndex(lines, l => l.StartsWith("attempt") || l.StartsWith("fallback"));
                bool ok = true;
                for (int i = Mathf.Max(0, start); i < lines.Length; i++)
                {
                    if (lines[i].Contains(" FAIL "))
                    {
                        ok = false;
                        if (firstFail.Length == 0) firstFail = $" (seed {s}: {lines[i]})";
                    }
                    if (lines[i].StartsWith("V8") && s <= 3) v8.Add($"seed {s}:{lines[i].Substring(lines[i].IndexOf(':') + 1)}");
                }
                if (!ok) failed++;
                if (s == 1 || s == 2) foreach (var l in lines) if (l.Length > 0) Log("[DEPTH] seed " + s + " " + l);
                // D3b: the derived profile is the finished rows' medians
                for (int j = 0; j < b.Nz; j++)
                {
                    float off = Mathf.Abs(b.FinalRowMedian(j) - b.ProfileRows[j]);
                    if (off > rowOff)
                    {
                        rowOff = off;
                        rowOffSeed = s;
                    }
                }
                var ch = b.Character;
                float q1 = b.Quantile(10f), q5 = b.Quantile(50f), q9 = b.Quantile(90f);
                q50.Add(q5);
                sweepQ50[s] = q5;
                weed.Add(b.MatShare(BedMat.Weed));
                gravel.Add(b.MatShare(BedMat.Gravel));
                for (int q = 0; q < b.RefNodes.Length; q++)
                {
                    pool[Mathf.Clamp(Mathf.RoundToInt(b.NodeDepth(b.RefNodes[q]) * 100f), 0, 1000)] += b.RefU[q];
                    poolW += b.RefU[q];
                }
                Log(string.Format(CIc, "[DEPTH] D2 seed {0} attempt {1}{2} \"{3}\" u {4:0.00} {5:0.00} {6:0.00} {7:0.00} Dm {8:0.00} Q {9:0.00}/{10:0.00}/{11:0.00} mat {12:0.0}/{13:0.0}/{14:0.0}/{15:0.0} kind {16:0.0}/{17:0.0}/{18:0.0}/{19:0.0} features {20} {21} {22}",
                    s, b.Attempt, b.Fallback ? " fallback" : "", ch.label, ch.uDepth, ch.uWeed, ch.uRock, ch.uSide, ch.mainDepth, q1, q5, q9,
                    100f * b.MatShare(BedMat.Weed), 100f * b.MatShare(BedMat.Gravel), 100f * b.MatShare(BedMat.Sand), 100f * b.MatShare(BedMat.Mud),
                    100f * (b.KindShare(BedKind.Flat) + b.KindShare(BedKind.Shoal)), 100f * b.KindShare(BedKind.Dropoff), 100f * (b.KindShare(BedKind.Hole) + b.KindShare(BedKind.Channel)),
                    100f * b.KindShare(BedKind.Hump), ch.holes, ch.humps, ch.weedFlats));
                if (s % 10 == 0) yield return null;
            }
            DCheck(string.Format(CIc, "D2 seeds 1..50: V1-V11 pass on every accepted grid ({0} failed{1}); attempts {2}; fallback {3}; distinct hashes {4}/50; built in max {5:0.0} ms, mean {6:0.0} ms",
                failed, firstFail, string.Join(" ", hist.Select((c, i) => i + ":" + c)), fallbacks, hashes.Count, msMax, msSum / 50f),
                failed == 0 && fallbacks == 0 && hashes.Count == 50);
            DCheck($"D2 first attempts failing V10 or V11: {first1011} of 50 (<= 5: the character ranges' target); any check {firstAny} of 50", first1011 <= 5);
            foreach (var l in v8) Log("[DEPTH] " + l);
            // V12: the seeds make different lakes
            float span = q50.Max() - q50.Min(), wSpan = 100f * (weed.Max() - weed.Min()), gSpan = 100f * (gravel.Max() - gravel.Min());
            int shallow = q50.Count(q => q <= 2.8f), deep = q50.Count(q => q >= 4.2f);
            DCheck(string.Format(CIc, "D2 V12 over seeds 1..50: Q50 {0:0.00}..{1:0.00} m (span {2:0.00} >= 2.0; {3} lakes <= 2.8 and {4} >= 4.2, each >= 5); weed {5:0.0}..{6:0.0}% (span {7:0.0} >= 15 points); gravel {8:0.0}..{9:0.0}% (span {10:0.0} >= 6)",
                q50.Min(), q50.Max(), span, shallow, deep, 100f * weed.Min(), 100f * weed.Max(), wSpan, 100f * gravel.Min(), 100f * gravel.Max(), gSpan),
                span >= 2.0f && shallow >= 5 && deep >= 5 && wSpan >= 15f && gSpan >= 6f);
            DCheck(string.Format(CIc, "D3b seeds 1..50: the derived profile P* = the finished rows' medians (worst {0:0.00} m{1}, <= 0.25)", rowOff, rowOffSeed > 0 ? " on seed " + rowOffSeed : ", every seed exact"), rowOff <= 0.25f);
            // (information) the pooled depths: the old metre bands' percentiles
            float Pct(float m)
            {
                double acc = 0;
                int c = Mathf.Clamp(Mathf.RoundToInt(m * 100f), 0, 1001);
                for (int i = 0; i < c; i++) acc += pool[i];
                return poolW > 0 ? (float)(100.0 * acc / poolW) : 0f;
            }
            string Q(double p)
            {
                double acc = 0;
                for (int i = 0; i <= 1000; i++)
                {
                    acc += pool[i];
                    if (acc >= p * poolW) return (i * 0.01f).ToString("0.00", CIc);
                }
                return "9.00";
            }
            Log($"[DEPTH] D2 the 50 lakes pooled: Q10 {Q(0.1)} Q25 {Q(0.25)} Q50 {Q(0.5)} Q75 {Q(0.75)} Q90 {Q(0.9)} m; the old metre bands: " +
                string.Join(", ", new[] { ("crucian_carp", 1.2f, 4.0f), ("bluegill", 0.8f, 3.0f), ("carp", 3.0f, 7.0f), ("largemouth_bass", 1.0f, 4.5f) }
                    .Select(t => string.Format(CIc, "{0} {1:0.0}-{2:0.0} m = p{3:0}-p{4:0}", t.Item1, t.Item2, t.Item3, Pct(t.Item2), Pct(t.Item3)))));
            var fb = BathyGen.BuildFallback(L, r, 1, raw, out int fbFails, out string report);
            foreach (var l in report.Split('\n')) if (l.Length > 0) Log("[DEPTH] fallback " + l);
            DCheck($"D2 the fallback (the middle character, no channel, holes, humps or extra flats) passes V1-V10 and V11's materials, flat + shoal and drop-off ({fbFails} failed, hash 0x{fb.Hash:x8}; \"{fb.Character.label}\")", fbFails == 0);
        }

        // ------------------------------------------------------------------ D3: every other stage bit-identical
        /// <summary>The old StageLayout.DepthAt(z), verbatim: BaseDepthAt(z) + TideOffset.</summary>
        static float OldDepth(StageLayout L, float z)
        {
            float Base(float zz)
            {
                if (L.depthZ == null || L.depthZ.Length == 0) return 6f;
                if (zz <= L.depthZ[0]) return L.depthV[0];
                for (int i = 1; i < L.depthZ.Length; i++)
                {
                    if (zz <= L.depthZ[i])
                        return Mathf.Lerp(L.depthV[i - 1], L.depthV[i], Mathf.InverseLerp(L.depthZ[i - 1], L.depthZ[i], zz));
                }
                return L.depthV[L.depthV.Length - 1];
            }
            return Base(z) + StageLayout.TideOffset;
        }

        static int SameAsOld(StageLayout L, out int n)
        {
            int off = 0;
            n = 0;
            foreach (float x in new[] { -20f, 0f, 13.7f })
                for (float z = 0f; z <= L.zFar; z += 0.5f)
                {
                    n++;
                    float o = OldDepth(L, z), a = L.DepthAt(x, z), p = L.ProfileDepth(z);
                    if (BitConverter.SingleToInt32Bits(a) != BitConverter.SingleToInt32Bits(o) || BitConverter.SingleToInt32Bits(p) != BitConverter.SingleToInt32Bits(o)) off++;
                }
            return off;
        }

        void LegacyCheck(StageLayout lake)
        {
            float tideWas = StageLayout.TideOffset;
            foreach (var id in new[] { "stream", "sea", "swamp", "ice", "cave", "ocean" })
            {
                var L = Art.Layout(id);
                bool none = Bathymetry.For(L, 1) == null && L.Bathy == null;
                var tides = id == "sea" ? new[] { 0f, 0.4f, -0.4f } : new[] { 0f };
                int off = 0, n = 0;
                foreach (float t in tides)
                {
                    StageLayout.TideOffset = t;
                    off += SameAsOld(L, out int nn);
                    n += nn;
                }
                StageLayout.TideOffset = tideWas;
                DCheck($"D3 {id}: no grid ({none}); DepthAt(x, z) = ProfileDepth(z) = the old DepthAt(z) bit for bit at {n} points{(id == "sea" ? " (tide offset 0, +0.4, -0.4)" : "")} ({off} differ)", none && off == 0);
            }
            // the lake with the grid off
            var grid = lake.Bathy;
            Bathymetry.Off = true;
            bool offNull = Bathymetry.For(lake, 1) == null;
            Bathymetry.Off = false;
            lake.Bathy = null;
            int lakeOff = SameAsOld(lake, out int ln);
            lake.Bathy = grid;
            DCheck($"D3 lake with -fkbathy off: no grid ({offNull}), the old profile bit for bit at {ln} points ({lakeOff} differ)", offNull && lakeOff == 0);
            // off the grid: the profile; across its far edges a step of at most 2 cm
            int offGrid = 0;
            float jump = 0f;
            for (float z = 0f; z <= 63.5f; z += 0.5f)
            {
                foreach (float x in new[] { -48.3f, 48.3f })
                    if (lake.DepthAt(x, z) != lake.ProfileDepth(z)) offGrid++;
                jump = Mathf.Max(jump, Mathf.Abs(lake.DepthAt(47.99f, z) - lake.DepthAt(48.01f, z)), Mathf.Abs(lake.DepthAt(-47.99f, z) - lake.DepthAt(-48.01f, z)));
            }
            for (float x = -47.5f; x <= 47.5f; x += 0.5f)
            {
                if (lake.DepthAt(x, 64.3f) != lake.ProfileDepth(64.3f)) offGrid++;
                jump = Mathf.Max(jump, Mathf.Abs(lake.DepthAt(x, 63.99f) - lake.DepthAt(x, 64.01f)));
            }
            DCheck($"D3 lake off the grid = its derived profile ProfileDepth = P* ({offGrid} differ); the step across the grid's edge {F2(jump)} m (<= 0.02)", offGrid == 0 && jump <= 0.02f);
            // the authored profile (today's lake for the economy) is the old DepthAt(z), bit for bit, whatever the bed
            int authOff = 0, an = 0;
            foreach (float t in new[] { 0f, 0.4f })
            {
                StageLayout.TideOffset = t;
                for (float z = 0f; z <= lake.zFar; z += 0.5f)
                {
                    an++;
                    if (BitConverter.SingleToInt32Bits(lake.AuthoredDepth(z)) != BitConverter.SingleToInt32Bits(OldDepth(lake, z))) authOff++;
                }
            }
            StageLayout.TideOffset = tideWas;
            DCheck($"D3 lake AuthoredDepth(z) = the old DepthAt(z) bit for bit with the bed on at {an} points (tide 0, +0.4) ({authOff} differ)", authOff == 0);
        }

        // ------------------------------------------------------------------ D4: the queries
        void QueriesCheck(StageLayout L)
        {
            var b = L.Bathy;
            int nodeOff = 0, midOff = 0, gradOff = 0, downOff = 0, edgeOff = 0, alongOff = 0, discOff = 0, gradN = 0, downN = 0;
            var rng = new System.Random(4);
            for (int t = 0; t < 2000; t++)
            {
                int i = rng.Next(1, b.Nx - 2), j = rng.Next(1, b.Nz - 2);
                int k = j * b.Nx + i;
                var p = b.NodePos(k);
                if (Mathf.Abs(b.Depth(p.x, p.y) - b.NodeDepth(k)) > 1e-4f) nodeOff++;
                float mean = (b.NodeDepth(k) + b.NodeDepth(k + 1) + b.NodeDepth(k + b.Nx) + b.NodeDepth(k + b.Nx + 1)) * 0.25f;
                if (Mathf.Abs(b.Depth(p.x + 0.25f, p.y + 0.25f) - mean) > 1e-3f) midOff++;
                var q = p + new Vector2((float)rng.NextDouble() * 0.5f, (float)rng.NextDouble() * 0.5f);
                var g = b.Gradient(q.x, q.y);
                float fx = b.Depth(q.x + 0.5f, q.y) - b.Depth(q.x - 0.5f, q.y);
                if (Mathf.Abs(fx) > 0.05f)
                {
                    gradN++;
                    if (Mathf.Sign(fx) != Mathf.Sign(g.x)) gradOff++;
                }
                var dd = b.DeeperDir(q.x, q.y);
                if (b.Slope(q.x, q.y) > 0.1f)
                {
                    // (across the point at the gradient's own scale, +-Cell: the deeper side is the way it points)
                    downN++;
                    if (b.Depth(q.x + dd.x * Bathymetry.Cell, q.y + dd.y * Bathymetry.Cell) < b.Depth(q.x - dd.x * Bathymetry.Cell, q.y - dd.y * Bathymetry.Cell) - 1e-3f) downOff++;
                }
                if ((b.NodeFlags(k) & BedFlag.Edge) != 0 && b.EdgeDist(p.x, p.y) > 1e-3f) edgeOff++;
                var e = q + new Vector2((float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 6f - 3f);
                float along = b.MinDepthAlong(q, e);
                if (along > L.DepthAt(q.x, q.y) + 1e-4f || along > L.DepthAt(e.x, e.y) + 1e-4f) alongOff++;
                if (b.MinDepthDisc(q.x, q.y, 0.9f) > L.DepthAt(q.x, q.y) + 1e-4f) discOff++;
            }
            int unnamed = b.Zones.Count(z => string.IsNullOrEmpty(z.name));
            DCheck($"D4 queries at 2000 points: node = its value ({nodeOff} off), cell middle = the 4 nodes' mean ({midOff}), gradient's sign = finite differences ({gradOff} of {gradN}), DeeperDir goes down ({downOff} of {downN}), EdgeDist 0 on edges ({edgeOff}), MinDepthAlong <= its ends ({alongOff}), MinDepthDisc <= its centre ({discOff}); {b.Zones.Count} zones, {unnamed} unnamed ({string.Join(", ", b.Zones.Select(z => z.id + " " + z.name))})",
                nodeOff == 0 && midOff == 0 && gradOff == 0 && downOff == 0 && edgeOff == 0 && alongOff == 0 && discOff == 0 && unnamed == 0);
            // the lake's statistics (R_ref): the quantiles monotone from its shallowest to its deepest node, the ranks consistent
            float refMin = float.MaxValue, refMax = 0f;
            foreach (int k in b.RefNodes)
            {
                refMin = Mathf.Min(refMin, b.NodeDepth(k));
                refMax = Mathf.Max(refMax, b.NodeDepth(k));
            }
            int down = 0;
            for (float p = 0.5f; p <= 100f; p += 0.5f) if (b.Quantile(p) < b.Quantile(p - 0.5f)) down++;
            int rankOff = 0;
            for (int t = 0; t < 2000; t++)
            {
                int k = rng.Next(0, b.NodeCount);
                if (Mathf.Abs(b.NodeRankPct(k) - b.RankPct(b.NodeDepth(k))) > 0.051f) rankOff++;
            }
            float shares = 0f;
            foreach (BedMat m in Enum.GetValues(typeof(BedMat))) shares += b.MatShare(m);
            DCheck(string.Format(CIc, "D4 the lake's statistics: Quantile monotone ({0} steps down), Q(0) {1:0.00} = R_ref's least {2:0.00}, Q(100) {3:0.00} = its most {4:0.00}; NodeRankPct = RankPct(NodeDepth) at 2000 nodes ({5} off); the materials' shares sum to {6:0.000}",
                down, b.Quantile(0f), refMin, b.Quantile(100f), refMax, rankOff, shares),
                down == 0 && Mathf.Abs(b.Quantile(0f) - refMin) < 1e-4f && Mathf.Abs(b.Quantile(100f) - refMax) < 1e-4f && rankOff == 0 && Mathf.Abs(shares - 1f) < 1e-3f);
            Log(b.CharacterLine());
        }

        // ------------------------------------------------------------------ D5: the save's seed
        void SaveSeedCheck(StageLayout L)
        {
            var old = JsonUtility.FromJson<SaveData>("{\"coins\":5,\"level\":3}");
            bool zero = old.worldSeed == 0;
            bool minted = SaveSystem.EnsureWorldSeed(old) && old.worldSeed != 0;
            bool again = !SaveSystem.EnsureWorldSeed(old);
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(old));
            var fresh = SaveData.NewGame();
            DCheck($"D5 an old save (no worldSeed: {old.worldSeed} after) gets one once ({zero}, {minted}, then left: {again}); it survives ToJson / FromJson ({back.worldSeed}); a new game has one ({fresh.worldSeed})",
                zero && minted && again && back.worldSeed == old.worldSeed && fresh.worldSeed != 0);
            // the test's seed (-fkauto -> 1) never touches the save file
            string file = Path.Combine(Application.persistentDataPath, (Arg("-fksave") ?? "fishingking_save") + ".json");
            Game.I.Save();
            byte[] before = File.Exists(file) ? File.ReadAllBytes(file) : new byte[0];
            int savedSeed = Game.I.WorldSeed;
            Bathymetry.ClearCache();
            var b = Bathymetry.For(L, Bathymetry.SeedOverride ?? 1);
            Game.I.Save();
            byte[] after = File.Exists(file) ? File.ReadAllBytes(file) : new byte[0];
            DCheck($"D5 -fkauto's seed {Bathymetry.SeedOverride} builds the lake (hash 0x{b.Hash:x8} = in use 0x{L.Bathy.Hash:x8}) and leaves the save as it was (world seed {savedSeed} -> {Game.I.WorldSeed}, file {before.Length} -> {after.Length} bytes, same {before.SequenceEqual(after)})",
                b.Hash == L.Bathy.Hash && savedSeed == Game.I.WorldSeed && before.SequenceEqual(after) && savedSeed != 0);
        }

        // ------------------------------------------------------------------ D7': where the targets go, on three lakes
        /// <summary>
        /// D7' (Docs/lake_phase2_spec.md A8): 2000 targets per ordinary species and period (the bamboo's water, z &lt;= 30) on
        /// seed 1, the shallowest-Q50 and the deepest-Q50 lake of D2's sweep. The relative bands put each fish at the same
        /// depth rank on every lake: carp deep by day, bluegill shallow at dawn and by day, every species' mean rank within 10
        /// points across the three lakes; on seed 1 the six kind shifts (the strings' kind weights) still show.
        /// </summary>
        IEnumerator HabitatCheck(FishingController ctl, TerrainRecipe r, ObstacleSet raw)
        {
            var L = ctl.Stage.L;
            float zMax = 30f;   // (the bamboo rod's FishZMax)
            int shallowSeed = sweepQ50.Count > 0 ? sweepQ50.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key : 1;
            int deepSeed = sweepQ50.Count > 0 ? sweepQ50.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key : 1;
            var seeds = new[] { 1, shallowSeed, deepSeed };
            var species = OrdinaryOf(ctl);
            var mean = new Dictionary<(string, int), float[]>();
            var uniMean = new float[3];
            Dictionary<string, float[][]> sh = null, shM = null;   // (seed 1: the targets' share per bed kind / material, for the shifts and the niche gates)
            float[] uni = null, uniM = null;
            Shares seed1 = null;
            for (int li = 0; li < 3; li++)
            {
                var b = li == 0 ? L.Bathy : BathyGen.Build(L, r, seeds[li], raw);
                var s = SampleShares(b, new FishHabitat(ctl.Stage, ctl, b, true), species, zMax);
                foreach (var sp in species)
                    for (int p = 0; p < 4; p++)
                    {
                        if (!mean.TryGetValue((sp.id, p), out var m)) mean[(sp.id, p)] = m = new float[3];
                        m[li] = s.rank[sp.id][p];
                    }
                uniMean[li] = s.uniMean;
                if (li == 0)
                {
                    seed1 = s;
                    sh = s.kind;
                    shM = s.mat;
                    uni = s.uni;
                    uniM = s.uniM;
                }
                Log(string.Format(CIc, "[DEPTH] D7' lake {0} (seed {1}, Q50 {2:0.00} m, \"{3}\"): uniform targets' mean rank {4:0.0}", li, seeds[li], b.Quantile(50f), b.Character.label, uniMean[li]));
                yield return null;
            }
            foreach (var sp in species)
                Log("[DEPTH] D7' " + sp.id + " mean rank (seed 1 / shallow / deep) " + string.Join(" | ", Enumerable.Range(0, 4).Select(p =>
                    GameClock.Id((Period)p) + " " + string.Join("/", mean[(sp.id, p)].Select(v => v.ToString("0", CIc))))));
            for (int p = 0; p < 4; p++)
                foreach (var sp in species)
                    Log("[DEPTH] D7' " + sp.id + " " + GameClock.Id((Period)p) + " " + string.Join(" ", Enumerable.Range(0, 9).Select(k => $"{(BedKind)k}:{F2(sh[sp.id][p][k])}/{F2(uni[k])}")));
            // carp deep by day, bluegill shallow at dawn and by day (the spec's absolute 60 for the carp is logged: its beta 0.65
            // steers at most about two thirds of the density, which caps the mean rank near 57 on these lakes)
            bool hasCarp = mean.ContainsKey(("carp", 1)), hasBg = mean.ContainsKey(("bluegill", 0));
            var carp = hasCarp ? mean[("carp", 1)] : new float[3];
            bool carpOk = hasCarp && Enumerable.Range(0, 3).All(i => carp[i] >= uniMean[i] + 10f);
            DCheck(string.Format(CIc, "D7' carp by day: mean rank {0:0}/{1:0}/{2:0} (seed 1 / shallow / deep) >= the uniform targets' {3:0}/{4:0}/{5:0} + 10 (spec 60: {6})",
                carp[0], carp[1], carp[2], uniMean[0], uniMean[1], uniMean[2], carp.All(v => v >= 60f) ? "met" : "not reached"), carpOk);
            var bgDawn = hasBg ? mean[("bluegill", 0)] : new float[3];
            var bgDay = hasBg ? mean[("bluegill", 1)] : new float[3];
            DCheck(string.Format(CIc, "D7' bluegill at dawn {0:0}/{1:0}/{2:0} and by day {3:0}/{4:0}/{5:0} (<= 40)", bgDawn[0], bgDawn[1], bgDawn[2], bgDay[0], bgDay[1], bgDay[2]),
                hasBg && bgDawn.All(v => v <= 40f) && bgDay.All(v => v <= 40f));
            // (the four phase-1 species within 10 points; the step-2 species within 12.5: their bed-material weights, gravel /
            // sand up to 1.7, follow where a lake of that character keeps those beds, which moves their depth rank with it)
            var phase1 = new HashSet<string> { "crucian_carp", "bluegill", "carp", "largemouth_bass" };
            float spread = 0f, spread2 = 0f;
            string worst = "", worst2 = "";
            foreach (var kv in mean)
            {
                float d = kv.Value.Max() - kv.Value.Min();
                bool p1 = phase1.Contains(kv.Key.Item1);
                if (d <= (p1 ? spread : spread2)) continue;
                string w = $" ({kv.Key.Item1} {GameClock.Id((Period)kv.Key.Item2)})";
                if (p1)
                {
                    spread = d;
                    worst = w;
                }
                else
                {
                    spread2 = d;
                    worst2 = w;
                }
            }
            DCheck(string.Format(CIc, "D7' every species and period: the mean rank differs at most {0:0.0} points across the three lakes{1} for the phase-1 species (<= 10), {2:0.0}{3} for the step-2 species (<= 12.5)", spread, worst, spread2, worst2),
                spread <= 10f && spread2 <= 12.5f);
            // the six kind shifts on seed 1 (phase 1's checks; three thresholds eased to a clear preference, logged against the old)
            float S(float[] x, params BedKind[] ks) => ks.Sum(k => x[(int)k]);
            bool all = sh.ContainsKey("largemouth_bass") && sh.ContainsKey("crucian_carp") && sh.ContainsKey("carp");
            if (!all)
            {
                DCheck("D7' the kind shifts: the lake's four first species", false);
                yield break;
            }
            float bassDay = S(sh["largemouth_bass"][1], BedKind.Dropoff, BedKind.Hump, BedKind.Shoal) / S(uni, BedKind.Dropoff, BedKind.Hump, BedKind.Shoal);
            float bassDawn = S(sh["largemouth_bass"][0], BedKind.Flat, BedKind.Shelf) / S(uni, BedKind.Flat, BedKind.Shelf);
            float cruDawn = sh["crucian_carp"][0][(int)BedKind.Flat] / uni[(int)BedKind.Flat];
            float cruDay = sh["crucian_carp"][1][(int)BedKind.Flat] / sh["crucian_carp"][0][(int)BedKind.Flat];
            float carpNight = S(sh["carp"][3], BedKind.Flat, BedKind.Shoal) / S(uni, BedKind.Flat, BedKind.Shoal);
            float carpDay = S(sh["carp"][1], BedKind.Hole, BedKind.Channel, BedKind.Basin) / S(uni, BedKind.Hole, BedKind.Channel, BedKind.Basin);
            DCheck($"D7' seed 1's kind shifts (flat + shelf {F2(S(uni, BedKind.Flat, BedKind.Shelf))} of the uniform targets): bass by day on drop-off + hump + shoal x{F2(bassDay)} (>= 1.4), at dawn on flat + shelf x{F2(bassDawn)} (>= 1.1; phase 1 1.3); crucian at dawn on the flats x{F2(cruDawn)} (>= 1.1; phase 1 1.3), by day x{F2(cruDay)} of its dawn share (<= 0.8); carp at night on flat + shoal x{F2(carpNight)} (>= 1.1; phase 1 1.4), by day in hole + channel + basin x{F2(carpDay)} (>= 1.2)",
                bassDay >= 1.4f && bassDawn >= 1.1f && cruDawn >= 1.1f && cruDay <= 0.8f && carpNight >= 1.1f && carpDay >= 1.2f);
            // the step-2 species' niches on seed 1 (Docs/lake_phase2_spec.md C4), against the uniform targets' shares
            if (!NicheIds.All(id => shM.ContainsKey(id)))
            {
                Log("[DEPTH] D7' the step-2 niches: skipped (" + string.Join(", ", NicheIds.Where(id => !shM.ContainsKey(id))) + " not on the roster)");
                yield break;
            }
            Log($"[DEPTH] D7' seed 1 uniform targets (z <= {zMax:0}): materials " + string.Join(" ", Enumerable.Range(0, 4).Select(m => $"{(BedMat)m}:{F2(uniM[m])}")) + $", hole {F2(uni[(int)BedKind.Hole])}, channel {uni[(int)BedKind.Channel]:0.000}");
            var gates = NicheIds.Select(id => NicheGate(ctl, seed1, id).Value).ToList();
            DCheck("D7' seed 1's step-2 niches: " + string.Join(", ", gates.Select(g => g.text)), gates.All(g => g.ok));
        }

        // ------------------------------------------------------------------ D7': the targets' shares and the niche gates
        /// <summary>The stage's ordinary species (its roster, no legends), in roster order.</summary>
        static List<FishSpecies> OrdinaryOf(FishingController ctl) =>
            ctl.Stage.Def.spawns.Select(kv => GameDatabase.GetFish(kv.Key)).Where(sp => sp != null && sp.encounter == null).ToList();

        /// <summary>
        /// D7''s targets on one bed: per species and period the share of 2000 seeded targets per bed kind and material and
        /// their mean depth rank (water z &lt;= zMax), and the uniform targets' (the first species' region: shares of 2000,
        /// the exact mean rank).
        /// </summary>
        sealed class Shares
        {
            public readonly Dictionary<string, float[][]> kind = new Dictionary<string, float[][]>(), mat = new Dictionary<string, float[][]>();
            public readonly Dictionary<string, float[]> rank = new Dictionary<string, float[]>();
            public float[] uni, uniM;
            public float uniMean = 50f;
        }

        /// <summary>
        /// <see cref="Shares"/> on bed <paramref name="b"/>. <paramref name="only"/>: that species alone, with the same numbers
        /// as in the whole stock's run (each species' draws are seeded by itself, the uniform ones by the first species' region).
        /// </summary>
        static Shares SampleShares(Bathymetry b, FishHabitat hab, List<FishSpecies> species, float zMax, string only = null)
        {
            var s = new Shares();
            for (int si = 0; si < species.Count; si++)
            {
                var sp = species[si];
                bool want = only == null || sp.id == only;
                if (!want && si != 0) continue;
                var per = new float[4][];
                var perM = new float[4][];
                var rank = new float[4];
                for (int p = 0; p < 4; p++)
                {
                    var (reg, cum, total) = hab.DebugSampler(sp, p, zMax);
                    if (want)
                    {
                        var rng = new System.Random(1515 + p + 10 * sp.id.Length);
                        var c = new float[9];
                        var cmat = new float[4];
                        double rk = 0;
                        for (int t = 0; t < 2000; t++)
                        {
                            int k = reg.nodes[HabitatModel.Pick(cum, (float)rng.NextDouble() * total)];
                            c[(int)b.NodeKind(k)] += 1f / 2000f;
                            cmat[(int)b.NodeMat(k)] += 1f / 2000f;
                            rk += b.NodeRankPct(k);
                        }
                        per[p] = c;
                        perM[p] = cmat;
                        rank[p] = (float)(rk / 2000);
                    }
                    if (si != 0 || p != 0) continue;
                    // the uniform targets: the region's own weights
                    var u = new float[reg.u.Length];
                    float acc = 0f;
                    for (int q = 0; q < u.Length; q++) u[q] = acc += reg.u[q];
                    var rng2 = new System.Random(1515 + 999);
                    s.uni = new float[9];
                    s.uniM = new float[4];
                    for (int t = 0; t < 2000; t++)
                    {
                        int k = reg.nodes[HabitatModel.Pick(u, (float)rng2.NextDouble() * acc)];
                        s.uni[(int)b.NodeKind(k)] += 1f / 2000f;
                        s.uniM[(int)b.NodeMat(k)] += 1f / 2000f;
                    }
                    double ru = 0, us = 0;
                    for (int q = 0; q < reg.nodes.Length; q++)
                    {
                        ru += reg.u[q] * b.NodeRankPct(reg.nodes[q]);
                        us += reg.u[q];
                    }
                    s.uniMean = us > 0 ? (float)(ru / us) : 50f;
                }
                if (!want) continue;
                s.kind[sp.id] = per;
                s.mat[sp.id] = perM;
                s.rank[sp.id] = rank;
            }
            return s;
        }

        /// <summary>The species with a hand-written niche gate (D7', Docs/lake_phase2_spec.md C4).</summary>
        static readonly string[] NicheIds = { "barbel_steed", "white_crucian", "freshwater_eel", "redfin_culter", "yellow_catfish" };

        /// <summary>
        /// A step-2 species' hand-written niche gate on seed 1 (<paramref name="s"/>: seed 1's shares with it in them) against
        /// the uniform targets: (its text, passed); null for a species without one (see <see cref="GenericNiche"/>). The
        /// material shares are over the four periods by their length (3 / 9 / 3 / 9 h).
        /// </summary>
        (string text, bool ok)? NicheGate(FishingController ctl, Shares s, string id)
        {
            float M(params BedMat[] ms)
            {
                float v = 0f;
                for (int p = 0; p < 4; p++) v += LakeEconomy.PeriodW[p] * ms.Sum(m => s.mat[id][p][(int)m]);
                return v / LakeEconomy.PeriodW.Sum();
            }
            float MU(params BedMat[] ms) => ms.Sum(m => s.uniM[(int)m]);
            switch (id)
            {
                case "barbel_steed":
                {
                    float v = M(BedMat.Gravel, BedMat.Sand) / MU(BedMat.Gravel, BedMat.Sand);
                    return ($"barbel_steed on gravel + sand x{F2(v)} (>= 1.4)", v >= 1.4f);
                }
                case "white_crucian":
                {
                    float v = M(BedMat.Weed) / MU(BedMat.Weed);
                    return ($"white_crucian on weed x{F2(v)} (>= 1.4)", v >= 1.4f);
                }
                case "freshwater_eel":
                {
                    float v = s.kind[id][3][(int)BedKind.Hole] / s.uni[(int)BedKind.Hole];
                    return ($"freshwater_eel at night in holes x{F2(v)} (>= 2)", v >= 2f);
                }
                case "yellow_catfish":
                {
                    float v = s.rank[id][3];
                    return ($"yellow_catfish at night mean rank {v:0} (>= 50)", v >= 50f);
                }
                case "redfin_culter":
                {
                    // the redfin culter's channel: the old creek starts at z 17-20 and runs 28-38 m out, its banks mostly
                    // drop-off, so the bamboo's water (z <= 30) holds almost none of it; judged on the dragon's water (z <= 50)
                    // with 4000 targets per period against the region's exact uniform share
                    var L = ctl.Stage.L;
                    var cul = GameDatabase.GetFish(id);
                    var habW = new FishHabitat(ctl.Stage, ctl, L.Bathy, true);
                    const float zWide = 50f;
                    var chan = new float[4];
                    float uniChan = 0f;
                    for (int p = 0; p < 4; p++)
                    {
                        var (reg, cum, total) = habW.DebugSampler(cul, p, zWide);
                        var rng = new System.Random(2515 + p);
                        int nch = 0;
                        for (int t = 0; t < 4000; t++)
                            if (L.Bathy.NodeKind(reg.nodes[HabitatModel.Pick(cum, (float)rng.NextDouble() * total)]) == BedKind.Channel) nch++;
                        chan[p] = nch / 4000f;
                        if (p == 0) uniChan = ExactShare(reg, null, k => L.Bathy.NodeKind(k) == BedKind.Channel);
                    }
                    float culDawn = uniChan > 0f ? chan[0] / uniChan : 0f, culEve = uniChan > 0f ? chan[2] / uniChan : 0f;
                    Log($"{depthTag} D7' seed 1 redfin_culter in the channel on the dragon's water (z <= {zWide:0}, uniform share {uniChan:0.0000}): " + string.Join(" ", Enumerable.Range(0, 4).Select(p => $"{GameClock.Id((Period)p)} {chan[p]:0.0000} x{(uniChan > 0f ? chan[p] / uniChan : 0f):0.00}")));
                    return ($"redfin_culter in the channel (z <= {zWide:0}) at dawn x{F2(culDawn)} and evening x{F2(culEve)} (>= 1.5)", culDawn >= 1.5f && culEve >= 1.5f);
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// The exact share of a region's weights on the nodes <paramref name="on"/> picks: a sampler's (its cumulative
        /// weights <paramref name="cum"/>), or the uniform targets' (cum null: the region's u).
        /// </summary>
        static float ExactShare(HabitatModel.Region reg, float[] cum, Func<int, bool> on)
        {
            double hit = 0, all = 0;
            for (int q = 0; q < reg.nodes.Length; q++)
            {
                double w = cum == null ? reg.u[q] : cum[q] - (q > 0 ? cum[q - 1] : 0f);
                all += w;
                if (on(reg.nodes[q])) hit += w;
            }
            return all > 0 ? (float)(hit / all) : 0f;
        }

        /// <summary>
        /// The niche gate of a species without a hand-written one (-fkauto newspecies): its strongest declared bed
        /// preference in its most active period (the kind weight times that period's kind weight, or the material weight,
        /// whichever is the larger), as its exact share of the targets on seed 1 (the dragon's water, z &lt;= 50) over the
        /// uniform share there. Gate x1.3; null when it declares nothing of 1.3 or more, or that ground is under 0.5 % of
        /// the water (nothing to judge).
        /// </summary>
        (string text, bool ok)? GenericNiche(FishingController ctl, FishSpecies sp)
        {
            var h = sp.habitat;
            if (h == null) return null;
            int pb = Enumerable.Range(0, 4).OrderByDescending(p => TimeActivity.A(sp.id, (Period)p)).First();
            int bestK = 0, bestM = 0;
            float wK = 0f, wM = 0f;
            for (int k = 0; k < 9; k++)
                if (h.kindW[k] * h.periodKind[pb, k] > wK)
                {
                    wK = h.kindW[k] * h.periodKind[pb, k];
                    bestK = k;
                }
            for (int m = 0; m < 4; m++)
                if (h.matW[m] > wM)
                {
                    wM = h.matW[m];
                    bestM = m;
                }
            bool byKind = wK >= wM;
            float w = byKind ? wK : wM;
            if (w < 1.3f) return null;
            var L = ctl.Stage.L;
            var (reg, cum, _) = new FishHabitat(ctl.Stage, ctl, L.Bathy, true).DebugSampler(sp, pb, 50f);
            Func<int, bool> on = byKind ? (Func<int, bool>)(k => (int)L.Bathy.NodeKind(k) == bestK) : k => (int)L.Bathy.NodeMat(k) == bestM;
            float uniS = ExactShare(reg, null, on), its = ExactShare(reg, cum, on);
            if (uniS < 0.005f) return null;
            string where = byKind ? ((BedKind)bestK).ToString() : ((BedMat)bestM).ToString();
            float v = its / uniS;
            return ($"{sp.id} at {GameClock.Id((Period)pb)} (its most active period) on {where} (its strongest preference, weight {F2(w)}): {its:0.000} of its targets, x{F2(v)} the uniform {uniS:0.000} (z <= 50; >= 1.3)", v >= 1.3f);
        }

        // ------------------------------------------------------------------ D12: the obstacle bands
        void BandsCheck(FishingController ctl)
        {
            var obs = ctl.Stage.Obstacles;
            int zones = 0, bad = 0;
            string first = "";
            foreach (var o in obs.Weeds.Concat(obs.Snags))
            {
                zones++;
                for (int t = 0; t < 20; t++)
                {
                    var q = Obstacles.RandomPoint(o);
                    float bot = obs.BotAt(o, q.x, q.y);
                    if (bot < o.top) continue;
                    bad++;
                    if (first.Length == 0) first = $" (first {o.id} at ({F2(q.x)}, {F2(q.y)}): bot {F2(bot)} >= top {F2(o.top)})";
                }
            }
            DCheck($"D12 {zones} weed / snag zones x 20 points: the band's bottom (the bed) under its top everywhere ({bad} not{first})", zones > 0 && bad == 0);
        }

        // ------------------------------------------------------------------ D10: the golden carp
        IEnumerator LegendCheck(FishingController ctl)
        {
            var w = ctl.Watch;
            var L = ctl.Stage.L;
            if (w == null)
            {
                DCheck("D10 the lake has its legend watch", false);
                yield break;
            }
            string rodWas = Game.I.Rod.id;
            foreach (var rid in new[] { "rod_bamboo", "rod_dragon" })
            {
                SteerGear(rid, null, null);
                yield return null;
                int deep = 0, edge = 0, spots = 0, deepSpots = 0;
                float minLurk = 99f, minDisc = 99f;
                for (int t = 0; t < 40; t++)
                {
                    var lurk = w.DebugPlace();
                    float bed = L.DepthAt(lurk.x, lurk.z);
                    minLurk = Mathf.Min(minLurk, bed);
                    if (bed >= 4.5f - 1e-3f) deep++;
                    if ((L.Bathy.FlagsAt(lurk.x, lurk.z) & BedFlag.WeedEdge) != 0) edge++;
                    if (w.DebugChooseSpot(out var s))
                    {
                        spots++;
                        float disc = L.Bathy.MinDepthDisc(s.x, s.z, 0.9f);
                        minDisc = Mathf.Min(minDisc, disc);
                        if (disc >= 4.3f - 1e-3f) deepSpots++;
                    }
                }
                DCheck($"D10 {rid} (cast {Game.I.Rod.castDist:0}, {w.DebugLurkCandidates.Count} lurk candidates): 40 lurk points, {deep} over >= 4.5 m (least {F2(minLurk)}), {edge} on the drop-off beside weed (>= 30); a spot every time ({spots}/40), {deepSpots} with >= 4.3 m over 0.9 m (least {F2(minDisc)})",
                    deep == 40 && edge >= 30 && spots == 40 && deepSpots == 40);
            }
            SteerGear(rodWas, null, null);
            yield return null;
        }

        // ------------------------------------------------------------------ D11', D11b: the economy and the derived weights
        /// <summary>The simulations of some economy jobs on the workers (at most <paramref name="threads"/> at a time), then each job's estimate.</summary>
        static void RunJobs(List<FishHabitat.EcoJob> jobs, int threads)
        {
            var all = new List<Lazy<HabitatModel.Occupancy>>();
            foreach (var j in jobs)
            {
                foreach (var o in j.todayOcc) if (o != null) all.Add(o);
                foreach (var o in j.newOcc) if (o != null) all.Add(o);
            }
            Parallel.ForEach(all, new ParallelOptions { MaxDegreeOfParallelism = threads }, o => _ = o.Value);
            foreach (var j in jobs) j.Run(1);
        }

        /// <summary>
        /// D11' (Docs/lake_phase2_spec.md A7, A8): the economy over seeds 1..50 x the bamboo, carbon and dragon rods (the beds
        /// built here in turn, the simulations and estimates on the workers): the feeding chance F never at its clamp; after
        /// it the lake's catches and income per minute within 0.8..1.25 of today's for every seed and rod; seeds 1 and 37
        /// recomputed give the same numbers; on seed 1 (bamboo, carbon) every species' best 10 % of casts on its best rig at
        /// least 1.5 x its own fan mean (where each fish lives shows). Information: per rig / period / species, the golden
        /// paste and the dragon's luck, XP, F's medians per rod (FishHabitat.FDefault). D11b: the derived weights (computed
        /// twice the same, a roster override halves the base with A and the activity still applied, at most 10 % of the
        /// (seed, species, period) at A's clamp, the stock's mean multiplier within 0.9..1.1). Always logged: how close the
        /// catches and income came to the band's edges (a NOTE under 0.03: a live soak is advised; none is run).
        /// <paramref name="only"/> (-fkauto newspecies): the positive gate for that species alone, its derived weight and
        /// clamp hits reported; no golden paste / luck, no recompute, no override check.
        /// </summary>
        IEnumerator EconomyCheck(FishingController ctl, TerrainRecipe r, ObstacleSet raw, string only = null)
        {
            var L = ctl.Stage.L;
            var def = ctl.Stage.Def;
            var rods = new[] { ("rod_bamboo", 16f), ("rod_carbon", 24f), ("rod_dragon", 36f) };
            int threads = Mathf.Max(1, SystemInfo.processorCount - 1);
            var t0 = Time.realtimeSinceStartup;
            var F = new Dictionary<float, List<float>>();
            var C = new Dictionary<float, List<float>>();
            var I = new Dictionary<float, List<float>>();
            foreach (var rd in rods)
            {
                F[rd.Item2] = new List<float>();
                C[rd.Item2] = new List<float>();
                I[rd.Item2] = new List<float>();
            }
            int clamped = 0, outBand = 0, rigWarn = 0, clampHits = 0, pairs = 0;
            float mulMin = 9f, mulMax = 0f;
            string firstOut = "", firstWarn = "";
            var keep = new Dictionary<(int, float), (double c1, double i1, float f)>();
            bool posOk = true;
            ecoFocus = "";
            // the band's closest approach (catches or income, any seed and rod), and the focus species' derived weight
            float margin = 9f;
            string marginAt = "";
            int fClamp = 0, fPairs = 0;
            var fAMin = new[] { 9f, 9f, 9f, 9f };
            var fAMax = new float[4];
            string fSeed1 = "";
            for (int seed = 1; seed <= 50; seed++)
            {
                var b = seed == 1 ? L.Bathy : BathyGen.Build(L, r, seed, raw);
                var hab = new FishHabitat(ctl.Stage, ctl, b, true);
                // D11b on this bed: the clamp hits and the stock's mean multiplier per period
                var d = hab.Derived;
                clampHits += d.clampHits;
                pairs += d.pairs;
                for (int p = 0; p < 4; p++)
                {
                    double num = 0, den = 0;
                    for (int s = 0; s < d.ids.Length; s++)
                    {
                        float bw = def.spawns.First(kv => kv.Key == d.ids[s]).Value, a = TimeActivity.A(d.ids[s], (Period)p);
                        num += bw * a * d.A[s, p];
                        den += bw * a;
                    }
                    float mul = den > 0 ? (float)(num / den) : 1f;
                    mulMin = Mathf.Min(mulMin, mul);
                    mulMax = Mathf.Max(mulMax, mul);
                }
                int fs = only != null ? Array.IndexOf(d.ids, only) : -1;
                if (fs >= 0)
                {
                    for (int p = 0; p < 4; p++)
                    {
                        if (!(TimeActivity.A(only, (Period)p) > 0f)) continue;
                        fPairs++;
                        float A = d.A[fs, p];
                        if (A <= HabitatModel.AvailMin || A >= HabitatModel.AvailMax) fClamp++;
                        fAMin[p] = Mathf.Min(fAMin[p], A);
                        fAMax[p] = Mathf.Max(fAMax[p], A);
                    }
                    if (seed == 1)
                    {
                        var sumW = Enumerable.Range(0, 4).Select(p => Enumerable.Range(0, d.ids.Length).Sum(s => d.W[s, p])).ToArray();
                        fSeed1 = string.Format(CIc, "base {0:0.###}{1}; seed 1 A {2:0.00}/{3:0.00}/{4:0.00}/{5:0.00}, W {6:0.0}/{7:0.0}/{8:0.0}/{9:0.0} = {10} of the stock's W",
                            def.spawns.First(kv => kv.Key == only).Value, def.GivenWeight.TryGetValue(only, out bool given) && given ? " (given in the roster)" : " (the rarity's)",
                            d.A[fs, 0], d.A[fs, 1], d.A[fs, 2], d.A[fs, 3], d.W[fs, 0], d.W[fs, 1], d.W[fs, 2], d.W[fs, 3],
                            string.Join("/", Enumerable.Range(0, 4).Select(p => (sumW[p] > 0 ? d.W[fs, p] / sumW[p] * 100f : 0f).ToString("0.0", CIc) + "%")));
                    }
                }
                var jobs = rods.Select(rd => hab.Economy(rd.Item2)).ToList();
                var task = Task.Run(() => RunJobs(jobs, threads));
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted)
                {
                    DCheck("D11' the estimates ran: " + task.Exception?.GetBaseException(), false);
                    yield break;
                }
                foreach (var j in jobs)
                {
                    float c = (float)(j.C1 * j.F), i = (float)(j.I1 * j.F);
                    F[j.castDist].Add(j.F);
                    C[j.castDist].Add(c);
                    I[j.castDist].Add(i);
                    if (j.clamped) clamped++;
                    if (c < LakeEconomy.BandLo || c > LakeEconomy.BandHi || i < LakeEconomy.BandLo || i > LakeEconomy.BandHi)
                    {
                        outBand++;
                        if (firstOut.Length == 0) firstOut = string.Format(CIc, " (first: seed {0} rod {1:0} catches x{2:0.00} income x{3:0.00})", seed, j.castDist, c, i);
                    }
                    float mg = Mathf.Min(Mathf.Min(c - LakeEconomy.BandLo, LakeEconomy.BandHi - c), Mathf.Min(i - LakeEconomy.BandLo, LakeEconomy.BandHi - i));
                    if (mg < margin)
                    {
                        margin = mg;
                        marginAt = string.Format(CIc, "seed {0} rod {1:0}: catches x{2:0.000} income x{3:0.000}", seed, j.castDist, c, i);
                    }
                    for (int k = 0; k < j.rigs.Length; k++)
                    {
                        double rr = j.Today.rigC[k] > 0 ? j.New.rigC[k] * j.F / j.Today.rigC[k] : 1;
                        if (rr < 0.5 || rr > 2)
                        {
                            rigWarn++;
                            if (firstWarn.Length == 0) firstWarn = string.Format(CIc, " (first: seed {0} rod {1:0} {2} x{3:0.00})", seed, j.castDist, j.rigs[k].id, rr);
                        }
                    }
                    if (seed == 1 || seed == 37) keep[(seed, j.castDist)] = (j.C1, j.I1, j.F);
                    Log(string.Format(CIc, "[ECO] seed {0} rod {1:0} C1 {2:0.000} I1 {3:0.000} -> F {4:0.000}{5}: catches x{6:0.000} income x{7:0.000} ({8:0} ms)",
                        seed, j.castDist, j.C1, j.I1, j.F, j.clamped ? " CLAMPED" : "", c, i, j.ms));
                    if (seed == 1) posOk &= EconomyTables(j, j.castDist <= 24f, only);
                }
                if (seed == 1 && only == null) yield return EconomyExtras(ctl, jobs);
            }
            float Med(List<float> v)
            {
                var a = v.OrderBy(x => x).ToList();
                return a.Count == 0 ? 0f : (a.Count % 2 == 1 ? a[a.Count / 2] : 0.5f * (a[a.Count / 2 - 1] + a[a.Count / 2]));
            }
            DCheck(string.Format(CIc, "D11' seeds 1..50 x bamboo / carbon / dragon ({0:0} s): F never at its clamp [0.25, 1] ({1} clamped); F {2}",
                Time.realtimeSinceStartup - t0, clamped, string.Join(", ", rods.Select(rd => string.Format(CIc, "{0:0} m {1:0.000}..{2:0.000} (median {3:0.000})", rd.Item2, F[rd.Item2].Min(), F[rd.Item2].Max(), Med(F[rd.Item2]))))),
                clamped == 0);
            DCheck(string.Format(CIc, "D11' after F the lake's catches and income per minute over today's within [0.80, 1.25] for every seed and rod ({0} out{1}): {2}",
                outBand, firstOut, string.Join(", ", rods.Select(rd => string.Format(CIc, "{0:0} m catches x{1:0.000}..{2:0.000} income x{3:0.000}..{4:0.000}",
                    rd.Item2, C[rd.Item2].Min(), C[rd.Item2].Max(), I[rd.Item2].Min(), I[rd.Item2].Max())))),
                outBand == 0);
            Log(string.Format(CIc, "{0} D11' the band's closest approach: {1:0.000} from an edge ({2}){3}", depthTag, margin, marginAt,
                margin < EcoSoakMargin ? "" : " (a live soak is not needed: run one only within " + EcoSoakMargin.ToString("0.00", CIc) + " of an edge)"));
            if (margin < EcoSoakMargin)
                Log(string.Format(CIc, "{0} NOTE the economy came within {1:0.00} of the band's edge ({2:0.000}): a live soak (-fkauto economy, Docs/lake_phase2_spec.md A8) is advised; it is not run here", depthTag, EcoSoakMargin, margin));
            Log($"{depthTag} D11' rigs whose fan ratio is under x0.5 or over x2 (WARN, information): {rigWarn} of {50 * rods.Length * LakeEconomy.Reference(id => RigOf(id)).Length}{firstWarn}");
            if (only != null)
            {
                DCheck($"D11' seed 1 (bamboo, carbon): {only}'s best 10 % of casts on its best rig >= 1.5 x its own fan mean:{(ecoFocus.Length > 0 ? ecoFocus : " (not in the stock's estimate)")}", posOk && ecoFocus.Length > 0);
                DCheck(string.Format(CIc, "D11b the derived weights over seeds 1..50 (the whole stock): A at its clamp [0.6, 1.4] in {0} of {1} (species, period) with a > 0 (<= 10 %); the stock's mean multiplier {2:0.000}..{3:0.000} (in [0.9, 1.1])",
                    clampHits, pairs, mulMin, mulMax), pairs > 0 && clampHits <= pairs / 10 && mulMin >= 0.9f && mulMax <= 1.1f);
                // (its own weight: reported, not gated; a clamp hit says its habitat's availability is extreme on that bed)
                Log(string.Format(CIc, "{0} D11b {1}'s derived spawn weight: {2}; over seeds 1..50 A {3}; at its clamp [0.6, 1.4] in {4} of {5} (seed, period) with a > 0{6}",
                    depthTag, only, fSeed1.Length > 0 ? fSeed1 : "not derived (no habitat or weight 0)",
                    string.Join(" ", Enumerable.Range(0, 4).Select(p => GameClock.Id((Period)p) + " " + (fAMax[p] > 0f ? string.Format(CIc, "{0:0.00}..{1:0.00}", fAMin[p], fAMax[p]) : "-"))),
                    fClamp, fPairs, fClamp > 0 ? " -- CLAMP HIT" : " (never clamped)"));
                yield break;
            }
            DCheck("D11' seed 1 (bamboo, carbon): every species' best 10 % of casts on its best rig >= 1.5 x its own fan mean (the [ECO] best lines)", posOk);
            // recomputed: the same numbers
            bool same = true;
            string again = "";
            foreach (int seed in new[] { 1, 37 })
            {
                var b = BathyGen.Build(L, r, seed, raw);
                var hab = new FishHabitat(ctl.Stage, ctl, b, true);
                var jobs = rods.Select(rd => hab.Economy(rd.Item2)).ToList();
                var task = Task.Run(() => RunJobs(jobs, threads));
                while (!task.IsCompleted) yield return null;
                foreach (var j in jobs)
                {
                    var k = keep[(seed, j.castDist)];
                    bool eq = j.C1 == k.c1 && j.I1 == k.i1 && j.F == k.f;
                    same &= eq;
                    again += string.Format(CIc, " {0}/{1:0}:{2}", seed, j.castDist, eq ? "same" : $"C1 {k.c1:R} -> {j.C1:R}");
                }
            }
            DCheck("D11' seeds 1 and 37 recomputed (the beds rebuilt, the simulations again):" + again, same);
            Log(string.Format(CIc, "[DEPTH] D11' F medians per rod (FishHabitat.FDefault): {0}", string.Join(", ", rods.Select(rd => string.Format(CIc, "{0} {1:0.000}", rd.Item1, Med(F[rd.Item2]))))));
            // D11b
            DCheck(string.Format(CIc, "D11b the derived weights over seeds 1..50: A at its clamp [0.6, 1.4] in {0} of {1} (species, period) with a > 0 (<= 10 %); the stock's mean multiplier sum(base a A) / sum(base a) {2:0.000}..{3:0.000} (in [0.9, 1.1])",
                clampHits, pairs, mulMin, mulMax), pairs > 0 && clampHits <= pairs / 10 && mulMin >= 0.9f && mulMax <= 1.1f);
            WeightsCheck(ctl);
        }

        static RigClass RigOf(string baitId) => FishHabitat.RigOf(GameDatabase.GetItem<BaitDef>(baitId), 0f);

        /// <summary>The economy's distance from the band's edges under which a live soak is advised (Docs/data_reference.md 2.7).</summary>
        const float EcoSoakMargin = 0.03f;

        /// <summary>-fkauto newspecies: the focus species' best-cast ratios (" rod 16: on paste2 x2.4 ...").</summary>
        string ecoFocus = "";

        /// <summary>
        /// Seed 1's tables for one rod ([ECO] lines) and the positive gate (when <paramref name="gate"/>): every species', or
        /// <paramref name="only"/>'s alone.
        /// </summary>
        bool EconomyTables(FishHabitat.EcoJob j, bool gate, string only = null)
        {
            var n = j.New;
            var t = j.Today;
            Log("[ECO] seed 1 rod " + j.castDist.ToString("0", CIc) + " per rig (new x F / today): " + string.Join(" ", j.rigs.Select((rg, k) =>
                string.Format(CIc, "{0} {1:0.00}", rg.id, t.rigC[k] > 0 ? n.rigC[k] * j.F / t.rigC[k] : 0))));
            Log("[ECO] seed 1 rod " + j.castDist.ToString("0", CIc) + " per period (dawn day evening night): " + string.Join(" ", Enumerable.Range(0, 4).Select(p =>
                string.Format(CIc, "{0:0.00}/{1:0.00}", t.perC[p] > 0 ? n.perC[p] * j.F / t.perC[p] : 0, t.perI[p] > 0 ? n.perI[p] * j.F / t.perI[p] : 0))));
            var ids = j.side.species.Select(s => s.h.id).ToList();
            Log("[ECO] seed 1 rod " + j.castDist.ToString("0", CIc) + " per species (share of catches new / today, of income new / today): " + string.Join(" ", ids.Select((id, s) =>
            {
                int ti = j.today.species.FindIndex(x => x.h.id == id);
                return string.Format(CIc, "{0} {1:0.00}/{2:0.00} {3:0.00}/{4:0.00}", id, n.spC[s] / Math.Max(1e-12, n.C), ti >= 0 ? t.spC[ti] / Math.Max(1e-12, t.C) : 0,
                    n.spI[s] / Math.Max(1e-12, n.I), ti >= 0 ? t.spI[ti] / Math.Max(1e-12, t.I) : 0);
            })));
            Log(string.Format(CIc, "[ECO] seed 1 rod {0:0} XP per minute x{1:0.000}", j.castDist, t.Xp > 0 ? n.Xp * j.F / t.Xp : 0));
            bool ok = true;
            int nc = j.cs.at.Length;
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < ids.Count; s++)
            {
                int best = 0;
                double bestV = -1;
                for (int k = 0; k < j.rigs.Length; k++)
                {
                    double v = 0;
                    for (int c = 0; c < nc; c++) v += n.spRigCast[s, k, c];
                    if (v > bestV)
                    {
                        bestV = v;
                        best = k;
                    }
                }
                var vals = new List<double>();
                for (int c = 0; c < nc; c++) vals.Add(n.spRigCast[s, best, c]);
                vals.Sort();
                int tenth = Math.Max(1, nc / 10);
                double top = vals.Skip(nc - tenth).Average(), mean = vals.Average();
                double ratio = mean > 0 ? top / mean : 0;
                if (ratio < 1.5 && (only == null || ids[s] == only)) ok = false;
                sb.Append(string.Format(CIc, " {0} on {1} x{2:0.00}", ids[s], j.rigs[best].id, ratio));
                if (ids[s] == only && gate) ecoFocus += string.Format(CIc, " rod {0:0} on {1} x{2:0.00}", j.castDist, j.rigs[best].id, ratio);
            }
            Log("[ECO] seed 1 rod " + j.castDist.ToString("0", CIc) + " best 10 % of casts over the fan mean (>= 1.5):" + sb + (gate ? "" : " (information)"));
            return !gate || ok;
        }

        /// <summary>Seed 1's information: the golden paste (rare x 3 on the stock) and the dragon's luck (x 1.6 on rare and up).</summary>
        IEnumerator EconomyExtras(FishingController ctl, List<FishHabitat.EcoJob> jobs)
        {
            var golden = GameDatabase.GetItem<BaitDef>("bait_golden");
            var dragon = GameDatabase.GetItem<RodDef>("rod_dragon");
            foreach (var j in jobs)
            {
                var g = new[] { new LakeEconomy.Rig { id = "golden2", bait = "bait_golden", cls = RigClass.F2, weight = 1f } };
                j.side.rareBoost = j.today.rareBoost = golden != null ? golden.rareBoost : 3f;
                var gt = LakeEconomy.Evaluate(j.cs, j.today, g, LakeEconomy.PeriodW, j.stealth, j.biteMult);
                var gn = LakeEconomy.Evaluate(j.cs, j.side, g, LakeEconomy.PeriodW, j.stealth, j.biteMult);
                j.side.rareBoost = j.today.rareBoost = 1f;
                Log(string.Format(CIc, "[ECO] seed 1 rod {0:0} golden paste (F2, rare x{1:0}): catches x{2:0.00} income x{3:0.00} of today's golden paste", j.castDist, golden != null ? golden.rareBoost : 3f,
                    gt.C > 0 ? gn.C * j.F / gt.C : 0, gt.I > 0 ? gn.I * j.F / gt.I : 0));
                if (j.castDist >= 36f)
                {
                    float luck = dragon != null ? dragon.luck : 1.6f;
                    j.side.luck = j.today.luck = luck;
                    var lt = LakeEconomy.Evaluate(j.cs, j.today, j.rigs, LakeEconomy.PeriodW, j.stealth, j.biteMult);
                    var ln = LakeEconomy.Evaluate(j.cs, j.side, j.rigs, LakeEconomy.PeriodW, j.stealth, j.biteMult);
                    j.side.luck = j.today.luck = 1f;
                    Log(string.Format(CIc, "[ECO] seed 1 dragon luck x{0:0.0}: catches x{1:0.00} income x{2:0.00} of today's with the same luck", luck, lt.C > 0 ? ln.C * j.F / lt.C : 0, lt.I > 0 ? ln.I * j.F / lt.I : 0));
                }
            }
            yield return null;
        }

        /// <summary>D11b's determinism and the override path on seed 1 (the roster's loader and the derivation).</summary>
        void WeightsCheck(FishingController ctl)
        {
            var b = ctl.Stage.L.Bathy;
            var def = ctl.Stage.Def;
            var ins = new List<HabitatModel.WeightIn>();
            foreach (var kv in def.spawns)
            {
                var sp = GameDatabase.GetFish(kv.Key);
                if (sp == null || sp.encounter != null) continue;
                ins.Add(new HabitatModel.WeightIn { s = FishHabitat.HabOf(sp, b), baseW = kv.Value, act = Enumerable.Range(0, 4).Select(p => TimeActivity.A(sp.id, (Period)p)).ToArray() });
            }
            var a = HabitatModel.DerivedWeights(b, ins);
            var a2 = HabitatModel.DerivedWeights(b, ins);
            bool det = true;
            for (int s = 0; s < a.ids.Length; s++)
                for (int p = 0; p < 4; p++)
                    det &= BitConverter.SingleToInt32Bits(a.W[s, p]) == BitConverter.SingleToInt32Bits(a2.W[s, p]) && BitConverter.SingleToInt32Bits(a.A[s, p]) == BitConverter.SingleToInt32Bits(a2.A[s, p]);
            // the override: the roster gives crucian_carp a weight of 20 (half its rarity's 40)
            var ctx = SpeciesCheck.FromResources();
            var roster = DataJson.Parse(ctx.stages.text, out _);
            var lakeFish = roster["stages"].items.First(st => st["id"].text == "lake")["fish"];
            lakeFish.items.First(e => e["id"].text == "crucian_carp").Set("weight", JNode.Num("20"));
            var res = SpeciesData.Build(new SpeciesData.Source { name = ctx.stages.name, text = DataJson.Write(roster) }, ctx.species);
            var lake = res.stages.First(st => st.id == "lake");
            float cruBase = lake.spawns.First(kv => kv.Key == "crucian_carp").Value;
            bool given = lake.GivenWeight.TryGetValue("crucian_carp", out bool g) && g, othersDerived = lake.GivenWeight.Where(kv => kv.Key != "crucian_carp").All(kv => !kv.Value);
            var ov = new List<HabitatModel.WeightIn>(ins);
            int ci = ov.FindIndex(x => x.s.id == "crucian_carp");
            if (ci >= 0) ov[ci] = new HabitatModel.WeightIn { s = ov[ci].s, baseW = cruBase, act = ov[ci].act };
            var o = HabitatModel.DerivedWeights(b, ov);
            bool applied = ci >= 0;
            float wRatio = 0f;
            if (ci >= 0)
                for (int p = 0; p < 4; p++)
                {
                    float want = cruBase * o.A[ci, p] * ov[ci].act[p];
                    applied &= Mathf.Abs(o.W[ci, p] - want) <= 1e-4f * Mathf.Max(1f, want);
                    if (p == 1 && a.W[ci, 1] > 0f) wRatio = o.W[ci, 1] / a.W[ci, 1];
                }
            DCheck(string.Format(CIc, "D11b seed 1: the derived weights twice the same ({0}); a roster weight of 20 for crucian_carp loads as an override ({1}; the others derived {2}, the stage derived {3}) and is its base: W = 20 x A x a in every period ({4}; by day x{5:0.00} of the derived)",
                det, given && cruBase == 20f, othersDerived, lake.Derived, applied, wRatio), det && given && cruBase == 20f && othersDerived && lake.Derived && applied);
        }

        // ------------------------------------------------------------------ D14: the population and the feeding roll
        IEnumerator FeedCheck(FishingController ctl)
        {
            ctl = depthCtl != null ? depthCtl : ctl;
            yield return ToReady(ctl);
            FishingController.NoBites = true;
            yield return new WaitForSeconds(5f);
            int pop = ctl.Spawner.Fish.Count, want = ctl.Stage.Def.population;
            DCheck($"D14 the lake's stock after settling: {pop} fish (the roster's population {want}, 15)", pop == want && want == 15);
            var f = ctl.Spawner.Fish.FirstOrDefault(x => x != null && x.State == FishAgent.St.Wander && x.LeaveAt < 0f);
            if (f == null || ctl.Habitat == null)
            {
                DCheck("D14 a wandering fish on the bed", false);
                FishingController.NoBites = false;
                yield break;
            }
            float p = ctl.Habitat.FeedP;
            int feeding = 0;
            for (int i = 0; i < 2000; i++)
            {
                f.FeedDecided = false;
                if (ctl.RollFeeding(f)) feeding++;
            }
            float sd = Mathf.Sqrt(2000f * p * (1f - p));
            DCheck(string.Format(CIc, "D14 2000 encounters: {0} feeding, F {1:0.000} expects {2:0} +- 3 x {3:0.0}", feeding, p, 2000f * p, sd), Mathf.Abs(feeding - 2000f * p) <= 3f * sd + 0.5f);
            // the decision is kept while the fish has been out of reach under 10 s, made afresh after
            f.FeedDecided = true;
            f.LastInReach = Time.time - 9f;
            yield return null;
            yield return null;
            bool kept = f.FeedDecided;
            f.LastInReach = Time.time - 10.5f;
            yield return null;
            yield return null;
            bool dropped = !f.FeedDecided;
            DCheck($"D14 the decision kept 9 s out of reach ({kept}), dropped after 10.5 s ({dropped})", kept && dropped);
            // FeedAll: always feeding, and no draw
            bool was = FishingController.FeedAll;
            FishingController.FeedAll = true;
            string st0 = JsonUtility.ToJson(Random.state);
            int all = 0;
            for (int i = 0; i < 100; i++)
            {
                f.FeedDecided = false;
                if (ctl.RollFeeding(f)) all++;
            }
            string st1 = JsonUtility.ToJson(Random.state);
            FishingController.FeedAll = was;
            DCheck($"D14 FeedAll: {all} of 100 feeding, Random's state untouched ({st0 == st1})", all == 100 && st0 == st1);
            FishingController.NoBites = false;
        }

        // ------------------------------------------------------------------ D6: the lying float
        /// <summary>A plan point on a flat (water 1.0..1.8 m, the generated bed) in open water within reach, clear of every zone, pad and cover.</summary>
        static Vector3 FlatSpot(FishingController ctl, float zLo, float zHi, Vector2 near)
        {
            var b = ctl.Stage.L.Bathy;
            var obs = ctl.Stage.Obstacles;
            var water = ctl.Stage.Water;
            Vector3 best = new Vector3(near.x, 0f, near.y);
            float bd = float.MaxValue;
            for (int k = 0; k < b.NodeCount; k++)
            {
                var p = b.NodePos(k);
                float d = b.NodeDepth(k);
                if (p.y < zLo || p.y > zHi || d < 1.0f || d > 1.8f) continue;
                if (b.MinDepthDisc(p.x, p.y, 1.0f) < 0.9f) continue;
                if (water != null && !water.OpenWater(p.x, p.y)) continue;
                if (obs != null && !obs.Empty)
                {
                    var xz = new Vector2(p.x, p.y);
                    bool clear = !obs.BlockedAtSurface(xz, 1f) && obs.PadAt(xz) == null && obs.ZoneAt(new Vector3(p.x, -d * 0.5f, p.y), true, true) == null;
                    foreach (var o in obs.All) if ((o.kind == "pad" || o.kind == "weed" || o.kind == "snag") && obs.Inside(o, xz, 1.2f)) clear = false;
                    if (!clear) continue;
                }
                float dist = (p - near).magnitude;
                if (dist < bd)
                {
                    bd = dist;
                    best = new Vector3(p.x, 0f, p.y);
                }
            }
            return best;
        }

        /// <summary>A crop of the screen round a scene point, x<paramref name="scale"/> (nearest), as a texture (call it at the end
        /// of a frame after <see cref="AutoShot.Frame"/>, so the test label is not in it).</summary>
        static Texture2D ScreenCrop(Vector2 world2D, int w, int h, int scale)
        {
            var pv = PixelView.Current;
            var tex = AutoShot.Texture();
            var c = pv != null ? pv.WorldToScreen(world2D) : new Vector2(tex.width * 0.5f, tex.height * 0.5f);
            w = Mathf.Min(w, tex.width);
            h = Mathf.Min(h, tex.height);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(c.x) - w / 2, 0, tex.width - w), y0 = Mathf.Clamp(Mathf.RoundToInt(c.y) - h / 2, 0, tex.height - h);
            var px = tex.GetPixels(x0, y0, w, h);
            var big = new Color[w * scale * h * scale];
            for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                big[y * w * scale + x] = px[(y / scale) * w + x / scale];
            var outTex = new Texture2D(w * scale, h * scale, TextureFormat.RGB24, false);
            outTex.SetPixels(big);
            outTex.Apply(false);
            Destroy(tex);
            return outTex;
        }

        Texture2D lieCrop, upCrop;

        IEnumerator FloatCheck(FishingController ctl)
        {
            var tk = ctl.Tackle;
            var L = ctl.Stage.L;
            FishingController.NoBites = true;
            SteerGear("rod_carbon", null, null);
            yield return ToReady(ctl);
            EquipTest("bait_paste", ctl);
            yield return null;
            bool hintWas = Game.Data.lieHint;
            Game.Data.lieHint = false;
            int hints0 = ctl.LieHints;
            var spot = FlatSpot(ctl, 6f, 16f, new Vector2(4f, 10f));
            float water = L.DepthAt(spot.x, spot.z);
            ctl.DebugFloatDepth(4.0f);
            ctl.DebugPlaceRig(spot);
            float t = 0f, settleT = -1f, tiltT = -1f, lieT = -1f;
            bool tiltShot = false;
            while (t < 6f && lieT < 0f)
            {
                yield return null;
                t += Time.deltaTime;
                if (settleT < 0f && tk.Depth >= tk.Bottom - 0.01f) settleT = t;
                if (tiltT < 0f && tk.Sit == Tackle.FloatSit.Tilt) tiltT = t;
                if (tk.Sit == Tackle.FloatSit.Tilt && !tiltShot)
                {
                    tiltShot = true;
                    float scaleWas = Time.timeScale;
                    Time.timeScale = 0f;
                    yield return NamedShot("depth_float_tilt");
                    Time.timeScale = scaleWas;
                }
                if (lieT < 0f && tk.Sit == Tackle.FloatSit.Lie) lieT = t;
            }
            yield return new WaitForSeconds(0.6f);
            string label = ctl.DepthLabelText;
            DCheck(string.Format(CIc, "D6 a float set at 4.0 m on the flat at ({0:0.00}, {1:0.00}) ({2:0.00} m of water): the bait settles at {3:0.00} s, the float tilts at {4:0.00} s and lies at {5:0.00} s (<= settle + 0.3); the label says '{6}' (찌 누움); the hint came once ({7})",
                spot.x, spot.z, water, settleT, tiltT, lieT, label, ctl.LieHints - hints0),
                settleT >= 0f && tiltT >= settleT - 1e-3f && lieT >= tiltT && lieT <= settleT + 0.3f + 1e-3f && label == "찌 누움" && ctl.LieHints - hints0 == 1 && tk.LieR.enabled && !tk.FloatR.enabled);
            yield return NamedShot("depth_float_lie");
            yield return AutoShot.Frame();
            lieCrop = ScreenCrop(tk.LieShown2D, 240, 150, 2);
            File.WriteAllBytes(Path.Combine(shots, "depth_float_lie_zoom.png"), lieCrop.EncodeToPNG());
            // the night: the 케미 light at the lying float's tip
            GameClock.Min = GameClock.Centre(Period.Night);
            yield return new WaitForSeconds(1.2f);
            yield return NamedShot("depth_float_night");
            var chemi = tk.ChemiR;
            float tipOff = chemi != null ? ((Vector2)chemi.transform.position - tk.LieShown2D).x * PixelView.PPU / Mathf.Max(0.01f, tk.FloatScale) : 0f;
            DCheck(string.Format(CIc, "D6 at night the 케미 light shows at the lying float's tip ({0} at {1:+0.0;-0.0} sprite px from its centre, the tip away from the middle)",
                chemi != null && chemi.enabled ? "on" : "off", tipOff), chemi != null && chemi.enabled && Mathf.Abs(Mathf.Abs(tipOff) - 12.93f) <= 1.5f);
            GameClock.Min = GameClock.Centre(Period.Day);
            yield return new WaitForSeconds(1.0f);
            // shortened: it stands up again through the tilt
            ctl.DebugFloatDepth(1.0f);
            t = 0f;
            float tilt2 = -1f, up2 = -1f;
            while (t < 2f && up2 < 0f)
            {
                yield return null;
                t += Time.deltaTime;
                if (tilt2 < 0f && tk.Sit == Tackle.FloatSit.Tilt) tilt2 = t;
                if (tilt2 >= 0f && up2 < 0f && tk.Sit == Tackle.FloatSit.Up) up2 = t;
            }
            yield return new WaitForSeconds(0.6f);
            DCheck(string.Format(CIc, "D6 shortened to 1.0 m it tilts at {0:0.00} s and stands at {1:0.00} s; the label says '{2}' (찌 수심), the standing sprite drawn ({3})",
                tilt2, up2, ctl.DepthLabelText, tk.FloatR.enabled), tilt2 >= 0f && up2 > tilt2 && ctl.DepthLabelText == "찌 수심" && tk.FloatR.enabled && !tk.LieR.enabled);
            yield return NamedShot("depth_float_up");
            yield return AutoShot.Frame();
            upCrop = ScreenCrop(tk.FloatShown2D, 240, 150, 2);
            // laid again: no second hint
            ctl.DebugFloatDepth(4.0f);
            for (t = 0f; t < 3f && tk.Sit != Tackle.FloatSit.Lie; t += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.3f);
            DCheck($"D6 laid again ({tk.Sit}): the hint is not shown again ({ctl.LieHints - hints0} in all)", tk.Sit == Tackle.FloatSit.Lie && ctl.LieHints - hints0 == 1);
            // a fight from the lying rig: the float stands on the line on the first frame
            var cru = GameDatabase.GetFish("crucian_carp");
            bool hooked = ctl.DebugHook(cru, 25f, 77, new Vector3(tk.Surface.x, -1f, tk.Surface.z));
            yield return null;
            DCheck($"D6 a fight from the lying float: hooked {hooked}, on its first frame the lying sprite is off ({!tk.LieR.enabled}), the float stands ({tk.Sit}) with its own sprite ({(tk.FloatR.sprite != null ? tk.FloatR.sprite.name : "-")})",
                hooked && !tk.LieR.enabled && tk.Sit == Tackle.FloatSit.Up && tk.FloatR.sprite != null && tk.FloatR.sprite.name == "float_stick");
            ctl.DebugRelease();
            yield return new WaitForSeconds(0.5f);
            yield return ToReady(ctl);
            // far out (z ~ 30): the smallest it is drawn
            var far = FlatSpot(ctl, 22f, 32f, new Vector2(11f, 28f));
            ctl.DebugFloatDepth(4.0f);
            ctl.DebugPlaceRig(far);
            for (t = 0f; t < 6f && tk.Sit != Tackle.FloatSit.Lie; t += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.5f);
            Log(string.Format(CIc, "[DEPTH] far lying float at ({0:0.00}, {1:0.00}) water {2:0.00} m: {3}, scale {4:0.00}", far.x, far.z, L.DepthAt(far.x, far.z), tk.Sit, tk.FloatScale));
            yield return NamedShot("depth_float_far");
            yield return ToReady(ctl);
            // the side by side: lying (left) and standing (right), x4
            if (lieCrop != null && upCrop != null)
            {
                var cmp = new Texture2D(lieCrop.width + upCrop.width + 8, Mathf.Max(lieCrop.height, upCrop.height), TextureFormat.RGB24, false);
                var fill = new Color[cmp.width * cmp.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color(0.1f, 0.1f, 0.12f);
                cmp.SetPixels(fill);
                cmp.SetPixels(0, 0, lieCrop.width, lieCrop.height, lieCrop.GetPixels());
                cmp.SetPixels(lieCrop.width + 8, 0, upCrop.width, upCrop.height, upCrop.GetPixels());
                cmp.Apply(false);
                File.WriteAllBytes(Path.Combine(shots, "depth_float_compare_zoom.png"), cmp.EncodeToPNG());
                Log("shot " + Path.Combine(shots, "depth_float_compare_zoom.png"));
                Destroy(cmp);
            }
            Game.Data.lieHint = hintWas || Game.Data.lieHint;
            FishingController.NoBites = false;
        }

        // ------------------------------------------------------------------ D8: the body rule
        IEnumerator BodyCheck(FishingController ctl)
        {
            int fpsWas = Application.targetFrameRate;
            float capWas = Time.captureDeltaTime;
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            FishingController.NoBites = true;
            var onlyWas = FishSpawner.OnlySpecies;
            int shallow = 0, under = 0;
            float soaked = 0f;
            foreach (var stock in new[] { "carp", "largemouth_bass", null })
            {
                FishSpawner.OnlySpecies = stock != null ? GameDatabase.GetFish(stock) : null;
                var prev = ctl;
                yield return GoStage("lake", 2f);
                ctl = null;
                for (float w = 0f; w < 30f && ctl == null; w += Time.unscaledDeltaTime)
                {
                    var c = FindAnyObjectByType<FishingController>();
                    if (c != null && c != prev && c.State == FishingController.S.Ready) ctl = c;
                    yield return null;
                }
                if (ctl == null) break;
                GameClock.Scale = 0f;
                FishAgent.ShallowFrames = FishAgent.UnderBedFrames = 0;
                float g = 0f;
                while (g < 300f)
                {
                    g += Time.deltaTime;
                    yield return null;
                }
                soaked += g;
                shallow += FishAgent.ShallowFrames;
                under += FishAgent.UnderBedFrames;
                Log($"[DEPTH] D8 soak {(stock ?? "the stage's stock")}: {g:0} s, {FishAgent.ShallowFrames} frames in too little water, {FishAgent.UnderBedFrames} under the bed");
            }
            FishSpawner.OnlySpecies = onlyWas;
            if (ctl == null)
            {
                DCheck("D8 the lake reloaded for the soaks", false);
                yield break;
            }
            // 10 fights by the lane's edges
            var b = ctl.Stage.L.Bathy;
            var edges = new List<Vector2>();
            for (int k = 0; k < b.NodeCount; k++)
            {
                var p = b.NodePos(k);
                if ((b.NodeFlags(k) & BedFlag.Lane) != 0 && (b.NodeFlags(k) & BedFlag.Edge) != 0 && p.y > 13f && p.y < 22f) edges.Add(p);
            }
            FishingController.FightShallowFrames = FishingController.FightUnderBedFrames = 0;
            EquipTest("bait_corn", ctl);
            int fights = 0;
            for (int f = 0; f < 10 && edges.Count > 0; f++)
            {
                yield return ToReady(ctl);
                var at = edges[(f * 7919) % edges.Count];
                if (!ctl.DebugPlaceRig(new Vector3(at.x, 0f, at.y))) continue;
                yield return null;
                bool carp = f % 2 == 0;
                var sp = GameDatabase.GetFish(carp ? "carp" : "largemouth_bass");
                float cm = carp ? 60f + 2.5f * f : 45f + f;
                if (!ctl.DebugHook(sp, cm, 300 + f, new Vector3(at.x, -2f, at.y))) continue;
                fights++;
                for (float g = 0f; g < 25f && ctl.State == FishingController.S.Fighting; g += Time.deltaTime) yield return null;
                ctl.DebugRelease();
                yield return new WaitForSeconds(0.3f);
            }
            yield return ToReady(ctl);
            DCheck($"D8 {soaked:0} s of soak (carp, bass, the stock) and {fights} fights by the lane's edges: {shallow} frames of a free fish in water shallower than it swims in, {under} under the bed - 0.3; {FishingController.FightShallowFrames} running fight frames in too little water, {FishingController.FightUnderBedFrames} under the bed - 0.2",
                fights >= 8 && shallow == 0 && under == 0 && FishingController.FightShallowFrames == 0 && FishingController.FightUnderBedFrames == 0);
            FishingController.NoBites = false;
            Application.targetFrameRate = fpsWas;
            Time.captureDeltaTime = capWas;
            depthCtl = ctl;
        }

        FishingController depthCtl;

        // ------------------------------------------------------------------ D9: the deep runs
        IEnumerator DeepRunCheck(FishingController ctl)
        {
            ctl = depthCtl != null ? depthCtl : ctl;
            int fpsWas = Application.targetFrameRate;
            float capWas = Time.captureDeltaTime;
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / 60f;
            FishingController.NoBites = true;
            FishingController.RecordRuns = true;
            EquipTest("bait_corn", ctl);
            var b = ctl.Stage.L.Bathy;
            // fight spots where the two sides differ: the lane's edges and the flats' rims, z 10..26
            var spots = new List<Vector2>();
            for (int k = 0; k < b.NodeCount; k++)
            {
                var p = b.NodePos(k);
                if (p.y < 10f || p.y > 26f || Mathf.Abs(p.x) > 12f || b.NodeDepth(k) < 2.5f) continue;
                if ((b.NodeFlags(k) & BedFlag.Edge) != 0 && (k % 5) == 0) spots.Add(p);
            }
            // The carp (a big runner) must take the deeper side clearly more often (its 30 % pull). The crucian has no pull, but
            // its deeper share is NOT 0.5: these spots sit on edges and the room rule turns runs back towards the middle, which is
            // the deeper water here, so the plain geometry alone gives ~0.6. For it the share is only logged; what is checked is
            // the rule itself: an unforced run keeps the step's own side, and a forced flip only happens away from water too
            // shallow for the fish into water deep enough (or, for a big runner, into water 0.3 m+ deeper).
            foreach (var (id, cm, lo, hi, bandGated) in new[] { ("carp", 70f, 0.58f, 0.75f, true), ("crucian_carp", 25f, 0f, 1f, false) })
            {
                var sp = GameDatabase.GetFish(id);
                float need = HabitatModel.MinWater(cm);
                bool runner = sp != null && sp.habitat != null && sp.habitat.runDeep && cm >= 40f;
                int deeper = 0, counted = 0, badDest = 0, replayOff = 0, runs = 0, ruleOff = 0, forcedN = 0;
                for (int f = 0; f < 60 && spots.Count > 0; f++)
                {
                    yield return ToReady(ctl);
                    var at = spots[(f * 104729) % spots.Count];
                    if (!ctl.DebugPlaceRig(new Vector3(at.x, 0f, at.y))) continue;
                    yield return null;
                    FishingController.RunSides.Clear();
                    FishingController.RunSteps.Clear();
                    int seed = 5000 + f;
                    if (!ctl.DebugHook(sp, cm, seed, new Vector3(at.x, -1.5f, at.y))) continue;
                    for (float g = 0f; g < 30f && ctl.State == FishingController.S.Fighting && FishingController.RunSides.Count < 4; g += Time.deltaTime) yield return null;
                    ctl.DebugRelease();
                    // the step draws are runRnd's own: a fresh System.Random(seed + 1) gives the same steps (their size: the
                    // room rule may flip the sign before the bed's choice)
                    var replay = new System.Random(seed + 1);
                    for (int i = 0; i < FishingController.RunSides.Count; i++)
                    {
                        var r = FishingController.RunSides[i];
                        runs++;
                        float step = (0.3f + 0.4f * (float)replay.NextDouble()) * (replay.NextDouble() < 0.5 ? -1f : 1f);
                        if (i >= FishingController.RunSteps.Count || Mathf.Abs(Mathf.Abs(step) - Mathf.Abs(FishingController.RunSteps[i])) > 1e-5f) replayOff++;
                        float wTaken = r.y, wOther = r.z;
                        if (Mathf.Abs(wTaken - wOther) >= 0.3f)
                        {
                            counted++;
                            if (wTaken > wOther) deeper++;
                        }
                        if (Mathf.Max(wTaken, wOther) >= need + 0.3f && wTaken < need + 0.3f) badDest++;
                        // the rule: RunSteps[i] is the step handed to the bed (after the room rule), r.x the side taken, r.w forced
                        if (i < FishingController.RunSteps.Count)
                        {
                            bool flipped = Mathf.Sign(r.x) != Mathf.Sign(FishingController.RunSteps[i]);
                            bool forced = r.w > 0.5f;
                            if (forced) forcedN++;
                            bool fromShallow = wOther < need + 0.3f && wTaken >= need + 0.3f;
                            bool deepPull = runner && wTaken >= wOther + 0.3f;
                            if (forced != flipped || (forced && !fromShallow && !deepPull)) ruleOff++;
                        }
                    }
                    yield return new WaitForSeconds(0.2f);
                }
                float share = counted > 0 ? deeper / (float)counted : 0f;
                DCheck(string.Format(CIc, "D9 {0} {1:0} cm, 60 fights, {2} runs: of the {3} with sides 0.3 m+ apart {4} took the deeper ({5:0.00}, {6}); {7} headed into too little water with the other side open; {8} forced flips, {9} against the rule; the step draws match a fresh System.Random(seed + 1) ({10} off)",
                    id, cm, runs, counted, deeper, share, bandGated ? string.Format(CIc, "in [{0:0.00}, {1:0.00}]", lo, hi) : "geometry only, logged", badDest, forcedN, ruleOff, replayOff),
                    counted >= 30 && (!bandGated || (share >= lo && share <= hi)) && badDest == 0 && replayOff == 0 && ruleOff == 0);
            }
            yield return ToReady(ctl);
            FishingController.NoBites = false;
            FishingController.RecordRuns = false;
            Application.targetFrameRate = fpsWas;
            Time.captureDeltaTime = capWas;
        }

        // ------------------------------------------------------------------ D13: the pictures
        IEnumerator DepthShots(FishingController ctl, TerrainRecipe r, ObstacleSet raw)
        {
            ctl = depthCtl != null ? depthCtl : ctl;
            var L = ctl.Stage.L;
            yield return ToReady(ctl);
            // (the home view, no legend cues, no HUD: the overlay alone over the water)
            ctl.Watch?.DebugLurk(null);
            yield return new WaitForSeconds(3f);
            BathyOverlay.WriteDump(L.Bathy, "bathy_lake_" + L.Bathy.WorldSeed);
            var grid = L.Bathy;
            foreach (int seed in new[] { 1, 7 })
            {
                var b = seed == grid.WorldSeed ? grid : BathyGen.Build(L, r, seed, raw);
                if (b != grid) BathyOverlay.WriteDump(b, "bathy_lake_" + seed);
                L.Bathy = b;
                var ov = BathyOverlay.Create(ctl.Stage);
                yield return new WaitForSeconds(0.5f);
                DCheck($"D13 the contour overlay for seed {seed}: {ov.Dots} contour dots", ov.Dots > 200);
                var hud = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(cv => cv.enabled && cv.isRootCanvas && cv.name != "PixelCanvas").ToList();
                foreach (var cv in hud) cv.enabled = false;
                yield return NamedShot("depth_show_lake_seed" + seed);
                foreach (var cv in hud) if (cv != null) cv.enabled = true;
                Destroy(ov.gameObject);
                yield return null;
            }
            L.Bathy = grid;
        }
    }
}
