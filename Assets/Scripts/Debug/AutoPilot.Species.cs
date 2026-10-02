using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto species (Docs/data_reference.md 2.7): the species data check, no scene or pointer needed. The validator on
    /// the shipped data (Resources; with -fkrepo &lt;repo root&gt; also the Blender scripts, skipped without it) must find no
    /// error, the game must have loaded the same data cleanly (GameDatabase.LoadErrors, the same counts), and every broken
    /// fixture (SpeciesFixtures) must be caught. No species or stage count is fixed here: adding a fish needs no edit. Writes
    /// species_dump.txt (every species and stage value, SpeciesDump) and spawn_baseline.txt (FishSpawner.Pick's shares per
    /// stage, period and gear, and the stock's bite mass per bait) to -fkshots. [SPECIES] lines, at the end
    /// "species test done: N failed".
    /// </summary>
    public partial class AutoPilot
    {
        int speciesFails;
        static readonly CultureInfo CIs = CultureInfo.InvariantCulture;

        void SpCheck(string what, bool ok)
        {
            if (!ok) speciesFails++;
            Log($"[SPECIES] CHECK {(ok ? "PASS" : "FAIL")} {what}");
        }

        IEnumerator SpeciesTest()
        {
            yield return null;
            var t0 = Time.realtimeSinceStartup;
            // ---- the validator on the shipped data
            var ctx = SpeciesCheck.FromResources(Arg("-fkrepo"));
            var found = SpeciesCheck.Run(ctx, out var res);
            foreach (var f in found) Log($"[SPECIES] {(f.error ? "ERROR" : "WARN")} {f}");
            Log("[SPECIES] validate: " + SpeciesCheck.Summary(found, res) +
                (ctx.fkFishPy != null ? ", with the Blender scripts" : ", without the Blender scripts (no -fkrepo)"));
            // (no fixed counts: a new species file and its roster line must pass without touching this test)
            SpCheck($"the validator passes the shipped data: {SpeciesCheck.Errors(found)} errors, {res.fish.Count} species, {res.stages.Count} stages",
                SpeciesCheck.Errors(found) == 0 && res.fish.Count > 0 && res.stages.Count > 0);
            // (-fkrepo is optional: without it the Blender checks are skipped; given, its scripts must be found)
            if (string.IsNullOrEmpty(Arg("-fkrepo"))) Log("[SPECIES] CHECK SKIP the Blender checks: no -fkrepo");
            else SpCheck("the Blender checks ran (-fkrepo " + Arg("-fkrepo") + ")", ctx.fkFishPy != null);
            SpCheck($"GameDatabase loaded cleanly: {GameDatabase.LoadErrors.Count} load errors, {GameDatabase.Fish.Count} species, {GameDatabase.Stages.Count} stages (the validator read {res.fish.Count}, {res.stages.Count})",
                GameDatabase.LoadErrors.Count == 0 && GameDatabase.Fish.Count == res.fish.Count && GameDatabase.Stages.Count == res.stages.Count);
            // ---- the validator catches broken data
            speciesFails += SpeciesFixtures.Run(ctx, Log);
            yield return null;
            // ---- the dumps
            File.WriteAllText(Path.Combine(shots, "species_dump.txt"), SpeciesDump.Write(GameDatabase.Fish, GameDatabase.Stages));
            File.WriteAllText(Path.Combine(shots, "spawn_baseline.txt"), SpawnBaseline());
            Log($"[SPECIES] wrote species_dump.txt and spawn_baseline.txt to {shots}");
            Log(string.Format(CIs, "[SPECIES] species test done: {0} failed ({1:0.0} s)", speciesFails, Time.realtimeSinceStartup - t0));
            Application.Quit();
        }

        // ------------------------------------------------------------------ FishSpawner.Pick on a detached spawner
        /// <summary>A spawner that never stocks (Paused), its stage set by reflection: Pick() for the tests.</summary>
        sealed class PickRig
        {
            public GameObject go;
            public FishSpawner sp;
            System.Reflection.FieldInfo def;
            public System.Func<FishSpecies> Pick;

            public PickRig()
            {
                go = new GameObject("[SpeciesPick]");
                sp = go.AddComponent<FishSpawner>();
                sp.Paused = true;
                var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                def = typeof(FishSpawner).GetField("def", bf);
                Pick = (System.Func<FishSpecies>)System.Delegate.CreateDelegate(typeof(System.Func<FishSpecies>), sp, typeof(FishSpawner).GetMethod("Pick", bf));
            }

            public void Stage(string id) => def.SetValue(sp, GameDatabase.GetStage(id));
        }

        /// <summary>The gear the spawn tests run with: the starter rod and bait, and the luckiest rod with the golden bait (rare x 3).</summary>
        static readonly (string rod, string bait)[] PickGear = { ("rod_bamboo", "bait_paste"), ("rod_dragon", "bait_golden") };

        /// <summary>The clock points: each period's centre, and each boundary (the cross-fade's midpoint, F = 0.5).</summary>
        static float[] PickClock() => new[]
        {
            GameClock.Centre(Period.Dawn), GameClock.Centre(Period.Day), GameClock.Centre(Period.Evening), GameClock.Centre(Period.Night),
            300f, 480f, 1020f, 1200f,
        };

        void SetGear(string rod, string bait)
        {
            if (!Game.I.HasBait(bait)) Game.I.AddBait(bait, 10);
            Game.I.Equip(GameDatabase.GetItem(rod));
            Game.I.Equip(GameDatabase.GetItem(bait));
        }

        /// <summary>
        /// spawn_baseline.txt: per stage, clock point and gear, each species' share of 5000 Pick draws (seeded), and per
        /// stage, period and bait the stock's bite mass (sum of weight x a x sqrt(a) x appeal over its ordinary species,
        /// the depth test's D11 share). The reference a later rebalance is compared with.
        /// </summary>
        string SpawnBaseline()
        {
            var sb = new StringBuilder();
            sb.Append("# FishSpawner.Pick shares: stage clock(min) rod bait -> species share (5000 seeded draws each)\n");
            var rig = new PickRig();
            float clockWas = GameClock.Min;
            string rodWas = Game.I.Rod.id, baitWas = Game.I.Bait.id;
            int run = 0;
            foreach (var st in GameDatabase.Stages)
            foreach (float m in PickClock())
            foreach (var (rod, bait) in PickGear)
            {
                SetGear(rod, bait);
                GameClock.Set(m);
                rig.Stage(st.id);
                Random.InitState(1000 + run++);
                var n = new Dictionary<string, int>();
                for (int k = 0; k < 5000; k++)
                {
                    string id = rig.Pick().id;
                    n[id] = (n.TryGetValue(id, out int c) ? c : 0) + 1;
                }
                sb.Append(string.Format(CIs, "{0} {1:0000} {2} {3} ->", st.id, m, rod, bait));
                foreach (var kv in st.spawns)
                    if (n.TryGetValue(kv.Key, out int c)) sb.Append(string.Format(CIs, " {0} {1:0.0000}", kv.Key, c / 5000f));
                sb.Append('\n');
            }
            sb.Append("# bite mass: stage period bait -> sum over its ordinary species of weight x a x sqrt(a) x appeal\n");
            foreach (var st in GameDatabase.Stages)
            for (int p = 0; p < 4; p++)
            foreach (var b in GameDatabase.Baits)
            {
                double mass = 0;
                foreach (var kv in st.spawns)
                {
                    var sp = GameDatabase.GetFish(kv.Key);
                    if (sp == null || sp.encounter != null) continue;
                    float a = TimeActivity.A(sp.id, (Period)p);
                    mass += kv.Value * a * Mathf.Sqrt(Mathf.Max(0f, a)) * sp.Appeal(b);
                }
                if (mass > 0) sb.Append(string.Format(CIs, "{0} {1} {2} {3:0.000}\n", st.id, GameClock.Id((Period)p), b.id, mass));
            }
            GameClock.Set(clockWas);
            SetGear(rodWas, baitWas);
            Destroy(rig.go);
            return sb.ToString();
        }
    }
}
