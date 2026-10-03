using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The aquarium check for one species (-fkaqua species -fkspecies &lt;id&gt;, and -fkauto newspecies; Docs/data_reference.md
    /// 2.7 확인 절차): run only when its size class fits a tank. A hungry one (60 % up its size range, as on its catch card;
    /// its smallest when that is too big for every tank) alone in the smallest tank that takes it; its food dropped over it
    /// through AquaFeed's test hooks (no pointer): a live food it eats (정어리 first, then 생새우) as one piece from above
    /// the water, else pellets. It must eat it by itself (its fullness up) within 25 s. Shots aqua_&lt;id&gt; and
    /// aqua_&lt;id&gt;_eat; [AQUA] CHECK lines.
    /// </summary>
    public partial class AquaPilot
    {
        /// <summary>An AquaPilot on <paramref name="host"/> for another scenario's use (nothing starts by itself).</summary>
        public static AquaPilot Attach(GameObject host, string shots)
        {
            var ap = host.AddComponent<AquaPilot>();
            ap.shots = shots;
            System.IO.Directory.CreateDirectory(shots);
            return ap;
        }

        /// <summary>The checks failed so far.</summary>
        public int Failed => fail;

        IEnumerator SpeciesTest()
        {
            yield return new WaitForSeconds(1.2f);
            yield return SpeciesFeed(Arg("-fkspecies"), null);
            Log($"done: {ok} ok, {fail} failed");
            Game.I.Save();
            Application.Quit();
        }

        /// <summary>
        /// The check (see the class summary). <paramref name="result"/>: "pass", "fail" or "skip: &lt;why&gt;" (no tank takes it).
        /// </summary>
        public IEnumerator SpeciesFeed(string id, Action<string> result)
        {
            var sp = GameDatabase.GetFish(id);
            if (sp == null)
            {
                Check(false, $"species {id ?? "(no -fkspecies)"} exists");
                result?.Invoke("fail");
                yield break;
            }
            int top = GameDatabase.Tanks.Max(t => t.maxClass);
            float cm = Mathf.Round(Mathf.Lerp(sp.minCm, sp.maxCm, 0.6f));
            if (AquaTank.ClassOf(cm) > top) cm = sp.minCm;
            int cls = AquaTank.ClassOf(cm);
            var tank = GameDatabase.Tanks.Where(t => t.maxClass >= cls && t.capacity >= AquaTank.ClassSpace[cls]).OrderBy(t => t.level).FirstOrDefault();
            if (tank == null)
            {
                string why = $"{sp.minCm:0} cm is {AquaTank.ClassNames[cls]}: no tank takes it";
                Log($"CHECK SKIP {id}: {why} (no aquarium check)");
                result?.Invoke("skip: " + why);
                yield break;
            }
            var d = Game.Data;
            d.coins = Math.Max(d.coins, 100000);
            d.tankLevel = tank.level;
            for (int i = 1; i <= tank.level; i++) if (!d.ownedItems.Contains("tank_" + i)) d.ownedItems.Add("tank_" + i);
            d.aquarium.Clear();
            var cf = Game.I.MakeCatch(sp, cm, GameDatabase.StageOfFish(id) ?? "lake");
            bool kept = Game.I.AddToAquarium(cf);
            AquaCare.FastForward(d, 12f, null); // (hungry)
            AquaTank.SetAll(0f, 0f);
            // its food on the ledge: the live ones it eats, a bag of pellets
            var kinds = AquaCare.KindsOf(sp.diet).ToList();
            foreach (var k in kinds.Where(k => k != Diet.Pellet))
            {
                var live = AquaCare.LiveOf(k);
                if (live == null) continue;
                AquaCare.Stock(live.id).pieces = live.piecesPerBuy;
                AquaCare.Stock(live.id).open = true;
            }
            AquaCare.Stock("feed_basic").bags = 1;
            Game.I.Save();
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.6f);
            var scene = FindAnyObjectByType<AquariumScene>();
            var feed = AquaFeed.Current;
            var tf = scene != null ? scene.TankOf(cf) : null;
            Check(kept && tf != null && feed != null && AquaCare.Hungry(cf),
                $"{id} {cm:0} cm ({AquaTank.ClassNames[cls]}) kept in the {tank.name} (level {tank.level}, the smallest that takes it): kept {kept}, swims {tf != null}, hungry {AquaCare.Hungry(cf)} ({cf.fullness:0.00}); eats {AquaCare.DietText(sp.diet)} [{sp.feedStyle}]");
            if (tf == null || feed == null)
            {
                result?.Invoke("fail");
                yield break;
            }
            var L = Lay;
            PlaceFish(scene, cf, new Vector2(L.GlassCentre.x, (L.surfaceY + L.gravelTopY) * 0.5f), false);
            yield return new WaitForSeconds(0.6f);
            yield return ShotAt("aqua_" + id, tf.Mouth);
            // its food: a live one (the sardine first: a surging fish's meal), else pellets
            Diet kind = kinds.Contains(Diet.Sardine) ? Diet.Sardine : kinds.Contains(Diet.Shrimp) ? Diet.Shrimp : Diet.Pellet;
            float f0 = cf.fullness;
            var over = new Vector2(Mathf.Clamp(tf.Mouth.x, L.tankMinX + 0.5f, L.tankMaxX - 0.5f), L.surfaceY + 1.2f);
            bool dropped = true;
            if (kind == Diet.Pellet) feed.DebugPellets("feed_basic", over, 12);
            else dropped = feed.DebugDrop(AquaCare.LiveOf(kind).id, over);
            float t0 = Time.time;
            while (dropped && cf.fullness <= f0 + 0.02f && Time.time - t0 < 25f) yield return null;
            float took = Time.time - t0;
            yield return new WaitForSeconds(0.4f);
            yield return ShotAt("aqua_" + id + "_eat", tf.Mouth);
            bool ate = dropped && cf.fullness > f0 + 0.02f;
            Check(ate, $"{AquaCare.KindName(kind)} dropped over the {id} (at {over.x:0.0}, {over.y:0.0}, no pointer): {(ate ? $"it ate in {took:0.0} s" : "not eaten in 25 s")}, fullness {f0:0.00} -> {cf.fullness:0.00}");
            result?.Invoke(ate && kept ? "pass" : "fail");
        }
    }
}
