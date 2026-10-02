using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>Keeps the stage stocked with fish picked from its weighted species table.</summary>
    public class FishSpawner : MonoBehaviour
    {
        readonly List<FishAgent> fish = new List<FishAgent>();
        FishingController ctl;
        StageDef def;
        float respawn;

        public IReadOnlyList<FishAgent> Fish => fish;

        /// <summary>No new fish while set (a legend encounter is on).</summary>
        public bool Paused;

        /// <summary>Test switch (-fkfish): stock only this species.</summary>
        public static FishSpecies OnlySpecies;

        public void Init(FishingController c)
        {
            ctl = c;
            def = c.Stage.Def;
            for (int i = 0; i < def.population; i++) Spawn(false);
            GameClock.PeriodBegan += OnPeriod;
        }

        void OnDestroy() => GameClock.PeriodBegan -= OnPeriod;

        /// <summary>
        /// A new period (Docs/time_currents_spec.md 6.1): every wandering fish whose species is not about now (a = 0) swims
        /// off at a random moment within the next 60 real seconds (engaged, hooked and lifted fish are left alone).
        /// </summary>
        void OnPeriod(Period p)
        {
            foreach (var f in fish)
                if (f != null && f.State == FishAgent.St.Wander && TimeActivity.A(f.Sp.id, p) <= 0f)
                    f.LeaveAt = Time.unscaledTime + Random.Range(0f, 60f);
        }

        public void Remove(FishAgent f)
        {
            fish.Remove(f);
            respawn = Random.Range(1.5f, 4f);
        }

        void Update()
        {
            if (Paused) return;
            if (fish.Count >= def.population) return;
            respawn -= Time.deltaTime;
            if (respawn <= 0)
            {
                Spawn(true);
                respawn = Random.Range(1.5f, 4f);
            }
        }

        FishSpecies Pick()
        {
            if (OnlySpecies != null && OnlySpecies.encounter == null) return OnlySpecies;
            var rod = Game.I.Rod;
            var bait = Game.I.Bait;
            bool legendAlive = fish.Any(f => f.Sp.rarity == Rarity.Legendary);
            float total = 0;
            var weights = new List<(FishSpecies, float)>();
            var look = GameClock.Look;
            foreach (var kv in def.spawns)
            {
                var sp = GameDatabase.GetFish(kv.Key);
                if (sp == null) continue;
                // a legend met through the underwater encounter never swims about as an ordinary fish
                if (sp.encounter != null) continue;
                // x its activity at this time of day (0: not about now; Docs/time_currents_spec.md 6); on a generated bed also
                // x how much of its habitat the lake has (Docs/lake_phase2_spec.md A4: the roster's weight is the base)
                float w = ctl != null && ctl.Habitat != null ? ctl.Habitat.SpawnWeight(sp, kv.Value, look) : kv.Value * TimeActivity.A(sp.id, look);
                if (sp.rarity >= Rarity.Rare) w *= rod.luck;
                if (sp.rarity >= Rarity.Epic) w *= bait.rareBoost;
                if (sp.rarity == Rarity.Legendary)
                {
                    w *= bait.rareBoost;
                    if (legendAlive) w = 0;
                }
                weights.Add((sp, w));
                total += w;
            }
            if (weights.Count == 0) return GameDatabase.FishOfStage(def.id).First(f => f.encounter == null);
            float r = Random.value * total;
            foreach (var (sp, w) in weights)
            {
                if (w <= 0f) continue;
                r -= w;
                if (r <= 0) return sp;
            }
            foreach (var (sp, w) in weights) if (w > 0f) return sp;
            return weights[0].Item1;
        }

        public static float RollSize(FishSpecies sp)
        {
            float t = Mathf.Pow(Random.value, 1.7f); // big ones are rarer
            return Mathf.Lerp(sp.minCm, sp.maxCm, t);
        }

        /// <summary>
        /// A fish already on the hook at <paramref name="pos"/> (a legend hooked out of its encounter): heading away from
        /// the angler (+z), added to the stock like any other.
        /// </summary>
        public FishAgent SpawnHooked(FishSpecies sp, float cm, Vector3 pos)
        {
            var go = new GameObject("Fish_" + sp.id);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<FishAgent>();
            a.Init(ctl, sp, cm, pos);
            a.Heading = Mathf.PI * 0.5f;
            a.SetHooked();
            fish.Add(a);
            return a;
        }

        void Spawn(bool fromDistance)
        {
            var sp = Pick();
            var L = ctl.Stage.L;
            var P = ctl.Stage.P;
            float zMax = Mathf.Min(L.zFar - 2f, ctl.FishZMax);
            float cm;
            Vector3 pos;
            if (L.Terrain && ctl.Habitat != null)
            {
                // on a generated bed (the lake): where its habitat puts it (Docs/terrain_depth_spec.md 7.4)
                cm = RollSize(sp);
                pos = ctl.Habitat.SpawnPoint(sp, fromDistance, zMax);
            }
            else
            {
                float z = fromDistance ? zMax - Random.Range(0f, 4f) : Random.Range(L.zNear + 2f, zMax);
                float half = Mathf.Min(L.xLim - 0.5f, P.VisibleHalfWidth(z, 600));
                float bottom = L.ProfileDepth(z) - 0.3f;
                float d = Random.Range(Mathf.Min(sp.depthMin, bottom), Mathf.Min(sp.depthMax, bottom));
                pos = new Vector3(Random.Range(-half, half), -Mathf.Max(0.3f, d), z);
                if (L.IsIce) pos = new Vector3(L.holeX + Random.Range(-8f, 8f), pos.y, Random.Range(L.zNear + 1f, 18f));
                cm = RollSize(sp);
            }
            var go = new GameObject("Fish_" + sp.id);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<FishAgent>();
            a.Init(ctl, sp, cm, pos);
            fish.Add(a);
        }
    }
}
