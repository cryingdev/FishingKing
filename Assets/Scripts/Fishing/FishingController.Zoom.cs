using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The view's zoom (<see cref="ViewZoom"/>, directed from here every frame), as 설정 → 캐스팅 후 줌인 says
    /// (<see cref="ZoomSetting"/>, read every frame: a change mid-fishing eases at once).
    /// <para>1.25배 (the default): once the cast lands it eases in (0.6 s) to the whole-pixel step nearest 1.25x (1080p:
    /// 4 -> 5 screen px per game px) towards a point between the rod tip and the float / lure, both kept in frame (the angler
    /// may be cropped at the bottom), and stays in through the wait, the bite, a snag and the fight. In the fight it follows
    /// the fish gently (slow smoothing towards the middle of the rod tip and the fish, both kept in frame and below the
    /// fight strip). It eases back out to 1x as the landing starts (the catch card shows the whole view), on the retrieve,
    /// when the fish is off (broken, escaped: then ready or winding in), for the legend encounter (its window and
    /// full-screen phases lay out on the whole view; back in for the fight it hooks or the wait if it turns away) and
    /// whenever he is back at the ready: casting is always at 1x (a wind-up hurries a zoom-out still under way: 1x within
    /// 0.15 s). While he waits, a legend's cue (its rings and eye glint over the lurk point, which may lie anywhere in the
    /// whole view) is kept in frame: the view turns to it a moment before it plays, or, when no frame at the step holds it
    /// with the rod tip and the rig, eases out to 1x while it plays. The HUD never zooms.</para>
    /// <para>1.5배: the same with the step nearest 1.5x (1080p: 4 -> 6); when the rod tip and the rig / fish (or a cue) do
    /// not fit in its frame, the largest step down to 1.25x's that holds them (<see cref="ViewZoom.StepAim"/>). The same
    /// timings (the eases, the follow); its smaller frame keeps a fast run in by the keep-in-frame pull sooner.</para>
    /// <para>액티브: 1x while he waits; as a bite starts, in quickly (<see cref="ZoomBiteIn"/>) to the 1.25x step on the
    /// float / lure (the rod tip kept in frame), kept through the fight it hooks (following the fish, out on the landing);
    /// a bite that ends without a hook (missed: winding in, or a lure back to the wait) eases out to 1x. A snag stays at
    /// the zoom it found; the legend encounter as above (1x, in for its fight).</para>
    /// <para>끔: never zooms (no fight follow either).</para>
    /// </summary>
    public partial class FishingController
    {
        const float ZoomWaitTau = 0.5f;    // s: the framing follows the float / lure (drifting, wound in)
        const float ZoomFightTau = 1.2f;   // s: ... and the fish in a fight, gently
        const float ZoomTipMargin = 10f, ZoomRigMargin = 14f, ZoomFishMargin = 20f;   // game px kept inside the frame
        const float FightStripUnits = 76f; // canvas units the fight strip covers along the top (FishingHUD)
        const float ZoomAimOut = 0.15f;    // s for a whole zoom-out once he winds up (one still under way is hurried)
        const float ZoomCueLead = 0.7f;    // s: the view turns to a legend's cue this long before it plays ...
        const float ZoomCueTau = 0.3f;     // ... this briskly ...
        const float ZoomCueMargin = 12f;   // ... and keeps its rings and glint this far inside the frame (game px)
        /// <summary>액티브: seconds for the zoom-in as a bite starts.</summary>
        public const float ZoomBiteIn = 0.35f;
        const float ZoomBiteTau = 0.25f;   // s: 액티브, the framing settles on the float / lure through the bite
        int zoomCue;                       // the cue being framed: 0 none, 1 panned into the frame, -1 too far (out to 1x)

        /// <summary>설정 → 캐스팅 후 줌인 now.</summary>
        public static ZoomMode ZoomSetting => Game.I != null && Game.Data != null ? (ZoomMode)Game.Data.zoomMode : ZoomMode.X125;

        /// <summary>
        /// The zoom is wanted in this state: 1.25배 / 1.5배 in while the rig is in the water (waiting, biting, snagged) or a
        /// fish is on; 액티브 only from the bite (biting, fighting); 끔 never. (액티브 holds a snag at the zoom it found.)
        /// </summary>
        public bool ZoomWanted
        {
            get
            {
                switch (ZoomSetting)
                {
                    case ZoomMode.Off: return false;
                    case ZoomMode.Active: return State == S.Biting || State == S.Fighting;
                    default: return State == S.Waiting || State == S.Biting || State == S.Fighting || State == S.Snagged;
                }
            }
        }

        /// <summary>The rod tip in the pixel scene (on the boat's deck with its bob).</summary>
        public Vector2 RodTip2D => P.To2D(Angler.RodTip) + Stage.DeckBob;

        /// <summary>Where the rig shows: perched on a prop, else its spot on the water (the float, or the line's entry over the lure).</summary>
        public Vector2 RigShown2D => Tackle.State == Tackle.Mode.Perched ? P.To2D(Tackle.PerchAt) : P.To2D(new Vector3(Tackle.Surface.x, 0f, Tackle.Surface.z));

        /// <summary>Where a fish shows (lifted by the refraction under water; a jump as it is).</summary>
        public Vector2 Fish2D(FishAgent f) => P.To2D(P.Apparent(f.Pos));

        void InitZoom()
        {
            if (view != null && view.Zoom != null) view.Zoom.Director = DirectZoom;
        }

        void DirectZoom(ViewZoom z)
        {
            var mode = ZoomSetting;
            bool active = mode == ZoomMode.Active;
            z.StepAim = mode == ZoomMode.X150 ? ViewZoom.AimWide : ViewZoom.Aim;
            // (액티브: a snag stays at the zoom it found)
            bool zoomIn = (ZoomWanted || (active && State == S.Snagged && z.ZoomedIn)) && Angler != null && Tackle != null;
            if (!zoomIn)
            {
                zoomCue = 0;
                // (a wind-up or a throw hurries any zoom-out still under way: casting is always at 1x)
                z.Want(false, State == S.Aiming || State == S.Casting ? ZoomAimOut : ViewZoom.EaseTime);
                return;
            }
            z.TopInsetPx = FightStripUnits * UIKit.ScaleFactor;
            var tip = RodTip2D;
            z.Keep(tip, ZoomTipMargin);
            if (State == S.Fighting && Hooked != null)
            {
                z.Want(true);
                var fish = Fish2D(Hooked);
                z.Focus(Vector2.Lerp(tip, fish, 0.5f), ZoomFightTau);
                z.Keep(fish, ZoomFishMargin);
                return;
            }
            var rig = RigShown2D;
            z.Keep(rig, ZoomRigMargin);
            // (a sunk lure shows a little below its entry)
            if (!Tackle.UsesFloat && Tackle.State == Tackle.Mode.Water && Tackle.Depth > 0.05f) z.Keep(P.To2D(P.Apparent(Tackle.HookPos)), ZoomRigMargin);
            if (active)
            {
                // the bite: in quickly, on the float / lure (the rod tip kept in frame)
                zoomCue = 0;
                bool bite = State == S.Biting;
                z.Want(true, bite ? ZoomBiteIn : ViewZoom.EaseTime);
                z.Focus(rig, bite ? ZoomBiteTau : ZoomWaitTau);
                return;
            }
            // a legend's cue while he waits (the rings and the glint over its lurk point, wherever it lies): turned to a
            // moment before it plays, it stays in frame with the rod tip and the rig; too far from them for one frame, the
            // view eases out to 1x while it plays and back in after
            bool cue = State == S.Waiting && Watch != null && Watch.CueSoon(ZoomCueLead);
            if (!cue) zoomCue = 0;
            else
            {
                z.Keep(Watch.CueRings2D, ZoomCueMargin, true);
                z.Keep(Watch.CueEyes2D, ZoomCueMargin, true);
                if (zoomCue == 0) zoomCue = z.KeepsFit ? 1 : -1;
            }
            z.Want(zoomCue >= 0);
            z.Focus(Vector2.Lerp(tip, rig, 0.5f), zoomCue > 0 ? ZoomCueTau : ZoomWaitTau);
        }

        /// <summary>Test hook (-fkauto zoom): the line breaks now (the fight's own break).</summary>
        internal void DebugBreak()
        {
            if (State == S.Fighting && Hooked != null) LineBroke(false);
        }
    }
}
