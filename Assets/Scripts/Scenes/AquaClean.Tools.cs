using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The cleaning tools on the cabinet ledge, right of the feed (Sprites/World/clean_*_rest, fk_aquaclean.py): the
    /// sponge (given free), the net and the siphon (bought in the shop's 청소 section; until then a faint dashed place
    /// that opens the shop). A press picks one up, it follows the finger, let go it glides back; a tap says how it is
    /// used. The sponge pressed on the glass wipes the algae under its pad (the brush stamped every 2 px of the drag: a
    /// pixel's level drops by the strongest brush alpha it got in the stroke, so the pad's half-strength rim leaves
    /// faint streaks), squeaking, with sparkles and a shine behind it. The net dipped in the water catches the bits its
    /// frame passes (they fly into the bag; emptied when let go). The siphon's mouth brought down to the gravel works:
    /// the gravel tumbles in the tube, dirty water runs up the hose and the dirt under its path is sucked away. The held
    /// net / siphon hang from their handle / hose running up past the top of the screen (under the tank's frame).
    /// </summary>
    public partial class AquaClean
    {
        static float LedgeY => L.ledgeY;

        enum TK { Sponge, Net, Siphon }

        class Tool
        {
            public TK kind;
            public ToolDef def;
            public Vector2 home;
            public int w, h;
            public SpriteRenderer sr, shadow, slot, ghost;
            public Sprite restS;
            public bool shown;
            public float pop = -1f;
            public Vector4 hit;     // (x0, x1, y0, y1) px from home: a press there picks it up
        }

        readonly List<Tool> tools = new List<Tool>();
        Tool held;
        bool back;
        float backT, heldT;
        Vector2 backFrom, pointer, startPointer, pos, prevPos, vel, gripFrom;
        bool movedFar;

        Sprite spongeHeld, netHeld, netFull, netHandleS, siphonHeld, hoseS;
        Sprite[] spongeWipe, netSweep, siphonWork, hoseFlow, suckS, squeakS, streakS, burstS, splashS;
        float[] spongeBrush, siphonBrush;
        int sbW, sbH, pbW, pbH;

        // a stroke: m = the strongest brush alpha each pixel got; its level = the level before x (1 - m)
        float[] aM, aA0, bM, bA0;
        readonly List<int> aTouched = new List<int>(), bTouched = new List<int>();
        bool wiping, working, inWater, netFlip;
        Vector2 lastStamp;
        float lastSiphonX, removedFrame;
        SpriteRenderer handle, hose, suck;
        int netCount;
        float squeakT, sparkT, streakT, gurgleT, swishT;

        Text hint, meter;
        Image hintPlate, meterPlate;
        float hintTime;
        Vector2 hintAt;

        // ---- read by the test autopilot
        public int Stamps { get; private set; }
        public int SiphonStamps { get; private set; }
        public int Scooped { get; private set; }
        public int Squeaks { get; private set; }
        public int Splashes { get; private set; }
        public bool Wiping => wiping;
        public bool SiphonWorking => working;
        public bool NetInWater => held != null && held.kind == TK.Net && inWater && !back;
        public int NetCount => netCount;
        /// <summary>A tool is held (or gliding back): fish taps wait.</summary>
        public bool Busy => held != null;
        public string HeldId => held != null && !back ? held.def.id : null;
        public Vector2 HeldPos => pos;
        public Vector2 SiphonMouth => pos + new Vector2(0f, -14f / Ppu);
        public string HintShown => hintTime > 0f && hint != null ? hint.text : "";
        public string MeterShown => meterPlate != null && meterPlate.enabled ? meter.text : "";
        public Vector2 ToolHome(string id) => tools.FirstOrDefault(t => t.def.id == id)?.home ?? Vector2.zero;
        public string ToolState(string id)
        {
            var t = tools.FirstOrDefault(x => x.def.id == id);
            if (t == null) return "None";
            if (t == held) return back ? "Back" : "Held";
            return t.shown ? "Rest" : "Slot";
        }

        // the bag of the net (where caught bits fly)
        Vector2 NetBag => pos + new Vector2(0f, -4f / Ppu);

        void InitTools()
        {
            spongeHeld = Art.Get("World/clean_sponge_held");
            spongeWipe = Frames("clean_sponge_wipe_f", 3);
            netHeld = Art.Get("World/clean_net_held");
            netFull = Art.Get("World/clean_net_full");
            netSweep = Frames("clean_net_sweep_f", 2);
            netHandleS = Art.Get("World/clean_net_handle");
            siphonHeld = Art.Get("World/clean_siphon_held");
            siphonWork = Frames("clean_siphon_work_f", 3);
            hoseS = Art.Get("World/clean_siphon_hose");
            hoseFlow = Frames("clean_siphon_hose_flow_f", 4);
            suckS = Frames("clean_siphon_suck_f", 3);
            squeakS = Frames("clean_squeak_f", 3);
            streakS = Frames("clean_streak_f", 3);
            burstS = Frames("clean_burst_f", 5);
            splashS = Frames("feed_splash_f", 4);
            spongeBrush = Brush(Art.Get("World/clean_sponge_brush"), out sbW, out sbH);
            siphonBrush = Brush(Art.Get("World/clean_siphon_brush"), out pbW, out pbH);
            aM = new float[AquaTank.AW * AquaTank.AH];
            aA0 = new float[aM.Length];
            bM = new float[AquaTank.BW * AquaTank.BH];
            bA0 = new float[bM.Length];
            // on the ledge right of the feed: the sponge, the net (its handle up), the siphon at the cabinet's end beside
            // the tank (clear of the late mid / front slots)
            tools.Add(MakeTool(TK.Sponge, AquaTank.SpongeId, "sponge", new Vector2(L.spongeX, LedgeY + 8f / Ppu), 24, 16, new Vector4(-13f, 13f, -9f, 9f)));
            tools.Add(MakeTool(TK.Net, AquaTank.NetId, "net", new Vector2(L.netX, LedgeY + 22f / Ppu), 20, 44, new Vector4(-11f, 11f, -23f, 22f)));
            tools.Add(MakeTool(TK.Siphon, AquaTank.SiphonId, "siphon", new Vector2(L.siphonX, LedgeY + 18f / Ppu), 28, 36, new Vector4(-15f, 15f, -19f, 16f)));
            handle = Shaft("NetHandle", netHandleS);
            hose = Shaft("SiphonHose", hoseS);
            suck = NewSprite("SiphonSuck", suckS[0], 38);
            suck.enabled = false;
            hint = UIKit.PlateLabel(ui, "", 16, UIKit.Cream, out hintPlate);
            hintPlate.name = "CleanHint";
            hintPlate.rectTransform.anchorMin = hintPlate.rectTransform.anchorMax = Vector2.zero;
            hintPlate.rectTransform.pivot = new Vector2(0.5f, 0f);
            UIKit.FitPlate(hint, hintPlate);
            meter = UIKit.PlateLabel(ui, "", 16, UIKit.Cream, out meterPlate);
            meterPlate.name = "CleanMeter";
            meterPlate.rectTransform.anchorMin = meterPlate.rectTransform.anchorMax = Vector2.zero;
            meterPlate.rectTransform.pivot = new Vector2(0.5f, 0f);
            UIKit.FitPlate(meter, meterPlate);
        }

        static Sprite[] Frames(string prefix, int n)
        {
            var a = new Sprite[n];
            for (int i = 0; i < n; i++) a[i] = Art.Get("World/" + prefix + i);
            return a;
        }

        SpriteRenderer Shaft(string name, Sprite s)
        {
            var sr = NewSprite(name, s, 36);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.enabled = false;
            return sr;
        }

        Tool MakeTool(TK kind, string id, string key, Vector2 home, int w, int h, Vector4 hit)
        {
            var t = new Tool
            {
                kind = kind, def = AquaTank.Tool(id), home = home, w = w, h = h, hit = hit,
                restS = Art.Get($"World/clean_{key}_rest"), shown = AquaTank.Owns(id),
            };
            t.shadow = NewSprite("ToolShadow_" + key, Art.Get($"World/clean_{key}_shadow"), 44);
            Place(t.shadow, new Vector2(home.x, LedgeY)); // (centred on the ledge line)
            t.sr = NewSprite("Tool_" + key, t.restS, 46);
            Place(t.sr, home);
            // not owned: a faint dashed place with the tool's faded shape (tap: the shop)
            t.slot = NewSprite("ToolSlot_" + key, Dashed(w + 2, h + 2), 44);
            t.slot.color = new Color(1f, 0.96f, 0.86f, 0.55f);
            Place(t.slot, home);
            t.ghost = NewSprite("ToolGhost_" + key, t.restS, 44);
            t.ghost.color = new Color(0.55f, 0.52f, 0.48f, 0.35f);
            Place(t.ghost, home);
            return t;
        }

        /// <summary>A dashed outline of a footprint (the tool's place while it is not owned).</summary>
        static Sprite Dashed(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                bool corner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1);
                bool dash = ((x + y) / 2) % 2 == 0;
                t.SetPixel(x, y, edge && !corner && dash ? Color.white : clear);
            }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16);
        }

        // ------------------------------------------------------------------ input
        /// <summary>A press on a tool (or its empty place): fish taps ignore it.</summary>
        public bool Hits(Vector2 w) => HitTool(w) != null;

        Tool HitTool(Vector2 w)
        {
            foreach (var t in tools)
            {
                var l = (w - t.home) * Ppu;
                if (l.x >= t.hit.x && l.x <= t.hit.y && l.y >= t.hit.z && l.y <= t.hit.w) return t;
            }
            return null;
        }

        void UpdateInput()
        {
            bool blocked = Dialog.Open || UIKit.ModalCount > 0 || AquaDecor.ModeOn;
            var w = pv.ScreenToWorld(PointerInput.Position);
            if (blocked)
            {
                if (held != null && !back) Release();
                return;
            }
            if (PointerInput.WorldPressed && held == null) Press(w);
            if (held != null && !back)
            {
                if (PointerInput.IsDown) pointer = w;
                else Release();
            }
        }

        void Press(Vector2 w)
        {
            var feed = AquaFeed.Current;
            if (feed != null && feed.Busy) return;
            var t = HitTool(w);
            if (t == null) return;
            if (!t.shown)
            {
                Sfx.Play(Sfx.Click);
                AquaCare.Log($"tool place {t.def.id}: to the shop");
                ShopUI.TankSub = ShopUI.SubClean;
                ShopUI.Focus = t.def.id;
                ShopUI.Open(ui, ItemKind.Tank);
                return;
            }
            PickUp(t, w);
        }

        void PickUp(Tool t, Vector2 w)
        {
            // the income so far at the dirt as it was
            AquaCare.Advance(Game.Data, SaveSystem.Now);
            NoteBefore();
            held = t;
            back = false;
            heldT = 0f;
            pointer = startPointer = w;
            movedFar = false;
            pos = prevPos = t.home;
            vel = Vector2.zero;
            gripFrom = t.home - w;
            wiping = working = inWater = false;
            netCount = 0;
            t.shadow.enabled = false;
            t.sr.sortingOrder = t.kind == TK.Sponge ? 44 : 37;
            Sfx.Play(Sfx.Rustle, 0.45f, 1.25f);
            AquaCare.Log($"pick up {t.def.id}: {AquaTank.Describe(Game.Data)}");
        }

        void Release()
        {
            var t = held;
            if (t == null || back) return;
            EndStroke();
            EndDirtStroke();
            back = true;
            backT = 0f;
            backFrom = pos;
            handle.enabled = hose.enabled = suck.enabled = false;
            AquaTank.Commit();
            Game.I.Save();
            string done = CheckDone(pos);
            // what came of it: the net's catch, a part cleaned, or (a tap) how the tool is used
            var over = t.home + new Vector2(-1.5f, 2.6f);
            if (t.kind == TK.Net && netCount > 0) ShowHint(over, $"찌꺼기 {netCount}개를 건졌어요" + (done != null ? " · " + done : ""));
            else if (done != null) ShowHint(over, done);
            else if (!movedFar && heldT < 0.5f) ShowHint(over, t.def.use);
            AquaCare.Log($"release {t.def.id}: stamps {Stamps}/{SiphonStamps}, scooped {Scooped} (this dip {netCount}), {AquaTank.Describe(Game.Data)}");
        }

        // ------------------------------------------------------------------ the tools
        static readonly Vector2 SpongeGrip = Vector2.zero;
        static readonly Vector2 NetGrip = new Vector2(0f, 2f / Ppu);        // the finger on the net frame's centre
        static readonly Vector2 SiphonGrip = new Vector2(0f, -6f / Ppu);    // the finger on the tube, the mouth below it

        void UpdateTools(float dt)
        {
            removedFrame = 0f;
            foreach (var t in tools) DrawRest(t, dt);
            if (held == null) return;
            var h = held;
            if (back)
            {
                backT += dt / 0.25f;
                float k = Mathf.Clamp01(backT);
                var p = Vector2.Lerp(backFrom, h.home, 1f - (1f - k) * (1f - k));
                if (k > 0.45f)
                {
                    h.sr.sprite = h.restS;
                    h.sr.flipX = false;
                }
                Place(h.sr, p);
                if (k >= 1f)
                {
                    held = null;
                    h.sr.sortingOrder = 46;
                    netCount = 0;
                    Sfx.Play(Sfx.Knock, 0.3f, 1.3f);
                }
                return;
            }
            heldT += dt;
            var grip = h.kind == TK.Net ? NetGrip : h.kind == TK.Siphon ? SiphonGrip : SpongeGrip;
            float e = Mathf.Clamp01(heldT / 0.18f);
            var target = pointer + Vector2.Lerp(gripFrom, grip, e * e * (3f - 2f * e));
            var half = pv.ViewSize * 0.5f;
            target.x = Mathf.Clamp(target.x, -half.x + 0.6f, half.x - 0.6f);
            target.y = Mathf.Clamp(target.y, -half.y + 0.6f, half.y - 0.6f);
            prevPos = pos;
            var step = target - pos;
            vel = Vector2.Lerp(vel, step / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 14f));
            pos = target;
            if ((pointer - startPointer).magnitude > 0.35f) movedFar = true;
            switch (h.kind)
            {
                case TK.Sponge: UpdateSponge(dt); break;
                case TK.Net: UpdateNet(dt); break;
                default: UpdateSiphon(dt); break;
            }
        }

        void DrawRest(Tool t, float dt)
        {
            if (!t.shown && AquaTank.Owns(t.def.id))
            {
                // bought while here: it pops in
                t.shown = true;
                t.pop = 0f;
            }
            t.slot.enabled = t.ghost.enabled = !t.shown;
            if (!t.shown)
            {
                t.sr.enabled = false;
                t.shadow.enabled = false;
                return;
            }
            t.sr.enabled = true;
            if (t == held) return;
            float scale = 1f, alpha = 1f;
            if (t.pop >= 0f)
            {
                t.pop += dt;
                float k = Mathf.Clamp01(t.pop / 0.22f);
                scale = Mathf.LerpUnclamped(0.6f, 1f, Tween.BackOut(k));
                alpha = k;
                if (k >= 1f) t.pop = -1f;
            }
            t.sr.sprite = t.restS;
            t.sr.flipX = false;
            t.sr.color = new Color(1f, 1f, 1f, alpha);
            t.sr.transform.localScale = new Vector3(scale, scale, 1f);
            Place(t.sr, t.home + new Vector2(0f, (scale - 1f) * -t.h * 0.5f / Ppu));
            t.shadow.enabled = true;
            t.shadow.color = new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>The net's handle / the siphon's hose: from the held sprite's top up past the top of the screen.</summary>
        void DrawShaft(SpriteRenderer sr, Vector2 bottom, Sprite s)
        {
            float top = pv.BaseCenter.y + pv.ViewSize.y * 0.5f + 1f;
            int px = Mathf.Max(2, Mathf.CeilToInt((top - bottom.y) * Ppu));
            if ((px & 1) == 1) px++;   // an even length: its centre on a pixel boundary like the sprites'
            float len = px / Ppu;
            sr.sprite = s;
            sr.size = new Vector2(4f / Ppu, len);
            sr.enabled = true;
            sr.transform.position = new Vector3(bottom.x, bottom.y + len * 0.5f, 0f);
        }

        // ---- the sponge
        void UpdateSponge(float dt)
        {
            var h = held;
            bool onGlass = heldT > 0.05f && pointer.x > GlassL && pointer.x < GlassR && pointer.y > GlassB && pointer.y < GlassT;
            float removed = 0f;
            if (onGlass)
            {
                // (the pad's full-strength core reaches the glass's edges; its rim runs over the frame)
                pos.x = Mathf.Clamp(pos.x, GlassL + 9f / Ppu, GlassR - 9f / Ppu);
                pos.y = Mathf.Clamp(pos.y, GlassB + 5f / Ppu, GlassT - 5f / Ppu);
                if (!wiping)
                {
                    wiping = true;
                    lastStamp = pos;
                    removed += StampSponge(pos);
                }
                else
                {
                    float dist = (pos - lastStamp).magnitude;
                    int n = Mathf.CeilToInt(dist / (2f / Ppu));
                    for (int i = 1; i <= n; i++) removed += StampSponge(Vector2.Lerp(lastStamp, pos, i / (float)n));
                    if (n > 0) lastStamp = pos;
                }
                if (removed > 0f) AquaTank.MapsChanged();
            }
            else if (wiping) EndStroke();
            removedFrame = removed;
            float speed = vel.magnitude;
            var snapped = Snap(pos);
            h.sr.sprite = wiping ? (speed > 1.5f ? spongeWipe[1 + ((int)(heldT / 0.07f)) % 2] : spongeWipe[0]) : spongeHeld;
            h.sr.flipX = false;
            h.sr.transform.position = snapped;
            if (!wiping || speed < 1f) return;
            // squeaky clean: a squeak now and then (louder while the algae comes off), sparkles and a shine behind the pad
            var dir = vel.normalized;
            squeakT -= dt;
            if (squeakT <= 0f)
            {
                squeakT = Random.Range(0.1f, 0.2f);
                Sfx.Play(CleanSfx.Squeak, removed > 0.0005f ? 0.4f : 0.18f, 0.85f + 0.07f * Mathf.Min(speed, 6f) + Random.Range(-0.05f, 0.05f));
                Squeaks++;
            }
            sparkT -= dt;
            if (sparkT <= 0f)
            {
                sparkT = 0.09f;
                var p = pos - dir * (13f / Ppu) + new Vector2(Random.Range(-5f, 5f), Random.Range(-4f, 4f)) / Ppu;
                Fx.Frames(squeakS, Snap(p), 0.06f, 45, Color.white);
            }
            streakT -= dt;
            if (streakT <= 0f)
            {
                streakT = 0.32f;
                Fx.Frames(streakS, Snap(pos - dir * (18f / Ppu)), 0.08f, 36, Color.white, dir.x < 0f);
            }
        }

        /// <summary>The sponge's pad pressed on the glass here: each pixel under it keeps its level x (1 - the strongest alpha this stroke).</summary>
        float StampSponge(Vector2 p)
        {
            var a = AquaTank.AlgaeMap;
            int x0 = Mathf.FloorToInt((p.x - GlassL) * Ppu) - sbW / 2, y0 = Mathf.FloorToInt((p.y - GlassB) * Ppu) - sbH / 2;
            float removed = 0f;
            for (int by = 0; by < sbH; by++)
            {
                int ty = y0 + by;
                if (ty < 0 || ty >= AquaTank.AH) continue;
                for (int bx = 0; bx < sbW; bx++)
                {
                    float b = spongeBrush[by * sbW + bx];
                    if (b <= 0f) continue;
                    int tx = x0 + bx;
                    if (tx < 0 || tx >= AquaTank.AW) continue;
                    int i = ty * AquaTank.AW + tx;
                    float m = aM[i];
                    if (m >= b) continue;
                    if (m <= 0f)
                    {
                        aA0[i] = a[i];
                        aTouched.Add(i);
                    }
                    aM[i] = b;
                    float nv = aA0[i] * (1f - b);
                    removed += a[i] - nv;
                    a[i] = nv;
                }
            }
            Stamps++;
            return removed;
        }

        void EndStroke()
        {
            foreach (int i in aTouched) aM[i] = 0f;
            aTouched.Clear();
            wiping = false;
        }

        // ---- the net
        void UpdateNet(float dt)
        {
            var h = held;
            bool nowIn = pos.y - 2f / Ppu < box.surfaceY && Mathf.Abs(pos.x) < GlassR - 0.325f && pos.y > GlassB + 0.3f;
            if (nowIn != inWater && heldT > 0.08f)
            {
                // in / out through the surface
                Fx.Frames(splashS, new Vector2(Mathf.Round(pos.x * Ppu) / Ppu, SurfaceFx), 0.07f, 36, Color.white);
                Sfx.Play(AquaSfx.Splash, 0.3f, 1.35f);
                Splashes++;
            }
            inWater = nowIn;
            bool sweep = inWater && Mathf.Abs(vel.x) > 1.2f;
            if (vel.x < -0.6f) netFlip = true;
            else if (vel.x > 0.6f) netFlip = false;
            h.sr.sprite = sweep ? netSweep[((int)(heldT / 0.09f)) % 2] : netCount > 0 ? netFull : netHeld;
            h.sr.flipX = netFlip;
            var snapped = Snap(pos);
            h.sr.transform.position = snapped;
            DrawShaft(handle, snapped + new Vector2(0f, 1f), netHandleS);
            if (sweep)
            {
                swishT -= dt;
                if (swishT <= 0f)
                {
                    swishT = 0.34f;
                    Sfx.Play(CleanSfx.Swish, 0.28f, Random.Range(0.9f, 1.15f));
                }
            }
            // the bits the frame passes (along the move, not to skip one on a quick sweep)
            if (!inWater && pos.y > SurfaceFx + 0.45f) return;
            float dist = (pos - prevPos).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(dist / (3f / Ppu)));
            foreach (var b in bits)
            {
                if (b.caught || b.gone || b.fade < 0.3f) continue;
                for (int i = 1; i <= n; i++)
                {
                    var at = Vector2.Lerp(prevPos, pos, i / (float)n);
                    var l = (b.pos - at) * Ppu;
                    if (netFlip) l.x = -l.x;
                    bool hit = sweep ? l.x >= -3f && l.x <= 3f && l.y >= -7.5f && l.y <= 3.5f : l.x >= -6.5f && l.x <= 6.5f && l.y >= -7f && l.y <= 3f;
                    if (!hit) continue;
                    Catch(b);
                    break;
                }
            }
        }

        void Catch(Bit b)
        {
            b.caught = true;
            b.catchT = 0f;
            b.catchFrom = b.pos;
            b.sr.sortingOrder = 38;
            netCount++;
            Scooped++;
            AquaTank.Scoop();
            Sfx.Play(AquaSfx.Plink, 0.4f, Random.Range(0.7f, 0.9f));
            AquaCare.Log($"scoop {b.kind} at {b.pos.x:0.00} {b.pos.y:0.00}: in the net {netCount}, debris {Game.Data.tank.debris:0.000} ({AquaTank.DebrisCount(Game.Data)} bits)");
        }

        // ---- the siphon
        void UpdateSiphon(float dt)
        {
            var h = held;
            var mouth = pos + new Vector2(0f, -14f / Ppu);
            bool over = mouth.x > GlassL + 0.12f && mouth.x < GlassR - 0.12f && pointer.y < GlassT;
            float gt = AquaTank.GravelTop(mouth.x);
            if (over && mouth.y < gt - 2f / Ppu)
            {
                // it rests on the gravel (the mouth digs in 2 px)
                pos.y = gt - 2f / Ppu + 14f / Ppu;
                mouth.y = gt - 2f / Ppu;
            }
            bool now = over && mouth.y <= gt + 6f / Ppu;
            float removed = 0f;
            if (now && !working)
            {
                working = true;
                lastSiphonX = mouth.x;
                removed += StampSiphon(mouth.x);
            }
            else if (now)
            {
                float dist = Mathf.Abs(mouth.x - lastSiphonX);
                int n = Mathf.CeilToInt(dist / (2f / Ppu));
                for (int i = 1; i <= n; i++) removed += StampSiphon(Mathf.Lerp(lastSiphonX, mouth.x, i / (float)n));
                if (n > 0) lastSiphonX = mouth.x;
            }
            else if (working) EndDirtStroke();
            if (removed > 0f) AquaTank.MapsChanged();
            removedFrame = removed;
            var snapped = Snap(pos);
            h.sr.sprite = working ? siphonWork[((int)(heldT / 0.07f)) % 3] : siphonHeld;
            h.sr.flipX = false;
            h.sr.transform.position = snapped;
            DrawShaft(hose, snapped + new Vector2(0f, 1f), working ? hoseFlow[((int)(heldT / 0.08f)) % 4] : hoseS);
            suck.enabled = working;
            if (!working) return;
            suck.sprite = suckS[((int)(heldT / 0.08f)) % 3];
            Place(suck, snapped + new Vector2(0f, -20f / Ppu));
            gurgleT -= dt;
            if (gurgleT <= 0f)
            {
                gurgleT = 0.26f;
                Sfx.Play(CleanSfx.Gurgle, removed > 0.0001f ? 0.45f : 0.22f, Random.Range(0.9f, 1.1f));
            }
            if (removed > 0.0001f && Random.value < dt * 10f)
                Fx.Puff(mouth + new Vector2(0f, 2f / Ppu), new Color32(0x6a, 0x5a, 0x3a, 0xd0), 2, 0.35f, 38, 0.6f);
        }

        /// <summary>The siphon's mouth over the gravel at x: the dirt under it (the whole strip's height) is sucked away.</summary>
        float StampSiphon(float x)
        {
            var a = AquaTank.DirtMap;
            int x0 = Mathf.FloorToInt((x - GlassL) * Ppu) - pbW / 2;
            float removed = 0f;
            // (the brush is the 중형 수조's strip height: a deeper strip takes it stretched, row for row)
            int bh = AquaTank.BH, bw = AquaTank.BW;
            for (int ty = 0; ty < bh; ty++)
            for (int bx = 0; bx < pbW; bx++)
            {
                int by = pbH >= bh ? ty : Mathf.Min(pbH - 1, ty * pbH / bh);
                if (by >= pbH) continue;
                float b = siphonBrush[by * pbW + bx];
                if (b <= 0f) continue;
                int tx = x0 + bx;
                if (tx < 0 || tx >= bw) continue;
                int i = ty * bw + tx;
                float m = bM[i];
                if (m >= b) continue;
                if (m <= 0f)
                {
                    bA0[i] = a[i];
                    bTouched.Add(i);
                }
                bM[i] = b;
                float nv = bA0[i] * (1f - b);
                removed += a[i] - nv;
                a[i] = nv;
            }
            SiphonStamps++;
            return removed;
        }

        void EndDirtStroke()
        {
            foreach (int i in bTouched) bM[i] = 0f;
            bTouched.Clear();
            working = false;
            if (suck != null) suck.enabled = false;
        }

        // ------------------------------------------------------------------ UI overlay
        void ShowHint(Vector2 world, string text, float time = 1.8f)
        {
            if (hint.text != text || hintTime <= 0.1f)
            {
                hint.text = text;
                UIKit.FitPlate(hint, hintPlate);
            }
            hintAt = new Vector2(world.x, Mathf.Min(world.y, box.surfaceY));
            hintTime = Mathf.Max(hintTime, time);
        }

        void UpdateUI(float dt)
        {
            hintTime -= dt;
            bool h = hintTime > 0f && !string.IsNullOrEmpty(hint.text);
            hintPlate.enabled = h;
            hint.enabled = h;
            if (h)
            {
                var c = ToCanvas(hintAt);
                c.x = Mathf.Clamp(c.x, hintPlate.rectTransform.sizeDelta.x * 0.5f + 8f, ui.rect.width - hintPlate.rectTransform.sizeDelta.x * 0.5f - 8f);
                hintPlate.rectTransform.anchoredPosition = c;
            }
            // while a tool works: how much is left ("이끼 42%", "찌꺼기 5개", "바닥 때 30%")
            string m = "";
            if (held != null && !back)
            {
                var t = Game.Data.tank;
                if (held.kind == TK.Sponge && wiping) m = $"이끼 {Mathf.CeilToInt(t.algae * 100f - 0.01f)}%";
                else if (held.kind == TK.Net && inWater) m = $"찌꺼기 {BitsAlive}개" + (netCount > 0 ? $" · 건짐 {netCount}" : "");
                else if (held.kind == TK.Siphon && working) m = $"바닥 때 {Mathf.CeilToInt(t.dirt * 100f - 0.01f)}%";
            }
            bool show = m != "";
            if (show && meter.text != m)
            {
                meter.text = m;
                UIKit.FitPlate(meter, meterPlate);
            }
            meterPlate.enabled = show;
            meter.enabled = show;
            if (show)
            {
                // over the tool, or under it when the top bar leaves no room (never over the tool itself)
                var rt = meterPlate.rectTransform;
                var c = ToCanvas(pos + new Vector2(0f, held.kind == TK.Sponge ? 1.1f : 1.6f));
                if (c.y + rt.sizeDelta.y > ui.rect.height - 150f)
                {
                    c = ToCanvas(pos - new Vector2(0f, held.kind == TK.Sponge ? 1.1f : 1.4f));
                    rt.pivot = new Vector2(0.5f, 1f);
                }
                else rt.pivot = new Vector2(0.5f, 0f);
                c.x = Mathf.Clamp(c.x, rt.sizeDelta.x * 0.5f + 8f, ui.rect.width - rt.sizeDelta.x * 0.5f - 8f);
                rt.anchoredPosition = c;
            }
        }
    }
}
