using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The fish off the line (Docs/controls.md, Docs/obstacles_spec.md 6.6 / 7): got away (the hook shaken out) or the line
    /// parted (the tension over the limit, worn through on structure, the spool emptied; at a snag: FishingController.Obstacles).
    /// Where the line parted decides what comes home: on the hook's side of the float (the tension parts it at the knot,
    /// a rub below the float) the float stays and is wound in with the bare hook; above the float (the spool emptied, a rub
    /// between the rod tip / the line's water entry and the float) the whole rig is gone; a lure is gone either way. A fish
    /// that only shook the hook leaves the rig on the line: a float rig's float and a lure are wound in from where they
    /// were. The parted line whips back (<see cref="LineSnap"/>) and a toast lists what was lost (none: no toast).
    /// </summary>
    public partial class FishingController
    {
        /// <summary>How the fish came off: shaken off (nothing parted), parted on the hook's side of the float, parted above it.</summary>
        public enum Off { Escape, AtHook, Above }

        /// <summary>What a parted line took (for the toast and the tests).</summary>
        public class LossReport
        {
            /// <summary>"tension", "rub", "spool" (a fight); "snag", "cut" (a snag forced, the 끊기 button).</summary>
            public string cause;
            public Off off;
            /// <summary>The lure lost (null: none).</summary>
            public BaitDef lure;
            /// <summary>A natural bait lost unused (a snag) and how many (0: none).</summary>
            public BaitDef bait;
            public int baitN;
            /// <summary>The float went with the line (it parted above it).</summary>
            public bool floatLost;
            /// <summary>The lure is a legend's key / costs <see cref="ExpensiveLure"/> or more: highlighted in the toast.</summary>
            public bool lureKey, lureExpensive;
            /// <summary>The rub's and the float's distance from the hook along the line (m; -1: not a rub / no float).</summary>
            public float rubFromHook = -1f, floatFromHook = -1f;
            /// <summary>Where the line parted (a rub: the point it wore through; else the mouth; the spool: the mouth) and the float and the mouth then (for the tests).</summary>
            public Vector3 breakAt, floatAt, mouth;
            /// <summary>The toast shown (null: nothing lost, no toast; or not yet: <see cref="toastPending"/>) and its items' texts.</summary>
            public RectTransform toast;
            public bool toastPending;
            public List<string> items = new List<string>();
            public int frame;
            public bool Highlighted => lureKey || lureExpensive;
        }

        /// <summary>A lure this dear (coins) or a legend's key lure is highlighted in the loss toast.</summary>
        public const int ExpensiveLure = 5000;
        /// <summary>The loss toast comes once the line's whip is over (s after the break).</summary>
        public const float LossToastDelay = LineSnap.Duration + 0.05f;

        LineSnap snap;
        /// <summary>The parted line's whip (for the tests).</summary>
        public LineSnap SnapFx => snap;
        bool spentRig;
        /// <summary>
        /// The rig being wound in came off a fish (shaken off, or the line parted with the float kept): it is spent, catches
        /// on nothing and is taken by no fish until it is home (set by FishOff and a snag's break, cleared by FinishRetrieve).
        /// </summary>
        public bool SpentRetrieve => spentRig;
        /// <summary>The last parted line (null: none yet), how many in all, bites and stolen baits since the scene opened (for the tests).</summary>
        public LossReport LastLoss { get; private set; }
        public int Breaks { get; private set; }
        public int BiteCount { get; private set; }
        public int BaitThefts { get; private set; }

        // the last rub on structure in this fight (where the line was wearing)
        Vector3 rubAt;
        bool rubAtOk;

        void LineBroke(bool spooled)
        {
            Sfx.Play(Sfx.Snap, 1f);
            view.Shake(0.3f, 0.35f);
            string msg = BreakText(spooled);
            if (Fight != null && Fight.Outclassed) msg += "\n이 녀석에겐 장비가 약해요. 더 튼튼한 줄과 릴이 필요해요!";
            hud.Flash(msg, UIKit.Bad, 2f);
            DropFromAir();
            Hooked.JumpT = -1;
            var mouth = Hooked.MouthPos;
            var r = new LossReport { cause = spooled ? "spool" : LastSnapCause == FightModel.Cause.Abrasion ? "rub" : "tension", frame = Time.frameCount };
            // where it parted: the spool emptied takes the lot; a rub above the float takes the float too; the tension parts
            // it at its weakest point, the knot at the hook
            bool above = spooled || (r.cause == "rub" && RubAboveFloat(mouth, out r.rubFromHook, out r.floatFromHook));
            r.off = above ? Off.Above : Off.AtHook;
            var bait = Tackle.Bait;
            // the eaten bait was paid for at the hook set: a parted line only takes a lure (and the float above it)
            if (bait.isLure && Game.I.LoseLure(bait)) r.lure = bait;
            r.floatLost = Tackle.FloatFight == Tackle.FightFloat.Line && above;
            Hooked.Flee();
            Hooked = null;
            // (a rub below the float parted it where the line wore through; a tension break at the knot, at the mouth)
            r.breakAt = r.cause == "rub" && rubAtOk ? rubAt : mouth;
            r.floatAt = Tackle.FloatAt;
            r.mouth = mouth;
            FishOff(r.off, mouth, r.breakAt);
            ShowLoss(r);
        }

        void FishEscaped()
        {
            Sfx.Play(Sfx.Escape, 0.9f);
            hud.Flash("물고기가 바늘을 털고 도망갔다...", UIKit.Bad, 2f);
            DropFromAir();
            Hooked.JumpT = -1;
            var mouth = Hooked.MouthPos;
            Hooked.Flee();
            Hooked = null;
            FishOff(Off.Escape, mouth, mouth);
        }

        /// <summary>
        /// The fish is off. Kept on the line (shaken off; a float rig parted on the hook's side): the float / the lure stays
        /// where it was (<paramref name="mouth"/>: where the hook came out of the fish), and after a moment is wound in, spent
        /// (<see cref="SpentRetrieve"/>); a float rig comes home with the bare hook. Gone (a lure parted; a float rig parted
        /// above the float): he is ready again at once, the line's end whipping back to the tip.
        /// </summary>
        void FishOff(Off how, Vector3 mouth, Vector3 breakAt)
        {
            // (where the line met the water, or its end in the air, before the fight lets it go)
            var lineEnd = Angler.LineUnderwater ? Angler.WaterEntry : Angler.LineTarget ?? mouth;
            bool floatRig = Tackle.FloatFight == Tackle.FightFloat.Line;
            EndFightCommon(true);
            Angler.SetPose("idle");
            snagPrevOk = false;   // (no stale jump from where the rig was before the bite into the next snag check)
            bool kept = false;
            if (how == Off.Escape || (how == Off.AtHook && floatRig))
                kept = floatRig ? Tackle.LetGo() : Tackle.LetGoLure(mouth);
            if (kept)
            {
                Angler.LineTarget = Tackle.LineEnd;
                Angler.Slack01 = 0.45f;
                retrieveWait = RetrieveWait;
                spentRig = true;
                if (how == Off.AtHook) snap.Recoil(breakAt);
                SetState(S.Retrieving);
            }
            else
            {
                Tackle.Hide();
                if (how != Off.Escape) snap.Home(lineEnd);
                SetState(S.Ready);
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[BREAK] off {0}: {1} {2} -> {3} (tackle {4}{5})", how, floatRig ? "float rig" : "lure",
                Tackle.Bait != null ? Tackle.Bait.id : "-", State, Tackle.State, Tackle.BareHook ? ", bare hook" : ""));
        }

        /// <summary>
        /// The rub that wore the line through lay above the float: between the rod tip / the line's water entry and the
        /// float, i.e. further from the hook along the line than the float. The float sits on the line its depth up from the
        /// hook (pulled under along it while the line is taut and longer than that, riding the surface at the entry
        /// otherwise: then everything under the water is below it). Through the ice the same: the rim's contact is the hole's
        /// lower edge, right under the line's entry, so a float still riding in the hole is above it (parts below: the float
        /// is pulled up the hole) and a float drawn under the ice along the line by a fish running wide is below it (parts
        /// above: it is lost under the ice). False without a float on the line or a rub.
        /// </summary>
        bool RubAboveFloat(Vector3 mouth, out float rubFromHook, out float floatFromHook)
        {
            rubFromHook = floatFromHook = -1f;
            if (Tackle.FloatFight != Tackle.FightFloat.Line || !rubAtOk) return false;
            rubFromHook = Vector3.Distance(rubAt, mouth);
            floatFromHook = Vector3.Distance(Tackle.FloatAt, mouth);
            return rubFromHook > floatFromHook + 0.05f;
        }

        /// <summary>The line parted at a snag (forced, or cut with 끊기): see FishingController.Obstacles.SnagBreak.</summary>
        LossReport SnagLoss(bool cut)
        {
            var r = new LossReport { cause = cut ? "cut" : "snag", off = Tackle.UsesFloat ? Off.AtHook : Off.Above, frame = Time.frameCount };
            var bait = Tackle.Bait;
            if (bait.isLure)
            {
                if (Game.I.LoseLure(bait)) r.lure = bait;
            }
            else if (!Tackle.BaitGone)
            {
                // the bait was still on the hook (not eaten): used up once with the hook's hold (a bare hook loses nothing)
                Game.I.ConsumeBait();
                if (!bait.infinite)
                {
                    r.bait = bait;
                    r.baitN = 1;
                }
            }
            return r;
        }

        /// <summary>
        /// The loss toast under the break's flash: the lure (its icon, gold with its price when it costs
        /// <see cref="ExpensiveLure"/> or more, the eye when it is a legend's key), the bait x n, the float. Nothing lost
        /// (a float rig parted at the hook: its bait was eaten at the bite, as when a fish gets off): no toast.
        /// </summary>
        void ShowLoss(LossReport r)
        {
            Breaks++;
            LastLoss = r;
            var items = new List<Toast.Item>();
            if (r.lure != null)
            {
                var b = r.lure;
                r.lureKey = GameDatabase.Fish.Any(f => f.encounter != null && f.encounter.KeyWeight(b.id) > 0f);
                r.lureExpensive = b.price >= ExpensiveLure;
                string text = r.lureExpensive ? $"{b.name} ({UIKit.Num(b.price)}코인)" : b.name;
                items.Add(new Toast.Item { icon = Art.Item(b.id), text = text, highlight = r.Highlighted, badge = r.lureKey ? Art.UI("icon_eye") : null });
            }
            if (r.bait != null && r.baitN > 0)
            {
                bool key = GameDatabase.Fish.Any(f => f.encounter != null && f.encounter.KeyWeight(r.bait.id) > 0f);
                items.Add(new Toast.Item { icon = Art.Item(r.bait.id), text = $"{r.bait.name} ×{r.baitN}", highlight = key || r.bait.price >= ExpensiveLure,
                    badge = key ? Art.UI("icon_eye") : null });
            }
            if (r.floatLost) items.Add(new Toast.Item { icon = Art.Item(Tackle.FloatItemId), text = "찌" });
            foreach (var it in items) r.items.Add(it.text);
            // under the flash at the top (the break's message stays readable over it), once the line's whip is over: the
            // rod tip it flies back to is often right there
            if (items.Count > 0)
            {
                r.toastPending = true;
                Tween.After(LossToastDelay, () =>
                {
                    r.toastPending = false;
                    if (this == null) return;
                    r.toast = Toast.ShowItems("잃어버린 채비", UIKit.Bad, items, 3.2f, 70f);
                    if (r.Highlighted) Sfx.Play(Sfx.Error, 0.4f, 0.8f);
                });
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[BREAK] {0} parted {1}: lost {2}{3}{4} (rub {5:0.00} m / float {6:0.00} m from the hook) toast {7}",
                r.cause, r.off, r.lure != null ? r.lure.id + (r.Highlighted ? " (highlighted)" : "") : "no lure", r.baitN > 0 ? $", {r.bait.id} x{r.baitN}" : "",
                r.floatLost ? ", the float" : "", r.rubFromHook, r.floatFromHook, r.items.Count > 0 ? string.Join(" | ", r.items) + string.Format(CultureInfo.InvariantCulture, " (in {0:0.00} s)", LossToastDelay) : "none"));
        }

        // ================================================================== test hooks
        /// <summary>
        /// Test hook (-fkauto breaks): the line parts now in the fight. <paramref name="kind"/>: "tension" (over the limit),
        /// "spool" (the spool emptied), "rub" (worn through on structure at the point <paramref name="rubT"/> of the way from
        /// the line's water entry (the rod tip with no line under the water) to the fish's mouth). False unless fighting.
        /// </summary>
        internal bool DebugPartLine(string kind, float rubT = 0.5f)
        {
            if (State != S.Fighting || Hooked == null || Fight == null) return false;
            switch (kind)
            {
                case "spool":
                    Fight.DebugEnd(FightModel.Outcome.Spooled, FightModel.Cause.None);
                    LineBroke(true);
                    return true;
                case "rub":
                {
                    var mouth = Hooked.MouthPos;
                    var e = Angler.LineUnderwater ? Angler.WaterEntry : Angler.RodTip;
                    rubAt = Vector3.Lerp(e, mouth, Mathf.Clamp01(rubT));
                    rubAtOk = true;
                    Fight.DebugEnd(FightModel.Outcome.Snapped, FightModel.Cause.Abrasion);
                    LineBroke(false);
                    return true;
                }
                default:
                    Fight.DebugEnd(FightModel.Outcome.Snapped, FightModel.Cause.Tension);
                    LineBroke(false);
                    return true;
            }
        }

        /// <summary>Test hook (-fkauto breaks): the waiting (or homecoming) rig catches a hard snag where it is now (the zone nearest it). False unless it is in the water, waiting or being wound in.</summary>
        internal bool DebugSnag()
        {
            if ((State != S.Waiting && State != S.Retrieving) || Tackle.State != Tackle.Mode.Water || Obst == null || Obst.Empty) return false;
            var h = Tackle.HookPos;
            Obstacle zone = null;
            float best = float.MaxValue;
            foreach (var o in Obst.Snags)
            {
                float d = Obstacles.Dist(o, new Vector2(h.x, h.z));
                if (d < best)
                {
                    best = d;
                    zone = o;
                }
            }
            if (zone == null) return false;
            SnagAt(zone, h, false, "hard");
            return true;
        }

        /// <summary>Test hook (-fkauto breaks): the snagged rig comes free now, as a freed snag does (back to waiting). False unless snagged.</summary>
        internal bool DebugFreeSnag()
        {
            if (State != S.Snagged) return false;
            FreeSnag("test");
            return true;
        }
    }
}
