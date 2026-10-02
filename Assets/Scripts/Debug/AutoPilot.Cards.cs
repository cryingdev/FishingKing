using System.Collections;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkauto cards [-fkcards id,id,...] (screenshots; Docs/lake_phase2_spec.md C6): on a stage (-fkscene Fishing -fkstage
    /// lake), each species in turn (default: the stage's ordinary species) is hooked on a float laid 14 m out (the test hook
    /// DebugHook, no natural bites meanwhile), landed at once (DebugLand) and its catch card shot as card_&lt;id&gt; (a fresh
    /// save: the first catch of each, so the card shows it as new), then sold. A species that cannot be hooked or whose card
    /// does not come up is a CHECK FAIL. "cards done: N failed", then it quits.
    /// </summary>
    public partial class AutoPilot
    {
        IEnumerator CardsTest()
        {
            PointerInput.SimActive = true;
            PointerInput.SimDown = false;
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                Log("[CARDS] no FishingController (use -fkscene Fishing -fkstage <id>)");
                Application.Quit();
                yield break;
            }
            string list = Arg("-fkcards");
            var ids = !string.IsNullOrEmpty(list)
                ? list.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList()
                : GameDatabase.FishOfStage(ctl.Stage.Def.id).Where(f => f.encounter == null).Select(f => f.id).ToList();
            FishingController.NoBites = true;
            int fails = 0, n = 0;
            foreach (var id in ids)
            {
                var sp = GameDatabase.GetFish(id);
                if (sp == null)
                {
                    Log($"[CARDS] CHECK FAIL {id}: no such species");
                    fails++;
                    continue;
                }
                yield return ToReady(ctl);
                EquipTest("bait_worm", ctl);
                ctl.FloatDepthChanged(2f);
                yield return null;
                var at = new Vector3(ctl.Angler.X, 0f, 14f);
                ctl.DebugPlaceRig(at);
                yield return new WaitForSeconds(0.9f);
                float cm = Mathf.Round(Mathf.Lerp(sp.minCm, sp.maxCm, 0.6f));
                if (!ctl.DebugHook(sp, cm, 4100 + n++, new Vector3(at.x, -1.5f, at.z)))
                {
                    Log($"[CARDS] CHECK FAIL {id}: not hooked ({ctl.State}, tackle {ctl.Tackle.State})");
                    fails++;
                    continue;
                }
                yield return new WaitForSeconds(0.8f);
                bool landed = ctl.DebugLand();
                for (float w = 0f; w < 10f && !HasButton("판매"); w += Time.deltaTime) yield return null;
                bool card = HasButton("판매");
                yield return new WaitForSeconds(0.5f);
                yield return Shot("card_" + id);
                Log($"[CARDS] CHECK {(landed && card ? "PASS" : "FAIL")} {id} {sp.name} {cm:0} cm: landed {landed}, catch card {card}, recorded {Game.I.Record(id)?.caught ?? 0}");
                if (!(landed && card)) fails++;
                Click("판매");
                yield return new WaitForSeconds(0.8f);
            }
            FishingController.NoBites = false;
            PointerInput.SimDown = false;
            PointerInput.SimActive = false;
            Log($"[CARDS] cards done: {fails} failed");
            Application.Quit();
        }
    }
}
