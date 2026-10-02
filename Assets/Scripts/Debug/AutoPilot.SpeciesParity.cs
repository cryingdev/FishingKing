using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The parity proof of the move to the species files (part of -fkauto species while <see cref="LegacySpecies"/> exists):
    /// P1 every species and stage value of the loaded data equals the old C# tables' bit for bit (SpeciesDump), after the
    /// declared deltas (the four cover tags no cover zone of their stage offers, dropped); P2 the real FishSpawner.Pick
    /// draws the same species from the same seeds over the old and the new data (7 stages x 8 clock points x 2 gear sets x
    /// 5000 draws); P3 TimeActivity, FishOfStage, StageOfFish and the legends per stage agree. Delete with LegacySpecies.
    /// </summary>
    public partial class AutoPilot
    {
        /// <summary>The cover tags no cover zone of the species' stage offers (they never matched): dropped by the move.</summary>
        static readonly (string id, string tag)[] DroppedCoverTags =
            { ("rockfish", "rock"), ("black_porgy", "rock"), ("snakehead", "weed"), ("arapaima", "weed") };

        void SpeciesParity()
        {
            var newFish = new List<FishSpecies>(GameDatabase.Fish);
            var newStages = new List<StageDef>(GameDatabase.Stages);

            // ---- P1: the dumps
            LegacySpecies.Build(out var lf, out var ls);
            var now = SpeciesDump.Lines(newFish, newStages);
            var raw = SpeciesDump.Lines(lf, ls);
            var rawDiff = DiffKeyed(raw, now);
            var deltaPaths = DroppedCoverTags.Select(d => $"fish[{d.id}].coverFor").ToList();
            bool onlyDeclared = rawDiff.All(d => deltaPaths.Any(p => d.Contains(p)));
            Log($"[SPECIES] P1 before the declared deltas: {rawDiff.Count} lines differ, all in the four dropped cover tags: {onlyDeclared}");
            foreach (var d in rawDiff.Take(20)) Log("[SPECIES]   " + d);
            foreach (var (id, tag) in DroppedCoverTags)
            {
                var sp = lf.First(f => f.id == id);
                sp.coverFor = sp.coverFor.Where(t => t != tag).ToArray();
                Log($"[SPECIES] P1 declared delta: {id} coverFor -{tag} (now {string.Join(",", sp.coverFor)})");
            }
            var old = SpeciesDump.Lines(lf, ls);
            var diff = DiffKeyed(old, now);
            foreach (var d in diff.Take(20)) Log("[SPECIES] P1 differs: " + d);
            File.WriteAllText(Path.Combine(shots, "species_dump_legacy.txt"), string.Join("\n", old) + "\n");
            SpCheck($"P1 dump parity: {now.Count} lines (old {old.Count}), {diff.Count} differ after the {DroppedCoverTags.Length} declared deltas" +
                    $" ({rawDiff.Count} before them, all theirs: {onlyDeclared})",
                diff.Count == 0 && old.SequenceEqual(now) && onlyDeclared && rawDiff.Count > 0);

            // ---- P2: Pick sequences over the old and the new data, same seeds
            LegacySpecies.Build(out lf, out ls);
            var rig = new PickRig();
            float clockWas = GameClock.Min;
            string rodWas = Game.I.Rod.id, baitWas = Game.I.Bait.id;
            int runs = 0, bad = 0;
            long draws = 0;
            string firstBad = "";
            var a = new string[5000];
            var b = new string[5000];
            foreach (var st in newStages)
            foreach (float m in PickClock())
            foreach (var (rod, bait) in PickGear)
            {
                SetGear(rod, bait);
                GameClock.Set(m);
                int seed = 7919 * runs + 17;
                GameDatabase.Install(lf, ls);
                rig.Stage(st.id);
                Random.InitState(seed);
                for (int k = 0; k < a.Length; k++) a[k] = rig.Pick().id;
                GameDatabase.Install(newFish, newStages);
                rig.Stage(st.id);
                Random.InitState(seed);
                for (int k = 0; k < b.Length; k++) b[k] = rig.Pick().id;
                runs++;
                draws += a.Length;
                int at = -1;
                for (int k = 0; k < a.Length && at < 0; k++)
                    if (a[k] != b[k]) at = k;
                if (at >= 0)
                {
                    bad++;
                    if (firstBad.Length == 0) firstBad = $" (first: {st.id} {m} {rod} {bait} draw {at}: old {a[at]}, new {b[at]})";
                }
                if (runs == 1) Log($"[SPECIES] P2 sample {st.id} {m} {rod} {bait}: {string.Join(",", b.Take(12))} ...");
            }
            GameDatabase.Install(newFish, newStages);
            GameClock.Set(clockWas);
            SetGear(rodWas, baitWas);
            Destroy(rig.go);
            SpCheck($"P2 Pick sequences: {runs} runs (7 stages x 8 clock points x 2 gear sets) x 5000 = {draws} draws, {bad} runs differ{firstBad}",
                bad == 0 && runs == 7 * 8 * 2);

            // ---- P3: the accessors
            int actBad = 0;
            foreach (var f in newFish)
            for (int p = 0; p < 4; p++)
            {
                float x = TimeActivity.A(f.id, (Period)p), y = LegacySpecies.Activity(f.id, (Period)p);
                if (System.BitConverter.ToInt32(System.BitConverter.GetBytes(x), 0) != System.BitConverter.ToInt32(System.BitConverter.GetBytes(y), 0)) actBad++;
            }
            SpCheck($"P3 TimeActivity.A: {newFish.Count} species x 4 periods, {actBad} differ from the old table; unknown id {TimeActivity.A("no_such_fish", Period.Day)}",
                actBad == 0 && TimeActivity.A("no_such_fish", Period.Day) == 1f && TimeActivity.A(null, Period.Night) == 1f);
            LegacySpecies.Build(out lf, out ls);
            string Acc()
            {
                var o = new List<string>();
                foreach (var f in GameDatabase.Fish)
                    o.Add($"{f.id}: night-only {TimeActivity.NightOnly(f.id)}, '{TimeActivity.Describe(f.id)}', stage {GameDatabase.StageOfFish(f.id)}");
                foreach (var s in GameDatabase.Stages)
                {
                    o.Add($"{s.id}: fish {string.Join(",", GameDatabase.FishOfStage(s.id).Select(f => f.id))}");
                    o.Add($"{s.id}: legends {string.Join(",", GameDatabase.FishOfStage(s.id).Where(f => f.encounter != null).Select(f => f.id))}");
                }
                return string.Join("\n", o);
            }
            GameDatabase.Install(lf, ls);
            string accOld = Acc();
            GameDatabase.Install(newFish, newStages);
            string accNew = Acc();
            var ad = Diff(accOld.Split('\n').ToList(), accNew.Split('\n').ToList());
            foreach (var d in ad.Take(10)) Log("[SPECIES] P3 differs: " + d);
            SpCheck($"P3 NightOnly, Describe, StageOfFish, FishOfStage and the legends per stage: {accNew.Split('\n').Length} lines, {ad.Count} differ", ad.Count == 0);
        }

        /// <summary>The dump lines ("path = value") that differ, by path: a path on one side only, or another value.</summary>
        static List<string> DiffKeyed(List<string> a, List<string> b)
        {
            Dictionary<string, string> Map(List<string> l)
            {
                var m = new Dictionary<string, string>();
                foreach (var x in l)
                {
                    int i = x.IndexOf(" = ", System.StringComparison.Ordinal);
                    m[i >= 0 ? x.Substring(0, i) : x] = i >= 0 ? x.Substring(i + 3) : "";
                }
                return m;
            }
            var ma = Map(a);
            var mb = Map(b);
            var o = new List<string>();
            foreach (var kv in ma)
                if (!mb.TryGetValue(kv.Key, out var v)) o.Add($"{kv.Key}: old {kv.Value} | new <none>");
                else if (v != kv.Value) o.Add($"{kv.Key}: old {kv.Value} | new {v}");
            foreach (var kv in mb)
                if (!ma.ContainsKey(kv.Key)) o.Add($"{kv.Key}: old <none> | new {kv.Value}");
            if (ma.Count != a.Count || mb.Count != b.Count) o.Add($"repeated paths: old {a.Count - ma.Count}, new {b.Count - mb.Count}");
            return o;
        }

        static List<string> Diff(List<string> a, List<string> b)
        {
            var o = new List<string>();
            int n = Mathf.Max(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                string x = i < a.Count ? a[i] : "<none>", y = i < b.Count ? b[i] : "<none>";
                if (x != y) o.Add($"#{i}: old {x} | new {y}");
            }
            return o;
        }
    }
}
