using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto hooks (with -fkscene Fishing -fkstage lake, -fkrich): the hooks of the natural-bait rigs. A fresh save has
    /// only the free small hook on; the shop's 바늘 tab buys a pack of 10 (the first pack goes on), a second kind stays in
    /// the bag; 내 채비's 바늘 tab lists the hooks owned; the bait button shows the hook and its count. A float rig hooked
    /// for real and parted by the tension takes one hook (the toast lists it with its icon, the float comes home with
    /// nothing under it); a bait stolen leaves the equipped hook's own sprite under the float. The fight model, one seed per
    /// fish, the same scripted play: a big fish shakes the small hook more often than the large; a small one is held alike.
    /// The bite fit on the real species: the free small hook changes nothing for any of them, the large hook keeps the
    /// small fish off. The graded 챔질: its grades from timing and strength, a real swipe up in a bite (graded, the stamina
    /// cut, the line jerked short of breaking), a tap still setting the hook ungraded, PERFECT holding the big carp on the
    /// small hook better than BAD. [HOOKS] CHECK lines; shots hooks_shop, hooks_tackle, hooks_loss, hooks_bare (+ _zoom),
    /// hooks_strike.
    /// </summary>
    public partial class AutoPilot
    {
        int hookFails;

        void HCheck(string what, bool ok, string detail)
        {
            if (!ok) hookFails++;
            Log($"[HOOKS] CHECK {what} {(ok ? "PASS" : "FAIL")} {detail}");
        }

        IEnumerator HooksTest()
        {
            yield return new WaitForSeconds(2f);
            var ctl = FindAnyObjectByType<FishingController>();
            var hud = FindAnyObjectByType<FishingHUD>();
            if (ctl == null || hud == null)
            {
                HCheck("scene", false, "no FishingController (use -fkscene Fishing -fkstage lake)");
                Log("hooks test done: 1 failed");
                Application.Quit();
                yield break;
            }
            CloseDialogs();
            yield return null;
            var g = Game.I;
            var small = GameDatabase.GetItem<HookDef>("hook_small");
            var medium = GameDatabase.GetItem<HookDef>("hook_medium");
            var large = GameDatabase.GetItem<HookDef>("hook_large");
            var weedless = GameDatabase.GetItem<HookDef>("hook_weedless");
            SteerGear("rod_carbon", "reel_highgear", "line_nylon4");
            ctl.GearChanged();
            EquipTest("bait_worm", ctl);
            yield return ToReady(ctl);

            // 1. a fresh save: the free small hook on, nothing else
            HCheck("fresh", g.Hook == small && g.HasHook(small.id) && !g.HasHook(medium.id) && !g.HasHook(large.id) && !g.HasHook(weedless.id),
                $"on {g.Hook.id}; owned: {string.Join(", ", GameDatabase.Hooks.Where(h => g.HasHook(h.id)).Select(h => h.id))}");

            // 2. the shop's 바늘 tab: a pack of medium (it goes on), then weedless (into the bag)
            int coins0 = g.Coins;
            ShopUI.Open(hud.Canvas.transform, ItemKind.Hook);
            yield return new WaitForSeconds(0.6f);
            int rows = GameDatabase.Hooks.Count(h => FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text == h.name));
            bool note = FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.Contains("미끼 채비에 쓰는 바늘"));
            yield return NamedShot("hooks_shop");
            Click(UIKit.Num(medium.price));
            yield return new WaitForSeconds(0.3f);
            bool medOn = g.Hook == medium && g.HookCount(medium.id) == medium.packSize && g.Coins == coins0 - medium.price;
            Click(UIKit.Num(weedless.price));
            yield return new WaitForSeconds(0.3f);
            bool weedBag = g.HookCount(weedless.id) == weedless.packSize && g.Hook == medium && g.Coins == coins0 - medium.price - weedless.price;
            HCheck("shop", rows == GameDatabase.Hooks.Count && note && medOn && weedBag,
                $"{rows} hook rows of {GameDatabase.Hooks.Count}, the note {note}; medium bought and on {medOn} (x{g.HookCount(medium.id)}), weedless bought into the bag {weedBag} (x{g.HookCount(weedless.id)}), coins {coins0} -> {g.Coins}");
            CloseDialogs();
            var shopWin = GameObject.Find("Shop");
            if (shopWin != null) Destroy(shopWin.transform.parent.gameObject);
            yield return new WaitForSeconds(0.3f);

            // 3. 내 채비's 바늘 tab and the bait button's tag
            Click("채비");
            yield return new WaitForSeconds(0.5f);
            Click("바늘");
            yield return new WaitForSeconds(0.4f);
            var texts = FindObjectsByType<Text>(FindObjectsSortMode.None);
            int listed = GameDatabase.Hooks.Count(h => texts.Any(t => t.text.StartsWith(h.name)));
            int inUse = Buttons("사용 중").Length, canUse = Buttons("사용").Count(b => b.interactable);
            yield return NamedShot("hooks_tackle");
            CloseDialogs();
            yield return null;
            var tag = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "HookTag");
            string tagText = tag != null ? tag.text : "-";
            HCheck("tackle", listed == 3 && inUse == 1 && canUse == 2 && tagText == "중형 " + medium.packSize,
                $"{listed} hooks listed (small, medium, weedless), 사용 중 {inUse}, 사용 {canUse}; the bait button reads '{tagText}'");

            // 4. a float rig hooked for real, parted by the tension: one medium hook lost, the toast lists it with its icon,
            // the float comes home with nothing under it
            yield return BrPlace(ctl, "bait_worm", BrAt(ctl, 1f, 0f, 15f));
            int h0 = g.HookCount(medium.id), breaks0 = ctl.Breaks;
            FishAgent f = null;
            yield return BrForceBite(ctl, x => f = x);
            yield return new WaitForSeconds(0.15f);
            yield return Tap(Scr(0.5f, 0.6f));
            for (float w = 0f; w < 0.5f && ctl.State != FishingController.S.Fighting; w += Time.deltaTime) yield return null;
            bool hooked = ctl.State == FishingController.S.Fighting;
            yield return BrWind(ctl, 0.6f, 1.5f);
            bool parted = ctl.DebugPartLine("tension");
            var tk = ctl.Tackle;
            bool gone = tk.HookGone && tk.BaitR.sprite == null;
            var snap = ctl.SnapFx;
            for (float w = 0f; w < 1.2f && snap.Active; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.3f);
            var r = ctl.LastLoss;
            ToastParts(r, out bool shown, out int icons, out _, out _);
            yield return NamedShot("hooks_loss");
            HCheck("loss", f != null && hooked && parted && ctl.Breaks == breaks0 + 1 && r != null && r.hook == medium && g.HookCount(medium.id) == h0 - 1
                && shown && icons >= 2 && r.items.Any(s => s == medium.name) && gone,
                $"fish {(f != null ? f.Sp.id : "none")} hooked {hooked}, parted {parted}: {medium.id} {h0} -> {g.HookCount(medium.id)}, the toast '{(r != null ? string.Join(" | ", r.items) : "-")}' ({icons} icons), nothing under the float {gone}");
            for (float w = 0f; w < 30f && ctl.State != FishingController.S.Ready; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(3.2f);   // (the toast gone)

            // 5. the last medium hooks lost one by one fall back to the free small hook
            g.Equip(medium);
            while (g.HookCount(medium.id) > 1) g.LoseHook();
            var last = g.LoseHook();
            HCheck("last_hook", last == medium && g.Hook == small && !g.HasHook(medium.id) && g.LoseHook() == null && g.Hook == small,
                $"the last medium lost ({(last != null ? last.id : "none")}): on {g.Hook.id}, medium left {g.HookCount(medium.id)}; losing the free small one takes nothing");

            // 6. a bait stolen: the equipped hook's own sprite under the float (the large hook, bought here)
            g.AddCoins(large.price);
            g.Buy(large);
            g.Equip(large);
            yield return BrPlace(ctl, "bait_worm", BrAt(ctl, 0f, 0f, 14f));
            int th0 = ctl.BaitThefts;
            f = null;
            yield return BrForceBite(ctl, x => f = x);
            for (float w = 0f; w < 3f && ctl.State == FishingController.S.Biting; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.4f);
            string spr = tk.BaitR.sprite != null ? tk.BaitR.sprite.name : "none";
            int hl = g.HookCount(large.id);
            yield return NamedShot("hooks_bare");
            yield return BrCrop("hooks_bare_zoom", ctl.Stage.P.To2D(tk.Surface), 200, 120, 4);
            HCheck("bare", f != null && ctl.BaitThefts == th0 + 1 && tk.BareHook && !tk.HookGone && spr == "hook_large_w" && hl == large.packSize,
                $"fish {(f != null ? f.Sp.id : "none")} stole the bait {ctl.BaitThefts - th0}: under the float '{spr}', the hook kept (x{hl})");
            for (float w = 0f; w < 30f && ctl.State != FishingController.S.Ready; w += Time.deltaTime)
            {
                if (ctl.State == FishingController.S.Waiting) ctl.Retrieve();
                yield return null;
            }

            // 7. the hold, on the fight model: each fish on one seed, the same play (wind 1.2 rev/s for 3 s, give line for 2 s:
            // the slack a weak hold does not survive), with the hook's hold for that fish; a big carp shakes the small hook off
            // more often than the large
            int big = 0, bigL = 0, sm = 0, smL = 0;
            const int N = 120;
            var carp = GameDatabase.GetFish("carp");
            var crucian = GameDatabase.GetFish("crucian_carp");
            for (int i = 0; i < N; i++)
            {
                if (HookFight(carp, 85f, small.HoldK(85f), 5000 + i) == FightModel.Outcome.Escaped) big++;
                if (HookFight(carp, 85f, large.HoldK(85f), 5000 + i) == FightModel.Outcome.Escaped) bigL++;
                if (HookFight(crucian, 22f, small.HoldK(22f), 6000 + i) == FightModel.Outcome.Escaped) sm++;
                if (HookFight(crucian, 22f, medium.HoldK(22f), 6000 + i) == FightModel.Outcome.Escaped) smL++;
            }
            HCheck("hold", big > bigL + N / 20 && sm == smL,
                string.Format(CIc, "carp 85 cm shaken off: small hook {0} / large {1} of {2} (hold {3:0.00} / {4:0.00}); crucian 22 cm: small {5} / medium {6} (hold {7:0.00} / {8:0.00})",
                    big, bigL, N, small.HoldK(85f), large.HoldK(85f), sm, smL, small.HoldK(22f), medium.HoldK(22f)));

            // 8. the bite fit on every species: the free small hook changes nothing, the large keeps the small fish off
            bool smallSame = GameDatabase.Fish.All(s => Mathf.Approximately(small.BiteK(s.minCm), 1f) && Mathf.Approximately(small.BiteK(s.maxCm), 1f));
            var tiny = GameDatabase.Fish.Where(s => s.maxCm <= 25f).ToList();
            bool largeOff = tiny.Count > 0 && tiny.All(s => large.BiteK(s.maxCm) <= 0.5f);
            bool mediumBig = GameDatabase.Fish.Where(s => s.minCm >= 20f).All(s => Mathf.Approximately(medium.BiteK(s.minCm), 1f));
            HCheck("bite_fit", smallSame && largeOff && mediumBig,
                string.Format(CIc, "small hook x1 for all {0} species {1}; large on the small fish ({2}): {3}; medium x1 from 20 cm {4}",
                    GameDatabase.Fish.Count, smallSame, string.Join(", ", tiny.Select(s => $"{s.id} x{large.BiteK(s.maxCm):0.00}")), largeOff, mediumBig));


            // 9. the graded 챔질 (FishingController.Strike): the grade from timing and strength
            var P = FishingController.StrikeGrade.Perfect;
            bool grades = FishingController.GradeStrike(0.25f, 0.4f, 0.4f) == P
                && FishingController.GradeStrike(0.35f, 0.5f, 0.4f) == FishingController.StrikeGrade.Great
                && FishingController.GradeStrike(0.5f, 0.4f, 0.4f) == FishingController.StrikeGrade.Good
                && FishingController.GradeStrike(0.25f, 0.75f, 0.4f) == FishingController.StrikeGrade.Bad
                && FishingController.GradeStrike(0.7f, 0.4f, 0.4f) == FishingController.StrikeGrade.Bad;
            HCheck("strike_grades", grades, "on time and spot on PERFECT; 0.1 s and 0.1 off GREAT; 0.25 s late GOOD; 0.35 too hard BAD; 0.45 s late BAD");

            // a real swipe up in a bite (the small hook: ideal 0.4, a swipe of 0.10 H at 1 H/s): graded, the stamina cut,
            // the line jerked tight but short of what it holds
            g.Equip(small);
            yield return BrPlace(ctl, "bait_worm", BrAt(ctl, 1f, 0f, 15f));
            f = null;
            yield return BrForceBite(ctl, x => f = x);
            for (float w = 0f; w < 1f && ctl.State != FishingController.S.Biting; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.1f);
            yield return LureFlick(1.0f, 0.10f, up: true);
            PointerInput.SimDown = false;
            yield return null;
            bool fighting = ctl.State == FishingController.S.Fighting && ctl.Fight != null;
            var grade = ctl.LastStrike;
            float stam = fighting ? ctl.Fight.Stamina : -1f, ten = fighting ? ctl.Fight.Tension / ctl.Fight.LineLimit : -1f;
            float cut = FishingController.StrikeStamina[(int)grade];
            yield return NamedShot("hooks_strike");
            HCheck("strike_swipe", f != null && fighting && grade >= FishingController.StrikeGrade.Good && stam <= 1f - cut + 0.02f && ten > 0.1f && ten <= FishingController.StrikeTensionMax + 0.01f,
                string.Format(CIc, "swiped up {0:0.00} s into the bite, strength {1:0.00} (ideal {2:0.00}): {3}, stamina {4:0.00} (cut {5:0.00}), tension {6:0.00} of what the line holds",
                    ctl.LastStrikeAt, ctl.LastStrikeStrength, small.setStrength, grade, stam, cut, ten));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(1.5f);

            // a tap still sets the hook, ungraded: the fish at full stamina
            yield return BrPlace(ctl, "bait_worm", BrAt(ctl, -1f, 0f, 15f));
            f = null;
            yield return BrForceBite(ctl, x => f = x);
            for (float w = 0f; w < 1f && ctl.State != FishingController.S.Biting; w += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.15f);
            yield return Tap(Scr(0.5f, 0.6f));
            for (float w = 0f; w < 0.5f && ctl.State != FishingController.S.Fighting; w += Time.deltaTime) yield return null;
            bool tapFight = ctl.State == FishingController.S.Fighting && ctl.Fight != null;
            float tapStam = tapFight ? ctl.Fight.Stamina : -1f;
            HCheck("strike_tap", f != null && tapFight && tapStam >= 0.97f, string.Format(CIc, "tapped: fighting {0}, stamina {1:0.00} (no cut)", tapFight, tapStam));
            if (ctl.State == FishingController.S.Fighting) ctl.DebugRelease();
            yield return new WaitForSeconds(1.5f);

            // the grade on the fight model: the big carp on the small hook, PERFECT (hold x1.4, 35 % less stamina) against
            // BAD (hold x0.8), one seed per fight, the same play as above
            int perf = 0, bad = 0;
            float perfT = 0f, badT = 0f;
            for (int i = 0; i < N; i++)
            {
                var a = HookFight(carp, 85f, small.HoldK(85f) * FishingController.StrikeHold[(int)P], 5000 + i, 1f - FishingController.StrikeStamina[(int)P]);
                var b = HookFight(carp, 85f, small.HoldK(85f) * FishingController.StrikeHold[(int)FishingController.StrikeGrade.Bad], 5000 + i, 1f);
                if (a == FightModel.Outcome.Escaped) perf++;
                if (b == FightModel.Outcome.Escaped) bad++;
            }
            HCheck("strike_hold", perf < bad,
                string.Format(CIc, "carp 85 cm on the small hook shaken off: PERFECT {0} / BAD {1} of {2} (hold {3:0.00} / {4:0.00})",
                    perf, bad, N, small.HoldK(85f) * FishingController.StrikeHold[(int)P], small.HoldK(85f) * FishingController.StrikeHold[(int)FishingController.StrikeGrade.Bad]));

            Log($"hooks test done: {hookFails} failed");
            Application.Quit();
        }

        /// <summary>One fight on the model with the hook's hold: wind 1.2 rev/s for 3 s, then give line (-1.5 rev/s) for 2 s, up to 90 s.</summary>
        static FightModel.Outcome HookFight(FishSpecies sp, float cm, float hold, int seed, float stamina = 1f)
        {
            var m = new FightModel(sp, cm, Game.I.Rod, Game.I.Reel, Game.I.Line, 1f, 16f, 2f, seed, hold) { Stamina = stamina };
            const float dt = 1f / 60f;
            for (float t = 0f; t < 90f && m.Result == FightModel.Outcome.None; t += dt)
                m.Step(dt, Mathf.Repeat(t, 5f) >= 3f ? -1.5f : 1.2f, false);
            return m.Result;
        }
    }
}
