using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// The tank's decorations (Sprites/World/decor_*, Tools/Blender/variants/hybrid/hyb_aquadecor.py) standing in their
    /// slots (<see cref="AquaTank.Slots"/>): plants sway, the water wheel turns, the bubbler sends up a stream of bubbles,
    /// the treasure chest opens now and then with a burst of bubbles, the light hangs over the hood and tints the water
    /// (its glow over the tank; the fish and the decorations take its colour). The decoration mode (the 꾸미기 button, or
    /// a long press on a decoration): every slot shows its dashed marker (locked ones grey), a storage tray of the owned
    /// decorations not standing anywhere slides in at the bottom; drag one from the tray onto a slot of its kind (the
    /// valid ones light up, the one under it glows, the item snaps there), a placed one onto another slot (they swap) or
    /// off the slots (back to storage). 완료 leaves the mode.
    /// </summary>
    public class AquaDecor : MonoBehaviour
    {
        public static AquaDecor Current { get; private set; }

        /// <summary>The decoration mode is on: the feed and the cleaning tools ignore the pointer, fish taps wait.</summary>
        public static bool ModeOn { get; private set; }

        /// <summary>The mode was entered / left (the scene hides / shows its bottom panels).</summary>
        public event System.Action ModeChanged;

        const float Ppu = 16f;
        // the tank in use (AquaLayout; the 중형 수조's values in the comments)
        static float SurfaceFx => AquaLayout.Active.surfaceFx;       // 5.1875
        static Vector2 LightAt => AquaLayout.Active.LightAt;         // the fixture's centre (its bottom on the hood cap: 0, 7.875)
        static Vector2 GlowAt => AquaLayout.Active.GlowAt;           // the glow over the glass opening (0, 0.5)

        class Placed
        {
            public DecorSlot slot;
            public DecorDef def;
            public SpriteRenderer sr;
            public Sprite[] frames;
            public float t, phase;
            public int chest;           // 0 shut, 1 opening, 2 open, 3 closing
            public float chestT, nextOpen, bubbleT;
            public int frame;
        }

        class Bubble
        {
            public SpriteRenderer sr;
            public Vector2 pos;
            public float x0, t, phase, speed, popT;
            public bool pop;
        }

        PixelView pv;
        AquariumData box;
        RectTransform ui;
        Transform root;
        readonly Dictionary<string, Placed> placed = new Dictionary<string, Placed>();
        readonly List<Bubble> bubbles = new List<Bubble>();
        SpriteRenderer fixture, glow;
        Sprite[] bubbleS, popS, burstS;
        Sprite chestShut, chestOpening, chestOpen;

        // ---- the decoration mode
        readonly Dictionary<string, SpriteRenderer> markers = new Dictionary<string, SpriteRenderer>();
        Image tray;
        Text trayTitle, trayEmpty;
        Button prevBtn, nextBtn;
        int page, perPage = 6, ownedSeen = -1;
        class TrayItem
        {
            public DecorDef def;
            public RectTransform rt;
            public Image slot, icon;
            public Text name;
        }
        readonly List<TrayItem> trayItems = new List<TrayItem>();
        // drag
        DecorDef dragDef;
        string dragFrom, hover;
        SpriteRenderer ghost;
        Vector2 dragPointer;
        // a long press on a decoration outside the mode
        string pressSlot;
        float pressT;
        Vector2 pressAt;

        // ---- read by the test autopilot
        public int Drops { get; private set; }
        public int Swaps { get; private set; }
        public int Removes { get; private set; }
        public int Rejects { get; private set; }
        public int ChestOpens { get; private set; }
        public bool Dragging => dragDef != null;
        public string HoverSlot => hover;
        public int MarkersShown => markers.Values.Count(m => m != null && m.enabled);
        public int BubblesAlive => bubbles.Count;
        public int TrayCount => TrayDefs().Count;
        public int WheelFrame => placed.Values.Where(p => p.def.wheel).Select(p => p.frame).FirstOrDefault();
        public string ChestState => placed.Values.Where(p => p.def.chest).Select(p => p.chest == 2 ? "Open" : p.chest == 0 ? "Shut" : "Moving").FirstOrDefault() ?? "None";
        public string SpriteIn(string slot) => placed.TryGetValue(slot, out var p) && p.sr.sprite != null ? p.sr.sprite.name : "";
        public Color ColourIn(string slot) => placed.TryGetValue(slot, out var p) ? p.sr.color : Color.clear;
        public bool LightShown => fixture != null && fixture.enabled;

        public void Init(PixelView view, AquariumData data, RectTransform canvas)
        {
            Current = this;
            pv = view;
            box = data;
            ui = canvas;
            root = transform;
            bubbleS = new[] { Art.Get("World/decor_bubble_0"), Art.Get("World/decor_bubble_1"), Art.Get("World/decor_bubble_2"), Art.Get("World/decor_bubble_3") };
            popS = new[] { Art.Get("World/decor_bubble_pop_f0"), Art.Get("World/decor_bubble_pop_f1") };
            burstS = new[] { Art.Get("World/decor_chest_burst_f0"), Art.Get("World/decor_chest_burst_f1"), Art.Get("World/decor_chest_burst_f2"), Art.Get("World/decor_chest_burst_f3") };
            chestShut = Art.Get("World/decor_chest_closed");
            chestOpening = Art.Get("World/decor_chest_opening");
            chestOpen = Art.Get("World/decor_chest_open");
            fixture = NewSprite("LightFixture", null, 42);
            glow = NewSprite("LightGlow", null, 39);
            Sync();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (ModeOn)
            {
                ModeOn = false;
                AquaFeed.Suspended = false;
            }
        }

        SpriteRenderer NewSprite(string name, Sprite s, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x * Ppu) / Ppu, Mathf.Round(v.y * Ppu) / Ppu);

        Vector2 ToCanvas(Vector2 world) => pv.WorldToScreen(world) * UIKit.CanvasPerScreenPx;

        static Sprite[] FramesOf(DecorDef def)
        {
            if (def.kind == DecorKind.Light) return new[] { Art.Get("World/decor_" + def.key) };
            if (def.chest) return new[] { Art.Get("World/decor_chest_closed") };
            if (def.frames <= 0) return new[] { Art.Get("World/decor_" + def.key) };
            var a = new Sprite[def.frames];
            for (int i = 0; i < a.Length; i++) a[i] = Art.Get($"World/decor_{def.key}_f{i}");
            return a;
        }

        /// <summary>Where an item stands in a slot (its sprite centre): a floor item's bottom edge on the base line.</summary>
        public static Vector2 CentreIn(DecorSlot s, DecorDef def)
        {
            if (s.kind == DecorKind.Light) return LightAt;
            return s.Base + new Vector2(0f, (def != null ? def.h : s.maxH) * 0.5f / Ppu);
        }

        /// <summary>The world centre of a slot for an item (the autopilot's drop point).</summary>
        public Vector2 SlotCentre(string slot, string item) => CentreIn(AquaTank.Slot(slot), AquaTank.Decor(item));

        // ------------------------------------------------------------------ what stands where
        /// <summary>Brings the sprites in line with the saved placement.</summary>
        public void Sync()
        {
            var d = Game.Data;
            AquaTank.Ensure(d);
            var want = new Dictionary<string, DecorDef>();
            foreach (var s in AquaTank.Slots)
            {
                if (!AquaTank.Unlocked(s)) continue;
                var def = AquaTank.Decor(AquaTank.ItemAt(s.id));
                if (def != null && s.kind != DecorKind.Light) want[s.id] = def;
            }
            foreach (var id in placed.Keys.ToList())
                if (!want.TryGetValue(id, out var def) || def != placed[id].def)
                {
                    Destroy(placed[id].sr.gameObject);
                    placed.Remove(id);
                }
            foreach (var kv in want)
            {
                if (placed.ContainsKey(kv.Key)) continue;
                var s = AquaTank.Slot(kv.Key);
                var p = new Placed { slot = s, def = kv.Value, frames = FramesOf(kv.Value), phase = Random.value * 4f, nextOpen = Random.Range(4f, 9f) };
                p.sr = NewSprite("Decor_" + kv.Key, p.frames[0], s.order);
                p.sr.transform.position = Snap(CentreIn(s, p.def));
                placed[kv.Key] = p;
            }
            var light = AquaTank.Decor(AquaTank.ItemAt("light"));
            fixture.enabled = glow.enabled = light != null;
            if (light != null)
            {
                fixture.sprite = Art.Get("World/decor_" + light.key);
                glow.sprite = Art.Get($"World/decor_{light.key}_glow");
                fixture.transform.position = LightAt;
                glow.transform.position = GlowAt;
                // (the glow is made for the 436 x 164 glass: a soft light, stretched over this tank's)
                var gs = AquaLayout.Active.GlassScale;
                glow.transform.localScale = new Vector3(gs.x, gs.y, 1f);
            }
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            float dt = Time.deltaTime;
            var tint = AquaTank.DecorTint;
            foreach (var p in placed.Values)
            {
                p.t += dt;
                bool lifted = dragFrom == p.slot.id && dragDef != null;
                p.sr.enabled = !lifted;
                p.sr.color = hover == p.slot.id && dragDef != null && dragFrom != p.slot.id ? new Color(tint.r, tint.g, tint.b, 0.45f) : tint;
                if (p.def.chest) UpdateChest(p, dt);
                else if (p.frames.Length > 1)
                {
                    int f = ((int)((p.t + p.phase) * p.def.fps)) % p.frames.Length;
                    if (f != p.frame || p.sr.sprite == null)
                    {
                        p.frame = f;
                        p.sr.sprite = p.frames[f];
                    }
                }
                if (p.def.bubbler && !lifted)
                {
                    // a stream out of the diver's helmet
                    p.bubbleT -= dt;
                    if (p.bubbleT <= 0f)
                    {
                        p.bubbleT = Random.Range(0.08f, 0.17f);
                        SpawnBubble(p.slot.Base + new Vector2(-2f, 27f) / Ppu, Random.value < 0.6f ? Random.Range(0, 2) : Random.Range(1, 4));
                    }
                }
            }
            if (glow.enabled)
            {
                var l = AquaTank.LookLight;
                // the neon tubes hum a little
                float a = l != null && l.key.StartsWith("light_neon") ? 0.93f + 0.07f * Mathf.Sin(Time.time * 7.3f) * Mathf.Sin(Time.time * 2.1f) : 1f;
                glow.color = new Color(1f, 1f, 1f, a);
                fixture.enabled = !(dragFrom == "light" && dragDef != null);
            }
            UpdateBubbles(dt, tint);
            if (ModeOn) UpdateMode(dt);
            else UpdateLongPress();
        }

        void UpdateChest(Placed p, float dt)
        {
            p.chestT -= dt;
            switch (p.chest)
            {
                case 0:
                    p.nextOpen -= dt;
                    if (p.nextOpen <= 0f)
                    {
                        p.chest = 1;
                        p.chestT = 0.16f;
                    }
                    break;
                case 1:
                    if (p.chestT <= 0f)
                    {
                        p.chest = 2;
                        p.chestT = 1.9f;
                        ChestBurst(p);
                    }
                    break;
                case 2:
                    if (p.chestT <= 0f)
                    {
                        p.chest = 3;
                        p.chestT = 0.16f;
                    }
                    break;
                default:
                    if (p.chestT <= 0f)
                    {
                        p.chest = 0;
                        p.nextOpen = Random.Range(10f, 18f);
                    }
                    break;
            }
            p.sr.sprite = p.chest == 0 ? chestShut : p.chest == 2 ? chestOpen : chestOpening;
        }

        /// <summary>The lid flips up: a burst of bubbles out of the mouth (0, 14.3 px over the base).</summary>
        void ChestBurst(Placed p)
        {
            ChestOpens++;
            var mouth = p.slot.Base + new Vector2(0f, 14f / Ppu);
            Fx.Frames(burstS, Snap(mouth + new Vector2(0f, 24f / Ppu)), 0.09f, 8, AquaTank.DecorTint);
            for (int i = 0; i < 7; i++) SpawnBubble(mouth + new Vector2(Random.Range(-5f, 5f), Random.Range(0f, 4f)) / Ppu, Random.Range(0, 4), Random.Range(0f, 0.4f));
            Sfx.Play(Sfx.Bubble, 0.5f, 1.2f);
            AquaCare.Log($"chest opens ({ChestOpens})");
        }

        /// <summary>Opens the treasure chest now (the autopilot's shot).</summary>
        public void OpenChestNow()
        {
            foreach (var p in placed.Values)
                if (p.def.chest && p.chest == 0) p.nextOpen = 0f;
        }

        void SpawnBubble(Vector2 at, int size, float delay = 0f)
        {
            var b = new Bubble { pos = at, x0 = at.x, phase = Random.Range(0f, 6.28f), speed = Random.Range(1.4f, 2.3f) * (1f + size * 0.08f), t = -delay };
            b.sr = NewSprite("DecorBubble", bubbleS[Mathf.Clamp(size, 0, 3)], 20);
            b.sr.enabled = delay <= 0f;
            b.sr.transform.position = Snap(at);
            bubbles.Add(b);
        }

        void UpdateBubbles(float dt, Color tint)
        {
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                b.t += dt;
                if (b.t < 0f) continue;
                b.sr.enabled = true;
                if (b.pop)
                {
                    b.popT += dt;
                    b.sr.sprite = popS[b.popT < 0.07f ? 0 : 1];
                    if (b.popT >= 0.14f)
                    {
                        Destroy(b.sr.gameObject);
                        bubbles.RemoveAt(i);
                    }
                    continue;
                }
                b.pos.y += b.speed * dt;
                b.pos.x = b.x0 + 0.09f * Mathf.Sin(b.t * 5.5f + b.phase) + 0.05f * b.t;
                if (b.pos.y >= box.surfaceY - 0.08f)
                {
                    b.pop = true;
                    b.pos.y = SurfaceFx;
                }
                b.sr.color = new Color(tint.r, tint.g, tint.b, 0.95f);
                b.sr.transform.position = Snap(b.pos);
            }
        }

        // ------------------------------------------------------------------ hit tests
        /// <summary>The placed item under a world point (front first, then mid, back, the light).</summary>
        string HitPlaced(Vector2 w)
        {
            string best = null;
            int bestOrder = -1;
            foreach (var s in AquaTank.Slots)
            {
                if (!AquaTank.Unlocked(s)) continue;
                var def = AquaTank.Decor(AquaTank.ItemAt(s.id));
                if (def == null) continue;
                var c = CentreIn(s, def);
                var l = (w - c) * Ppu;
                float hw = def.w * 0.5f + 1f, hh = def.h * 0.5f + 1f;
                if (Mathf.Abs(l.x) > hw || Mathf.Abs(l.y) > hh) continue;
                if (s.order > bestOrder)
                {
                    bestOrder = s.order;
                    best = s.id;
                }
            }
            return best;
        }

        /// <summary>A press outside the mode on a decoration (no fish there): held a moment, the mode opens with it lifted.</summary>
        public bool PressNormal(Vector2 w)
        {
            if (ModeOn) return false;
            var s = HitPlaced(w);
            if (s == null) return false;
            pressSlot = s;
            pressT = Time.time;
            pressAt = w;
            return true;
        }

        public bool HitsDecor(Vector2 w) => HitPlaced(w) != null;

        void UpdateLongPress()
        {
            if (pressSlot == null) return;
            var w = pv.ScreenToWorld(PointerInput.Position);
            if (!PointerInput.IsDown || Dialog.Open || UIKit.ModalCount > 0)
            {
                pressSlot = null;
                return;
            }
            if ((w - pressAt).magnitude > 0.5f)
            {
                pressSlot = null;
                return;
            }
            if (Time.time - pressT < 0.45f) return;
            var s = pressSlot;
            pressSlot = null;
            Enter();
            StartDrag(AquaTank.Decor(AquaTank.ItemAt(s)), s, w);
        }

        // ------------------------------------------------------------------ the mode
        public void Enter()
        {
            if (ModeOn) return;
            ModeOn = true;
            AquaFeed.Suspended = true;
            AquaCare.Log($"decor mode: {AquaTank.Slots.Count(AquaTank.Unlocked)} slots, storage {TrayDefs().Count}");
            BuildMarkers();
            BuildTray();
            Sfx.Play(Sfx.Rustle, 0.6f, 0.9f);
            ModeChanged?.Invoke();
        }

        public void Exit()
        {
            if (!ModeOn) return;
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            dragDef = null;
            dragFrom = hover = null;
            foreach (var m in markers.Values) if (m != null) Destroy(m.gameObject);
            markers.Clear();
            if (tray != null) Destroy(tray.gameObject);
            tray = null;
            trayItems.Clear();
            ModeOn = false;
            AquaFeed.Suspended = false;
            Game.I.Save();
            var d = Game.Data;
            float bonus = Mathf.Min(AquaTank.BonusCap, AquaTank.DecorBonus(d));
            if (bonus > 0f) Toast.Show($"장식 보너스 관람 수입 +{bonus * 100f:0}%", UIKit.Good);
            AquaCare.Log($"decor mode done: {string.Join(", ", d.tank.places.Select(p => p.slot + "=" + p.item))} | {AquaTank.Describe(d)}");
            Sfx.Play(Sfx.Click);
            ModeChanged?.Invoke();
        }

        void BuildMarkers()
        {
            foreach (var s in AquaTank.Slots)
            {
                if (!AquaLayout.Active.HasSlot(s.id)) continue; // (a slot this tank does not have yet: it comes with a bigger tank)
                string k = s.kind == DecorKind.Light ? "light" : s.kind == DecorKind.Back ? "back" : s.kind == DecorKind.Mid ? "mid" : "front";
                var m = NewSprite("Slot_" + s.id, Art.Get("World/decor_slot_" + k), 41);
                m.transform.position = Snap(s.kind == DecorKind.Light ? LightAt : s.Base);
                markers[s.id] = m;
            }
        }

        static readonly Color Idle = new Color(1f, 0.96f, 0.86f, 0.75f);
        static readonly Color Valid = new Color(0.55f, 1f, 0.55f, 1f);
        static readonly Color Hot = new Color(1f, 0.86f, 0.3f, 1f);
        static readonly Color Locked = new Color(0.55f, 0.55f, 0.6f, 0.35f);

        void UpdateMarkers()
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            foreach (var s in AquaTank.Slots)
            {
                if (!markers.TryGetValue(s.id, out var m) || m == null) continue;
                bool open = AquaTank.Unlocked(s);
                m.enabled = true;
                m.transform.localScale = Vector3.one;
                if (!open) m.color = Locked;
                else if (dragDef == null) m.color = Idle;
                else if (dragDef.kind != s.kind) m.color = new Color(1f, 1f, 1f, 0.12f);
                else if (hover == s.id)
                {
                    m.color = Hot;
                    m.transform.localScale = new Vector3(1.0f, 1.0f, 1f);
                }
                else m.color = new Color(Valid.r, Valid.g, Valid.b, pulse);
            }
        }

        // ---- the storage tray
        List<DecorDef> TrayDefs()
        {
            var t = Game.Data.tank;
            return AquaTank.Decors.Where(x => t.owned.Contains(x.id) && AquaTank.SlotOf(x.id) is var s && (s == null || !AquaTank.Unlocked(AquaTank.Slot(s)))).ToList();
        }

        void BuildTray()
        {
            tray = UIKit.Panel(ui, "panel_dark", null, "DecorTray");
            tray.raycastTarget = true;
            var rt = tray.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(12f, 6f);
            rt.offsetMax = new Vector2(-12f, 102f);
            trayTitle = UIKit.Label(rt, "", 17, UIKit.Cream, TextAnchor.MiddleLeft, true, "TrayTitle");
            trayTitle.rectTransform.At(new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(130, 80), new Vector2(0, 0.5f));
            trayEmpty = UIKit.Label(rt, "보관함이 비었어요 · 상점에서 장식을 사요", 16, UIKit.Cream, TextAnchor.MiddleLeft, false, "TrayEmpty");
            trayEmpty.horizontalOverflow = HorizontalWrapMode.Overflow;
            trayEmpty.rectTransform.At(new Vector2(0, 0.5f), new Vector2(150, 0), new Vector2(420, 40), new Vector2(0, 0.5f));
            var done = UIKit.Button(rt, "완료", "green", Exit, new Vector2(110, 60), 21, null, "DecorDone");
            done.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(110, 60), new Vector2(1, 0.5f));
            var shop = UIKit.Button(rt, "상점", "blue", () =>
            {
                ShopUI.TankSub = ShopUI.SubDecor;
                ShopUI.Open(ui, ItemKind.Tank);
            }, new Vector2(96, 60), 21, null, "DecorShop");
            shop.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-128, 0), new Vector2(96, 60), new Vector2(1, 0.5f));
            prevBtn = UIKit.Button(rt, "<", "grey", () => { page--; RefreshTray(); }, new Vector2(40, 60), 18, null, "TrayPrev");
            prevBtn.GetComponent<RectTransform>().At(new Vector2(0, 0.5f), new Vector2(146, 0), new Vector2(40, 60), new Vector2(0, 0.5f));
            nextBtn = UIKit.Button(rt, ">", "grey", () => { page++; RefreshTray(); }, new Vector2(40, 60), 18, null, "TrayNext");
            nextBtn.GetComponent<RectTransform>().At(new Vector2(1, 0.5f), new Vector2(-234, 0), new Vector2(40, 60), new Vector2(1, 0.5f));
            Tween.Pop(rt, 0.85f, 0.18f);
            RefreshTray();
        }

        void RefreshTray()
        {
            if (tray == null) return;
            foreach (var ti in trayItems) if (ti.rt != null) Destroy(ti.rt.gameObject);
            trayItems.Clear();
            var defs = TrayDefs();
            ownedSeen = Game.Data.tank.owned.Count;
            var rt = tray.rectTransform;
            float width = rt.rect.width;
            perPage = Mathf.Max(1, Mathf.FloorToInt((width - 194f - 250f) / 80f));
            int pages = Mathf.Max(1, Mathf.CeilToInt(defs.Count / (float)perPage));
            page = Mathf.Clamp(page, 0, pages - 1);
            bool paged = defs.Count > perPage;
            prevBtn.gameObject.SetActive(paged);
            nextBtn.gameObject.SetActive(paged);
            prevBtn.interactable = page > 0;
            nextBtn.interactable = page < pages - 1;
            int unlocked = AquaTank.Slots.Count(AquaTank.Unlocked);
            int used = AquaTank.Slots.Count(s => AquaTank.Unlocked(s) && AquaTank.ItemAt(s.id) != null);
            trayTitle.text = $"보관함\n<size=15>자리 {used}/{unlocked}</size>";
            trayEmpty.enabled = defs.Count == 0;
            float x0 = paged ? 194f : 152f;
            int i = 0;
            foreach (var def in defs.Skip(page * perPage).Take(perPage))
            {
                var cell = UIKit.Rect(rt, "Tray_" + def.id);
                cell.At(new Vector2(0, 0.5f), new Vector2(x0 + i * 80f, 4f), new Vector2(72, 92), new Vector2(0, 0.5f));
                var slot = UIKit.Panel(cell, "slot", null, "Slot");
                slot.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(68, 68), new Vector2(0.5f, 1));
                var icon = UIKit.Img(slot.transform, Art.Item(def.id), new Vector2(64, 64), "Icon");
                var name = UIKit.Label(cell, def.name, 14, UIKit.Cream, TextAnchor.LowerCenter, true, "Name");
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                name.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(90, 20), new Vector2(0.5f, 0));
                trayItems.Add(new TrayItem { def = def, rt = cell, slot = slot, icon = icon, name = name });
                i++;
            }
        }

        TrayItem TrayHit(Vector2 screen)
        {
            foreach (var ti in trayItems)
                if (ti.rt != null && RectTransformUtility.RectangleContainsScreenPoint(ti.slot.rectTransform, screen, null)) return ti;
            return null;
        }

        bool OverTray(Vector2 screen) => tray != null && RectTransformUtility.RectangleContainsScreenPoint(tray.rectTransform, screen, null);

        /// <summary>A tray icon's centre on screen (the autopilot's press); the tray turns to its page.</summary>
        public Vector2 TrayIconScreen(string item)
        {
            var defs = TrayDefs();
            int i = defs.FindIndex(x => x.id == item);
            if (i < 0 || tray == null) return new Vector2(-1f, -1f);
            if (i / perPage != page)
            {
                page = i / perPage;
                RefreshTray();
            }
            var ti = trayItems.FirstOrDefault(x => x.def.id == item);
            if (ti == null) return new Vector2(-1f, -1f);
            Canvas.ForceUpdateCanvases();
            var r = ti.slot.rectTransform;
            return RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));
        }

        // ---- dragging
        void UpdateMode(float dt)
        {
            if (Game.Data.tank.owned.Count != ownedSeen) RefreshTray(); // (bought in the shop meanwhile)
            bool blocked = Dialog.Open || UIKit.ModalCount > 0;
            var screen = PointerInput.Position;
            var w = pv.ScreenToWorld(screen);
            if (blocked)
            {
                if (dragDef != null) CancelDrag();
            }
            else
            {
                if (PointerInput.Pressed && dragDef == null)
                {
                    var ti = TrayHit(screen);
                    if (ti != null) StartDrag(ti.def, null, w);
                    else if (!PointerInput.StartedOverUI)
                    {
                        var s = HitPlaced(w);
                        if (s != null) StartDrag(AquaTank.Decor(AquaTank.ItemAt(s)), s, w);
                    }
                }
                if (dragDef != null)
                {
                    if (PointerInput.IsDown) MoveDrag(w, screen);
                    else Drop();
                }
            }
            UpdateMarkers();
            foreach (var ti in trayItems)
                if (ti.icon != null) ti.icon.color = dragDef == ti.def && dragFrom == null ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
        }

        void StartDrag(DecorDef def, string from, Vector2 w)
        {
            if (def == null) return;
            dragDef = def;
            dragFrom = from;
            hover = from;
            dragPointer = w;
            ghost = NewSprite("DecorGhost", FramesOf(def)[0], 60);
            ghost.color = new Color(1f, 1f, 1f, 0.9f);
            MoveDrag(w, PointerInput.Position);
            Sfx.Play(Sfx.Rustle, 0.5f, 1.15f);
            AquaCare.Log($"lift {def.id} from {from ?? "storage"}");
        }

        void MoveDrag(Vector2 w, Vector2 screen)
        {
            dragPointer = w;
            // the nearest slot of its kind around the finger (within about its size)
            hover = null;
            float best = 1f;
            foreach (var s in AquaTank.Slots)
            {
                if (s.kind != dragDef.kind || !AquaTank.Unlocked(s)) continue;
                var c = CentreIn(s, dragDef);
                float rx = Mathf.Max(dragDef.w, 40) * 0.5f / Ppu + 1.1f, ry = Mathf.Max(dragDef.h, 32) * 0.5f / Ppu + 1.1f;
                var dv = w - c;
                float e = dv.x * dv.x / (rx * rx) + dv.y * dv.y / (ry * ry);
                if (e < best)
                {
                    best = e;
                    hover = s.id;
                }
            }
            if (OverTray(screen)) hover = null;
            // over a slot the item snaps into it (a preview), else it follows the finger
            var at = hover != null ? CentreIn(AquaTank.Slot(hover), dragDef) : w;
            ghost.sortingOrder = hover != null ? 60 : 62;
            ghost.color = hover != null ? AquaTank.DecorTint : new Color(1f, 1f, 1f, 0.85f);
            ghost.transform.position = Snap(at);
        }

        void Drop()
        {
            var def = dragDef;
            string from = dragFrom, to = hover;
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            dragDef = null;
            dragFrom = hover = null;
            if (to != null)
            {
                if (from == null) AquaTank.Place(to, def.id);
                else if (from != to)
                {
                    AquaTank.Swap(from, to);
                    Swaps++;
                }
                Drops++;
                var s = AquaTank.Slot(to);
                Sfx.Play(Sfx.Knock, 0.55f, s.kind == DecorKind.Light ? 1.4f : 0.9f);
                if (s.kind != DecorKind.Light) Fx.Puff(s.Base + new Vector2(0f, 1f / Ppu), new Color32(0xc8, 0xb0, 0x88, 0xe0), 6, 0.9f, 33, 0.3f);
                AquaCare.Log($"drop {def.id} ({from ?? "storage"}) -> {to}");
            }
            else if (from != null)
            {
                AquaTank.Remove(from);
                Removes++;
                Sfx.Play(Sfx.Rustle, 0.5f, 0.9f);
                AquaCare.Log($"drop {def.id} off the slots: back to storage");
            }
            else
            {
                Rejects++;
                AquaCare.Log($"drop {def.id}: no slot of its kind there ({AquaTank.KindName(def.kind)})");
            }
            Sync();
            RefreshTray();
            Game.I.Save();
        }

        void CancelDrag()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            dragDef = null;
            dragFrom = hover = null;
        }
    }
}
