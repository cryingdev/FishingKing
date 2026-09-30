using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>The -fkaqua clean / decor scenarios (see the switch list in AquaPilot.cs).</summary>
    public partial class AquaPilot
    {
        static void ClickIn(string row, string button = "Buy")
        {
            var go = GameObject.Find(row);
            var b = go != null ? go.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == button) : null;
            if (b != null) b.onClick.Invoke();
            else Log($"button not found: {row}/{button}");
        }

        static void CloseShop()
        {
            var shop = GameObject.Find("Shop");
            if (shop != null) Destroy(shop.transform.parent.gameObject);
        }

        static bool Near(float a, float b, float eps) => Mathf.Abs(a - b) <= eps;

        static bool Near(Color a, Color b, float eps) => Near(a.r, b.r, eps) && Near(a.g, b.g, eps) && Near(a.b, b.b, eps);

        /// <summary>The gravel's dirt level over a world x span (straight from the map).</summary>
        static float DirtCols(float x0, float x1)
        {
            var m = AquaTank.DirtMap;
            int c0 = Mathf.Clamp(Mathf.FloorToInt((x0 + 13.625f) * 16f), 0, AquaTank.BW - 1), c1 = Mathf.Clamp(Mathf.FloorToInt((x1 + 13.625f) * 16f), c0 + 1, AquaTank.BW);
            double s = 0;
            int n = 0;
            for (int y = 0; y < AquaTank.BH; y++)
            for (int x = c0; x < c1; x++)
            {
                s += m[y * AquaTank.BW + x];
                n++;
            }
            return (float)(s / n);
        }

        static string IncomeText()
        {
            var go = GameObject.Find("Income");
            var t = go != null ? go.GetComponentsInChildren<Text>().FirstOrDefault(x => x.text.Contains("관람")) : null;
            return t != null ? t.text : "";
        }

        /// <summary>
        /// The held sponge wiped over [x0, x1] row by row (rows 0.6 apart: the pad's core overlaps), top to bottom or
        /// bottom to top; it gets there along the ledge under the glass (off it: no stray streak).
        /// </summary>
        IEnumerator WipeRaster(AquaClean clean, Vector2 from, float x0, float x1, string shot, bool upward = false)
        {
            float top = AquaClean.GlassT - 5f / 16f, bottom = AquaClean.GlassB + 5f / 16f;
            var under = new Vector2(x0, AquaClean.GlassB - 0.3f);
            yield return Carry(from, () => under, 0.4f);
            var start = new Vector2(x0, upward ? bottom : top);
            yield return Drag(under, start, upward ? 0.08f : 0.3f);
            var cur = start;
            int rows = Mathf.CeilToInt((top - bottom) / 0.6f) + 1;
            for (int r = 0; r < rows; r++)
            {
                float y = upward ? Mathf.Min(top, bottom + r * 0.6f) : Mathf.Max(bottom, top - r * 0.6f);
                var a = new Vector2(r % 2 == 0 ? x0 : x1, y);
                var b = new Vector2(r % 2 == 0 ? x1 : x0, y);
                if ((a - cur).sqrMagnitude > 1e-6f) yield return Drag(cur, a, 0.06f);
                if (r == rows - 1 && shot != null)
                {
                    yield return Drag(a, b, 0.45f, 0.55f);
                    yield return ShotAt(shot, clean.HeldPos);
                    yield return Drag(Vector2.Lerp(a, b, 0.55f), b, 0.2f);
                }
                else yield return Drag(a, b, 0.42f);
                cur = b;
            }
        }

        // ------------------------------------------------------------------ -fkaqua clean
        IEnumerator CleanTest()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Game.Data;
            d.coins = Math.Max(d.coins, 200000);
            d.tankLevel = 1; // (the 중형 수조: the coordinates below are its art's)
            foreach (var id in new[] { "tank_1" }) if (!d.ownedItems.Contains(id)) d.ownedItems.Add(id);
            d.feedTornOnce = true;

            // ---- a new tank state; an old save without one
            d.tank = new TankCare();
            AquaTank.Ensure(d);
            var t = d.tank;
            Check(AquaTank.Owns(AquaTank.SpongeId) && !AquaTank.Owns(AquaTank.NetId) && !AquaTank.Owns(AquaTank.SiphonId) && t.algae == 0f && t.debris == 0f && t.dirt == 0f && t.places.Count == 0,
                $"a new tank: clean, the sponge given free, no net / siphon (owned {string.Join(",", t.owned)})");
            var old = JsonUtility.FromJson<SaveData>("{\"aquaVer\":1,\"coins\":5,\"tankLevel\":1}");
            AquaTank.Ensure(old);
            Check(old.tank != null && old.tank.ver == 1 && old.tank.owned.SequenceEqual(new[] { AquaTank.SpongeId }) && old.tank.algae == 0f && old.tank.places.Count == 0 && old.tank.at > 0,
                $"an old save without the tank reads clean with the free sponge (ver {old.tank?.ver}, owned {string.Join(",", old.tank?.owned ?? new List<string>())})");
            AquaTank.Ensure(d);

            d.aquarium.Clear();
            foreach (var (id, cm, st) in new[]
            {
                ("crucian_carp", 25f, "lake"), ("bluegill", 18f, "lake"), ("carp", 60f, "lake"), ("cherry_salmon", 28f, "stream"),
                ("rainbow_trout", 45f, "stream"), ("pale_chub", 14f, "stream"),
            }) AddFish(id, cm, st);
            foreach (var f in d.aquarium) f.fullness = 1f;
            var basic = AquaCare.Feed("feed_basic");
            // (the feed on the ledge, as a player would have it)
            AquaCare.Stock("feed_basic").bags = 2;
            AquaCare.Stock("feed_premium").bags = 1;
            AquaCare.Stock("feed_shrimp").pieces = 10;

            // ---- the real-time model: 24 h with 6 fish (x1.25), no plants
            AquaTank.Rates(d, out float ra, out float rd, out float rb);
            float ff = AquaTank.FishFactor(6);
            Check(Near(ff, 1.25f, 1e-4f) && Near(ra * 3600f, ff / AquaTank.AlgaeHours, 1e-6f) && Near(rd * 3600f, ff / AquaTank.DebrisHours, 1e-6f) && Near(rb * 3600f, ff / AquaTank.DirtHours, 1e-6f),
                $"rates with 6 fish (x{ff:0.00}): algae {ra * 360000f:0.00}%/h, debris {rd * 360000f:0.00}%/h, dirt {rb * 360000f:0.00}%/h");
            AquaCare.FastForward(d, 24f, basic);
            float ea = Mathf.Min(1f, ff * 24f / AquaTank.AlgaeHours), ed = Mathf.Min(1f, ff * 24f / AquaTank.DebrisHours), eb = Mathf.Min(1f, ff * 24f / AquaTank.DirtHours);
            Check(Near(t.algae, ea, 0.01f) && Near(t.debris, ed, 0.01f) && Near(t.dirt, eb, 0.01f),
                $"24 h with 6 fish: algae {t.algae:0.000} (expected {ea:0.000}), debris {t.debris:0.000} ({ed:0.000}), dirt {t.dirt:0.000} ({eb:0.000})");
            float pen = AquaTank.Penalty(d), expectPen = AquaTank.MaxPenalty * (AquaTank.WAlgae * ea + AquaTank.WDebris * ed + AquaTank.WDirt * eb);
            float own = d.aquarium.Sum(AquaCare.IncomeF);
            Check(Near(pen, expectPen, 0.005f) && Near(AquaTank.IncomePerMin(d), own * (1f - pen), 0.01f) && Game.I.IncomePerMinute == Mathf.RoundToInt(own * (1f - pen)),
                $"the dirt costs -{pen * 100f:0.0}% (expected -{expectPen * 100f:0.0}%): {own:0.00}/min -> {AquaTank.IncomePerMin(d):0.00}/min, panel {Game.I.IncomePerMinute}/min");
            var f0 = d.aquarium[0];
            double avg = AquaTank.AvgMult(d, f0, t.at, t.at + 24 * 3600), now = AquaTank.Mult(d, f0);
            Check(avg < now && avg > 0.75, $"a span ahead is banked at its average dirt: x{avg:0.0000} over the next 24 h vs x{now:0.0000} now");

            // ---- a long absence: filthy, but the catch-up stops at 7 days
            t.at -= 30L * 86400;
            AquaCare.Advance(d, SaveSystem.Now);
            Check(t.algae >= 0.999f && t.debris >= 0.999f && t.dirt >= 0.999f && Math.Abs(t.at - SaveSystem.Now) <= 1 && Near(AquaTank.Penalty(d), AquaTank.MaxPenalty, 0.001f),
                $"30 days away: algae {t.algae:0.000} debris {t.debris:0.000} dirt {t.dirt:0.000} (capped), penalty -{AquaTank.Penalty(d) * 100f:0}%");

            // ---- uneaten food rots on the gravel where it lay
            AquaTank.SetAll(0.1f, 0.1f);
            float under0 = DirtCols(1.8f, 2.2f), far0 = DirtCols(-10f, -9f);
            AquaTank.OnUneaten(new Vector2(2f, -3.5f), false);
            float under1 = DirtCols(1.8f, 2.2f), far1 = DirtCols(-10f, -9f);
            Check(under1 - under0 > 0.05f && Near(far1, far0, 1e-4f) && Near(t.debris, 0.1f + AquaTank.PelletDebris, 1e-4f),
                $"a dissolved pellet dirties the gravel under it: {under0:0.000} -> {under1:0.000} (far {far0:0.000} -> {far1:0.000}), debris {t.debris:0.0000}");
            AquaTank.OnUneaten(new Vector2(-6f, -3.5f), true);
            float live1 = DirtCols(-6.2f, -5.8f);
            Check(live1 - far0 > 0.25f, $"a rotten sardine / shrimp much more: {far0:0.000} -> {live1:0.000}");

            // ---- a dirty tank (the debris a hair under the algae: the top hint's tie would flip with the seconds of growth)
            AquaTank.SetAll(0.8f, 0.78f);
            Game.I.Save();
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.4f);
            var scene = FindAnyObjectByType<AquariumScene>();
            var clean = scene.Clean;
            int bits = clean.BitsAlive;
            Check(bits == AquaTank.DebrisCount(d) && bits == 16 && clean.VisibleAlgae > AquaTank.AW * AquaTank.AH * 2 / 5 && clean.VisibleDirt > 2000 && clean.MurkLevel == 3,
                $"filthy tank: {bits} bits floating, algae {clean.VisibleAlgae} px ({clean.VisibleAlgae * 100f / (AquaTank.AW * AquaTank.AH):0}% of the glass), dirt {clean.VisibleDirt} px, murk {clean.MurkLevel}");
            string inc = IncomeText();
            var tf0 = scene.TankOf(d.aquarium[0]);
            Check(inc.Contains("청소 -20%") && scene.TopHint.Contains("이끼") && tf0 != null && tf0.Tint.b < 0.9f,
                $"the panel: '{inc.Replace("\n", " / ")}', top hint '{scene.TopHint}', fish dulled {tf0?.Tint}");
            yield return new WaitForSeconds(0.4f);
            yield return Shot("dirty");

            // ---- the sponge: a tap says how
            var sh = clean.ToolHome(AquaTank.SpongeId);
            yield return Tap(sh);
            yield return new WaitForSeconds(0.35f);
            Check(clean.HintShown == "유리를 문질러 이끼를 닦아요" && clean.ToolState(AquaTank.SpongeId) != "Held", $"a tap on the sponge: '{clean.HintShown}'");
            yield return new WaitForSeconds(1.8f);

            // ---- wiping the left half of the glass, bottom to top
            float a0 = t.algae;
            Down(sh);
            yield return null;
            yield return null;
            Check(clean.HeldId == AquaTank.SpongeId && scene.TopHint != "" , $"pressed: the sponge is up ({clean.HeldId})");
            yield return WipeRaster(clean, sh, -13.3f, -0.3f, "wiping", true);
            float left = clean.AlgaeCover(new Rect(-13.6f, -4.6f, 13.2f, 10.2f)), right = clean.AlgaeCover(new Rect(0.9f, -4.6f, 12.7f, 10.2f));
            Check(clean.Wiping && left < 0.005f && right > 0.4f && t.algae < a0 * 0.6f && t.algae > a0 * 0.35f && clean.Squeaks > 0 && clean.Stamps > 100,
                $"wiped the left half: algae there {left * 100f:0.0}% of the pixels, the right half {right * 100f:0}%, level {a0:0.000} -> {t.algae:0.000}, stamps {clean.Stamps}, squeaks {clean.Squeaks}, meter '{clean.MeterShown}'");
            Check(!clean.AlgaeAt(new Vector2(-6f, 1f)) && (clean.AlgaeAt(new Vector2(7.3f, 1.3f)) || clean.AlgaeCover(new Rect(6.8f, 0.8f, 1f, 1f)) > 0.2f),
                "pixel-exact: the wiped glass shows through, the unwiped keeps its film");
            Up();
            yield return new WaitForSeconds(0.6f);
            Check(clean.ToolState(AquaTank.SpongeId) == "Rest" && clean.Celebrations == 0 && t.algaeMap.Length > 0,
                $"let go: the sponge back on the ledge; the wiped map saved ({t.algaeMap.Length} chars)");

            // ---- the shop's 청소 section: the net and the siphon
            var canvas = GameObject.Find("AquariumUI").transform;
            ShopUI.TankSub = ShopUI.SubClean;
            ShopUI.Open(canvas, ItemKind.Tank);
            yield return new WaitForSeconds(0.6f);
            int c0 = d.coins;
            ClickIn(AquaTank.NetId);
            yield return new WaitForSeconds(0.4f);
            ClickIn(AquaTank.SiphonId);
            yield return new WaitForSeconds(0.4f);
            int paid = c0 - d.coins, price = AquaTank.Tool(AquaTank.NetId).price + AquaTank.Tool(AquaTank.SiphonId).price;
            Check(AquaTank.Owns(AquaTank.NetId) && AquaTank.Owns(AquaTank.SiphonId) && paid == price, $"bought the net and the siphon: paid {paid} (= {price})");
            yield return new WaitForSeconds(2.2f);
            yield return Shot("shop_clean");
            CloseShop();
            yield return new WaitForSeconds(0.8f);
            Check(clean.ToolState(AquaTank.NetId) == "Rest" && clean.ToolState(AquaTank.SiphonId) == "Rest", "both on the ledge now");

            // ---- the net: every bit scooped out
            var nh = clean.ToolHome(AquaTank.NetId);
            Down(nh);
            yield return null;
            yield return null;
            Check(clean.HeldId == AquaTank.NetId, "pressed: the net is up");
            Vector2 cur = nh;
            var dip = new Vector2(8f, 3f);
            yield return Carry(cur, () => dip, 0.5f);
            cur = dip;
            int sc0 = clean.Scooped;
            bits = clean.BitsAlive;
            bool shotNet = false;
            float until = Time.time + 45f;
            while (clean.BitsAlive > 0 && Time.time < until)
            {
                var list = clean.BitPositions.ToList();
                if (list.Count == 0)
                {
                    yield return null;
                    continue;
                }
                var c = cur;
                var bp = list.OrderBy(p => (p - c).sqrMagnitude).First();
                float side = cur.x <= bp.x ? -1f : 1f;
                var pa = bp + new Vector2(side * 1.3f, 0f);
                var pb = bp - new Vector2(side * 1.3f, 0f);
                yield return Carry(cur, () => pa, 0.22f);
                yield return Drag(pa, pb, 0.28f);
                cur = pb;
                if (!shotNet && clean.NetCount >= 3 && clean.HeldPos.y < 3.8f && clean.HeldPos.y > -1.5f)
                {
                    shotNet = true;
                    yield return ShotAt("scooping", clean.HeldPos);
                }
            }
            if (!shotNet) yield return ShotAt("scooping", clean.HeldPos);
            int caught = clean.Scooped - sc0;
            Check(caught == bits && clean.BitsAlive == 0 && AquaTank.DebrisCount(d) == 0 && clean.NetCount == caught,
                $"the net: scooped {caught}/{bits} bits (in the net {clean.NetCount}), debris {t.debris:0.000}, splashes {clean.Splashes}");
            Up();
            yield return new WaitForSeconds(0.5f);
            Check(t.debris < 0.001f && clean.HintShown.Contains("건졌어요") && clean.NetCount == 0 && clean.ToolState(AquaTank.NetId) == "Rest",
                $"let go: the net empties ('{clean.HintShown}'), debris {t.debris:0.000}");

            // ---- the siphon along the gravel, left to right (the wiped glass shows the gravel it leaves clean)
            var ph = clean.ToolHome(AquaTank.SiphonId);
            Func<float, Vector2> onGravel = x => new Vector2(x, AquaTank.GravelTop(x) + 23f / 16f); // (the mouth 20 px under the finger, 3 px over the gravel)
            Down(ph);
            yield return null;
            yield return null;
            Check(clean.HeldId == AquaTank.SiphonId, "pressed: the siphon is up");
            var high = new Vector2(12.5f, 1.5f);
            yield return Carry(ph, () => high, 0.35f);
            yield return Drag(high, new Vector2(-13.45f, 1.5f), 0.5f);
            yield return Carry(new Vector2(-13.45f, 1.5f), () => onGravel(-13.45f), 0.3f);
            float dirt0 = t.dirt;
            float s0 = Time.time;
            const float SweepT = 8f;
            bool shotS = false;
            while (true)
            {
                float k = Mathf.Clamp01((Time.time - s0) / SweepT);
                float sx = Mathf.Lerp(-13.45f, 13.45f, k);
                PointerInput.SimPos = Scr(onGravel(sx));
                if (!shotS && k >= 0.36f)
                {
                    shotS = true;
                    float done = clean.DirtUnder(-13.4f, sx - 1f), todo = clean.DirtUnder(sx + 1f, 13.4f);
                    Check(clean.SiphonWorking && done < 0.01f && todo > 0.5f, $"a third of the way: the gravel left of the siphon {done:0.000}, right {todo:0.000}, meter '{clean.MeterShown}'");
                    yield return ShotAt("siphon", clean.SiphonMouth);
                }
                if (k >= 1f) break;
                yield return null;
            }
            Check(clean.DirtUnder(-13.6f, 13.6f) < 0.005f && clean.SiphonStamps > 50, $"siphoned end to end: dirt {dirt0:0.000} -> {t.dirt:0.000}, stamps {clean.SiphonStamps}");
            Up();
            yield return new WaitForSeconds(0.6f);
            Check(t.dirt < 0.001f && clean.VisibleDirt == 0 && clean.HintShown == "바닥이 깨끗해졌어요" && clean.Celebrations == 0, $"let go: '{clean.HintShown}', dirt px {clean.VisibleDirt}");

            // ---- the rest of the glass: 반짝반짝!
            Down(sh);
            yield return null;
            yield return null;
            yield return WipeRaster(clean, sh, 0.3f, 13.3f, null);
            Up();
            float w0 = Time.time;
            while (clean.Celebrations == 0 && Time.time - w0 < 2f) yield return null;
            yield return new WaitForSeconds(0.3f);
            yield return Shot("sparkle");
            Check(clean.Celebrations == 1 && t.algae < 0.001f && t.dirt < 0.001f && t.debris < 0.001f && AquaTank.Penalty(d) < 0.001f && t.cleans == 1,
                $"the whole tank clean: 반짝반짝! (celebrations {clean.Celebrations}, cleans {t.cleans}), algae px {clean.VisibleAlgae}");
            yield return new WaitForSeconds(1.2f);
            Check(clean.MurkLevel == 0 && !IncomeText().Contains("청소 -") && Near(AquaTank.Mult(d, d.aquarium[0]), 1f, 1e-3f),
                $"clear water, full income: '{IncomeText().Replace("\n", " / ")}'");
            // a second go over clean glass: no second celebration
            Down(sh);
            yield return null;
            yield return Carry(sh, () => new Vector2(-4f, 1f), 0.4f);
            yield return Drag(new Vector2(-4f, 1f), new Vector2(4f, 1f), 0.4f);
            Up();
            yield return new WaitForSeconds(0.6f);
            Check(clean.Celebrations == 1, "wiping clean glass again: no second 반짝반짝");

            // ---- the maps' save: a half-wiped glass round-trips through its run-length string
            var m = AquaTank.AlgaeMap;
            for (int i = 0; i < m.Length; i++) m[i] = i % AquaTank.AW < AquaTank.AW / 2 ? 0f : 0.3f + 0.4f * ((i / AquaTank.AW) % 7) / 6f;
            AquaTank.MapsChanged();
            AquaTank.Commit();
            string enc = t.algaeMap;
            var back = AquaTank.Decode(enc, AquaTank.AW * AquaTank.AH, 0f);
            float maxDiff = 0f;
            for (int i = 0; i < m.Length; i++) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(back[i] - m[i]));
            var js = JsonUtility.ToJson(d);
            var d2 = JsonUtility.FromJson<SaveData>(js);
            Check(enc.Length > 0 && maxDiff <= 0.5f / 63f + 1e-4f && d2.tank.algaeMap == enc && Near(d2.tank.algae, t.algae, 1e-5f) && AquaTank.Encode(new float[100]) == "",
                $"maps saved run-length: {enc.Length} chars for a striped half-wiped glass, max error {maxDiff:0.0000}, save file {js.Length} chars; a uniform map saves as \"\"");
            AquaTank.SetAll(0f, 0f);

            Log($"done: {ok} ok, {fail} failed; coins {d.coins}, {AquaTank.Describe(d)}");
            Game.I.Save();
            Application.Quit();
        }

        // ------------------------------------------------------------------ -fkaqua decor
        /// <summary>From the storage tray onto a slot (or <paramref name="at"/>): press the icon, carry, hold, let go.</summary>
        IEnumerator FromTray(AquaDecor decor, string item, string slot, string shot, Vector2? at = null)
        {
            var sc = decor.TrayIconScreen(item);
            if (sc.x < 0f)
            {
                Log("not in the tray: " + item);
                yield break;
            }
            PointerInput.SimActive = true;
            PointerInput.SimPos = sc;
            PointerInput.SimDown = true;
            yield return null;
            yield return null;
            var from = PixelView.Current.ScreenToWorld(sc);
            var to = at ?? decor.SlotCentre(slot, item);
            yield return Carry(from, () => to, 0.45f);
            yield return HoldAt(() => to, 0.12f);
            if (shot != null) yield return ShotAt(shot, to);
            Up();
            yield return null;
            yield return null;
        }

        /// <summary>A placed item dragged from its slot to a world point.</summary>
        IEnumerator MoveItem(string slot, string item, Vector2 to)
        {
            var a = AquaDecor.CentreIn(AquaTank.Slot(slot), AquaTank.Decor(item));
            Down(a);
            yield return null;
            yield return null;
            yield return Carry(a, () => to, 0.4f);
            yield return HoldAt(() => to, 0.12f);
            Up();
            yield return null;
            yield return null;
        }

        IEnumerator DecorTest()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Game.Data;
            d.coins = Math.Max(d.coins, 500000);
            d.tankLevel = 4;
            foreach (var id in new[] { "tank_1", "tank_2", "tank_3", "tank_4" }) if (!d.ownedItems.Contains(id)) d.ownedItems.Add(id);
            d.feedTornOnce = true;
            d.tank = new TankCare();
            AquaTank.Ensure(d);
            int[] want = { 6, 7, 8, 9, 10 };
            Check(Enumerable.Range(0, 5).All(l => AquaTank.SlotCount(l) == want[l]),
                $"slots by tank level: {string.Join(" / ", Enumerable.Range(0, 5).Select(AquaTank.SlotCount))} (the light's included)");
            d.aquarium.Clear();
            foreach (var (id, cm, st) in new[]
            {
                ("crucian_carp", 25f, "lake"), ("carp", 60f, "lake"), ("cherry_salmon", 28f, "stream"), ("rainbow_trout", 45f, "stream"),
                ("black_porgy", 42f, "sea"), ("largemouth_bass", 40f, "lake"), ("pale_chub", 14f, "stream"),
            }) AddFish(id, cm, st);
            foreach (var f in d.aquarium) f.fullness = 1f;
            // (the feed and every cleaning tool on the ledge, as a player a while in would have them)
            AquaCare.Stock("feed_basic").bags = 2;
            AquaCare.Stock("feed_premium").bags = 1;
            AquaCare.Stock("feed_shrimp").pieces = 10;
            AquaCare.Stock("feed_sardine").pieces = 6;
            d.tank.owned.Add(AquaTank.NetId);
            d.tank.owned.Add(AquaTank.SiphonId);
            Game.I.Save();
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.4f);
            var scene = FindAnyObjectByType<AquariumScene>();
            var decor = scene.Decor;
            var canvas = GameObject.Find("AquariumUI").transform;

            // ---- the shop's 장식 section: two bought (the shot: bought and for sale), then the rest
            ShopUI.TankSub = ShopUI.SubDecor;
            ShopUI.Open(canvas, ItemKind.Tank);
            yield return new WaitForSeconds(0.6f);
            int c0 = d.coins, total = AquaTank.Decors.Sum(x => x.price);
            ClickIn("decor_ship");
            yield return new WaitForSeconds(0.1f);
            ClickIn("decor_driftwood");
            yield return new WaitForSeconds(0.1f);
            CloseShop();
            yield return new WaitForSeconds(2.4f);
            ShopUI.TankSub = ShopUI.SubDecor;
            ShopUI.Focus = "Group_Back";
            ShopUI.Open(canvas, ItemKind.Tank);
            yield return new WaitForSeconds(0.8f);
            yield return Shot("shop_decor");
            foreach (var def in AquaTank.Decors.Where(x => !AquaTank.Owns(x.id)))
            {
                ClickIn(def.id);
                yield return new WaitForSeconds(0.1f);
            }
            Check(AquaTank.Decors.All(x => AquaTank.Owns(x.id)) && c0 - d.coins == total, $"bought all {AquaTank.Decors.Count} decorations for {c0 - d.coins} (= {total})");
            CloseShop();
            Toast.Clear();
            yield return new WaitForSeconds(0.4f);

            // ---- the 꾸미기 button: the markers and the storage tray
            GameObject.Find("DecorButton").GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSeconds(0.5f);
            Check(AquaDecor.ModeOn && decor.MarkersShown == 10 && decor.TrayCount == AquaTank.Decors.Count && GameObject.Find("DecorTray") != null && GameObject.Find("Income") == null,
                $"the decoration mode: {decor.MarkersShown} slot markers, {decor.TrayCount} in the tray, the bottom panels hidden; hint '{scene.TopHint}'");
            yield return FromTray(decor, "decor_ship", "back_c", "decor_mode");
            Check(AquaTank.ItemAt("back_c") == "decor_ship" && decor.SpriteIn("back_c") == "decor_ship", $"the ship dragged from the tray onto the back centre ({AquaTank.ItemAt("back_c")})");
            (string item, string slot)[] rest =
            {
                ("decor_cabomba", "back_l"), ("decor_driftwood", "back_r"), ("decor_chest", "mid_l"), ("decor_wheel", "mid_r"), ("decor_bubbler", "mid_rr"),
                ("decor_fern", "front_l"), ("decor_rotala", "front_c"), ("decor_coral_fan", "front_r"), ("decor_light_day", "light"),
            };
            foreach (var (item, slot) in rest) yield return FromTray(decor, item, slot, null);
            Check(rest.All(r => AquaTank.ItemAt(r.slot) == r.item) && decor.TrayCount == AquaTank.Decors.Count - 10 && decor.Drops == 10 && decor.LightShown,
                $"every slot filled: {string.Join(", ", d.tank.places.Select(p => p.slot + "=" + p.item.Replace("decor_", "")))}; tray {decor.TrayCount}");
            // a mid item onto a back slot: refused (it stays in storage)
            yield return FromTray(decor, "decor_coral_brain", "back_l", null, decor.SlotCentre("back_l", "decor_cabomba"));
            Check(AquaTank.SlotOf("decor_coral_brain") == null && AquaTank.ItemAt("back_l") == "decor_cabomba" && decor.Rejects == 1,
                "the brain coral (mid) let go on a back slot: refused, back in the tray");
            // a swap: the chest onto the wheel's slot
            yield return MoveItem("mid_l", "decor_chest", decor.SlotCentre("mid_r", "decor_chest"));
            Check(AquaTank.ItemAt("mid_l") == "decor_wheel" && AquaTank.ItemAt("mid_r") == "decor_chest" && decor.Swaps == 1, "the chest dragged onto the wheel: they swap");
            // off the slots: the bubbler onto the tray goes back to storage
            var trayRt = GameObject.Find("DecorTray").GetComponent<RectTransform>();
            var trayWorld = PixelView.Current.ScreenToWorld(RectTransformUtility.WorldToScreenPoint(null, trayRt.TransformPoint(trayRt.rect.center)));
            yield return MoveItem("mid_rr", "decor_bubbler", trayWorld);
            Check(AquaTank.SlotOf("decor_bubbler") == null && decor.Removes == 1 && decor.TrayCount == AquaTank.Decors.Count - 9, $"the bubbler dragged off onto the tray: in storage (tray {decor.TrayCount})");
            yield return FromTray(decor, "decor_bubbler", "mid_rr", null);
            GameObject.Find("DecorDone").GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSeconds(0.4f);
            Check(!AquaDecor.ModeOn && GameObject.Find("Income") != null && GameObject.Find("DecorTray") == null, "완료: the mode closes, the panels are back");

            // ---- the effects
            float raw = AquaTank.DecorBonus(d);
            var lake = d.aquarium.First(f => f.stageId == "lake");
            var sea = d.aquarium.First(f => f.stageId == "sea");
            Check(Near(raw, 0.35f, 1e-4f) && Near(AquaTank.Bonus(d, lake), AquaTank.BonusCap, 1e-4f) && Near(AquaTank.Bonus(d, sea), AquaTank.BonusCap, 1e-4f),
                $"decorations +{raw * 100f:0}% in all, capped at +{AquaTank.BonusCap * 100f:0}%");
            var keep = d.tank.places.ToList();
            d.tank.places.RemoveAll(p => p.slot != "light");
            Check(Near(AquaTank.Bonus(d, lake), 0.08f, 1e-4f) && Near(AquaTank.Bonus(d, sea), 0.03f, 1e-4f),
                $"the day light alone: +3%, its lake / stream fish +5% more (lake {AquaTank.Bonus(d, lake) * 100f:0}%, sea {AquaTank.Bonus(d, sea) * 100f:0}%)");
            d.tank.places.Clear();
            d.tank.places.AddRange(keep);
            AquaTank.SetAll(0f, 0f);
            AquaTank.Rates(d, out float ra, out float rd, out float rb);
            float ff = AquaTank.FishFactor(d.aquarium.Count);
            Check(AquaTank.Plants(d) == 3 && Near(ra * 3600f, ff * (1f - 3 * AquaTank.PlantAlgae) / AquaTank.AlgaeHours, 1e-6f) && Near(rb * 3600f, ff * (1f - 3 * AquaTank.PlantDirt) / AquaTank.DirtHours, 1e-6f)
                  && Near(rd * 3600f, ff * AquaTank.BubblerDebris / AquaTank.DebrisHours, 1e-6f),
                $"3 plants: algae x{1f - 3 * AquaTank.PlantAlgae:0.00}, dirt x{1f - 3 * AquaTank.PlantDirt:0.00}; the bubbler: debris x{AquaTank.BubblerDebris:0.00} ({AquaTank.Describe(d)})");
            float own = d.aquarium.Sum(AquaCare.IncomeF);
            Check(Game.I.IncomePerMinute == Mathf.RoundToInt(own * (1f + AquaTank.BonusCap)), $"income {own:0.00}/min -> panel {Game.I.IncomePerMinute}/min (+15%)");
            yield return new WaitForSeconds(0.3f);
            var tf = scene.TankOf(d.aquarium[0]);
            Check(tf != null && Near(tf.Lively, 1.25f, 0.01f), $"the bubbler: the fish livelier x{tf?.Lively:0.00}");

            // ---- a fully decorated tank in the day light
            yield return new WaitForSeconds(1.6f);
            Toast.Clear();
            yield return Shot("decor_day");
            // the other lights (from the tray onto the hood slot)
            foreach (var l in new[] { "decor_light_sunset", "decor_light_moon", "decor_light_neon_pink", "decor_light_neon_cyan" })
            {
                decor.Enter();
                yield return new WaitForSeconds(0.3f);
                yield return FromTray(decor, l, "light", null);
                decor.Exit();
                yield return new WaitForSeconds(0.7f);
                var def = AquaTank.Decor(l);
                Check(AquaTank.Light(d) == def && Near(tf.Tint, def.tint, 0.02f) && Near(decor.ColourIn("back_c"), def.tint, 0.02f) && AquaTank.SlotOf("decor_light_day") == null,
                    $"{def.name}: fish tint {tf.Tint}, decor tint {decor.ColourIn("back_c")} (light #{ColorUtility.ToHtmlStringRGB(def.tint)})");
                Toast.Clear();
                if (l == "decor_light_sunset") yield return Shot("decor_sunset");
                if (l == "decor_light_moon") yield return Shot("decor_moon");
                if (l == "decor_light_neon_cyan") yield return Shot("decor_neon");
            }
            AquaTank.Place("light", "decor_light_day");
            decor.Sync();

            // ---- the chest opens now and then, the wheel turns
            int wf0 = decor.WheelFrame;
            yield return new WaitForSeconds(0.3f);
            int wf1 = decor.WheelFrame;
            decor.OpenChestNow();
            float o0 = Time.time;
            while (decor.ChestState != "Open" && Time.time - o0 < 1.5f) yield return null;
            yield return new WaitForSeconds(0.12f);
            var mid = (decor.SlotCentre("mid_l", "decor_wheel") + decor.SlotCentre("mid_r", "decor_chest")) * 0.5f;
            yield return ShotAt("chest", mid);
            Check(decor.ChestState == "Open" && decor.ChestOpens >= 1 && decor.BubblesAlive > 0 && wf0 != wf1,
                $"the chest opened (opens {decor.ChestOpens}, bubbles {decor.BubblesAlive}); the wheel turns (frame {wf0} -> {wf1})");
            int opens = decor.ChestOpens;
            o0 = Time.time;
            while (decor.ChestOpens == opens && Time.time - o0 < 24f) yield return null;
            Check(decor.ChestOpens > opens, $"it opens again by itself after {Time.time - o0:0.0} s");

            // ---- the income breakdown (a tap on the income panel)
            GameObject.Find("Income").GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSeconds(0.8f);
            var texts = FindObjectsByType<Text>(FindObjectsSortMode.None).Where(x => x.name == "Value").Select(x => x.text).ToList();
            Check(Dialog.Open && texts.Any(x => x.Contains("+15%")) && texts.Any(x => x.Contains("주광 조명")),
                $"the breakdown: {string.Join(" | ", texts.Select(x => System.Text.RegularExpressions.Regex.Replace(x, "<.*?>", "")))}");
            yield return Shot("income");
            CloseDialogs();
            yield return new WaitForSeconds(0.3f);

            // ---- a long press on a decoration: the mode opens with it lifted
            int i0 = 0;
            foreach (var f in d.aquarium) PlaceFish(scene, f, new Vector2(-11f + (i0++) * 0.8f, 4.2f), false);
            var wc = decor.SlotCentre("mid_l", "decor_wheel");
            Down(wc);
            yield return HoldAt(() => wc, 0.7f);
            Check(AquaDecor.ModeOn && decor.Dragging, $"a long press on the wheel: the mode opens with it lifted ({AquaDecor.ModeOn}, dragging {decor.Dragging})");
            Up();
            yield return new WaitForSeconds(0.3f);
            Check(AquaTank.ItemAt("mid_l") == "decor_wheel", "let go where it was: it stays");
            decor.Exit();
            yield return new WaitForSeconds(0.3f);

            // ---- kept over a scene reload; a smaller tank shows only its slots
            string before = string.Join(",", d.tank.places.OrderBy(p => p.slot).Select(p => p.slot + "=" + p.item));
            SceneFlow.Go("Map");
            yield return new WaitForSeconds(1.5f);
            SceneFlow.Go("Aquarium");
            yield return new WaitForSeconds(2.4f);
            scene = FindAnyObjectByType<AquariumScene>();
            decor = scene.Decor;
            string after = string.Join(",", d.tank.places.OrderBy(p => p.slot).Select(p => p.slot + "=" + p.item));
            Check(after == before && decor.SpriteIn("back_c") == "decor_ship" && decor.LightShown, $"back in the tank: the same placement ({after.Length} chars) and sprites");
            d.tankLevel = 2;
            decor.Sync();
            yield return null;
            Check(AquaTank.Placed(d).Count == 8 && decor.SpriteIn("mid_rr") == "" && decor.SpriteIn("front_r") == "" && AquaTank.ItemAt("mid_rr") == "decor_bubbler",
                $"a level-2 tank: {AquaTank.Placed(d).Count} placed shown, the late slots kept in the save");
            d.tankLevel = 4;
            decor.Sync();

            Log($"done: {ok} ok, {fail} failed; coins {d.coins}, {AquaTank.Describe(d)}");
            Game.I.Save();
            Application.Quit();
        }
    }
}
