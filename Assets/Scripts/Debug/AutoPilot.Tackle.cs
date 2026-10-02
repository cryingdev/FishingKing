using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto tackle (with -fkscene Fishing -fkstage lake, -fkgear for more than one of each): 내 채비. At the ready the
    /// 채비 button opens it on the rods; each tab lists what he owns; 장착 equips a rod / reel / line (the angler redrawn);
    /// with the rig out the rod, reel and line cannot change (the note, no 장착) while the bait still can; the float's
    /// depth -/+ show before the cast only, and a depth change while the rig is out does nothing. [TACKLE] CHECK lines;
    /// shots tackle_hud / tackle_rod / tackle_reel / tackle_line / tackle_bait / tackle_out.
    /// </summary>
    public partial class AutoPilot
    {
        int tackleFails;

        void KCheck(string what, bool ok, string detail)
        {
            if (!ok) tackleFails++;
            Log($"[TACKLE] CHECK {what} {(ok ? "PASS" : "FAIL")} {detail}");
        }

        static Button[] Buttons(string label) => FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Where(x => x.GetComponentInChildren<Text>() != null && x.GetComponentInChildren<Text>().text == label).ToArray();

        IEnumerator TackleTest()
        {
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                KCheck("scene", false, "no FishingController (use -fkscene Fishing -fkstage lake)");
                Application.Quit();
                yield break;
            }
            CloseDialogs();
            yield return null;
            var g = Game.I;
            SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
            ctl.GearChanged();
            EquipTest("bait_worm", ctl);
            yield return ToReady(ctl);
            // at the ready: the depth can be set, the window opens on the rods
            bool depthReady = Buttons("+").Any(b => b.gameObject.activeInHierarchy) && Buttons("-").Any(b => b.gameObject.activeInHierarchy);
            yield return NamedShot("tackle_hud");   // (the 채비 button by the tackle panel)
            Click("채비");
            yield return new WaitForSeconds(0.5f);
            int rods = Buttons("장착").Length + Buttons("장착 중").Length;
            int ownedRods = GameDatabase.Rods.Count(r => g.Owns(r.id));
            yield return NamedShot("tackle_rod");
            var rod0 = g.Rod;
            Click("장착");
            yield return null;
            var rod1 = g.Rod;
            KCheck("rods", depthReady && rods == ownedRods && ownedRods > 1 && rod1 != rod0 && Buttons("장착 중").Length == 1,
                $"depth -/+ at the ready {depthReady}; {rods} rows for {ownedRods} owned rods; 장착: {rod0.id} -> {rod1.id}");
            Click("릴");
            yield return new WaitForSeconds(0.3f);
            yield return NamedShot("tackle_reel");
            var reel0 = g.Reel;
            Click("장착");
            yield return null;
            KCheck("reels", g.Reel != reel0, $"{reel0.id} -> {g.Reel.id}");
            Click("낚싯줄");
            yield return new WaitForSeconds(0.3f);
            yield return NamedShot("tackle_line");
            var line0 = g.Line;
            Click("장착");
            yield return null;
            KCheck("lines", g.Line != line0 && g.HasLine, $"{line0.id} -> {g.Line.id} (owned {g.HasLine})");
            Click("미끼");
            yield return new WaitForSeconds(0.3f);
            yield return NamedShot("tackle_bait");
            CloseDialogs();
            yield return null;
            // the rig out: no depth change, no gear change, the bait still can
            float depth0 = ctl.Tackle.FloatDepth;
            ctl.DebugPlaceRig(new Vector3(ctl.Angler.X, 0f, 14f));
            yield return new WaitForSeconds(0.5f);
            bool depthHidden = !Buttons("+").Any(b => b.gameObject.activeInHierarchy) && !Buttons("-").Any(b => b.gameObject.activeInHierarchy);
            Click("채비");
            yield return new WaitForSeconds(0.5f);
            bool note = FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.Contains("회수한 뒤에"));
            bool locked = Buttons("장착").All(b => !b.interactable) && Buttons("장착").Length > 0;
            yield return NamedShot("tackle_out");
            Click("미끼");
            yield return new WaitForSeconds(0.3f);
            bool baitOk = HasButton("사용");
            CloseDialogs();
            KCheck("rig_out", ctl.State == FishingController.S.Waiting && depthHidden && note && locked && baitOk && Mathf.Approximately(ctl.Tackle.FloatDepth, depth0),
                $"waiting {ctl.State}: depth -/+ hidden {depthHidden}, the note {note}, 장착 locked {locked}, a bait can still be used {baitOk}, depth kept {ctl.Tackle.FloatDepth:0.0} m");
            Log($"[TACKLE] tackle test done: {tackleFails} failed");
            Application.Quit();
        }
    }
}
