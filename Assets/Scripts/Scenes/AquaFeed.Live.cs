using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The live food on the cabinet ledge (Sprites/World/feed_tub_* / feed_cooler_*, Tools/Blender/fk_items.py aqualive):
    /// the shrimp tub and the sardine cooler stand beside the feed bags. A tap on the lid opens / shuts it (saved); a
    /// press on the open container picks up one shrimp / sardine, which dangles from the finger; let go over the tank it
    /// drops (falls, splashes, sinks with a drift, lies on the gravel, then dissolves: no refund), let go anywhere else it
    /// goes back. Only the fish whose diet has that food go for it (<see cref="TankFish"/>): the big ones surge to the
    /// surface and gulp it, mid-size ones grab it in mid-water, bottom feeders take it on the gravel; the others ignore
    /// it and a hungry one says what it likes. A container the player has none of is a faint dashed slot (tap: the shop).
    /// </summary>
    public partial class AquaFeed
    {
        // ---- anchors, sprite pixels from the centre, +y up (see the art notes in fk_items.py aqualive)
        static float BoxY => L.boxY;                            // a 40 px container's centre: its base 20 px down, on the ledge (-4)
        static float LedgeY => L.ledgeY;                        // (-5.25)
        static float[] BoxX => new[] { L.tubX, L.coolerX };     // the tub, the cooler (beside the bags; -5.5 / -3)
        /// <summary>The gravel line (a resting pellet's bottom row): resting pieces lie on it (-3.625 in the 중형 수조).</summary>
        public static float Gravel => AquaLayout.Active.gravelTopY;
        const float AjarTime = 0.12f;                           // the lid's in-between frame

        internal class Box
        {
            public FeedDef feed;
            public int index;
            public Vector2 home;
            public SpriteRenderer sr, shadow, slot, slotIcon;
            public bool shown;              // on the ledge this visit: owned when the scene opened, or bought since
            public float ajar;              // > 0: the lid is moving (its in-between frame)
            public float pop = -1f;         // >= 0: popping in (just bought)
            public bool hinted;             // "집어서 수조 위에 놓아요" was shown this visit
            public Image badge;
            public Text badgeText;
            public Sprite closedS, ajarS, openS, fewS, emptyS;
            public Vector4 body, lid, mouth;    // (x0, x1, y0, y1) px: the closed container, the open lid, the open container
            public Vector2 tapShut, tapOpen;    // px: a tap on the closed lid / the open lid (the autopilot)
            public Vector2 pick;                // px: where a piece comes out
            public Vector2 grip;                // px: the held sprite's grip point from its centre (under the finger)
            public Vector2[] bodyC;             // px: the held sprites' body centres (where the fall sprite takes over)
            public Sprite[] heldS, fallS, sinkS, biteS, dropS;
            public Sprite restS, glintS;
            public float sink, restTime, restHalf;
        }

        /// <summary>A shrimp or a sardine in the tank.</summary>
        public class Piece
        {
            public FeedDef feed;
            public Diet kind;
            public SpriteRenderer sr;
            public Vector2 pos, vel;
            public int state;               // 0 falling through the air, 1 sinking, 2 on the gravel, 3 in a fish's mouth
            public float t, phase, rest, fade, glintT, holdT;
            public bool dead, gulp;
            public TankFish claim;          // a fish surging up to gulp it: the others leave it
            public TankFish holder;         // the fish with it in its mouth
            internal Box box;
            public Vector2 Pos => pos;
        }

        class Held
        {
            public Box box;
            public SpriteRenderer sr;
            public Vector2 at, last, start, vel, centre, backFrom;
            public float t, backT;
            public int frame;
            public bool flip, back, moved;
        }

        readonly List<Box> boxes = new List<Box>();
        readonly List<Piece> pieces = new List<Piece>();
        Held held;
        Sprite[] gulpS;

        // ---- read by the test autopilot
        public int Drops { get; private set; }
        public int LiveEaten { get; private set; }
        public int Gulps { get; private set; }
        public int Grabs { get; private set; }
        public int Dissolved { get; private set; }
        public int PutBacks { get; private set; }
        public int LidTaps { get; private set; }
        public TankFish LastGrabBy { get; private set; }
        public float LastGulpTime { get; private set; } = -99f;
        public Vector2 LastGulpAt { get; private set; }
        public bool Holding => held != null && !held.back;
        /// <summary>The held sprite's centre (world).</summary>
        public Vector2 HeldAt => held != null ? held.centre : Vector2.zero;
        public int PiecesAlive => pieces.Count(p => !p.dead);
        public IEnumerable<Piece> LivePieces => pieces.Where(p => !p.dead);

        bool AnyBoxShown => boxes.Any(b => b.shown);
        /// <summary>No food on the ledge at all: the bag outline says "상점에서 사료를 사요" (the tub / cooler slots hide).</summary>
        bool NoFood => !AnyBoxShown && drag == null && returning == null && slots.All(s => s.state == BagState.None);

        Box BoxOf(string id) => boxes.FirstOrDefault(b => b.feed.id == id);
        public Vector2 BoxCentre(string id) => BoxOf(id)?.home ?? Vector2.zero;
        /// <summary>A point on the lid (the closed one, or the open one standing behind).</summary>
        public Vector2 LidPoint(string id)
        {
            var b = BoxOf(id);
            if (b == null) return Vector2.zero;
            return b.home + (AquaCare.Stock(id).open ? b.tapOpen : b.tapShut) / Ppu;
        }
        public Vector2 PickPoint(string id)
        {
            var b = BoxOf(id);
            return b != null ? b.home + b.pick / Ppu : Vector2.zero;
        }
        /// <summary>Hidden / Slot (the faint dashed slot) / Closed / Ajar / Open.</summary>
        public string LiveState(string id)
        {
            var b = BoxOf(id);
            if (b == null) return "None";
            if (!b.shown) return b.slot.enabled ? "Slot" : "Hidden";
            if (b.ajar > 0f) return "Ajar";
            return AquaCare.Stock(id).open ? "Open" : "Closed";
        }
        /// <summary>The sprite on the ledge (closed / ajar / open / few / empty).</summary>
        public string BoxSprite(string id) => BoxOf(id)?.sr.sprite != null ? BoxOf(id).sr.sprite.name : "";
        /// <summary>The count on the container's badge.</summary>
        public string BadgeOf(string id) => BoxOf(id) is Box b && b.badge.enabled ? b.badgeText.text : "";

        // ------------------------------------------------------------------ setup
        void InitLive()
        {
            gulpS = Frames("feed_gulp_f", 5);
            for (int i = 0; i < AquaCare.Live.Count && i < BoxX.Length; i++) boxes.Add(MakeBox(AquaCare.Live[i], i));
        }

        Box MakeBox(FeedDef f, int i)
        {
            bool shrimp = f.kind == Diet.Shrimp;
            string k = "World/feed_" + f.box, p = "World/feed_" + f.key;
            var b = new Box
            {
                feed = f, index = i, home = new Vector2(BoxX[i], BoxY),
                closedS = Art.Get(k + "_closed"), ajarS = Art.Get(k + "_ajar"), openS = Art.Get(k + "_open"),
                fewS = Art.Get(k + "_few"), emptyS = Art.Get(k + "_empty"),
                // the art's rects, a pixel or two more generous: a tap anywhere on the closed container opens it, the
                // open lid standing behind shuts it, the open container below the lid gives a piece
                body = shrimp ? new Vector4(-11f, 11f, -20f, 0f) : new Vector4(-17f, 17f, -20f, 2f),
                lid = shrimp ? new Vector4(-10f, 10f, -1.8f, 18.5f) : new Vector4(-15.5f, 15.5f, -1.2f, 15.5f),
                mouth = shrimp ? new Vector4(-11f, 11f, -20f, -1.8f) : new Vector4(-17f, 17f, -20f, -1.2f),
                tapShut = shrimp ? new Vector2(0f, -6f) : new Vector2(0f, -3.5f),
                tapOpen = shrimp ? new Vector2(0f, 9f) : new Vector2(0f, 7f),
                pick = shrimp ? new Vector2(0f, -7.4f) : new Vector2(0f, -4.2f),
                grip = shrimp ? new Vector2(0f, 4f) : new Vector2(0f, 8f),
                bodyC = shrimp ? new[] { new Vector2(1.7f, 1.4f), new Vector2(2.1f, 1.7f) } : new[] { new Vector2(0f, 1.8f), new Vector2(-0.8f, 1.8f) },
                heldS = new[] { Art.Get(p + "_held_f0"), Art.Get(p + "_held_f1") },
                fallS = new[] { Art.Get(p + "_fall_f0"), Art.Get(p + "_fall_f1") },
                sinkS = new[] { Art.Get(p + "_sink_f0"), Art.Get(p + "_sink_f1") },
                restS = Art.Get(p + "_rest"),
                glintS = shrimp ? null : Art.Get(p + "_glint"),
                biteS = shrimp ? Frames("feed_shrimp_bite_f", 3) : null,
                dropS = shrimp ? Frames("feed_drop_s_f", 4) : Frames("feed_drop_l_f", 5),
                sink = shrimp ? 0.55f : 0.42f,
                restTime = shrimp ? 7f : 8f,
                restHalf = (shrimp ? 10f : 12f) * 0.5f / Ppu,
                shown = AquaCare.Stock(f.id).pieces > 0,
            };
            b.shadow = NewSprite("BoxShadow_" + f.key, Art.Get(k + "_shadow"), 44);
            b.shadow.transform.position = Snap(new Vector2(b.home.x, LedgeY)); // (centred on the ledge line)
            b.sr = NewSprite("Box_" + f.key, b.closedS, 46);
            Place(b.sr, b.home);
            // not owned: a faint dashed slot with the food's icon (tap: the shop)
            b.slot = NewSprite("BoxSlot_" + f.key, shrimp ? MakeSlotSprite(24, 40, 2, 21, 2, 21) : MakeSlotSprite(36, 40, 1, 34, 2, 23), 44);
            b.slot.color = new Color(1f, 0.96f, 0.86f, 0.55f);
            Place(b.slot, b.home);
            b.slotIcon = NewSprite("BoxSlotIcon_" + f.key, Art.UI(AquaCare.KindIcon(f.kind)), 44);
            b.slotIcon.color = new Color(1f, 1f, 1f, 0.45f);
            Place(b.slotIcon, b.home + new Vector2(0f, shrimp ? -8f : -7f) / Ppu);
            // the count of pieces over it
            b.badge = UIKit.Panel(ui, "badge", null, "BoxBadge_" + f.key);
            b.badge.raycastTarget = false;
            b.badge.rectTransform.anchorMin = b.badge.rectTransform.anchorMax = Vector2.zero;
            b.badge.rectTransform.pivot = new Vector2(0.5f, 0f);
            b.badge.rectTransform.sizeDelta = new Vector2(38, 26);
            b.badgeText = UIKit.Label(b.badge.transform, "", 16, UIKit.Ink, TextAnchor.MiddleCenter, false);
            b.badgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            b.badgeText.rectTransform.Fill(0, 0, 2, 4);
            return b;
        }

        /// <summary>A dashed outline of the closed container's footprint (texture px, inclusive).</summary>
        static Sprite MakeSlotSprite(int w, int h, int x0, int x1, int y0, int y1)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = (x == x0 || x == x1) && y >= y0 && y <= y1 || (y == y0 || y == y1) && x >= x0 && x <= x1;
                bool corner = (x == x0 || x == x1) && (y == y0 || y == y1);
                bool dash = ((x + y) / 2) % 2 == 0;
                t.SetPixel(x, y, edge && !corner && dash ? Color.white : clear);
            }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16);
        }

        static bool In(Vector2 l, Vector4 r) => l.x >= r.x && l.x <= r.y && l.y >= r.z && l.y <= r.w;

        /// <summary>Pieces in it now (the one in the hand not counted).</summary>
        int Count(Box b) => Mathf.Max(0, AquaCare.Stock(b.feed.id).pieces - (held != null && held.box == b ? 1 : 0));

        // ------------------------------------------------------------------ input
        bool HitsBox(Vector2 w)
        {
            bool noFood = NoFood;
            foreach (var b in boxes)
            {
                var l = (w - b.home) * Ppu;
                if (!b.shown) { if (!noFood && In(l, b.body)) return true; }
                else if (In(l, b.body) || In(l, b.lid) && AquaCare.Stock(b.feed.id).open) return true;
            }
            return false;
        }

        /// <summary>A press on a container: the lid, a piece, or (a faint slot) the shop. False: not on one.</summary>
        bool PressLive(Vector2 w)
        {
            if (held != null) return true;
            bool noFood = NoFood;
            foreach (var b in boxes)
            {
                var l = (w - b.home) * Ppu;
                if (!b.shown)
                {
                    if (noFood || !In(l, b.body)) continue;
                    Sfx.Play(Sfx.Click);
                    AquaCare.Log($"slot {b.feed.id}: to the shop");
                    ShopUI.TankFeed = true;
                    ShopUI.Focus = b.feed.id;
                    ShopUI.Open(ui, ItemKind.Tank);
                    return true;
                }
                if (!AquaCare.Stock(b.feed.id).open)
                {
                    if (!In(l, b.body)) continue;
                    SetLid(b, true);
                    return true;
                }
                if (In(l, b.lid))
                {
                    SetLid(b, false);
                    return true;
                }
                if (In(l, b.mouth))
                {
                    PickUp(b, w);
                    return true;
                }
            }
            return false;
        }

        void SetLid(Box b, bool open)
        {
            var st = AquaCare.Stock(b.feed.id);
            st.open = open;
            b.ajar = AjarTime;
            LidTaps++;
            Sfx.Play(AquaSfx.Lid, 0.7f, open ? 1.1f : 0.85f);
            Game.I.Save();
            AquaCare.Log($"lid {b.feed.id}: {(open ? "open" : "shut")}, {st.pieces} left");
            if (open && !b.hinted && st.pieces > 0)
            {
                b.hinted = true;
                StartCoroutine(HintLater(0.3f, b.home + new Vector2(0f, 1.7f), b.feed.kind == Diet.Shrimp ? "새우를 집어서 수조 위에 놓아요" : "정어리를 집어서 수조 위에 놓아요"));
            }
        }

        void PickUp(Box b, Vector2 w)
        {
            if (Count(b) <= 0)
            {
                ShowHint(b.home + new Vector2(0f, 1.7f), (b.feed.kind == Diet.Shrimp ? "새우" : "정어리") + "가 없어요 · 상점에서 사요");
                Sfx.Play(Sfx.Click, 0.6f);
                return;
            }
            held = new Held { box = b, at = w, last = w, start = w };
            held.sr = NewSprite("Held_" + b.feed.key, b.heldS[0], 53);
            PlaceHeld(held);
            Sfx.Play(AquaSfx.Plink, 0.35f, 0.7f);
            AquaCare.Log($"pick {b.feed.id}: {Count(b)} left in the {b.feed.box}");
        }

        /// <summary>Where the held piece's body is (world): the fall sprite takes over there.</summary>
        static Vector2 BodyAt(Held h)
        {
            var c = h.box.bodyC[h.frame];
            return h.centre + new Vector2(h.flip ? -c.x : c.x, c.y) / Ppu;
        }

        void PlaceHeld(Held h)
        {
            var half = pv.ViewSize * 0.5f;
            var c = h.at - h.box.grip / Ppu; // (the grip point under the finger)
            c.x = Mathf.Clamp(c.x, -half.x + 0.5f, half.x - 0.5f);
            c.y = Mathf.Clamp(c.y, -half.y + 0.8f, half.y - 0.8f);
            h.centre = c;
            h.sr.sprite = h.box.heldS[h.frame];
            h.sr.flipX = h.flip;
            Place(h.sr, c);
        }

        void UpdateHeld(float dt)
        {
            var h = held;
            if (h == null) return;
            if (h.back)
            {
                // back into its container
                h.backT += dt / 0.2f;
                float k = Mathf.Clamp01(h.backT);
                var to = h.box.home + h.box.pick / Ppu;
                h.centre = Vector2.Lerp(h.backFrom, to, 1f - (1f - k) * (1f - k));
                h.sr.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
                Place(h.sr, h.centre);
                if (h.backT >= 1f)
                {
                    Destroy(h.sr.gameObject);
                    held = null;
                    Sfx.Play(AquaSfx.Plink, 0.3f, 0.6f);
                }
                return;
            }
            h.t += dt;
            var step = h.at - h.last;
            h.last = h.at;
            h.vel = Vector2.Lerp(h.vel, step / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 12f));
            if ((h.at - h.start).magnitude > 0.5f) h.moved = true;
            // the dangle: the body swings behind a quick move, otherwise the shrimp wriggles / the sardine sways
            if (Mathf.Abs(h.vel.x) > 2.2f)
            {
                h.frame = 1;
                h.flip = (h.box.bodyC[1].x > 0f) == (h.vel.x > 0f);
            }
            else h.frame = ((int)(h.t / (h.box.feed.kind == Diet.Shrimp ? 0.22f : 0.4f))) % 2;
            PlaceHeld(h);
        }

        void ReleaseHeld()
        {
            var h = held;
            var body = BodyAt(h);
            bool over = body.x > TankMinX && body.x < TankMaxX && body.y > Gravel + 0.4f;
            if (!over)
            {
                PutBack((h.at - h.start).magnitude > 1.2f);
                return;
            }
            Destroy(h.sr.gameObject);
            held = null;
            DropPiece(h.box, body, h.vel, h.flip);
        }

        /// <summary>Let go off the tank (or a dialog opened): the piece goes back into its container.</summary>
        void PutBack(bool hint)
        {
            var h = held;
            h.back = true;
            h.backT = 0f;
            h.backFrom = h.centre;
            PutBacks++;
            if (hint) ShowHint(h.box.home + new Vector2(0f, 1.7f), "수조 위에서 놓아요");
            AquaCare.Log($"put back {h.box.feed.id}: {AquaCare.Stock(h.box.feed.id).pieces} left");
        }

        void DropPiece(Box b, Vector2 body, Vector2 vel, bool flip)
        {
            var st = AquaCare.Stock(b.feed.id);
            st.pieces = Mathf.Max(0, st.pieces - 1);
            Game.I.Save();
            bool air = body.y > box.surfaceY;
            var p = new Piece
            {
                feed = b.feed, kind = b.feed.kind, box = b, pos = body, state = air ? 0 : 1, phase = Random.Range(0f, 6.28f),
                vel = air ? Vector2.ClampMagnitude(vel * 0.3f, 2.5f) : new Vector2(0f, -0.4f),
                glintT = Random.Range(0.6f, 1.6f),
            };
            p.sr = NewSprite("Piece_" + b.feed.key, air ? b.fallS[0] : b.sinkS[0], air ? 51 : 32);
            p.sr.flipX = flip;
            Place(p.sr, p.pos);
            pieces.Add(p);
            Drops++;
            AquaCare.Log($"drop {b.feed.id} at {body.x:0.00} {body.y:0.00} ({(air ? "from above" : "into the water")}): {st.pieces} left");
            if (!air) OnSplash(p);
        }

        // ------------------------------------------------------------------ pieces in the tank
        void UpdateLive(float dt)
        {
            bool noFood = NoFood;
            foreach (var b in boxes) DrawBox(b, dt, noFood);
            UpdateHeld(dt);
            UpdatePieces(dt);
        }

        void UpdatePieces(float dt)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var p = pieces[i];
                if (p.dead)
                {
                    if (p.sr != null) Destroy(p.sr.gameObject);
                    pieces.RemoveAt(i);
                    continue;
                }
                p.t += dt;
                var b = p.box;
                bool shrimp = p.kind == Diet.Shrimp;
                switch (p.state)
                {
                    case 0:
                        p.vel.y -= 16f * dt;
                        p.pos += p.vel * dt;
                        p.pos.x = Mathf.Clamp(p.pos.x, TankMinX, TankMaxX);
                        // the shrimp tumbles; the sardine goes in nose first, then levels out
                        p.sr.sprite = shrimp ? b.fallS[((int)(p.t / 0.09f)) % 2] : b.fallS[p.t < 0.14f ? 0 : 1];
                        if (p.pos.y <= box.surfaceY)
                        {
                            p.pos.y = box.surfaceY - 0.05f;
                            p.state = 1;
                            p.t = 0f;
                            p.sr.sortingOrder = 32;
                            p.vel = new Vector2(p.vel.x * 0.2f, -Mathf.Min(2.2f, Mathf.Abs(p.vel.y) * 0.35f + 0.6f));
                            Fx.Frames(b.dropS, new Vector2(Mathf.Round(p.pos.x * Ppu) / Ppu, SurfaceFx), 0.07f, 36, Color.white);
                            Sfx.Play(AquaSfx.Splash, shrimp ? 0.45f : 0.7f, shrimp ? 1.25f : 0.95f);
                            OnSplash(p);
                        }
                        break;
                    case 1:
                        float drift = Mathf.Sin(p.t * 1.7f + p.phase) * 0.18f;
                        p.vel.y = Mathf.MoveTowards(p.vel.y, -b.sink, 2.2f * dt);
                        p.vel.x = Mathf.Lerp(p.vel.x, drift, 1f - Mathf.Exp(-dt * 2f));
                        p.pos += p.vel * dt;
                        p.pos.x = Mathf.Clamp(p.pos.x, TankMinX, TankMaxX);
                        p.sr.sprite = b.sinkS[((int)(p.t / (shrimp ? 0.15f : 0.32f))) % 2];
                        if (b.glintS != null)
                        {
                            // now and then a flash of silver on the sardine's flank
                            p.glintT -= dt;
                            if (p.glintT <= -0.12f) p.glintT = Random.Range(1.2f, 2.4f);
                            if (p.glintT <= 0f) p.sr.sprite = b.glintS;
                        }
                        if (p.pos.y - 7f / Ppu <= Gravel)
                        {
                            p.state = 2;
                            p.pos.y = Gravel + b.restHalf; // (the rest sprite's bottom row on the gravel)
                            p.rest = b.restTime;
                            p.sr.sprite = b.restS;
                        }
                        break;
                    case 2:
                        p.sr.sprite = b.restS;
                        p.rest -= dt;
                        if (p.rest <= 0f)
                        {
                            // left uneaten: it fades and melts into a little cloud on the gravel
                            p.fade += dt / 0.6f;
                            p.sr.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(p.fade));
                            if (p.fade >= 1f)
                            {
                                Fx.Frames(dissolveS, Snap(p.pos + new Vector2(0f, 1f / Ppu)), 0.15f, 33, p.feed.food);
                                p.dead = true;
                                Dissolved++;
                                AquaTank.OnUneaten(p.pos, true);
                                AquaCare.Log($"dissolved {p.feed.id} (uneaten)");
                            }
                        }
                        break;
                    default:
                        // in a fish's mouth: a grabbing fish eats it in a moment, a surging one at the surface
                        if (p.holder == null)
                        {
                            p.state = 1;
                            break;
                        }
                        p.pos = p.holder.Mouth;
                        p.sr.sprite = b.sinkS[0];
                        p.sr.flipX = !p.holder.FacingLeft;
                        if (!p.gulp)
                        {
                            p.holdT -= dt;
                            if (p.holdT <= 0f) EatPiece(p.holder, p, false);
                        }
                        break;
                }
                if (!p.dead) Place(p.sr, p.pos);
            }
        }

        /// <summary>A piece reached the water: who wants it is on the way; a hungry fish that wants something else says so.</summary>
        void OnSplash(Piece p)
        {
            if (fish.Count == 0)
            {
                ShowHint(p.pos + new Vector2(0f, 1.2f), "물고기가 없어요");
                return;
            }
            int n = 0, full = 0;
            foreach (var f in fish)
                if (f != null && AquaCare.Eats(f.Data, p.kind))
                {
                    n++;
                    if (AquaCare.Full(f.Data)) full++;
                }
            var other = HungryNonEater(p.kind, p.pos);
            if (other != null) FoodHint(other);
            else if (n > 0 && full == n) ShowHint(p.pos + new Vector2(0f, 1.2f), "배불러요!");
            else if (n == 0)
            {
                var nf = NearestFish(p.pos);
                if (nf != null) FoodHint(nf);
            }
            AquaCare.Log($"splash {p.feed.id} at {p.pos.x:0.00}: {n} eat it ({full} full){(other != null ? ", hungry " + other.Data.speciesId + " wants " + AquaCare.DietText(AquaCare.DietOf(other.Data)) : "")}");
        }

        // ------------------------------------------------------------------ the fish's side (TankFish)
        /// <summary>
        /// The piece this fish goes for: the nearest one of a food it eats that no other fish has in its mouth or is
        /// surging for (null = none). Only the surging kind sees one coming through the air.
        /// </summary>
        public Piece PieceFor(TankFish tf)
        {
            if (pieces.Count == 0) return null;
            Piece best = null;
            float bd = float.MaxValue;
            bool surge = tf.Style == FeedStyle.Surge;
            foreach (var p in pieces)
            {
                if (p.dead || p.state == 3 || p.claim != null && p.claim != tf) continue;
                if (p.state == 0 && !surge) continue;
                if (!AquaCare.Eats(tf.Data, p.kind)) continue;
                float d = (p.pos - tf.Pos).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = p;
                }
            }
            return best;
        }

        public void Claim(Piece p, TankFish tf)
        {
            if (p != null && p.claim == null) p.claim = tf;
        }

        public void Unclaim(Piece p, TankFish tf)
        {
            if (p == null) return;
            if (p.claim == tf) p.claim = null;
            if (p.state == 3 && p.holder == tf)
            {
                p.state = 1;
                p.holder = null;
            }
        }

        /// <summary>A fish's mouth reached it in mid-water or on the gravel: held a moment, then eaten.</summary>
        public void Grab(TankFish tf, Piece p)
        {
            if (p == null || p.dead || p.state == 3) return;
            p.state = 3;
            p.holder = tf;
            p.holdT = 0.14f;
            p.gulp = false;
            p.claim = tf;
            p.sr.sortingOrder = 32;
            LastGrabBy = tf;
            Grabs++;
            Sfx.Play(AquaSfx.Tick, 0.4f, 0.7f);
        }

        /// <summary>A surging fish took it on the way up: it rides in the mouth to the surface.</summary>
        public void Take(TankFish tf, Piece p)
        {
            if (p == null || p.dead || p.state == 3) return;
            p.state = 3;
            p.holder = tf;
            p.gulp = true;
            p.claim = tf;
        }

        /// <summary>The surging fish broke the surface with it: one gulp and a splash (drawn over the front frame).</summary>
        public void Gulp(TankFish tf, Piece p) => EatPiece(tf, p, true);

        void EatPiece(TankFish tf, Piece p, bool gulp)
        {
            if (p == null || p.dead || tf == null) return;
            p.dead = true;
            var b = p.box;
            bool wasFull = AquaCare.Full(tf.Data);
            AquaCare.Eat(Game.Data, tf.Data, p.feed);
            LiveEaten++;
            LastEatTime = Time.time;
            LastEatAt = tf.Mouth;
            LastEatKind = p.kind;
            if (gulp)
            {
                // (the air gap over the water is only 6 px: the spray rises over the rim, so it goes over the front frame)
                var at = new Vector2(Mathf.Round(tf.Mouth.x * Ppu) / Ppu, SurfaceFx);
                Fx.Frames(gulpS, at, 0.07f, 42, Color.white);
                Sfx.Play(AquaSfx.Gulp, 0.85f, Random.Range(0.9f, 1.05f));
                Gulps++;
                LastGulpTime = Time.time;
                LastGulpAt = at;
            }
            else
            {
                if (b.biteS != null) Fx.Frames(b.biteS, Snap(tf.Mouth), 0.08f, 34, Color.white);
                else Fx.Frames(crumbS, Snap(tf.Mouth), 0.08f, 34, p.feed.food);
                Sfx.Play(AquaSfx.Nom, 0.55f, Random.Range(0.75f, 0.9f));
            }
            if (!wasFull && AquaCare.Full(tf.Data)) GotFull(tf, gulp ? LiveCheerDelay : 0f);
            Game.I.Save();
            AquaCare.Log($"{(gulp ? "gulp" : "eat")} {p.feed.id} by {tf.Data.speciesId} [{tf.Style}] at {tf.Mouth.x:0.00} {tf.Mouth.y:0.00}: fullness {tf.Data.fullness:0.00}");
        }

        // ------------------------------------------------------------------ the containers at rest
        void DrawBox(Box b, float dt, bool noFood)
        {
            var st = AquaCare.Stock(b.feed.id);
            if (!b.shown && st.pieces > 0)
            {
                // bought while here: it pops in (shut)
                b.shown = true;
                b.pop = 0f;
            }
            bool slot = !b.shown && !noFood;
            b.slot.enabled = slot;
            b.slotIcon.enabled = slot;
            if (!b.shown)
            {
                b.sr.enabled = false;
                b.shadow.enabled = false;
                return;
            }
            if (b.ajar > 0f) b.ajar -= dt;
            int n = Count(b);
            b.sr.sprite = b.ajar > 0f ? b.ajarS : !st.open ? b.closedS : n >= 2 ? b.openS : n == 1 ? b.fewS : b.emptyS;
            float scale = 1f, alpha = 1f;
            if (b.pop >= 0f)
            {
                b.pop += dt;
                float k = Mathf.Clamp01(b.pop / 0.22f);
                scale = Mathf.LerpUnclamped(0.6f, 1f, Tween.BackOut(k));
                alpha = k;
                if (k >= 1f) b.pop = -1f;
            }
            b.sr.enabled = true;
            b.sr.color = new Color(1f, 1f, 1f, alpha);
            b.sr.transform.localScale = new Vector3(scale, scale, 1f);
            Place(b.sr, b.home + new Vector2(0f, (scale - 1f) * -20f / Ppu));
            b.shadow.enabled = true;
            b.shadow.color = new Color(1f, 1f, 1f, alpha);
        }

        void UpdateLiveUI()
        {
            foreach (var b in boxes)
            {
                bool show = b.shown && b.pop < 0f;
                b.badge.enabled = show;
                b.badgeText.enabled = show;
                if (!show) continue;
                bool open = AquaCare.Stock(b.feed.id).open || b.ajar > 0f;
                b.badgeText.text = Count(b).ToString();
                // over the lid: the closed one, or the open one standing behind
                float top = open ? (b.feed.kind == Diet.Shrimp ? 19f : 16f) : b.body.w + 1f;
                b.badge.rectTransform.anchoredPosition = ToCanvas(b.home + new Vector2(0f, top) / Ppu);
            }
        }
    }
}
