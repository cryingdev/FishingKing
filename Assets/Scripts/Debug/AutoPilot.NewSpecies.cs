using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto newspecies -fkspecies &lt;id&gt; (Docs/data_reference.md 2.7 확인 절차): the checks for a species added as data
    /// only (a species file, its art, a roster line; no code change), in one process and without pointer gestures (the
    /// simulated pointer is held, never pressed; the hooks are the test hooks). Start it anywhere (-fkfresh -fkrich -fksave
    /// &lt;unique&gt;): it opens its stage itself.
    /// N1 it is loaded and on a roster, no load errors.
    /// N2 (a stage with a generated bed: the lake) the depth test's economy gates with it in the stock (EconomyCheck: 50
    /// seeds x bamboo / carbon / dragon, the estimator, no live soak): F off its clamp, the stage's catches and income per
    /// minute within 0.8..1.25 of today's; its best 10 % of casts on its best rig &gt;= 1.5 x its fan mean (bamboo, carbon);
    /// the stock's derived weights (D11b) and its own weight reported (seed 1's A / W / share, A's range over the seeds,
    /// clamp hits at 0.6 / 1.4); the band's closest approach, with a NOTE under 0.03 (a live soak is advised, not run);
    /// its niche: the hand-written gate (D7', NicheGate) or else its strongest declared bed preference x1.3 (GenericNiche).
    /// N3 its catch card (CardOne: card_&lt;id&gt;) and its collection page (collection_detail_&lt;id&gt;).
    /// N4 when its size class fits a tank, the aquarium (AquaPilot.SpeciesFeed: aqua_&lt;id&gt;, aqua_&lt;id&gt;_eat).
    /// A legend stops after N1 (its encounter needs the full suites). [NEWSP] CHECK lines, at the end
    /// "newspecies test done: N failed".
    /// </summary>
    public partial class AutoPilot
    {
        IEnumerator NewSpeciesTest()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            depthTag = "[NEWSP]";
            var t0 = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(1f);
            string id = Arg("-fkspecies");
            var sp = GameDatabase.GetFish(id);
            var stages = sp == null ? new List<StageDef>() : GameDatabase.Stages.Where(st => st.spawns.Any(kv => kv.Key == id)).ToList();
            DCheck($"N1 {id ?? "(no -fkspecies)"}: loaded {sp != null}{(sp != null ? $" ({sp.name}, {sp.rarity}, {sp.minCm:0}-{sp.maxCm:0} cm)" : "")}, on the roster of {(stages.Count > 0 ? string.Join(", ", stages.Select(s => s.id)) : "no stage")}; the game's load errors {GameDatabase.LoadErrors.Count}",
                sp != null && stages.Count > 0 && GameDatabase.LoadErrors.Count == 0);
            if (sp == null || stages.Count == 0)
            {
                NewSpeciesDone(t0);
                yield break;
            }
            if (sp.encounter != null)
            {
                Log($"[NEWSP] CHECK SKIP N2-N4: {id} is a legend: its encounter, model and spot need the full suites (Tools/Test/new_species.ps1 -Regress encounter,legendspot)");
                NewSpeciesDone(t0);
                yield break;
            }

            // ---- N2 the economy and its niche, on each stage with a generated bed
            FishingController ctl = null;
            foreach (var st in stages)
            {
                yield return ToStage(st.id, c => ctl = c);
                if (ctl == null)
                {
                    DCheck($"N2 the fishing scene opened on {st.id}", false);
                    continue;
                }
                var L = ctl.Stage.L;
                if (!(st.Derived && L.Terrain && L.Bathy != null))
                {
                    Log($"[NEWSP] CHECK SKIP N2 the economy and the niche: {st.id} has no generated bed (the estimator runs on a stage with a terrain recipe: the lake)");
                    continue;
                }
                float scaleWas = GameClock.Scale, minWas = GameClock.Min;
                GameClock.Scale = 0f;
                GameClock.Min = GameClock.Centre(Period.Day);
                Log($"[NEWSP] N2 {st.id} (world seed {L.Bathy.WorldSeed}) with {id} in its stock: the economy over 50 seeds x 3 rods (the estimator, no live soak)");
                yield return EconomyCheck(ctl, TerrainRecipes.For(st.id), Obstacles.ReadSet(st.id), id);
                var s1 = SampleShares(L.Bathy, new FishHabitat(ctl.Stage, ctl, L.Bathy, true), OrdinaryOf(ctl), 30f, id);
                var hand = s1.kind.ContainsKey(id) ? NicheGate(ctl, s1, id) : null;
                var gen = GenericNiche(ctl, sp);
                if (hand != null)
                {
                    DCheck("D7' seed 1 niche (its hand-written gate): " + hand.Value.text, hand.Value.ok);
                    if (gen != null) Log("[NEWSP] D7' its strongest declared preference (information): " + gen.Value.text);
                }
                else if (gen != null) DCheck("D7' seed 1 niche (its strongest declared preference): " + gen.Value.text, gen.Value.ok);
                else Log("[NEWSP] CHECK SKIP D7' the niche: no hand-written gate and no declared bed preference of 1.3 or more (the best-cast gate above stands for it)");
                GameClock.Min = minWas;
                GameClock.Scale = scaleWas;
                yield return null;
            }

            // ---- N3 its catch card and its collection page
            if (ctl == null) yield return ToStage(stages[0].id, c => ctl = c);
            if (ctl != null)
            {
                FishingController.NoBites = true;
                bool card = false;
                yield return CardOne(ctl, id, 0, r => card = r);
                FishingController.NoBites = false;
                DCheck($"N3 its catch card on {ctl.Stage.Def.id} (card_{id}): hooked, landed, the card shown, recorded {Game.I.Record(id)?.caught ?? 0}", card);
            }
            else DCheck($"N3 the fishing scene opened on {stages[0].id} for its catch card", false);
            SceneFlow.Go("Map");
            yield return new WaitForSeconds(1.6f);
            var mapUi = GameObject.Find("MapUI");
            bool page = false;
            if (mapUi != null) yield return CollectionShots(mapUi.transform, id, "_" + id, r => page = r);
            DCheck($"N3 its collection page (collection_detail_{id}): {(page ? "its page opened" : mapUi == null ? "no map" : "its tile not caught / not found")}", page);

            // ---- N4 the aquarium, when a tank takes it
            var aq = AquaPilot.Attach(gameObject, shots);
            string res = null;
            yield return aq.SpeciesFeed(id, r => res = r);
            if (res != null && res.StartsWith("skip")) Log($"[NEWSP] CHECK SKIP N4 the aquarium: {res.Substring(5).Trim()}");
            else DCheck($"N4 the aquarium: kept and fed its food without the pointer ({res ?? "no result"}; the [AQUA] lines)", res == "pass");
            NewSpeciesDone(t0);
        }

        void NewSpeciesDone(float t0)
        {
            Log(string.Format(CIc, "[NEWSP] newspecies test done: {0} failed ({1:0} s)", depthFails, Time.realtimeSinceStartup - t0));
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Application.Quit();
        }

        /// <summary>The fishing scene on <paramref name="stageId"/> (opened unless it is), its tutorial closed; <paramref name="got"/>: its controller (null: none came up).</summary>
        IEnumerator ToStage(string stageId, Action<FishingController> got)
        {
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null || ctl.Stage == null || ctl.Stage.Def.id != stageId)
            {
                SceneFlow.PendingStage = stageId;
                SceneFlow.Go("Fishing");
                for (float w = 0f; w < 15f; w += Time.unscaledDeltaTime)
                {
                    yield return null;
                    ctl = FindAnyObjectByType<FishingController>();
                    if (ctl != null && ctl.Stage != null && ctl.Stage.Def != null && ctl.Stage.Def.id == stageId) break;
                }
                yield return new WaitForSeconds(2f);
            }
            if (Dialog.Open)
            {
                Click("알겠어요");
                yield return new WaitForSeconds(0.4f);
            }
            ctl = FindAnyObjectByType<FishingController>();
            got(ctl != null && ctl.Stage != null && ctl.Stage.Def.id == stageId ? ctl : null);
        }
    }
}
