using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The fish off the line (Docs/controls.md, Docs/obstacles_spec.md 6.6 / 7): got away (the hook shaken out) or the line
    /// parted. A float rig's float stays and is wound in spent with the bare hook (its bait was eaten at the bite, paid for
    /// there once: a parted line only takes a lure).
    /// </summary>
    public partial class FishingController
    {
        bool spentRig;
        /// <summary>
        /// The rig being wound in came off a fish: it is spent, catches on nothing and is taken by no fish until it is home
        /// (set by FishOff, cleared by FinishRetrieve).
        /// </summary>
        public bool SpentRetrieve => spentRig;
        /// <summary>Bites and stolen baits since the scene opened (for the tests).</summary>
        public int BiteCount { get; private set; }
        public int BaitThefts { get; private set; }

        void LineBroke(bool spooled)
        {
            Sfx.Play(Sfx.Snap, 1f);
            view.Shake(0.3f, 0.35f);
            string msg = BreakText(spooled);
            // the eaten bait was paid for at the hook set: a parted line only takes a lure
            bool lure = Game.I.LoseLure(Tackle.Bait);
            if (Fight != null && Fight.Outclassed) msg += "\n이 녀석에겐 장비가 약해요. 더 튼튼한 줄과 릴이 필요해요!";
            if (lure) msg += "  (루어를 잃었다)";
            hud.Flash(msg, UIKit.Bad, 2f);
            DropFromAir();
            Hooked.JumpT = -1;
            Hooked.Flee();
            Hooked = null;
            FishOff();
        }

        void FishEscaped()
        {
            Sfx.Play(Sfx.Escape, 0.9f);
            hud.Flash("물고기가 바늘을 털고 도망갔다...", UIKit.Bad, 2f);
            DropFromAir();
            Hooked.JumpT = -1;
            Hooked.Flee();
            Hooked = null;
            FishOff();
        }

        /// <summary>
        /// The fish is off (it got away, the line broke): a lure rig is gone at once and he is ready again; a float rig's
        /// float stays where it was, back up on the water, and after a moment is wound in spent with the bare hook.
        /// </summary>
        void FishOff()
        {
            bool keep = Tackle.FloatFight == Tackle.FightFloat.Line;
            EndFightCommon(keep);
            Angler.SetPose("idle");
            snagPrevOk = false;   // (no stale jump from where the rig was before the bite into the next snag check)
            if (keep && Tackle.LetGo())
            {
                Angler.LineTarget = Tackle.LineEnd;
                Angler.Slack01 = 0.45f;
                retrieveWait = RetrieveWait;
                spentRig = true;
                SetState(S.Retrieving);
            }
            else
            {
                Tackle.Hide();
                SetState(S.Ready);
            }
        }
    }
}
