using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>The -fkaqua tanks scenario (see the switch list in AquaPilot.cs).</summary>
    public partial class AquaPilot
    {
        static AquaLayout Lay => AquaLayout.Active;

        /// <summary>The stock of each tank level (all fit: 소형 1칸, 중형 2, 대형 4, 초대형 8).</summary>
        static readonly (string id, float cm, string st)[][] Stocks =
        {
            // 작은 어항 6칸, 소형만
            new[] { ("crucian_carp", 30f, "lake"), ("bluegill", 18f, "lake"), ("pale_chub", 14f, "stream"), ("cherry_salmon", 28f, "stream"), ("mandarin_fish", 35f, "stream"), ("cave_tetra", 9f, "cave") },
            // 중형 수조 12칸, 중형까지
            new[] { ("crucian_carp", 30f, "lake"), ("carp", 70f, "lake"), ("largemouth_bass", 45f, "lake"), ("rainbow_trout", 50f, "stream"), ("bluegill", 20f, "lake"), ("pale_chub", 14f, "stream"), ("black_porgy", 45f, "sea") },
            // 대형 수조 24칸, 대형까지
            new[] { ("carp", 80f, "lake"), ("arowana", 105f, "swamp"), ("catfish", 90f, "swamp"), ("flounder", 70f, "sea"), ("crucian_carp", 30f, "lake"), ("red_seabream", 90f, "sea"), ("mahi_mahi", 120f, "ocean"), ("northern_pike", 110f, "ice"), ("bluegill", 20f, "lake") },
            // 아쿠아리움 40칸, 초대형 2마리
            new[] { ("arapaima", 240f, "swamp"), ("crucian_carp", 30f, "lake"), ("sturgeon", 220f, "ice"), ("bluefin_tuna", 150f, "ocean"), ("carp", 70f, "lake"), ("largemouth_bass", 45f, "lake"), ("rainbow_trout", 50f, "stream"), ("bluegill", 20f, "lake"), ("crystal_koi", 60f, "cave") },
            // 황금 대수족관 64칸, 초대형 여러 마리
            new[] { ("great_white", 450f, "ocean"), ("arapaima", 260f, "swamp"), ("blue_marlin", 300f, "ocean"), ("ocean_sunfish", 220f, "ocean"), ("crucian_carp", 30f, "lake"), ("carp", 70f, "lake"), ("largemouth_bass", 45f, "lake"), ("rainbow_trout", 50f, "stream"), ("bluegill", 20f, "lake"), ("crystal_koi", 60f, "cave"), ("black_porgy", 45f, "sea"), ("red_seabream", 90f, "sea"), ("catfish", 90f, "swamp") },
        };

        CaughtFish Catch(string id, float cm, string st) => Game.I.MakeCatch(GameDatabase.GetFish(id), cm, st);

        /// <summary>Via the map: the tank level set, stocked through AddToAquarium (all must fit), then the aquarium opened.</summary>
        IEnumerator OpenTank(int level, bool hungry = true)
        {
            var d = Game.Data;
            SceneFlow.Go("Map");
            yield return new WaitForSeconds(1.4f);
            d.tankLevel = level;
            for (int i = 1; i <= level; i++) if (!d.ownedItems.Contains("tank_" + i)) d.ownedItems.Add("tank_" + i);
            d.aquarium.Clear();
            var refused = new List<string>();
            foreach (var (id, cm, st) in Stocks[level])
            {
                var cf = Catch(id, cm, st);
                if (!Game.I.AddToAquarium(cf)) refused.Add($"{id} {Game.I.KeepRefusal(cf)}");
            }
            var t = Game.I.Tank;
            Check(refused.Count == 0 && AquaTank.Used(d) <= t.capacity && AquaTank.Over(d).Count == 0,
                $"{t.name}: stocked {d.aquarium.Count} fish in {AquaTank.Used(d)}/{t.capacity}칸 ({string.Join(" ", Enumerable.Range(0, 4).Select(c => AquaTank.ClassNames[c] + d.aquarium.Count(f => AquaTank.ClassOf(f) == c)))}){(refused.Count > 0 ? " REFUSED " + string.Join(", ", refused) : "")}");
            if (hungry) AquaCare.FastForward(d, 12f, null);
            AquaTank.SetAll(0f, 0f);
            AquaCare.Stock("feed_basic").portions = AquaCare.Feed("feed_basic").portions;
            AquaCare.Stock("feed_basic").bags = 1;
            AquaCare.Stock("feed_shrimp").pieces = 10;
            AquaCare.Stock("feed_shrimp").open = true;
            AquaCare.Stock("feed_sardine").pieces = 6;
            Game.I.Save();
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.6f);
        }

        /// <summary>The scene is this level's: its art, its view height, the fish inside the water.</summary>
        void CheckRendered(int level)
        {
            var scene = FindAnyObjectByType<AquariumScene>();
            var L = AquaLayout.Of(level);
            var back = GameObject.Find("Back")?.GetComponent<SpriteRenderer>();
            var rt = PixelView.Current != null ? PixelView.Current.Target : null;
            var fish = Game.Data.aquarium.Select(scene.TankOf).Where(x => x != null).ToList();
            int outside = fish.Count(f => f.Pos.x - f.HalfWidth < L.glassL - 0.1f || f.Pos.x + f.HalfWidth > L.glassR + 0.1f
                                          || f.Pos.y + f.HalfHeight > L.surfaceY + 0.2f || f.Pos.y - f.HalfHeight < L.gravelTopY - 0.35f);
            var title = GameObject.Find("Title")?.GetComponentInChildren<Text>()?.text ?? "";
            Check(AquaLayout.Scene == L && back != null && back.sprite != null && back.sprite.name == L.back && rt != null && rt.height == L.viewH
                  && fish.Count == Game.Data.aquarium.Count && outside == 0 && title.Contains($"{AquaTank.Used(Game.Data)}/{Game.I.Capacity}칸"),
                $"level {level} {L.name}: art {back?.sprite?.name}, view {rt?.width}x{rt?.height} (want h {L.viewH}), {fish.Count} fish, {outside} outside the water, title '{title}'");
            foreach (var f in fish.OrderByDescending(x => x.DrawnPx))
                Log($"  drawn {f.Data.speciesId} {f.Data.sizeCm:0}cm {AquaTank.ClassNames[AquaTank.ClassOf(f.Data)]}: {f.DrawnPx:0} px (sprite {f.SpritePx} x{f.Scale:0.00}) at {f.Pos.x:0.0},{f.Pos.y:0.0}");
        }

        IEnumerator FeedPellets(AquaFeed feed, string shot)
        {
            var L = Lay;
            var c = feed.BagCentre("feed_basic");
            var over = new Vector2(L.GlassCentre.x - 2f, L.surfaceY + 1.3f);
            int eaten0 = feed.Eaten;
            Down(c);
            yield return null;
            yield return Drag(c, over, 0.7f);
            yield return new WaitForSeconds(0.2f);
            bool pouring = feed.Pouring;
            float t0 = Time.time;
            bool shot1 = false;
            while (Time.time - t0 < 14f && (feed.Eaten - eaten0 < 3 || Time.time - t0 < 2.5f))
            {
                float k = Time.time - t0;
                PointerInput.SimPos = Scr(over + new Vector2(Mathf.Sin(k * Mathf.PI * 2f / 0.55f) * 2.2f, Mathf.Sin(k * 9f) * 0.15f));
                if (shot != null && !shot1 && feed.Eaten > eaten0 && k > 1.2f)
                {
                    shot1 = true;
                    yield return ShotAt(shot, feed.LastEatAt);
                }
                yield return null;
            }
            if (shot != null && !shot1) yield return ShotAt(shot, over);
            Up();
            yield return new WaitForSeconds(0.6f);
            Check(pouring && feed.Eaten > eaten0, $"{L.name}: the bag poured over the water (at y {over.y:0.0}), pellets eaten {feed.Eaten - eaten0}");
        }

        IEnumerator DropShrimp(AquariumScene scene, AquaFeed feed, CaughtFish eater)
        {
            var L = Lay;
            var tf = scene.TankOf(eater);
            PlaceFish(scene, eater, new Vector2(L.GlassCentre.x + 1.5f, L.surfaceY - 2.6f), false);
            var pick = feed.PickPoint("feed_shrimp");
            int g0 = feed.Grabs, drops0 = feed.Drops;
            Down(pick);
            yield return null;
            yield return null;
            bool holding = feed.Holding;
            Func<Vector2> overIt = () => new Vector2(tf.Mouth.x + 0.4f, L.surfaceY + 1.4f);
            yield return Carry(pick, overIt, 0.9f);
            yield return HoldAt(overIt, 0.3f);
            Up();
            float t0 = Time.time;
            while (feed.Grabs == g0 && Time.time - t0 < 10f) yield return null;
            Check(holding && feed.Drops == drops0 + 1 && feed.Grabs > g0,
                $"{L.name}: a shrimp picked from the tub and dropped over the water: grabbed by {feed.LastGrabBy?.Data.speciesId} in {Time.time - t0:0.0} s");
            yield return new WaitForSeconds(0.8f);
        }

        IEnumerator CleanBits(AquariumScene scene)
        {
            var L = Lay;
            var d = Game.Data;
            var clean = scene.Clean;
            AquaTank.SetAll(0.8f, 0.3f);
            yield return new WaitForSeconds(1.2f);
            // ---- the sponge: three rows across the left of the glass
            var sh = clean.ToolHome(AquaTank.SpongeId);
            float x0 = L.glassL + 0.6f, x1 = L.glassL + 5f, y = L.GlassCentre.y + 1f;
            int st0 = clean.Stamps;
            Down(sh);
            yield return null;
            yield return null;
            yield return Carry(sh, () => new Vector2(x0, y), 0.45f);
            for (int r = 0; r < 3; r++)
            {
                var a = new Vector2(r % 2 == 0 ? x0 : x1, y - r * 0.6f);
                var b = new Vector2(r % 2 == 0 ? x1 : x0, y - r * 0.6f);
                yield return Drag(a, b, 0.45f);
                if (r < 2) yield return Drag(b, new Vector2(b.x, y - (r + 1) * 0.6f), 0.06f);
            }
            float wiped = clean.AlgaeCover(new Rect(x0 + 0.8f, y - 1.2f, x1 - x0 - 1.6f, 1.2f));
            float other = clean.AlgaeCover(new Rect(L.glassR - 5f, y - 1.2f, 3.4f, 1.2f));
            Up();
            yield return new WaitForSeconds(0.5f);
            Check(clean.Stamps - st0 > 30 && wiped < 0.1f && other > 0.4f,
                $"{L.name}: the sponge wiped a band ({clean.Stamps - st0} stamps): algae there {wiped * 100f:0}%, elsewhere {other * 100f:0}% ({AquaTank.AW}x{AquaTank.AH} map)");
            // ---- the net: bits scooped
            var nh = clean.ToolHome(AquaTank.NetId);
            int sc0 = clean.Scooped;
            Down(nh);
            yield return null;
            yield return null;
            Vector2 cur = nh;
            float until = Time.time + 12f;
            while (clean.Scooped - sc0 < 2 && Time.time < until)
            {
                var list = clean.BitPositions.ToList();
                if (list.Count == 0) { yield return null; continue; }
                var c = cur;
                var bp = list.OrderBy(p => (p - c).sqrMagnitude).First();
                float side = cur.x <= bp.x ? -1f : 1f;
                var pa = bp + new Vector2(side * 1.3f, 0f);
                var pb = bp - new Vector2(side * 1.3f, 0f);
                yield return Carry(cur, () => pa, 0.25f);
                yield return Drag(pa, pb, 0.28f);
                cur = pb;
            }
            Up();
            yield return new WaitForSeconds(0.5f);
            Check(clean.Scooped - sc0 >= 2, $"{L.name}: the net scooped {clean.Scooped - sc0} bits");
            // ---- the siphon: a stretch of gravel
            var ph = clean.ToolHome(AquaTank.SiphonId);
            float g0x = L.glassL + 1.2f, g1x = L.glassL + 6f;
            Func<float, Vector2> onGravel = x => new Vector2(x, AquaTank.GravelTop(x) + 23f / 16f);
            float before = clean.DirtUnder(g0x + 0.6f, g1x - 0.6f);
            int ss0 = clean.SiphonStamps;
            Down(ph);
            yield return null;
            yield return null;
            var high = new Vector2(g0x, L.GlassCentre.y);
            yield return Carry(ph, () => high, 0.5f);
            yield return Carry(high, () => onGravel(g0x), 0.3f);
            float s0 = Time.time;
            while (Time.time - s0 < 2.4f)
            {
                PointerInput.SimPos = Scr(onGravel(Mathf.Lerp(g0x, g1x, (Time.time - s0) / 2.4f)));
                yield return null;
            }
            float after = clean.DirtUnder(g0x + 0.6f, g1x - 0.6f);
            Up();
            yield return new WaitForSeconds(0.5f);
            Check(clean.SiphonStamps > ss0 && after < 0.05f && before > 0.5f, $"{L.name}: the siphon along the gravel: dirt there {before:0.00} -> {after:0.00} ({AquaTank.BW}x{AquaTank.BH} map)");
            AquaTank.SetAll(0f, 0f);
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Decorate(AquaDecor decor, (string item, string slot)[] items)
        {
            decor.Enter();
            yield return new WaitForSeconds(0.4f);
            int markers = decor.MarkersShown;
            foreach (var (item, slot) in items) yield return FromTray(decor, item, slot, null);
            decor.Exit();
            yield return new WaitForSeconds(0.5f);
            Toast.Clear();
            Check(markers == AquaTank.SlotCount(Game.Data.tankLevel) && items.All(x => AquaTank.ItemAt(x.slot) == x.item && (x.slot == "light" ? decor.LightShown : decor.SpriteIn(x.slot) != "")),
                $"{Lay.name}: decoration mode {markers} slot markers (slots {AquaTank.SlotCount(Game.Data.tankLevel)}), placed {string.Join(", ", items.Select(x => x.slot + "=" + AquaTank.ItemAt(x.slot)))}");
        }

        IEnumerator TanksTest()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Game.Data;
            d.coins = Math.Max(d.coins, 500000);
            d.feedTornOnce = true;
            d.tank = new TankCare();
            AquaTank.Ensure(d);
            d.tank.owned.Add(AquaTank.NetId);
            d.tank.owned.Add(AquaTank.SiphonId);
            foreach (var x in AquaTank.Decors) d.tank.owned.Add(x.id);

            // ---- the size classes, the tanks, the drawn size
            Check(AquaTank.ClassOf(39.9f) == 0 && AquaTank.ClassOf(40f) == 1 && AquaTank.ClassOf(99.9f) == 1 && AquaTank.ClassOf(100f) == 2 && AquaTank.ClassOf(200f) == 3,
                "size classes: 소형 <40cm, 중형 40-100, 대형 100-200, 초대형 200+ (1 / 2 / 4 / 8칸)");
            foreach (var t in GameDatabase.Tanks)
                Log($"tank {t.id} {t.name}: {AquaTank.LimitText(t)}, price {t.price}; art {AquaLayout.Of(t.level).glassW}x{AquaLayout.Of(t.level).glassH} glass, view {AquaLayout.Of(t.level).viewH}");
            foreach (var cm in new[] { 6f, 10f, 20f, 30f, 45f, 70f, 100f, 150f, 240f, 300f, 450f })
                Log($"drawn length {cm:0}cm -> {AquaTank.DrawnPx(cm):0.0} px ({AquaTank.DrawnPx(cm) / AquaTank.DrawnPx(30f):0.00}x a 30cm fish)");
            float ratio = AquaTank.DrawnPx(240f) / AquaTank.DrawnPx(30f);
            Check(ratio >= 3f && ratio <= 4f && AquaTank.DrawnPx(6f) >= AquaTank.MinPx, $"drawn size: a 240cm arapaima {ratio:0.00}x a 30cm crucian carp; a 6cm fish {AquaTank.DrawnPx(6f):0} px");

            // ---- refusals (the model): too big, no room, the 아쿠아리움's two 초대형
            d.tankLevel = 0;
            d.aquarium.Clear();
            var carp = Catch("carp", 62f, "lake");
            var fit = AquaTank.CanAdd(d, carp, out var need);
            string msg = AquaTank.RefuseText(fit, need);
            Check(fit == AquaTank.Fit.TooBig && need != null && need.id == "tank_1" && msg == "이 수조엔 너무 커요 · 중형 수조가 필요해요" && !Game.I.AddToAquarium(carp) && d.aquarium.Count == 0,
                $"작은 어항 + a 62cm carp: '{msg}', not put in");
            for (int i = 0; i < 6; i++) Game.I.AddToAquarium(Catch("bluegill", 15f + i, "lake"));
            var small = Catch("pale_chub", 12f, "stream");
            fit = AquaTank.CanAdd(d, small, out need);
            msg = AquaTank.RefuseText(fit, need);
            Check(d.aquarium.Count == 6 && fit == AquaTank.Fit.NoRoom && msg == "자리가 부족해요 · 중형 수조가 필요해요" && !Game.I.AddToAquarium(small) && Game.I.AquariumFull,
                $"작은 어항 full (6/6칸) + a small fish: '{msg}'");
            d.tankLevel = 3;
            d.aquarium.Clear();
            bool a1 = Game.I.AddToAquarium(Catch("arapaima", 240f, "swamp")), a2 = Game.I.AddToAquarium(Catch("sturgeon", 230f, "ice"));
            var third = Catch("blue_marlin", 250f, "ocean");
            fit = AquaTank.CanAdd(d, third, out need);
            msg = AquaTank.RefuseText(fit, need);
            Check(a1 && a2 && fit == AquaTank.Fit.NoRoom && need?.id == "tank_4" && msg == "자리가 부족해요 · 황금 대수족관이 필요해요" && AquaTank.Used(d) == 16,
                $"아쿠아리움: two 초대형 in ({AquaTank.Used(d)}/40칸), a third: '{msg}'");
            d.tankLevel = 4;
            Check(Game.I.AddToAquarium(third) && AquaTank.HugeCount(d) == 3, $"황금 대수족관: a third 초대형 goes in ({AquaTank.Used(d)}/64칸)");
            d.tankLevel = 4;
            d.aquarium.Clear();
            for (int i = 0; i < 8; i++) Game.I.AddToAquarium(Catch("great_white", 400f, "ocean"));
            fit = AquaTank.CanAdd(d, Catch("bluegill", 20f, "lake"), out need);
            Check(AquaTank.Used(d) == 64 && fit == AquaTank.Fit.NoRoom && need == null && AquaTank.RefuseText(fit, need) == "자리가 부족해요 · 물고기를 팔아 자리를 비워요",
                $"황금 대수족관 full: '{AquaTank.RefuseText(fit, need)}'");

            // ---- an older save over the new limits: every fish kept, the extra ones marked
            var old = JsonUtility.FromJson<SaveData>("{\"aquaVer\":1,\"tankLevel\":0,\"aquarium\":[" + string.Join(",", new[]
            {
                ("crucian_carp", 25f), ("bluegill", 18f), ("carp", 60f), ("pale_chub", 14f), ("cherry_salmon", 28f),
            }.Select((x, i) => $"{{\"uid\":\"old{i}\",\"speciesId\":\"{x.Item1}\",\"sizeCm\":{x.Item2},\"value\":50,\"baseCm\":{x.Item2},\"baseValue\":50,\"fullness\":0.6}}")) + "]}");
            AquaCare.Ensure(old);
            var overOld = AquaTank.Over(old);
            Check(old.capVer == 1 && old.aquarium.Count == 5 && overOld.Count == 1 && overOld.First().speciesId == "carp" && AquaTank.CanAdd(old, Catch("smelt", 10f, "ice"), out _) == AquaTank.Fit.NoRoom,
                $"an old 작은 어항 save with 5 fish (a 60cm carp): all kept, over {overOld.Count} ({string.Join(",", overOld.Select(f => f.speciesId))}), {AquaTank.Used(old)}칸 used");

            // ---- the maps follow a bigger tank (resampled, the level kept)
            d.tankLevel = 1;
            d.aquarium.Clear();
            AquaTank.SetAll(0f, 0f);
            var m = AquaTank.AlgaeMap;
            for (int i = 0; i < m.Length; i++) m[i] = i % AquaTank.AW < AquaTank.AW / 2 ? 0f : 0.6f;
            AquaTank.MapsChanged();
            AquaTank.Commit();
            float lvl1 = d.tank.algae;
            int n1 = m.Length;
            d.tankLevel = 4;
            var m4 = AquaTank.AlgaeMap;
            AquaTank.Commit();
            var back4 = AquaTank.Decode(d.tank.algaeMap, AquaTank.AW * AquaTank.AH, -1f);
            Check(n1 == 436 * 164 && m4.Length == 880 * 332 && Near(d.tank.algae, lvl1, 0.01f) && m4[10] == 0f && m4[879] > 0.5f && back4[879] > 0.5f,
                $"the algae map follows the tank: {n1} -> {m4.Length} px, level {lvl1:0.000} -> {d.tank.algae:0.000}, saved at the new size");
            AquaTank.SetAll(0f, 0f);
            d.tankLevel = 0;

            // ---- every level rendered, stocked
            for (int level = 0; level < AquaLayout.Levels; level++)
            {
                yield return OpenTank(level, level != 3);
                var scene = FindAnyObjectByType<AquariumScene>();
                CheckRendered(level);
                if (level == 3)
                {
                    // the arapaima beside a crucian carp: its real size
                    var ara = d.aquarium.First(f => f.speciesId == "arapaima");
                    var cru = d.aquarium.First(f => f.speciesId == "crucian_carp");
                    PlaceFish(scene, ara, new Vector2(-8f, 3.5f), false);
                    PlaceFish(scene, cru, new Vector2(8f, 3.5f), true);
                    int k = 0;
                    foreach (var f in d.aquarium.Where(x => x != ara && x != cru)) PlaceFish(scene, f, new Vector2(-18f + (k++) * 5f, -2f + (k % 2) * 1.5f), k % 2 == 0);
                    yield return new WaitForSeconds(0.3f);
                    float r = scene.TankOf(ara).DrawnPx / scene.TankOf(cru).DrawnPx;
                    Check(r >= 3f && r <= 4f, $"아쿠아리움: the 240cm arapaima drawn {scene.TankOf(ara).DrawnPx:0} px, the 30cm crucian carp {scene.TankOf(cru).DrawnPx:0} px ({r:0.00}x)");
                    yield return ShotAt("tank_3", (scene.TankOf(ara).Pos + scene.TankOf(cru).Pos) * 0.5f);
                }
                else
                {
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot("tank_" + level);
                }

                if (level == 0)
                {
                    // ---- the catch card's 보관 button in a small tank: a too-big fish
                    var canvas = GameObject.Find("AquariumUI").GetComponent<Canvas>();
                    CatchPopup.Choice? chosen = null;
                    var big = Catch("carp", 62f, "lake");
                    CatchPopup.Show(canvas, big, new CatchReport { xpGained = 24 }, c => chosen = c);
                    yield return new WaitForSeconds(0.6f);
                    var info = GameObject.Find("KeepInfo")?.GetComponent<Text>();
                    var keepBtn = GameObject.Find("KeepButton")?.GetComponent<Button>();
                    if (keepBtn != null) keepBtn.onClick.Invoke();
                    yield return new WaitForSeconds(0.25f);
                    yield return Shot("refuse");
                    string label = keepBtn?.GetComponentInChildren<Text>()?.text ?? "";
                    Check(info != null && info.text.Contains("이 수조엔 너무 커요 · 중형 수조가 필요해요") && label == "너무 커요" && chosen == null && GameObject.Find("CatchCard") != null && !d.aquarium.Contains(big),
                        $"catch card, a 62cm carp for the 작은 어항: button '{label}', '{System.Text.RegularExpressions.Regex.Replace(info?.text ?? "", "<.*?>", "")}', a tap keeps the card (chosen {chosen?.ToString() ?? "none"})");
                    var card = GameObject.Find("CatchCard");
                    if (card != null) Destroy(card.transform.parent.gameObject);
                    yield return new WaitForSeconds(0.3f);
                    // a small one fits: 보관
                    var ok1 = Catch("smelt", 11f, "ice");
                    d.aquarium.RemoveAt(d.aquarium.Count - 1); // (one place free)
                    chosen = null;
                    CatchPopup.Show(canvas, ok1, new CatchReport { xpGained = 8 }, c => chosen = c);
                    yield return new WaitForSeconds(0.5f);
                    info = GameObject.Find("KeepInfo")?.GetComponent<Text>();
                    keepBtn = GameObject.Find("KeepButton")?.GetComponent<Button>();
                    string label2 = keepBtn?.GetComponentInChildren<Text>()?.text ?? "";
                    string info2 = info != null ? info.text : "";
                    if (keepBtn != null) keepBtn.onClick.Invoke();
                    yield return new WaitForSeconds(0.3f);
                    Check(label2 == "보관 +1칸" && info2.Contains("5/6칸") && chosen == CatchPopup.Choice.Keep,
                        $"catch card, a small fish with room: '{label2}', '{info2}', chosen {chosen}");
                    Game.I.AddToAquarium(ok1);
                    scene.AfterTimeJump();
                    yield return new WaitForSeconds(0.4f);

                    // ---- the features in the smallest tank
                    var feed = AquaFeed.Current;
                    yield return FeedPellets(feed, null);
                    yield return DropShrimp(scene, feed, d.aquarium.First(f => f.speciesId == "mandarin_fish"));
                    yield return CleanBits(scene);
                    yield return Decorate(scene.Decor, new[] { ("decor_driftwood", "back_c"), ("decor_chest", "mid_l"), ("decor_fern", "front_l"), ("decor_light_day", "light") });

                    // ---- an older save's fish over the limits: kept, marked in the title and its info window
                    var oldCarp = Catch("carp", 60f, "lake");
                    d.aquarium.Add(oldCarp);
                    AquaCare.OnAdded(oldCarp);
                    scene.AfterTimeJump();
                    yield return new WaitForSeconds(0.5f);
                    scene.ShowInfo(oldCarp);
                    yield return new WaitForSeconds(0.7f);
                    var sc = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "SizeClass");
                    var title = GameObject.Find("Title")?.GetComponentInChildren<Text>()?.text ?? "";
                    yield return Shot("info_over");
                    Check(sc != null && sc.text.Contains("중형 2칸 · 이 수조엔 너무 커요") && title.Contains("(넘침)") && d.aquarium.Contains(oldCarp),
                        $"an over-limit fish kept: info '{System.Text.RegularExpressions.Regex.Replace(sc?.text ?? "", "<.*?>", "")}', title '{System.Text.RegularExpressions.Regex.Replace(title, "<.*?>", "")}'");
                    CloseDialogs();
                    yield return new WaitForSeconds(0.3f);
                }
                else if (level == 2)
                {
                    // ---- the shop's 수조 확장 rows; the 아쿠아리움 bought here: the scene sets up the new tank
                    var canvas = GameObject.Find("AquariumUI").transform;
                    ShopUI.TankSub = ShopUI.SubTanks;
                    ShopUI.Open(canvas, ItemKind.Tank);
                    yield return new WaitForSeconds(0.8f);
                    var rows = GameDatabase.Tanks.Select(t => GameObject.Find(t.id)).ToList();
                    var stats = rows.Select(r => r == null ? "" : string.Join(" ", r.GetComponentsInChildren<Text>().Select(x => x.text))).ToList();
                    // (scrolled down: the tanks still to buy)
                    var scroll = GameObject.Find("Shop")?.GetComponentInChildren<ScrollRect>();
                    if (scroll != null) scroll.verticalNormalizedPosition = 0f;
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("shop_tanks");
                    Check(rows.All(r => r != null) && stats[0].Contains("6칸 · 소형(40cm 미만)까지") && stats[1].Contains("12칸 · 중형") && stats[3].Contains("초대형(200cm 이상)까지 2마리") && stats[4].Contains("64칸")
                          && GameObject.Find("TankSummary") != null,
                        $"shop 수조 확장: {string.Join(" | ", stats.Select(s => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "")))}");
                    int coins0 = d.coins;
                    var fishBefore = d.aquarium.Select(f => f.uid).ToList();
                    var buy = GameObject.Find("tank_3")?.GetComponentInChildren<Button>();
                    if (buy != null) buy.onClick.Invoke();
                    yield return new WaitForSeconds(0.5f);
                    Check(d.tankLevel == 3 && coins0 - d.coins == 45000 && AquaLayout.Scene.level == 2, $"bought the 아쿠아리움 ({coins0 - d.coins}): the open scene keeps its tank while the shop is up");
                    CloseShop();
                    yield return new WaitForSeconds(3.2f);
                    var s2 = FindAnyObjectByType<AquariumScene>();
                    Check(s2 != null && AquaLayout.Scene != null && AquaLayout.Scene.level == 3 && PixelView.Current.Target.height == 432 && d.aquarium.Select(f => f.uid).SequenceEqual(fishBefore),
                        $"shop closed: the scene set up the 아쿠아리움 (view {PixelView.Current?.Target?.height}), the {d.aquarium.Count} fish moved along");
                }
                else if (level == 4)
                {
                    // ---- the info window's size class
                    var gw = d.aquarium.First(f => f.speciesId == "great_white");
                    scene.ShowInfo(gw);
                    yield return new WaitForSeconds(0.7f);
                    var sc = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "SizeClass");
                    yield return Shot("info_class");
                    Check(sc != null && sc.text.Contains("초대형 8칸") && !sc.text.Contains("너무"), $"info window: '{System.Text.RegularExpressions.Regex.Replace(sc?.text ?? "", "<.*?>", "")}'");
                    CloseDialogs();
                    yield return new WaitForSeconds(0.3f);

                    // ---- the features in the largest tank: decorated, then fed
                    yield return Decorate(scene.Decor, new[]
                    {
                        ("decor_ship", "back_c"), ("decor_cabomba", "back_l"), ("decor_driftwood", "back_r"), ("decor_wheel", "mid_l"), ("decor_chest", "mid_r"),
                        ("decor_bubbler", "mid_rr"), ("decor_fern", "front_l"), ("decor_rotala", "front_c"), ("decor_coral_fan", "front_r"), ("decor_light_day", "light"),
                    });
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot("decor_l4");
                    var feed = AquaFeed.Current;
                    yield return FeedPellets(feed, "feed_l4");
                    yield return DropShrimp(scene, feed, d.aquarium.First(f => f.speciesId == "largemouth_bass"));
                    yield return CleanBits(scene);
                }
            }

            Log($"done: {ok} ok, {fail} failed; coins {d.coins}, tank {Game.I.Tank.name} {AquaTank.Used(d)}/{Game.I.Capacity}칸");
            Game.I.Save();
            Application.Quit();
        }
    }
}
