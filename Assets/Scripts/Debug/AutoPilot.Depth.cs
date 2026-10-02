using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FishingKing
{
    /// <summary>
    /// -fkauto depth (Docs/terrain_depth_spec.md 14.1): the lake's generated bed. Run on the lake
    /// (-fkscene Fishing -fkstage lake; -fkauto implies world seed 1). [DEPTH] CHECK lines, then "depth test done: N failed":
    /// D1 determinism and the golden hash; D2 seeds 1..50 (V1-V8, attempts, the fallback); D3 every other stage (and the
    /// lake with the grid off, off the grid) bit-identical to the old profile; D4 the queries; D5 the save's world seed;
    /// D6 the lying float (tilt, lie, label, hint, standing up, a fight from it; shots); D7 the habitat's shift of the
    /// targets; D8 the body rule (wander / approach / bite / flee and fights never in water too shallow, never under the bed);
    /// D9 the deep runs; D10 the golden carp's lurk on the weed edge and its spot; D11 the bite budget's estimator over
    /// 50 seeds (per species, and the stock's bites per bait, rig class, period and rod); D12 the obstacle bands over the new bed; D13 the dump and the contour overlay (two seeds).
    /// </summary>
    public partial class AutoPilot
    {
        int depthFails;
        /// <summary>The golden hash per recipe version (D1: seed 12345); 0 = record it (print and pass with a note).</summary>
        static readonly Dictionary<int, uint> GoldenHash = new Dictionary<int, uint> { [1] = 0x5866ac8fu };   // (v1: recorded by the Mono player)

        void DCheck(string what, bool ok)
        {
            if (!ok) depthFails++;
            Log($"[DEPTH] CHECK {(ok ? "PASS" : "FAIL")} {what}");
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
            ShiftCheck(ctl);
            BandsCheck(ctl);
            yield return LegendCheck(ctl);
            yield return EstimatorCheck(ctl, recipe, raw);
            yield return FloatCheck(ctl);
            yield return BodyCheck(ctl);
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
            int fallbacks = 0, failed = 0;
            var hashes = new HashSet<uint>();
            float msMax = 0f, msSum = 0f;
            string firstFail = "";
            var v8 = new List<string>();
            for (int s = 1; s <= 50; s++)
            {
                var b = BathyGen.Build(L, r, s, raw);
                hist[Mathf.Min(r.attempts, b.Attempt)]++;
                if (b.Fallback) fallbacks++;
                hashes.Add(b.Hash);
                msMax = Mathf.Max(msMax, b.BuildMs);
                msSum += b.BuildMs;
                // (the accepted attempt's report: the last block)
                var lines = b.Checks.Split('\n');
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
                if (s % 10 == 0) yield return null;
            }
            DCheck(string.Format(CIc, "D2 seeds 1..50: V1-V8 pass on every accepted grid ({0} failed{1}); attempts {2}; fallback {3}; distinct hashes {4}/50; built in max {5:0.0} ms, mean {6:0.0} ms (target 30)",
                failed, firstFail, string.Join(" ", hist.Select((c, i) => i + ":" + c)), fallbacks, hashes.Count, msMax, msSum / 50f),
                failed == 0 && fallbacks == 0 && hashes.Count == 50);
            foreach (var l in v8) Log("[DEPTH] " + l);
            var fb = BathyGen.BuildFallback(L, r, 1, raw, out int fbFails, out string report);
            foreach (var l in report.Split('\n')) if (l.Length > 0) Log("[DEPTH] fallback " + l);
            DCheck($"D2 the fallback recipe alone passes V1-V8 ({fbFails} failed, hash 0x{fb.Hash:x8})", fbFails == 0);
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
            DCheck($"D3 lake off the grid = the profile ({offGrid} differ); the step across the grid's edge {F2(jump)} m (<= 0.02)", offGrid == 0 && jump <= 0.02f);
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

        // ------------------------------------------------------------------ D7: the targets' shift
        void ShiftCheck(FishingController ctl)
        {
            var b = ctl.Stage.L.Bathy;
            var hab = ctl.Habitat;
            float zMax = 30f;   // (the bamboo rod's FishZMax)
            float[] Shares(HabitatModel.Region reg, float[] cum, float total, int salt)
            {
                var rng = new System.Random(1515 + salt);
                var c = new float[9];
                for (int t = 0; t < 2000; t++) c[(int)b.NodeKind(reg.nodes[HabitatModel.Pick(cum, (float)rng.NextDouble() * total)])] += 1f / 2000f;
                return c;
            }
            float S(float[] x, params BedKind[] ks) => ks.Sum(k => x[(int)k]);
            var sh = new Dictionary<string, float[][]>();
            float[] old = null;
            foreach (var id in new[] { "crucian_carp", "bluegill", "carp", "largemouth_bass" })
            {
                var sp = GameDatabase.GetFish(id);
                var per = new float[4][];
                for (int p = 0; p < 4; p++)
                {
                    var (reg, cum, total) = hab.DebugSampler(sp, p, zMax);
                    per[p] = Shares(reg, cum, total, p + 10 * id.Length);
                    if (old == null)
                    {
                        var u = new float[reg.u.Length];
                        float acc = 0f;
                        for (int q = 0; q < u.Length; q++) u[q] = acc += reg.u[q];
                        old = Shares(reg, u, acc, 999);
                    }
                    Log("[DEPTH] D7 " + id + " " + GameClock.Id((Period)p) + " " + string.Join(" ", Enumerable.Range(0, 9).Select(k => $"{(BedKind)k}:{F2(per[p][k])}/{F2(old[k])}")));
                }
                sh[id] = per;
            }
            float bassDay = S(sh["largemouth_bass"][1], BedKind.Dropoff, BedKind.Hump, BedKind.Shoal) / S(old, BedKind.Dropoff, BedKind.Hump, BedKind.Shoal);
            float bassDawn = S(sh["largemouth_bass"][0], BedKind.Flat, BedKind.Shelf) / S(old, BedKind.Flat, BedKind.Shelf);
            float cruDawn = sh["crucian_carp"][0][(int)BedKind.Flat] / old[(int)BedKind.Flat];
            float cruDay = sh["crucian_carp"][1][(int)BedKind.Flat] / sh["crucian_carp"][0][(int)BedKind.Flat];
            float carpNight = S(sh["carp"][3], BedKind.Flat, BedKind.Shoal) / S(old, BedKind.Flat, BedKind.Shoal);
            float carpDay = S(sh["carp"][1], BedKind.Hole, BedKind.Channel, BedKind.Basin) / S(old, BedKind.Hole, BedKind.Channel, BedKind.Basin);
            DCheck($"D7 2000 targets each (bamboo's water, z <= 30): bass by day on drop-off + hump + shoal x{F2(bassDay)} (>= 1.4), at dawn on flat + shelf x{F2(bassDawn)} (>= 1.3); crucian at dawn on the flats x{F2(cruDawn)} (>= 1.3), by day x{F2(cruDay)} of its dawn share (<= 0.8); carp at night on flat + shoal x{F2(carpNight)} (>= 1.4), by day in hole + channel + basin x{F2(carpDay)} (>= 1.2)",
                bassDay >= 1.4f && bassDawn >= 1.3f && cruDawn >= 1.3f && cruDay <= 0.8f && carpNight >= 1.4f && carpDay >= 1.2f);
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

        // ------------------------------------------------------------------ D11: the estimator
        IEnumerator EstimatorCheck(FishingController ctl, TerrainRecipe r, ObstacleSet raw)
        {
            var L = ctl.Stage.L;
            var species = new[] { "crucian_carp", "bluegill", "carp", "largemouth_bass" }.Select(GameDatabase.GetFish).ToList();
            // (a species and a rig class go together when some bait of that class draws it at all)
            bool Valid(FishSpecies sp, RigClass rc) => GameDatabase.Baits.Any(bt =>
                (HabitatModel.IsLure(rc) ? bt.isLure && FishHabitat.RigOf(bt, 0f) == rc : !bt.isLure) && sp.Appeal(bt) > 0f);
            // the user's rule (spec 8.1): per bait, rig class, period and rod, the stock's bites after the lever over today's;
            // a species' share = its stock weight x activity x sqrt(activity) (the approach roll) x its appeal for the bait
            var stock = new Dictionary<string, float>();
            foreach (var kv in ctl.Stage.Def.spawns) stock[kv.Key] = kv.Value;
            var floats = new[] { RigClass.F1, RigClass.F2, RigClass.F4, RigClass.F6 };
            var baits = GameDatabase.Baits.Where(bt => species.Any(sp => sp.Appeal(bt) > 0f)).ToList();
            var rods = new[] { ("rod_bamboo", 16f), ("rod_carbon", 24f), ("rod_dragon", 36f) };
            int n = 0, rawOut = 0, scaledOut = 0, scaleOut = 0, pooledN = 0, pooledOut = 0;
            float rawMin = 9f, rawMax = 0f, scMin = 9f, scMax = 0f, sMin = 9f, sMax = 0f, poMin = 9f, poMax = 0f;
            string firstBad = "", firstPooled = "";
            var outByRod = new Dictionary<float, int>();
            var best = new Dictionary<string, float>();
            var t0 = Time.realtimeSinceStartup;
            for (int seed = 1; seed <= 50; seed++)
            {
                var b = seed == 1 ? L.Bathy : BathyGen.Build(L, r, seed, raw);
                var hab = new FishHabitat(ctl.Stage, ctl, b);
                foreach (var (rid, cd) in rods)
                {
                    var got = new Dictionary<(string, int, RigClass), HabitatModel.Budget>();
                    foreach (var sp in species)
                    for (int p = 0; p < 4; p++)
                    foreach (RigClass rc in Enum.GetValues(typeof(RigClass)))
                    {
                        if (!Valid(sp, rc)) continue;
                        var bu = hab.Budget(sp, p, rc, cd, true);
                        got[(sp.id, p, rc)] = bu;
                        n++;
                        rawMin = Mathf.Min(rawMin, bu.raw);
                        rawMax = Mathf.Max(rawMax, bu.raw);
                        scMin = Mathf.Min(scMin, bu.scaled);
                        scMax = Mathf.Max(scMax, bu.scaled);
                        sMin = Mathf.Min(sMin, bu.scale);
                        sMax = Mathf.Max(sMax, bu.scale);
                        bool ro = bu.raw < HabitatModel.RawMin || bu.raw > HabitatModel.RawMax, so = bu.scaled < 0.9f || bu.scaled > 1.1f;
                        bool co = bu.scale < HabitatModel.ScaleMin - 1e-4f || bu.scale > HabitatModel.ScaleMax + 1e-4f;
                        if (ro) rawOut++;
                        if (so) scaledOut++;
                        if (co) scaleOut++;
                        if (ro || so) outByRod[cd] = (outByRod.TryGetValue(cd, out int k) ? k : 0) + 1;
                        if ((ro || so || co) && firstBad.Length == 0) firstBad = $" (first: seed {seed} {rid} {sp.id} {GameClock.Id((Period)p)} {rc} raw {F2(bu.raw)} scaled {F2(bu.scaled)})";
                        if (seed == 1 && cd == 16f)
                        {
                            string key = sp.id + " " + GameClock.Id((Period)p);
                            best[key] = Mathf.Max(best.TryGetValue(key, out float v) ? v : 0f, bu.best10);
                            Log(string.Format(CIc, "[HAB] budget {0} {1} {2} rod {3:0} raw {4:0.00} scale {5:0.00} scaled {6:0.00} best10 {7:0.00} worst10 {8:0.00} (before the scale {9:0.00})",
                                sp.id, GameClock.Id((Period)p), rc, cd, bu.raw, bu.scale, bu.scaled, bu.best10, bu.worst10, bu.best10Raw));
                        }
                    }
                    foreach (var bt in baits)
                    foreach (var rc in bt.isLure ? new[] { FishHabitat.RigOf(bt, 0f) } : floats)
                    for (int p = 0; p < 4; p++)
                    {
                        double num = 0, den = 0;
                        foreach (var sp in species)
                        {
                            float app = sp.Appeal(bt);
                            if (app <= 0f || !got.TryGetValue((sp.id, p, rc), out var bu)) continue;
                            float a = TimeActivity.A(sp.id, (Period)p);
                            float w = (stock.TryGetValue(sp.id, out float sw) ? sw : 0f) * a * Mathf.Sqrt(Mathf.Max(0f, a)) * app;
                            num += w * bu.bed;
                            den += w * bu.today;
                        }
                        if (den <= 0) continue;
                        float ratio = (float)(num / den);
                        pooledN++;
                        poMin = Mathf.Min(poMin, ratio);
                        poMax = Mathf.Max(poMax, ratio);
                        if (ratio < 0.8f || ratio > 1.25f)
                        {
                            pooledOut++;
                            if (firstPooled.Length == 0) firstPooled = $" (first: seed {seed} {rid} {bt.id} {rc} {GameClock.Id((Period)p)} x{F2(ratio)})";
                        }
                    }
                }
                if (seed % 2 == 0) yield return null;
            }
            // the spec's per-species gate (fixed bounds: raw within what the clamp can take back, the scaled estimate within 10 %)
            DCheck(string.Format(CIc, "D11 the bite budget per species over seeds 1..50 x 4 species x 4 periods x their rig classes x bamboo / carbon / dragon ({0} estimates, {1:0} s): raw {2:0.00}..{3:0.00} (in [{4:0.00}, {5:0.00}]: {6} out), scaled {7:0.00}..{8:0.00} (in [0.90, 1.10]: {9} out), scale {10:0.00}..{11:0.00} (in [{12:0.00}, {13:0.00}]: {14} out); raw or scaled out by rod {15}{16}",
                n, Time.realtimeSinceStartup - t0, rawMin, rawMax, HabitatModel.RawMin, HabitatModel.RawMax, rawOut, scMin, scMax, scaledOut, sMin, sMax, HabitatModel.ScaleMin, HabitatModel.ScaleMax, scaleOut,
                string.Join(" / ", rods.Select(rd => rd.Item2.ToString("0", CIc) + " m: " + (outByRod.TryGetValue(rd.Item2, out int o) ? o : 0))), firstBad),
                rawOut == 0 && scaledOut == 0 && scaleOut == 0);
            // the user's rule: every bait, rig class, period and rod within 0.8..1.25 of today's
            DCheck(string.Format(CIc, "D11 the stock's bites per bait x rig class x period x rod after the lever, over today's (seeds 1..50, {0} cases): {1:0.00}..{2:0.00} (in [0.80, 1.25]: {3} out){4}",
                pooledN, poMin, poMax, pooledOut, firstPooled), pooledN > 0 && pooledOut == 0);
            DCheck("D11 seed 1, bamboo: the best 10 % of casts (the best rig class, after the scale) >= 1.2 for every species and period: "
                   + string.Join(", ", best.Select(kv => kv.Key + " " + F2(kv.Value))), best.Count == 16 && best.Values.All(v => v >= 1.2f));
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

        /// <summary>A crop of the screen round a scene point, x<paramref name="scale"/> (nearest), as a texture.</summary>
        static Texture2D ScreenCrop(Vector2 world2D, int w, int h, int scale)
        {
            var pv = PixelView.Current;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
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
            foreach (var (id, cm, lo, hi) in new[] { ("carp", 70f, 0.58f, 0.75f), ("crucian_carp", 25f, 0.40f, 0.60f) })
            {
                var sp = GameDatabase.GetFish(id);
                float need = HabitatModel.MinWater(cm);
                int deeper = 0, counted = 0, badDest = 0, replayOff = 0, runs = 0;
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
                    }
                    yield return new WaitForSeconds(0.2f);
                }
                float share = counted > 0 ? deeper / (float)counted : 0f;
                DCheck(string.Format(CIc, "D9 {0} {1:0} cm, 60 fights, {2} runs: of the {3} with sides 0.3 m+ apart {4} took the deeper ({5:0.00}, in [{6:0.00}, {7:0.00}]); {8} headed into too little water with the other side open; the step draws match a fresh System.Random(seed + 1) ({9} off)",
                    id, cm, runs, counted, deeper, share, lo, hi, badDest, replayOff), counted >= 30 && share >= lo && share <= hi && badDest == 0 && replayOff == 0);
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
