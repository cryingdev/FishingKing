using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>A decoration standing in a tank slot (saved).</summary>
    [Serializable]
    public class DecorPlace
    {
        public string slot, item;
    }

    /// <summary>
    /// The tank's cleanliness and decorations (SaveData.tank; a field missing in an older save reads as a fresh
    /// TankCare, <see cref="AquaTank.Ensure"/> sets it up clean with the free sponge). Levels are 0 (clean) .. 1 (filthy);
    /// the algae on the front glass and the dirt on the gravel are per-pixel maps (their means are the levels), saved as
    /// 6-bit run-length strings; an empty string means the whole map is at its level.
    /// </summary>
    [Serializable]
    public class TankCare
    {
        public int ver;                                  // 0 = not set up yet
        public long at;                                  // the dirt's last real-time update (unix s)
        public float algae, debris, dirt;                // glass algae, floating debris, bottom dirt (0..1)
        public string algaeMap = "", dirtMap = "";       // the per-pixel local levels (see AquaTank.Encode)
        public List<string> owned = new List<string>();  // tools and decorations bought (tool_sponge is free)
        public List<DecorPlace> places = new List<DecorPlace>(); // slot -> decoration (slots unlock with the tank level)
        public int cleans;                               // whole-tank cleans (반짝반짝!)
    }

    public enum DecorKind { Light, Back, Mid, Front }

    /// <summary>
    /// A decoration sold in the shop's 장식 section (Tools/Blender/variants/hybrid/hyb_aquadecor.py): World/decor_&lt;key&gt;
    /// (or _f0.. frames), shop icon Items/&lt;id&gt;. Floor items fit only their own slot kind; each adds a small viewing
    /// income bonus (all together capped at <see cref="AquaTank.BonusCap"/>).
    /// </summary>
    public class DecorDef
    {
        public string id, key, name, desc;
        public DecorKind kind;
        public int price;
        public float bonus;                 // viewing income + (0.05 = +5 %)
        public int w, h;                    // sprite px (floor items: the bottom edge is the base line)
        public int frames;                  // World/decor_<key>_f0.. (0 = one still sprite)
        public float fps;
        public bool plant, bubbler, chest, wheel;
        // lights
        public Color tint = Color.white;    // multiply on the fish and the decorations
        public string[] favour;             // stage ids whose fish earn +5 % under this light
        public Rarity favourRarity = (Rarity)99; // or fish of this rarity and up
        public string favourText;
    }

    /// <summary>A cleaning tool (shop 청소 section; the sponge is given free).</summary>
    public class ToolDef
    {
        public string id, name, desc, use;
        public int price;
    }

    /// <summary>A fixed decoration slot in the tank (hyb_aquadecor.py SLOTS; aquarium_back.png pixels).</summary>
    public class DecorSlot
    {
        public string id;
        public DecorKind kind;
        public int col, row;                // the base point (bottom centre of a sprite standing there)
        public int level;                   // the tank level that unlocks it
        public int order;                   // sorting order of what stands there
        public int maxW, maxH;
        /// <summary>World position of the base point in the tank in use (AquaLayout; the legacy art's when it has none).</summary>
        public Vector2 Base
        {
            get
            {
                var p = AquaLayout.Active.SlotPos(id);
                return p != null ? new Vector2(p.x, p.y) : new Vector2((col - 320) / 16f, (200 - row) / 16f);
            }
        }
    }

    /// <summary>
    /// The aquarium's dirt and decorations on real time (offline included, like <see cref="AquaCare"/>): the glass grows
    /// algae, debris floats about, dirt settles on the gravel; faster with more fish (x0.5 empty .. x2.5 with 16+) and with
    /// uneaten food (a dissolved pellet / shrimp / sardine dirties the gravel where it lay), slower with plants (-12 %
    /// algae / -8 % dirt each) and the bubbler (-30 % debris). A filthy tank earns up to 25 % less viewing income
    /// (filth = 0.4 algae + 0.3 debris + 0.3 dirt); decorations add +2..5 % each, capped at +15 % with the light's
    /// favoured fish. Fish never die. Cleaning is by hand (AquaClean): the sponge wipes the algae map, the net scoops the
    /// debris, the siphon cleans the dirt map. Test switch: -fkaquadirt &lt;0..1&gt; sets every level at boot.
    /// </summary>
    public static class AquaTank
    {
        // ------------------------------------------------------------------ constants
        public const float AlgaeHours = 72f;       // clean -> filthy glass with 4 fish and no plants
        public const float DebrisHours = 36f;
        public const float DirtHours = 60f;
        public const float PlantAlgae = 0.12f;     // each plant: algae x(1 - 0.12)
        public const float PlantDirt = 0.08f;      // each plant: bottom dirt x(1 - 0.08)
        public const float BubblerDebris = 0.7f;   // the bubbler: debris x0.7
        public const float MaxPenalty = 0.25f;     // filthy: -25 % viewing income
        public const float WAlgae = 0.4f, WDebris = 0.3f, WDirt = 0.3f;
        public const float BonusCap = 0.15f;       // decorations + the light's favour, at most +15 %
        public const float FavourBonus = 0.05f;
        public const int MaxDebris = 20;           // floating bits in a filthy tank
        public const float PelletDirt = 0.08f, LiveDirt = 0.35f;     // a dissolved piece: the gravel's local level + (peak)
        public const float PelletDebris = 0.004f, LiveDebris = 0.02f;
        public const long MaxCatchUp = 7 * 86400;
        public const string SpongeId = "tool_sponge", NetId = "tool_net", SiphonId = "tool_siphon";
        public static readonly Color Filthy = new Color(0.92f, 0.95f, 0.84f);

        // the per-pixel maps: the algae over the glass opening, the dirt over the gravel strip of the tank in use
        // (AquaLayout: 436 x 164 / 436 x 28 for the 중형 수조, 880 x 332 / 880 x 56 for the 황금 대수족관), texture order
        // (row 0 at the bottom); a map saved at another tank's size is resampled on load
        public static int AW => AquaLayout.Active.glassW;
        public static int AH => AquaLayout.Active.glassH;
        public static int BW => AquaLayout.Active.glassW;
        public static int BH => AquaLayout.Active.dirtH;

        public static readonly List<ToolDef> Tools = new List<ToolDef>
        {
            new ToolDef { id = SpongeId, name = "스펀지", price = 0, desc = "유리에 낀 이끼를 문질러 닦아요.", use = "유리를 문질러 이끼를 닦아요" },
            new ToolDef { id = NetId, name = "뜰채", price = 600, desc = "물에 떠다니는 찌꺼기를 건져요.", use = "뜰채로 떠다니는 찌꺼기를 건져요" },
            new ToolDef { id = SiphonId, name = "사이펀", price = 1500, desc = "바닥 자갈 위를 훑어 쌓인 때를 빨아들여요.", use = "사이펀으로 바닥 자갈 위를 훑어요" },
        };

        public static readonly List<DecorDef> Decors = new List<DecorDef>
        {
            // lights (the hood slot): tint the water / mood, each favours some fish
            L("day", "주광 조명", 2000, "#ffffff", "호수·계곡 물고기", new[] { "lake", "stream" }, "맑은 흰 빛으로 수조를 밝혀요."),
            L("sunset", "노을 조명", 4000, "#ffe0c0", "방파제·늪지 물고기", new[] { "sea", "swamp" }, "따뜻한 노을빛이 물속에 번져요."),
            L("moon", "달빛 조명", 5000, "#b4c4ee", "얼음 호수·동굴 물고기", new[] { "ice", "cave" }, "푸른 달빛 아래 조용한 밤의 수조."),
            L("neon_pink", "네온 핑크", 7000, "#ffd0ee", "영웅·전설 물고기", null, "물고기 모양 네온사인이 분홍빛을 뿌려요.", Rarity.Epic),
            L("neon_cyan", "네온 시안", 7000, "#d0f6ff", "먼바다 물고기", new[] { "ocean" }, "별 모양 네온사인의 시원한 청록빛."),
            // back (tall, hazed): the sunken ship, driftwood, branch coral, cabomba
            F("ship", "침몰선", DecorKind.Back, 12000, 0.05f, 128, 72, "모래에 반쯤 묻힌 오래된 난파선."),
            F("driftwood", "유목", DecorKind.Back, 2500, 0.03f, 108, 72, "이끼 낀 가지가 멋스러운 유목."),
            F("coral_branch", "가지 산호", DecorKind.Back, 4000, 0.04f, 82, 68, "분홍빛 가지가 뻗은 산호."),
            F("cabomba", "카봄바", DecorKind.Back, 1800, 0.03f, 54, 100, "하늘하늘 흔들리는 키 큰 수초.", 4, 3f, plant: true),
            // mid: the water wheel, the treasure chest, brain coral, the stone cairn, the bubbler
            F("wheel", "물레방아", DecorKind.Mid, 9000, 0.05f, 56, 52, "작은 방앗간 옆에서 물레방아가 빙글빙글 돌아요.", 6, 8f, wheel: true),
            F("chest", "보물상자", DecorKind.Mid, 7000, 0.05f, 40, 44, "가끔 뚜껑이 열리며 공기방울이 뿜어져 나와요.", chest: true),
            F("coral_brain", "뇌 산호", DecorKind.Mid, 1500, 0.02f, 44, 28, "동글동글 주름진 산호."),
            F("rocks", "돌탑", DecorKind.Mid, 800, 0.02f, 52, 36, "납작한 돌을 차곡차곡 쌓은 돌탑."),
            F("bubbler", "기포기", DecorKind.Mid, 3500, 0.03f, 36, 32, "잠수부 투구에서 공기방울이 보글보글.", bubbler: true),
            // front (by the glass, the fish pass behind): java fern, rotala, fan coral
            F("fern", "자바 펀", DecorKind.Front, 1000, 0.02f, 40, 36, "유리 앞에서 살랑이는 수초.", 4, 3f, plant: true),
            F("rotala", "로탈라", DecorKind.Front, 1500, 0.03f, 40, 40, "붉게 물드는 예쁜 수초.", 4, 3f, plant: true),
            F("coral_fan", "부채 산호", DecorKind.Front, 2500, 0.03f, 36, 40, "보랏빛 그물 무늬 부채 산호."),
        };

        static DecorDef L(string key, string name, int price, string tint, string favourText, string[] stages, string desc, Rarity rarity = (Rarity)99) =>
            new DecorDef
            {
                id = "decor_light_" + key, key = "light_" + key, name = name, kind = DecorKind.Light, price = price, bonus = 0.03f,
                w = 48, h = 24, tint = Art.Hex(tint), favour = stages, favourRarity = rarity, favourText = favourText, desc = desc,
            };

        static DecorDef F(string key, string name, DecorKind kind, int price, float bonus, int w, int h, string desc, int frames = 0, float fps = 0f,
            bool plant = false, bool bubbler = false, bool chest = false, bool wheel = false) =>
            new DecorDef
            {
                id = "decor_" + key, key = key, name = name, kind = kind, price = price, bonus = bonus, w = w, h = h, desc = desc,
                frames = frames, fps = fps, plant = plant, bubbler = bubbler, chest = chest, wheel = wheel,
            };

        /// <summary>
        /// The slots (canvas px of aquarium_back.png; hyb_aquadecor.py SLOTS): 6 at the first tank, one more per tank
        /// level up to 10. Back items stand 2-3 rows under the gravel top, mid ones 5-13, front ones on the glass bottom.
        /// </summary>
        public static readonly List<DecorSlot> Slots = new List<DecorSlot>
        {
            S("light", DecorKind.Light, 320, 86, 0, 42),
            S("back_c", DecorKind.Back, 338, 261, 0, 3),
            S("mid_l", DecorKind.Mid, 314, 266, 0, 5),
            S("mid_r", DecorKind.Mid, 412, 268, 0, 6),
            S("front_l", DecorKind.Front, 124, 274, 0, 31),
            S("front_c", DecorKind.Front, 364, 274, 0, 31),
            S("back_l", DecorKind.Back, 196, 260, 1, 2),
            S("back_r", DecorKind.Back, 472, 263, 2, 4),
            S("mid_rr", DecorKind.Mid, 488, 268, 3, 7),
            S("front_r", DecorKind.Front, 516, 274, 4, 31),
        };

        static DecorSlot S(string id, DecorKind kind, int col, int row, int level, int order)
        {
            var s = new DecorSlot { id = id, kind = kind, col = col, row = row, level = level, order = order };
            switch (kind)
            {
                case DecorKind.Light: s.maxW = 48; s.maxH = 24; break;
                case DecorKind.Back: s.maxW = 128; s.maxH = 100; break;
                case DecorKind.Mid: s.maxW = 56; s.maxH = 52; break;
                default: s.maxW = 40; s.maxH = 40; break;
            }
            return s;
        }

        public static DecorDef Decor(string id) => Decors.FirstOrDefault(x => x.id == id);
        public static DecorSlot Slot(string id) => Slots.FirstOrDefault(x => x.id == id);
        public static ToolDef Tool(string id) => Tools.FirstOrDefault(x => x.id == id);

        public static string KindName(DecorKind k) => k == DecorKind.Light ? "조명" : k == DecorKind.Back ? "뒤쪽" : k == DecorKind.Mid ? "가운데" : "앞쪽";

        public static bool LogOn => AquaCare.LogOn;
        static void Log(string m) => AquaCare.Log(m);

        // ------------------------------------------------------------------ state
        static TankCare T => Game.Data.tank;
        static TankCare ensured;

        /// <summary>Sets up a missing / older tank state: clean, the free sponge, nothing placed.</summary>
        public static void Ensure(SaveData d)
        {
            if (d == null) return;
            d.tank ??= new TankCare();
            var t = d.tank;
            if (ensured == t && t.ver >= 1) return;
            ensured = t;
            t.owned ??= new List<string>();
            t.places ??= new List<DecorPlace>();
            t.algaeMap ??= "";
            t.dirtMap ??= "";
            if (t.ver < 1)
            {
                t.ver = 1;
                t.at = SaveSystem.Now;
                t.algae = t.debris = t.dirt = 0f;
                t.algaeMap = t.dirtMap = "";
                Log("tank: set up clean, the free sponge");
            }
            if (!t.owned.Contains(SpongeId)) t.owned.Add(SpongeId);
            t.algae = Level(t.algae);
            t.debris = Level(t.debris);
            t.dirt = Level(t.dirt);
            if (t.places.Count > 0)
            {
                // drop unknown items / slots, an item twice, an item it does not own, a slot twice
                var seen = new HashSet<string>();
                var slots = new HashSet<string>();
                t.places.RemoveAll(p => p == null || Decor(p.item) == null || Slot(p.slot) == null || Slot(p.slot).kind != Decor(p.item).kind
                                        || !t.owned.Contains(p.item) || !seen.Add(p.item) || !slots.Add(p.slot));
            }
        }

        static float Level(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Clamp01(v);

        // ------------------------------------------------------------------ the per-pixel maps
        static TankCare bound;
        static float[] algaeMap, dirtMap;
        static bool mapsDirty;

        /// <summary>Bumped whenever a map changes (the scene re-renders its overlays).</summary>
        public static int Version { get; private set; }

        /// <summary>The glass algae's local levels (AW x AH, row 0 at the bottom).</summary>
        public static float[] AlgaeMap { get { Bind(); return algaeMap; } }

        /// <summary>The gravel dirt's local levels (BW x BH, row 0 at the bottom).</summary>
        public static float[] DirtMap { get { Bind(); return dirtMap; } }

        static int bAW, bAH, bBW, bBH;

        static void Bind()
        {
            var d = Game.Data;
            Ensure(d);
            int aw = AW, ah = AH, bw = BW, bh = BH;
            if (bound == d.tank && algaeMap != null)
            {
                if (bAW == aw && bAH == ah && bBW == bw && bBH == bh) return;
                // the tank in use changed size (a bigger tank): the maps follow, stretched to it
                algaeMap = Resample(algaeMap, bAW, bAH, aw, ah);
                dirtMap = Resample(dirtMap, bBW, bBH, bw, bh);
                Log($"tank maps resampled {bAW}x{bAH} -> {aw}x{ah}, {bBW}x{bBH} -> {bw}x{bh}");
                mapsDirty = true;
            }
            else
            {
                bound = d.tank;
                algaeMap = DecodeAny(bound.algaeMap, aw, ah, bound.algae, false);
                dirtMap = DecodeAny(bound.dirtMap, bw, bh, bound.dirt, true);
                mapsDirty = false;
            }
            bAW = aw; bAH = ah; bBW = bw; bBH = bh;
            bound.algae = Mean(algaeMap);
            bound.dirt = Mean(dirtMap);
            Version++;
        }

        /// <summary>
        /// A saved map at whatever tank size it was saved (its run length tells which: every tank's glass / gravel strip
        /// has its own pixel count), resampled to w x h; unreadable: uniform at its level.
        /// </summary>
        static float[] DecodeAny(string s, int w, int h, float level, bool dirt)
        {
            if (string.IsNullOrEmpty(s)) return Decode(s, w * h, level);
            var raw = DecodeRaw(s);
            if (raw != null)
            {
                if (raw.Length == w * h) return raw;
                for (int l = 0; l < AquaLayout.Levels; l++)
                {
                    var L = AquaLayout.Of(l);
                    if (L == null) continue;
                    int sw = L.glassW, sh = dirt ? L.dirtH : L.glassH;
                    if (sw * sh != raw.Length) continue;
                    Log($"tank map ({(dirt ? "dirt" : "algae")}) saved at {sw}x{sh}: resampled to {w}x{h}");
                    return Resample(raw, sw, sh, w, h);
                }
            }
            return Decode(null, w * h, level);
        }

        /// <summary>A run-length string at its own length (null = unreadable).</summary>
        static float[] DecodeRaw(string s)
        {
            try
            {
                var b = Convert.FromBase64String(s);
                var list = new List<float>(4096);
                int k = 0;
                while (k < b.Length)
                {
                    int v = b[k] & 63, c = b[k] >> 6;
                    k++;
                    int run = c + 1;
                    if (c == 3)
                    {
                        int r = 0, sh = 0;
                        while (k < b.Length)
                        {
                            int x = b[k++];
                            r |= (x & 127) << sh;
                            sh += 7;
                            if (x < 128) break;
                        }
                        run = 4 + r;
                    }
                    if (list.Count + run > 4000000) return null;
                    float f = v / 63f;
                    for (int i = 0; i < run; i++) list.Add(f);
                }
                return list.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Nearest-neighbour resample of a level map (texture order).</summary>
        public static float[] Resample(float[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new float[dw * dh];
            if (src == null || sw <= 0 || sh <= 0 || src.Length < sw * sh) return dst;
            for (int y = 0; y < dh; y++)
            {
                int sy = Mathf.Min(sh - 1, y * sh / dh);
                for (int x = 0; x < dw; x++) dst[y * dw + x] = src[sy * sw + Mathf.Min(sw - 1, x * sw / dw)];
            }
            return dst;
        }

        static float Mean(float[] m)
        {
            double s = 0;
            for (int i = 0; i < m.Length; i++) s += m[i];
            return (float)(s / m.Length);
        }

        /// <summary>The maps were changed in place (a wipe, the siphon): the levels follow, the save strings are stale.</summary>
        public static void MapsChanged()
        {
            Bind();
            T.algae = Mean(algaeMap);
            T.dirt = Mean(dirtMap);
            mapsDirty = true;
            Version++;
        }

        /// <summary>Writes the maps into the save strings (after a stroke, on leaving the tank, every 30 s).</summary>
        public static void Commit()
        {
            if (Game.I == null || !mapsDirty || bound != T) return;
            if (algaeMap == null || algaeMap.Length != AW * AH || dirtMap.Length != BW * BH) Bind();
            T.algaeMap = Encode(algaeMap);
            T.dirtMap = Encode(dirtMap);
            mapsDirty = false;
        }

        /// <summary>
        /// 6-bit levels, run-length coded: a byte v | k &lt;&lt; 6 is one level v (0..63) repeated k + 1 times (k 0..2), or
        /// (k = 3) 4 + a following 7-bit varint times; base64. A uniform map is "" (it is at its level).
        /// </summary>
        public static string Encode(float[] m)
        {
            int n = m.Length;
            var q = new byte[n];
            for (int i = 0; i < n; i++) q[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(m[i] * 63f), 0, 63);
            var bytes = new List<byte>(1024);
            int j0 = 0;
            while (j0 < n)
            {
                byte v = q[j0];
                int j = j0 + 1;
                while (j < n && q[j] == v) j++;
                int run = j - j0;
                if (run == n) return "";
                if (run <= 3) bytes.Add((byte)(v | (run - 1) << 6));
                else
                {
                    bytes.Add((byte)(v | 3 << 6));
                    int r = run - 4;
                    while (r >= 128)
                    {
                        bytes.Add((byte)(r & 127 | 128));
                        r >>= 7;
                    }
                    bytes.Add((byte)r);
                }
                j0 = j;
            }
            return Convert.ToBase64String(bytes.ToArray());
        }

        public static float[] Decode(string s, int n, float level)
        {
            var m = new float[n];
            bool ok = false;
            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    var b = Convert.FromBase64String(s);
                    int i = 0, k = 0;
                    while (k < b.Length && i < n)
                    {
                        int v = b[k] & 63, c = b[k] >> 6;
                        k++;
                        int run = c + 1;
                        if (c == 3)
                        {
                            int r = 0, sh = 0;
                            while (k < b.Length)
                            {
                                int x = b[k++];
                                r |= (x & 127) << sh;
                                sh += 7;
                                if (x < 128) break;
                            }
                            run = 4 + r;
                        }
                        float f = v / 63f;
                        for (int e = Math.Min(n, i + run); i < e; i++) m[i] = f;
                    }
                    ok = i == n && k == b.Length;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[AquaTank] bad map: " + e.Message);
                }
            }
            if (!ok)
                for (int i = 0; i < n; i++) m[i] = Level(level);
            return m;
        }

        /// <summary>The last specks wiped off the glass.</summary>
        public static void ZeroAlgae()
        {
            Bind();
            Array.Clear(algaeMap, 0, algaeMap.Length);
            MapsChanged();
        }

        /// <summary>The last dirt siphoned off the gravel.</summary>
        public static void ZeroDirt()
        {
            Bind();
            Array.Clear(dirtMap, 0, dirtMap.Length);
            MapsChanged();
        }

        /// <summary>Floating bits showing: one per 1/20 of the debris level (rounded: the first shows at 2.5 %).</summary>
        public static int DebrisCount(SaveData d) =>
            d?.tank == null ? 0 : Mathf.Clamp(Mathf.FloorToInt(d.tank.debris * MaxDebris + 0.5f), 0, MaxDebris);

        /// <summary>One bit scooped out with the net.</summary>
        public static void Scoop()
        {
            T.debris = Mathf.Max(0f, T.debris - 1f / MaxDebris);
            if (T.debris < 0.0005f) T.debris = 0f;
        }

        /// <summary>Every level set to <paramref name="v"/> (uniform maps: the test switches, the autopilot).</summary>
        public static void SetAll(float v, float? debris = null)
        {
            Bind();
            v = Level(v);
            for (int i = 0; i < algaeMap.Length; i++) algaeMap[i] = v;
            for (int i = 0; i < dirtMap.Length; i++) dirtMap[i] = v;
            T.debris = Level(debris ?? v);
            T.at = SaveSystem.Now;
            MapsChanged();
            Commit();
            Log($"tank: set algae / dirt {v:0.00}, debris {T.debris:0.00}");
        }

        // ------------------------------------------------------------------ ownership
        public static bool Owns(string id) => Game.Data.tank != null && Game.Data.tank.owned.Contains(id);

        public static bool Buy(string id, int price)
        {
            Ensure(Game.Data);
            if (Owns(id)) return false;
            if (Game.I.Coins < price) return false;
            T.owned.Add(id);
            Game.I.Spend(price); // (saves and notifies)
            Log($"buy {id} for {price}: coins {Game.I.Coins}");
            return true;
        }

        // ------------------------------------------------------------------ placement
        public static int SlotCount(int tankLevel) => Slots.Count(s => s.level <= tankLevel);

        /// <summary>Open at the player's tank level and standing in the tank in use (a tank bought while the scene is open: next visit).</summary>
        public static bool Unlocked(DecorSlot s) => s.level <= Game.Data.tankLevel && AquaLayout.Active.HasSlot(s.id);

        public static string ItemAt(string slot) => T.places.FirstOrDefault(p => p.slot == slot)?.item;

        public static string SlotOf(string item) => T.places.FirstOrDefault(p => p.item == item)?.slot;

        /// <summary>Puts an owned item in a slot of its kind (whatever stood there goes back to storage; the item leaves its old slot).</summary>
        public static bool Place(string slot, string item)
        {
            var s = Slot(slot);
            var def = Decor(item);
            if (s == null || def == null || s.kind != def.kind || !Owns(item)) return false;
            AquaCare.Advance(Game.Data, SaveSystem.Now); // (the income so far at the old bonus)
            T.places.RemoveAll(p => p.slot == slot || p.item == item);
            T.places.Add(new DecorPlace { slot = slot, item = item });
            Log($"place {item} -> {slot}: bonus {DecorBonus(Game.Data) * 100f:0}%");
            return true;
        }

        /// <summary>Swaps what stands in two slots of the same kind (one may be empty).</summary>
        public static bool Swap(string a, string b)
        {
            var sa = Slot(a);
            var sb = Slot(b);
            if (sa == null || sb == null || sa.kind != sb.kind || a == b) return false;
            string ia = ItemAt(a), ib = ItemAt(b);
            T.places.RemoveAll(p => p.slot == a || p.slot == b);
            if (ia != null) T.places.Add(new DecorPlace { slot = b, item = ia });
            if (ib != null) T.places.Add(new DecorPlace { slot = a, item = ib });
            Log($"swap {a} ({ia}) <-> {b} ({ib})");
            return true;
        }

        public static void Remove(string slot)
        {
            AquaCare.Advance(Game.Data, SaveSystem.Now);
            int n = T.places.RemoveAll(p => p.slot == slot);
            if (n > 0) Log($"remove {slot}: bonus {DecorBonus(Game.Data) * 100f:0}%");
        }

        /// <summary>The decorations standing in unlocked slots.</summary>
        public static List<DecorDef> Placed(SaveData d)
        {
            var list = new List<DecorDef>();
            if (d?.tank?.places == null) return list;
            foreach (var p in d.tank.places)
            {
                var s = Slot(p.slot);
                var def = Decor(p.item);
                if (s != null && def != null && s.level <= d.tankLevel) list.Add(def);
            }
            return list;
        }

        public static DecorDef Light(SaveData d) => Placed(d).FirstOrDefault(x => x.kind == DecorKind.Light);

        public static int Plants(SaveData d) => Placed(d).Count(x => x.plant);

        public static bool Bubbler(SaveData d) => Placed(d).Any(x => x.bubbler);

        // ------------------------------------------------------------------ rates
        public static float FishFactor(int n) => Mathf.Clamp(0.5f + 0.125f * n, 0.5f, 2.5f);

        /// <summary>Growth per second of the algae, the debris and the bottom dirt now.</summary>
        public static void Rates(SaveData d, out float ra, out float rd, out float rb)
        {
            float ff = FishFactor(d.aquarium.Count);
            int plants = Plants(d);
            ra = ff * Mathf.Max(0.5f, 1f - PlantAlgae * plants) / (AlgaeHours * 3600f);
            rd = ff * (Bubbler(d) ? BubblerDebris : 1f) / (DebrisHours * 3600f);
            rb = ff * Mathf.Max(0.5f, 1f - PlantDirt * plants) / (DirtHours * 3600f);
        }

        /// <summary>Brings the dirt up to <paramref name="now"/> (at most 7 days at once: it is filthy long before).</summary>
        public static void Advance(SaveData d, long now)
        {
            Ensure(d);
            var t = d.tank;
            if (t.at <= 0) t.at = now;
            long span = now - t.at;
            if (span <= 0)
            {
                if (span < 0) t.at = now; // (the clock was set back)
                return;
            }
            span = Math.Min(span, MaxCatchUp);
            Grow(d, span);
            t.at = now;
        }

        static void Grow(SaveData d, double secs)
        {
            Rates(d, out float ra, out float rd, out float rb);
            var t = d.tank;
            t.debris = Mathf.Min(1f, t.debris + (float)(rd * secs));
            if (d != Game.Data)
            {
                t.algae = Mathf.Min(1f, t.algae + (float)(ra * secs));
                t.dirt = Mathf.Min(1f, t.dirt + (float)(rb * secs));
                return;
            }
            Bind();
            float da = (float)(ra * secs), db = (float)(rb * secs);
            if (da <= 0f && db <= 0f) return;
            for (int i = 0; i < algaeMap.Length; i++) algaeMap[i] = Mathf.Min(1f, algaeMap[i] + da);
            for (int i = 0; i < dirtMap.Length; i++) dirtMap[i] = Mathf.Min(1f, dirtMap[i] + db);
            MapsChanged();
        }

        /// <summary>The test fast-forward moves the tank's clock back with the fish (AquaCare.FastForward).</summary>
        public static void Shift(SaveData d, long secs)
        {
            Ensure(d);
            d.tank.at -= secs;
        }

        /// <summary>
        /// Food left to rot: the gravel's local dirt rises around where it dissolved (a pellet a little, a shrimp /
        /// sardine a lot) and a bit of debris floats up.
        /// </summary>
        public static void OnUneaten(Vector2 at, bool live)
        {
            Bind();
            float peak = live ? LiveDirt : PelletDirt;
            int half = live ? 22 : 12;
            int c = Mathf.RoundToInt((at.x - AquaLayout.Active.glassL) * 16f);
            int bw = BW, bh = BH;
            for (int x = c - half; x <= c + half; x++)
            {
                if (x < 0 || x >= bw) continue;
                float k = 1f - Mathf.Abs(x - c) / (float)(half + 1);
                float add = peak * k * k;
                for (int y = 0; y < bh; y++)
                {
                    int i = y * bw + x;
                    dirtMap[i] = Mathf.Min(1f, dirtMap[i] + add);
                }
            }
            T.debris = Mathf.Min(1f, T.debris + (live ? LiveDebris : PelletDebris));
            MapsChanged();
            UneatenCount++;
            Log($"uneaten {(live ? "live piece" : "pellet")} at {at.x:0.00}: dirt {T.dirt:0.000}, debris {T.debris:0.000}");
        }

        public static int UneatenCount { get; private set; }

        // ------------------------------------------------------------------ income
        public static float Filth(SaveData d) => d?.tank == null ? 0f : WAlgae * d.tank.algae + WDebris * d.tank.debris + WDirt * d.tank.dirt;

        public static float Penalty(SaveData d) => MaxPenalty * Filth(d);

        /// <summary>The decorations' bonus before the cap.</summary>
        public static float DecorBonus(SaveData d) => Placed(d).Sum(x => x.bonus);

        /// <summary>The light's favour for this fish (+5 % for the fish of its stages / rarity).</summary>
        public static float Favour(SaveData d, CaughtFish f)
        {
            var l = Light(d);
            if (l == null || f == null) return 0f;
            if (l.favour != null && l.favour.Contains(f.stageId)) return FavourBonus;
            var sp = f.Species;
            return sp != null && sp.rarity >= l.favourRarity ? FavourBonus : 0f;
        }

        public static float Bonus(SaveData d, CaughtFish f) => Mathf.Min(BonusCap, DecorBonus(d) + Favour(d, f));

        /// <summary>The tank's multiplier on a fish's viewing income now.</summary>
        public static float Mult(SaveData d, CaughtFish f) => (1f + Bonus(d, f)) * (1f - Penalty(d));

        /// <summary>
        /// The multiplier averaged over [from, to] (unix s): the levels rise linearly from the tank's last update (and
        /// stop at 1), so a long offline span is banked at its average dirt.
        /// </summary>
        public static double AvgMult(SaveData d, CaughtFish f, long from, long to)
        {
            if (d?.tank == null) return 1.0;
            if (to <= from) return Mult(d, f);
            Rates(d, out float ra, out float rd, out float rb);
            var t = d.tank;
            double a = from - t.at, b = to - t.at;
            double filth = WAlgae * AvgLevel(t.algae, ra, a, b) + WDebris * AvgLevel(t.debris, rd, a, b) + WDirt * AvgLevel(t.dirt, rb, a, b);
            return (1.0 + Bonus(d, f)) * (1.0 - MaxPenalty * filth);
        }

        /// <summary>The mean over [a, b] s (from the last update) of min(1, level + rate s) (the level itself before 0).</summary>
        static double AvgLevel(float level, float rate, double a, double b)
        {
            double I(double s)
            {
                if (s <= 0 || rate <= 0f || level >= 1f) return Math.Min(1.0, level) * s;
                double ts = (1.0 - level) / rate;
                if (s <= ts) return level * s + 0.5 * rate * s * s;
                return level * ts + 0.5 * rate * ts * ts + (s - ts);
            }
            if (b - a < 1e-6) return Math.Min(1.0, level + rate * Math.Max(0.0, a));
            return (I(b) - I(a)) / (b - a);
        }

        /// <summary>Viewing income of the whole tank now (coins/min), fed / hungry, decorations and dirt included.</summary>
        public static float IncomePerMin(SaveData d)
        {
            float s = 0f;
            foreach (var f in d.aquarium) s += AquaCare.IncomeF(f) * Mult(d, f);
            return s;
        }

        /// <summary>A decoration's effect lines for the shop ("관람 수입 +5% · 이끼 -12%").</summary>
        public static string EffectText(DecorDef def)
        {
            var parts = new List<string> { $"관람 수입 +{def.bonus * 100f:0}%" };
            if (def.plant) parts.Add($"이끼 -{PlantAlgae * 100f:0}% · 바닥 때 -{PlantDirt * 100f:0}%");
            if (def.bubbler) parts.Add($"부유물 -{(1f - BubblerDebris) * 100f:0}% · 물고기가 활발해져요");
            if (def.chest) parts.Add("가끔 열리며 공기방울");
            if (def.wheel) parts.Add("빙글빙글 돌아요");
            if (def.kind == DecorKind.Light) parts.Add($"{def.favourText} +{FavourBonus * 100f:0}%");
            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------ the look
        /// <summary>
        /// How lively a kept fish is (swim speed / tail beat): +25 % with the bubbler (+10 % while hungry), a little
        /// sluggish in dirty water.
        /// </summary>
        public static float Lively(SaveData d, CaughtFish f, bool bubbler, float filth)
        {
            float l = 1f;
            if (bubbler) l += AquaCare.Hungry(f) ? 0.1f : 0.25f;
            return l - 0.15f * filth;
        }

        /// <summary>The colour multiply on the fish and the decorations: the light's tint, dulled toward olive when filthy.</summary>
        public static Color Tint(DecorDef light, float filth, bool bubbler)
        {
            var c = light != null ? light.tint : Color.white;
            var dirt = Color.Lerp(Color.white, Filthy, Mathf.Clamp01(filth) * (bubbler ? 0.5f : 1f));
            return new Color(c.r * dirt.r, c.g * dirt.g, c.b * dirt.b, 1f);
        }

        static int lookFrame = -1;
        static DecorDef lookLight;
        static bool lookBubbler;
        static float lookFilth;

        static void RefreshLook()
        {
            if (lookFrame == Time.frameCount || Game.I == null) return;
            lookFrame = Time.frameCount;
            var d = Game.Data;
            var placed = Placed(d);
            lookLight = placed.FirstOrDefault(x => x.kind == DecorKind.Light);
            lookBubbler = placed.Any(x => x.bubbler);
            lookFilth = Filth(d);
        }

        /// <summary>This frame's light (null = none), bubbler and filth (the tank scene's sprites read them).</summary>
        public static DecorDef LookLight { get { RefreshLook(); return lookLight; } }
        public static bool LookBubbler { get { RefreshLook(); return lookBubbler; } }
        public static float LookFilth { get { RefreshLook(); return lookFilth; } }
        public static Color LightTint => LookLight != null ? LookLight.tint : Color.white;
        public static Color DecorTint => Tint(LookLight, LookFilth, false);
        public static Color FishTint => Tint(LookLight, LookFilth, LookBubbler);

        /// <summary>The gravel's top line (world y) at x in the tank in use (the rendered per-column rows).</summary>
        public static float GravelTop(float x) => AquaLayout.Active.GravelTop(x);

        // ------------------------------------------------------------------ space (칸) and size classes
        /// <summary>소형 &lt;40 cm, 중형 40-100, 대형 100-200, 초대형 200+ (by the fish's length now).</summary>
        public static readonly string[] ClassNames = { "소형", "중형", "대형", "초대형" };
        public static readonly int[] ClassSpace = { 1, 2, 4, 8 };
        public static readonly string[] ClassRange = { "40cm 미만", "40~100cm", "100~200cm", "200cm 이상" };

        public static int ClassOf(float cm) => cm >= 200f ? 3 : cm >= 100f ? 2 : cm >= 40f ? 1 : 0;
        public static int ClassOf(CaughtFish f) => ClassOf(f.sizeCm);
        public static int Space(CaughtFish f) => ClassSpace[ClassOf(f)];
        public static int Used(SaveData d) => d.aquarium.Sum(Space);
        public static int HugeCount(SaveData d) => d.aquarium.Count(f => ClassOf(f) == 3);

        public enum Fit { Ok, TooBig, NoRoom }

        /// <summary>Whether a tank takes one more fish on top of <paramref name="used"/> 칸 and <paramref name="huge"/> 초대형.</summary>
        static Fit FitsIn(TankDef t, int cls, int used, int huge)
        {
            if (cls > t.maxClass) return Fit.TooBig;
            if (used + ClassSpace[cls] > t.capacity) return Fit.NoRoom;
            if (cls == 3 && t.maxHuge > 0 && huge + 1 > t.maxHuge) return Fit.NoRoom;
            return Fit.Ok;
        }

        /// <summary>
        /// Can this fish go into the player's tank now? No: too big for it (its size class) or no room left (칸, or the
        /// 아쿠아리움's two 초대형); <paramref name="need"/> is the first tank that would take it with the fish kept now
        /// (null: none, sell some first).
        /// </summary>
        public static Fit CanAdd(SaveData d, CaughtFish f, out TankDef need)
        {
            need = null;
            int cls = ClassOf(f), used = Used(d), huge = HugeCount(d);
            var fit = FitsIn(GameDatabase.TankForLevel(d.tankLevel), cls, used, huge);
            if (fit == Fit.Ok) return fit;
            need = GameDatabase.Tanks.FirstOrDefault(t => t.level > d.tankLevel && FitsIn(t, cls, used, huge) == Fit.Ok);
            return fit;
        }

        /// <summary>"이 수조엔 너무 커요 · 대형 수조가 필요해요" / "자리가 부족해요 · …" ("" when it fits).</summary>
        public static string RefuseText(Fit fit, TankDef need, CaughtFish f = null)
        {
            if (fit == Fit.Ok) return "";
            string head = fit == Fit.TooBig ? "이 수조엔 너무 커요" : "자리가 부족해요";
            if (need != null) return $"{head} · {need.name}{Josa(need.name, "이", "가")} 필요해요";
            return head + " · 물고기를 팔아 자리를 비워요";
        }

        public static string RefuseText(SaveData d, CaughtFish f) => RefuseText(CanAdd(d, f, out var need), need, f);

        static string Josa(string word, string withFinal, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0 ? withFinal : without;
        }

        /// <summary>
        /// The kept fish over the tank's limits (an older save, or a fish grown into a bigger class): too big for the
        /// tank, or beyond its 칸 in the order they were put in. They stay (never lost); only new fish are refused.
        /// </summary>
        public static HashSet<CaughtFish> Over(SaveData d)
        {
            var over = new HashSet<CaughtFish>();
            var t = GameDatabase.TankForLevel(d.tankLevel);
            int used = 0, huge = 0;
            foreach (var f in d.aquarium)
            {
                int cls = ClassOf(f);
                if (FitsIn(t, cls, used, huge) != Fit.Ok)
                {
                    over.Add(f);
                    continue;
                }
                used += ClassSpace[cls];
                if (cls == 3) huge++;
            }
            return over;
        }

        /// <summary>A tank's limits for the shop / the title: "12칸 · 중형(40~100cm)까지".</summary>
        public static string LimitText(TankDef t) =>
            $"{t.capacity}칸 · {ClassNames[t.maxClass]}({ClassRange[t.maxClass]})까지" + (t.maxClass == 3 && t.maxHuge > 0 ? $" {t.maxHuge}마리" : t.maxClass == 3 ? " 여러 마리" : "");

        // ------------------------------------------------------------------ how big a kept fish is drawn
        /// <summary>
        /// Drawn length (px) by real length: 3.1 x cm^0.6 (compressed: a 30 cm crucian carp 24 px, a 240 cm arapaima
        /// 83 px = 3.5x, a 500 cm great white 130 px), at least <see cref="MinPx"/>; per tank at most 40 % of its glass
        /// width and 55 % of its water depth.
        /// </summary>
        public const float ScaleK = 3.1f, ScaleP = 0.6f, MinPx = 12f;

        public static float DrawnPx(float cm) => Mathf.Max(MinPx, ScaleK * Mathf.Pow(Mathf.Max(1f, cm), ScaleP));

        /// <summary>The sprite scale of a kept fish (its sprite's width is its length) in a tank.</summary>
        public static float FishScale(CaughtFish f, Sprite s, AquaLayout L)
        {
            if (s == null) return 1f;
            float w = s.rect.width, h = s.rect.height;
            float px = DrawnPx(AquaCare.GrownCm(f));
            float k = px / w;
            if (L != null)
            {
                float depth = (L.surfaceY - L.gravelTopY) * L.ppu;
                k = Mathf.Min(k, 0.4f * L.glassW / w, 0.55f * depth / h);
            }
            return Mathf.Max(0.25f, k);
        }

        public static string Describe(SaveData d)
        {
            Rates(d, out float ra, out float rd, out float rb);
            var t = d.tank;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "algae {0:0.000} debris {1:0.000} dirt {2:0.000} filth {3:0.000} penalty {4:0.0}% | rates/h algae {5:0.0000} debris {6:0.0000} dirt {7:0.0000} (fish {8}, plants {9}, bubbler {10}) | decor +{11:0}% light {12}",
                t.algae, t.debris, t.dirt, Filth(d), Penalty(d) * 100f, ra * 3600f, rd * 3600f, rb * 3600f, d.aquarium.Count, Plants(d), Bubbler(d),
                DecorBonus(d) * 100f, Light(d)?.key ?? "none");
        }
    }
}
