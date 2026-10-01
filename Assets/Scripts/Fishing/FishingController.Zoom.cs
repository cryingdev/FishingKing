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
    /// <para>Beyond the home frame (every mode): the stage art is wider than the render target (<see cref="ViewZoom.ArtPx"/>),
    /// so a fish running out past the 1x frame's side (the sea, the ocean, a wide run on the lake), or a rig cast or
    /// drifted there, is followed onto it: zoomed, the crop's pan simply goes on past the old edge (the camera moves by
    /// whole px under it), and a run so far out that no zoomed frame holds the fish beside the rod tip (the sea's 800 px
    /// art lets it get ~400 px from the tip; a 1.25x frame is 384 wide) eases out to 1x until they have fitted again
    /// with room to spare for 1 s; at 1x (끔 all the time, 액티브 while he waits, any mode while the zoom eases) the view
    /// itself pans, only as far as keeps the rod tip and the rig / fish their margins inside, accelerating gently and
    /// easing back home once they are back in the home frame or the fight is over (the landing, the catch card, the
    /// ready: always home, briskly, before the card; a float a fish let go out there is followed while it is wound in;
    /// a wind-up eases it home in 0.15 s). 끔 pans too: it means "no zoom", and a
    /// fish off the screen is never wanted; the view only
    /// moves once the fish (or the float) comes within its margin (+ <see cref="ViewZoom.OneLead"/>: ~30 px) of the home
    /// frame's side, so a fight that stays inside it looks exactly as before. Vertically the view stays put (a fish below
    /// the home frame is under the stand, hidden by the front layer).</para>
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
        const float ZoomApartSlack = 12f;  // game px of room the fish and the rod tip must fit with to zoom back in ...
        const float ZoomApartBack = 1f;    // ... for this long (s)
        bool zoomApart;                    // a run out over the overscan too far from the rod tip for a zoomed frame: at 1x
        float zoomApartT;

        // ---- the legend spot's look (Docs/fishing_gameplay.md 9.4)
        /// <summary>Seconds the look takes to zoom in on a new spot (the zoom's own full ease: the step change eases as long).</summary>
        public const float LookIn = ViewZoom.EaseTime;
        /// <summary>Seconds it then stays on the spot (at least one whole blink of the marker, 0.9 s).</summary>
        public const float LookHold = 1.4f;
        /// <summary>Whole-pixel steps beyond the mode's own step the look zooms to (1080p: 1.25배 / 액티브 5 -> 6, 1.5배 6 -> 7).</summary>
        public const int LookSteps = 1;
        /// <summary>Seconds the way back may take at most before the usual framing (hard keep-in-frame) takes over.</summary>
        public const float LookBackMax = 1.2f;
        /// <summary>Seconds: no look for a spot offered this soon after the last one ended under his own cast (a landing outside, a wrong rig, a claim lost): he knows where it is.</summary>
        public const float LookGap = 12f;
        int lookPhase;                     // 0 none, 1 looking at the spot, 2 on the way back
        int lookSeen;                      // the watch's spot offers already handled
        float lookT;
        S lookState;
        string lookLastEnd = "";
        float lookLastEndAt = -99f;
        /// <summary>The look is on the spot now (zooming in or holding).</summary>
        public bool SpotLooking => lookPhase == 1;
        /// <summary>The look's way back (soft keep-in-frame, glided) is under way.</summary>
        public bool SpotLookBack => lookPhase == 2;
        /// <summary>Test: looks started, the spot's time when the last one started, how the last one ended ("back", "windup", "state", "spot"), the last offer not looked at and why ("off", "ice", "busy", "retry").</summary>
        public int LookCount { get; private set; }
        public float LookDelay { get; private set; }
        public string LookEnd { get; private set; } = "";
        public int LookSkipOffer { get; private set; }
        public string LookSkip { get; private set; } = "";

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
            if (view == null || view.Zoom == null) return;
            view.Zoom.Director = DirectZoom;
            // the stage art's overscan: the view may pan sideways over all of it (ViewZoom.ArtPx)
            view.Zoom.ArtPx = new Vector2Int(L.widthPx > 0 ? L.widthPx : 640, L.heightPx > 0 ? L.heightPx : 400);
        }

        /// <summary>
        /// The rig is in the water (waiting, biting, snagged, or being wound in: a float a fish let go far out comes back
        /// across the overscan in view) or a fish is on: the rod tip and the rig / the fish are must-see points (zoomed:
        /// kept in the crop; at 1x: the view pans over the stage art's overscan when they go beyond the home frame).
        /// </summary>
        bool ZoomFollows => Angler != null && Tackle != null
                            && (State == S.Waiting || State == S.Biting || State == S.Snagged || (State == S.Fighting && Hooked != null)
                                || (State == S.Retrieving && (Tackle.State == Tackle.Mode.Water || Tackle.State == Tackle.Mode.Perched)));

        void DirectZoom(ViewZoom z)
        {
            var mode = ZoomSetting;
            bool active = mode == ZoomMode.Active;
            z.StepAim = mode == ZoomMode.X150 ? ViewZoom.AimWide : ViewZoom.Aim;
            if (SpotLook(z, mode)) return;
            if (lookPhase == 2)
            {
                // the way back: the usual framing, its must-see points soft and the pan glided, so the rod tip and the rig
                // come back into frame without the keep pull's snap
                z.Glide = true;
                z.SoftKeeps = true;
            }
            // (액티브: a snag stays at the zoom it found)
            bool zoomIn = (ZoomWanted || (active && State == S.Snagged && z.ZoomedIn)) && Angler != null && Tackle != null;
            if (State != S.Fighting)
            {
                zoomApart = false;
                zoomApartT = 0f;
            }
            if (!zoomIn)
            {
                zoomCue = 0;
                // (a wind-up or a throw hurries any zoom-out still under way, and the view home: casting is always at 1x
                // from the home view)
                bool aiming = State == S.Aiming || State == S.Casting;
                z.Want(false, aiming ? ZoomAimOut : ViewZoom.EaseTime);
                z.HurryHome = aiming;
                // 끔 (and 액티브 while he waits): at 1x the view still pans, gently and only as far as needed, to keep the rod
                // tip and the float / the fish in when they go beyond the home frame (a long run, a wide cast)
                if (ZoomFollows) KeepInFrame(z);
                return;
            }
            z.TopInsetPx = FightStripUnits * UIKit.ScaleFactor;
            var tip = RodTip2D;
            z.Keep(tip, ZoomTipMargin);
            if (State == S.Fighting && Hooked != null)
            {
                var fish = Fish2D(Hooked);
                z.Focus(Vector2.Lerp(tip, fish, 0.5f), ZoomFightTau);
                z.Keep(fish, ZoomFishMargin);
                // a run out over the overscan so far that no zoomed frame holds the fish beside the rod tip: out to 1x
                // (its view pans to hold both), back in once they have fitted with room to spare for a moment
                if (!z.KeepsFitAcross())
                {
                    zoomApart = true;
                    zoomApartT = 0f;
                }
                else if (zoomApart)
                {
                    zoomApartT = z.KeepsFitAcross(ZoomApartSlack) ? zoomApartT + Time.deltaTime : 0f;
                    if (zoomApartT >= ZoomApartBack) zoomApart = false;
                }
                z.Want(!zoomApart);
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
                // (a legend's blinking spot while he waits: in frame too, or out to 1x while it blinks)
                bool spot = State == S.Waiting && Watch != null && Watch.SpotOn;
                if (spot) z.Keep(Watch.Spot2D, ZoomCueMargin, true);
                z.Want(!spot || z.KeepsFit, bite ? ZoomBiteIn : ViewZoom.EaseTime);
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
                // (and the spot that blinks with it: the player has to see where to cast)
                if (Watch.SpotOn) z.Keep(Watch.Spot2D, ZoomCueMargin, true);
                if (zoomCue == 0) zoomCue = z.KeepsFit ? 1 : -1;
            }
            z.Want(zoomCue >= 0);
            z.Focus(Vector2.Lerp(tip, rig, 0.5f), zoomCue > 0 ? ZoomCueTau : ZoomWaitTau);
        }

        /// <summary>
        /// The legend spot's look: as a new spot comes up (with its cue) the view zooms one whole-pixel step beyond the
        /// mode's (<see cref="LookSteps"/>) onto it in <see cref="LookIn"/>, its pan glided (acceleration-limited), stays
        /// there <see cref="LookHold"/> with the marker blinking, then eases back to the usual framing (the rod tip and the
        /// rig soft, glided: home by <see cref="LookBackMax"/>). The rod tip may leave the frame meanwhile. Not with 끔 (the
        /// spot always lies in the home view: nothing to bring into sight, and the player asked for no zoom), not on the ice
        /// (the spot is the hole he always fishes), not while he winds up or throws (casting is from the home view), and not
        /// for a quick retry after his own cast missed it or lost the claim (<see cref="LookGap"/>). Any change of state (a
        /// wind-up: hurried home as ever, a bite, the encounter) cuts it at once. True while the look directs the view.
        /// </summary>
        bool SpotLook(ViewZoom z, ZoomMode mode)
        {
            var w = Watch;
            if (w == null)
            {
                lookPhase = 0;
                return false;
            }
            // (how the last spot ended, kept past the next offer, which clears it)
            if (!string.IsNullOrEmpty(w.SpotEnd) && w.SpotEndedAt >= 0f)
            {
                lookLastEnd = w.SpotEnd;
                lookLastEndAt = w.SpotEndedAt;
            }
            if (w.SpotOffers != lookSeen)
            {
                lookSeen = w.SpotOffers;
                string skip = !w.SpotOn ? "gone" : mode == ZoomMode.Off ? "off" : L.IsIce ? "ice"
                    : State != S.Ready && State != S.Waiting && State != S.Retrieving ? "busy"
                    : (lookLastEnd == "outside" || lookLastEnd == "rig" || lookLastEnd == "lost") && Time.time - lookLastEndAt < LookGap ? "retry" : null;
                if (skip != null)
                {
                    LookSkipOffer = lookSeen;
                    LookSkip = skip;
                    Debug.Log($"[ZOOM] spot {lookSeen}: no look ({skip})");
                }
                else
                {
                    lookPhase = 1;
                    lookT = 0f;
                    lookState = State;
                    LookCount++;
                    LookDelay = w.SpotT;
                    LookEnd = "";
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ZOOM] spot {0}: look from {1} (level {2:0.00}, step {3})",
                        lookSeen, State, z.Level, z.StepPx.y));
                }
            }
            if (lookPhase == 0) return false;
            if (State != lookState)
            {
                // cut: the wind-up's own hurry home (or the bite's, the encounter's framing) from here
                EndLook(State == S.Aiming || State == S.Casting ? "windup" : "state");
                return false;
            }
            lookT += Time.deltaTime;
            if (lookPhase == 1)
            {
                if (!w.SpotOn || lookT >= LookIn + LookHold)
                {
                    lookPhase = 2;
                    lookT = 0f;
                    if (!w.SpotOn) LookEnd = "spot";
                    return false;
                }
                z.StepExtra = LookSteps;
                z.Glide = true;
                z.Want(true, LookIn);
                z.Focus(w.Spot2D, ZoomCueTau);
                return true;
            }
            // the way back: done once the usual framing has settled (or after LookBackMax)
            if ((lookT > 0.1f && z.Settled) || lookT >= LookBackMax) EndLook(LookEnd == "" ? "back" : LookEnd);
            return false;
        }

        void EndLook(string why)
        {
            if (lookPhase == 0) return;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[ZOOM] spot look ends: {0} ({1} {2:0.00}s in)", why, lookPhase == 1 ? "looking" : "back", lookT));
            lookPhase = 0;
            LookEnd = why;
        }

        /// <summary>The 1x view's must-see points: the rod tip and the fish, or the rig (a sunk lure a little below its entry).</summary>
        void KeepInFrame(ViewZoom z)
        {
            z.Keep(RodTip2D, ZoomTipMargin);
            if (State == S.Fighting && Hooked != null)
            {
                z.Keep(Fish2D(Hooked), ZoomFishMargin);
                return;
            }
            z.Keep(RigShown2D, ZoomRigMargin);
            if (!Tackle.UsesFloat && Tackle.State == Tackle.Mode.Water && Tackle.Depth > 0.05f) z.Keep(P.To2D(P.Apparent(Tackle.HookPos)), ZoomRigMargin);
            if (Watch != null && Watch.SpotOn) z.Keep(Watch.Spot2D, ZoomCueMargin, true);
        }

        /// <summary>Test hook (-fkauto zoom): the line breaks now (the fight's own break).</summary>
        internal void DebugBreak()
        {
            if (State == S.Fighting && Hooked != null) LineBroke(false);
        }
    }
}
