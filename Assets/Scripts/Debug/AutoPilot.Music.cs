using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>
    /// -fkauto music (Docs/music.md, Tools/Music/README.md 4): the background music through a session, checked as
    /// [MUSIC] CHECK lines (the cue and the stems heard, the ducks, the stings) and a summary. Start it on the title (no
    /// -fkscene), e.g. -fkfresh -fkrich -fkgear -fksave music -fkstage lake -fkencounter now -fkauto music -fkmusiclog
    /// -fkshots &lt;dir&gt;:
    /// <list type="number">
    /// <item>the title, then the map (their cues, crossfaded); a cue not in the manifest plays the chiptune, a real one
    /// stops it; a save from before 배경음 reads it on; 설정 → 배경음 off (nothing heard, nothing left playing, a sting
    /// silent) and on again (the map's cue back), shot music_settings;</item>
    /// <item>the stage (-fkstage, default lake) by day (the day stem alone), the clock set to night (both stems half-way
    /// through the 8 s crossfade, then the night stem alone);</item>
    /// <item>a bite (the stage ducked), the hook set by a tap, the fish fought by circles (the fight stem 0.55..1 and
    /// rising and falling with the line's tension), landed (its sting by rarity, the stage ducked under it, then the fight
    /// stem down and the stage back up); a rare fish landed (sting_rare); a fish shaken off (sting_escape);</item>
    /// <item>with -fkencounter: an encounter played well (the omen: sting_omen, the stage gone; lurk; + approach;
    /// + tease; silence in the hook window; sting_hook; legend_&lt;id&gt;; landed: sting_legend, the fight's deck silent
    /// under it as it fades out; the stage back) and one played badly (sting_fail, the encounter's deck silent under
    /// it; the stage back), driven by the encounter test's plays (AutoPilot.Encounter.cs);</item>
    /// <item>back to the map and on to the aquarium (their cues, nothing of the stage left).</item>
    /// </list>
    /// A loop cue not in the manifest yet is checked for the chiptune in its place, a missing sting for silence. Every
    /// 0.5 s the current deck's stems are read: their playback positions must stay within <see cref="SyncTolMs"/>.
    /// </summary>
    public partial class AutoPilot
    {
        const float SyncTolMs = 5f;
        static readonly CultureInfo CIm = CultureInfo.InvariantCulture;
        int musChecks, musFails, musSyncReads, musSyncBad;
        float musSyncWorst;
        bool musEncOn;

        void MCheck(string what, bool ok, string detail = null)
        {
            musChecks++;
            if (!ok) musFails++;
            Log($"[MUSIC] CHECK {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")")} | {Music.Describe()}");
        }

        static string Gs(string stem) => Music.Gain(stem).ToString("0.00", CIm);

        /// <summary>The current deck is <paramref name="cue"/>, playing, each stem's gain in its range (a cue not in the manifest: the chiptune instead).</summary>
        void CueCheck(string what, string cue, params (string stem, float lo, float hi)[] want)
        {
            if (!Music.Has(cue))
            {
                MCheck($"{what}: {cue} is not in the manifest yet, the chiptune plays", Music.Chiptune && Music.Current == null);
                return;
            }
            bool ok = Music.Current == cue && !Music.Loading && !Music.Chiptune
                      && want.All(x => Music.Gain(x.stem) >= x.lo - 1e-3f && Music.Gain(x.stem) <= x.hi + 1e-3f);
            MCheck($"{what}: {cue}", ok, string.Join(", ", want.Select(x => string.Format(CIm, "{0} {1} in {2:0.00}..{3:0.00}", x.stem, Gs(x.stem), x.lo, x.hi))));
        }

        /// <summary>Waits up to <paramref name="timeout"/> real s for the sting <paramref name="cue"/>, then checks it and the deck's duck under it.</summary>
        IEnumerator StingCheck(string what, string cue, float duck, float timeout = 2.5f)
        {
            float t = 0f;
            while (t < timeout && Music.StingNow != cue)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!Music.HasSting(cue))
            {
                MCheck($"{what}: {cue} is not in the manifest yet, no sting", Music.StingNow != cue);
                yield break;
            }
            bool heard = Music.StingNow == cue;
            yield return new WaitForSecondsRealtime(Music.StingAttack + 0.15f);   // (its duck down, a few frames to spare)
            float d = Music.DuckNow;
            MCheck($"{what}: {cue}", heard && Mathf.Abs(d - duck) <= 0.06f,
                string.Format(CIm, "heard {0} after {1:0.00} s, the deck ducked to {2:0.00} (want {3:0.00})", heard, t, d, duck));
        }

        /// <summary>The current deck's stems read six times over 1.2 s: their positions within <see cref="SyncTolMs"/>.</summary>
        IEnumerator SyncCheck(string what)
        {
            float worst = -1f;
            for (int i = 0; i < 6; i++)
            {
                worst = Mathf.Max(worst, Music.SyncMs());
                yield return new WaitForSecondsRealtime(0.2f);
            }
            if (worst < 0f) Log($"[MUSIC] sync {what}: fewer than two stems playing, nothing to compare");
            else MCheck($"{what}: the stems in sync", worst <= SyncTolMs, string.Format(CIm, "widest gap {0:0.00} ms", worst));
        }

        /// <summary>The whole run: the current deck's stems read every 0.5 s.</summary>
        IEnumerator MusicSyncWatch()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                float ms = Music.SyncMs();
                if (ms < 0f) continue;
                musSyncReads++;
                musSyncWorst = Mathf.Max(musSyncWorst, ms);
                if (ms > SyncTolMs && musSyncBad++ < 10) Log(string.Format(CIm, "[MUSIC] sync gap {0:0.00} ms on {1}", ms, Music.Current));
            }
        }

        IEnumerator MusicTest()
        {
            StartCoroutine(MusicSyncWatch());
            var ids = Music.CueIds.ToList();
            var expect = new List<string> { "title", "map", "aquarium", "encounter" };
            expect.AddRange(GameDatabase.Stages.Select(s => "stage_" + s.id));
            expect.AddRange(GameDatabase.Fish.Where(f => f.encounter != null).Select(f => "legend_" + f.id));
            expect.AddRange(new[] { "sting_catch", "sting_rare", "sting_legend", "sting_escape", "sting_omen", "sting_hook", "sting_fail" });
            var notYet = expect.Where(c => !ids.Contains(c)).ToList();
            Log($"[MUSIC] music test: the manifest has {ids.Count} cues; not yet: {(notYet.Count > 0 ? string.Join(", ", notYet) : "none")}");
            var key = Game.I.Bait;   // (with -fkencounter: the stage legend's key, equipped at boot)

            // 1. the title and the map, the fallback, the setting
            if (SceneManager.GetActiveScene().name != "Title") SceneFlow.Go("Title");
            yield return new WaitForSecondsRealtime(3.5f);
            CueCheck("the title", "title", ("main", 0.95f, 1f));
            SceneFlow.Go("Map");
            yield return new WaitForSecondsRealtime(3.5f);
            CueCheck("the map (crossfaded from the title)", "map", ("main", 0.95f, 1f));
            MCheck("the title's deck freed", Music.DeckCount <= 1, $"{Music.DeckCount} decks");
            Music.Play("no_such_cue");
            yield return new WaitForSecondsRealtime(1.5f);
            MCheck("a cue not in the manifest: the chiptune stands in", Music.Chiptune && Music.Current == null);
            Music.Play("map");
            yield return new WaitForSecondsRealtime(2.5f);
            CueCheck("a real cue again: the chiptune stops", "map", ("main", 0.95f, 1f));

            var old = JsonUtility.FromJson<SaveData>("{\"version\":1,\"coins\":123,\"soundOn\":true,\"reelRing\":true}");
            MCheck("a save from before 배경음 reads it on (and a new game)", old != null && old.musicOn && SaveData.NewGame().musicOn);
            SettingsUI.Open();
            yield return new WaitForSecondsRealtime(0.6f);   // (the window's pop)
            var label = FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(x => x.text == "배경음");
            Button row = null;
            if (label != null)
            {
                float y = label.rectTransform.anchoredPosition.y;
                row = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.transform.parent == label.transform.parent
                    && Mathf.Abs(((RectTransform)b.transform).anchoredPosition.y - y) < 1f);
            }
            string RowText() => row != null ? row.GetComponentInChildren<Text>().text : "-";
            MCheck("설정 has the 배경음 row, 켜짐", row != null && RowText() == "켜짐" && Game.Data.musicOn, $"row {row != null}, it says {RowText()}");
            yield return Shot("music_settings");
            if (row != null)
            {
                row.onClick.Invoke();
                yield return new WaitForSecondsRealtime(1f);
                MCheck("배경음 꺼짐: saved, nothing heard, nothing left playing", !Game.Data.musicOn && RowText() == "꺼짐" && Music.Current == null
                    && !Music.Chiptune && Music.DeckCount == 0 && Music.StingNow == null, $"{Music.DeckCount} decks");
                Music.Sting("sting_catch");
                yield return null;
                MCheck("배경음 꺼짐: a sting stays silent", Music.StingNow == null);
                row.onClick.Invoke();
                yield return new WaitForSecondsRealtime(2.5f);
                MCheck("배경음 켜짐: saved", Game.Data.musicOn && RowText() == "켜짐");
                CueCheck("배경음 켜짐: the map's cue back", "map", ("main", 0.95f, 1f));
            }
            Click("닫기");
            yield return new WaitForSecondsRealtime(0.4f);

            // 2. the stage by day, then at night
            string stage = Arg("-fkstage") ?? "lake";
            string cue = "stage_" + stage;
            GameClock.Scale = 0f;
            GameClock.Min = GameClock.Centre(Period.Day);
            yield return GoStage(stage, 4f);
            var ctl = FindAnyObjectByType<FishingController>();
            if (ctl == null)
            {
                MCheck("the fishing scene", false);
                yield return MusicEnd();
                yield break;
            }
            CueCheck("the stage by day: the day stem alone", cue, ("day", 0.95f, 1f), ("night", 0f, 0.02f), ("fight", 0f, 0.02f));
            yield return SyncCheck("the stage by day");
            GameClock.Set(GameClock.Centre(Period.Night), true);
            yield return new WaitForSecondsRealtime(FishingController.PeriodFade * 0.5f);
            CueCheck("half-way through the period crossfade", cue, ("day", 0.3f, 0.9f), ("night", 0.3f, 0.9f));
            yield return new WaitForSecondsRealtime(FishingController.PeriodFade * 0.5f + 1f);
            CueCheck("the stage at night: the night stem alone", cue, ("night", 0.95f, 1f), ("day", 0f, 0.02f), ("fight", 0f, 0.02f));
            yield return SyncCheck("the stage at night");

            // 3. fights: a bite fought and landed, a rare fish landed, one shaken off
            string encMode = LegendWatch.DebugMode;
            LegendWatch.DebugMode = null;         // (no encounter before its part)
            FishingController.NoBites = true;     // (no fish of its own on the rig: the bite below is forced)
            Obstacles.SnagMult = 0f;              // (nor a snag while it waits)
            yield return MusicFight(ctl, cue, "bite");
            yield return MusicFight(ctl, cue, "rare");
            yield return MusicFight(ctl, cue, "escape");

            // 4. the encounter, played well and then badly
            if (encMode != null && ctl.Watch != null)
            {
                LegendWatch.DebugMode = encMode;
                FishingController.NoBites = false;
                encKey = key;
                encId = LegendWatch.DebugLegend ?? ctl.Watch.Legend.id;
                PointerInput.SimDpi = 0f;
                musEncOn = true;
                StartCoroutine(MusicEncWatch(ctl, cue));
                yield return EncounterRun(ctl, "perfect");
                yield return MusicStageBack(ctl, cue, "after the legend fight");
                yield return EncounterRun(ctl, "bad");
                FishingController.NoBites = true;
                yield return MusicStageBack(ctl, cue, "after the failed encounter");
                musEncOn = false;
            }
            else Log("[MUSIC] no -fkencounter (or no legend on this stage): the encounter's music is not checked");

            // 5. leaving: the map, the aquarium
            yield return ToReady(ctl);
            SceneFlow.Go("Map");
            yield return new WaitForSecondsRealtime(4f);
            CueCheck("back on the map", "map", ("main", 0.95f, 1f));
            MCheck("nothing of the stage left (one deck, no duck)", Music.DeckCount <= 1 && Mathf.Abs(Music.DuckNow - 1f) < 0.01f, $"{Music.DeckCount} decks");
            SceneFlow.Go("Aquarium");
            yield return new WaitForSecondsRealtime(4f);
            CueCheck("the aquarium", "aquarium", ("main", 0.95f, 1f));
            yield return MusicEnd();
        }

        IEnumerator MusicEnd()
        {
            MCheck("the stems stayed in sync all run", musSyncWorst <= SyncTolMs,
                string.Format(CIm, "{0} reads, widest gap {1:0.00} ms, {2} over {3:0} ms", musSyncReads, musSyncWorst, musSyncBad, SyncTolMs));
            Log($"[MUSIC] summary: {musChecks} checks, {musFails} failed (the encounter plays' own checks: {encFails} failed)");
            yield return new WaitForSecondsRealtime(0.5f);
            PointerInput.SimActive = false;
            Application.Quit();
        }

        /// <summary>
        /// One fish on the stage: <paramref name="kind"/> bite (a fish of the stage made to bite, the hook set by a tap,
        /// fought by circles for 7 s and landed), rare (a rare fish hooked and landed) or escape (hooked, then off).
        /// </summary>
        IEnumerator MusicFight(FishingController ctl, string cue, string kind)
        {
            yield return ToReady(ctl);
            var L = ctl.Stage.L;
            var at = L.IsIce ? new Vector3(L.holeX, 0f, L.holeZ) : new Vector3(ctl.Angler.X + 0.5f, 0f, Mathf.Min(L.zFar - 4f, L.zNear + 10f));
            if (!ctl.DebugPlaceRig(at))
            {
                MCheck($"{kind}: a rig on the water", false, $"state {ctl.State}");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);
            var stageFish = GameDatabase.FishOfStage(ctl.Stage.Def.id).Where(f => f.encounter == null).ToList();
            var biter = kind == "bite" ? ctl.Spawner.Fish.FirstOrDefault(f => f != null && f.State == FishAgent.St.Wander && f.Sp.encounter == null) : null;
            if (biter != null && ctl.State == FishingController.S.Waiting)
            {
                ctl.OnBite(biter);
                yield return new WaitForSecondsRealtime(0.3f);
                MCheck("a bite: the stage ducked", ctl.State == FishingController.S.Biting && Mathf.Abs(Music.DuckNow - FishingController.BiteDuck) <= 0.05f,
                    string.Format(CIm, "state {0}, duck {1:0.00}", ctl.State, Music.DuckNow));
                yield return Tap(Scr(0.5f, 0.6f));
                yield return null;
            }
            else
            {
                var sp = kind == "rare" ? stageFish.FirstOrDefault(f => f.rarity == Rarity.Rare || f.rarity == Rarity.Epic) ?? GameDatabase.GetFish("mandarin_fish")
                                        : stageFish.OrderBy(f => f.rarity).FirstOrDefault() ?? GameDatabase.GetFish("crucian_carp");
                ctl.DebugHook(sp, FishSpawner.RollSize(sp), 4242, new Vector3(at.x, -1f, at.z));
            }
            if (ctl.State != FishingController.S.Fighting || ctl.Hooked == null)
            {
                MCheck($"{kind}: hooked", false, $"state {ctl.State}");
                yield break;
            }
            var hooked = ctl.Hooked.Sp;
            Log($"[MUSIC] {kind}: fighting {hooked.id} ({hooked.rarity})");
            // reel by circles, easing off at a high tension; the fight stem's level read every 0.5 s against the
            // tension smoothed as the director smooths it
            float fightFor = kind == "bite" ? 7f : 2.5f, t = 0f, next = 1.5f, ang = 0f, sm = 0f;
            var c = Scr(0.72f, 0.4f);
            float r = Screen.height * 0.13f, windSign = CircleGesture.Reversed ? -1f : 1f;
            bool winding = true, inRange = true;
            var xs = new List<float>();
            var ys = new List<float>();
            PointerInput.SimActive = true;
            while (ctl.State == FishingController.S.Fighting && t < fightFor)
            {
                float dt = Time.deltaTime;
                t += dt;
                var f = ctl.Fight;
                if (f != null)
                {
                    sm += (Mathf.Clamp01(f.TensionRatio) - sm) * (1f - Mathf.Exp(-Time.unscaledDeltaTime / FishingController.TensionTau));
                    if (winding && f.TensionRatio > 0.8f) winding = false;
                    else if (!winding && f.TensionRatio < 0.5f) winding = true;
                }
                if (winding) ang -= windSign * dt * 2.3f * Mathf.PI * 2f;
                else if (f != null && f.TensionRatio > 0.95f) ang += windSign * dt * 1.2f * Mathf.PI * 2f;
                PointerInput.SimDown = true;
                PointerInput.SimPos = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (t >= next && f != null)
                {
                    next += 0.5f;
                    float g = Music.Gain("fight") / Mathf.Max(0.01f, Music.DuckNow);   // (the stem's own level: the ducks aside)
                    if (Music.Has(cue)) inRange &= g >= FishingController.FightMin - 0.03f && g <= 1.001f;
                    xs.Add(sm);
                    ys.Add(g);
                    Log(string.Format(CIm, "[MUSIC] fight t={0:0.0} tension={1:0.00} smoothed={2:0.00} fight stem={3:0.00}", t, f.TensionRatio, sm, g));
                }
                yield return null;
            }
            PointerInput.SimDown = false;
            if (kind == "bite" && Music.Has(cue) && xs.Count >= 4)
            {
                float span = xs.Max() - xs.Min(), corr = Corr(xs, ys);
                MCheck("fighting: the fight stem 0.55..1, with the line's tension", inRange && (span < 0.1f || corr >= 0.6f),
                    string.Format(CIm, "{0} reads, levels {1:0.00}..{2:0.00}, smoothed tension {3:0.00}..{4:0.00}, correlation {5:0.00}",
                        xs.Count, ys.Min(), ys.Max(), xs.Min(), xs.Max(), corr));
            }
            else if (kind == "bite") CueCheck("fighting", cue);

            // the end: landed (its sting by rarity), or off the hook
            float tEnd = Time.realtimeSinceStartup;
            string sting = "sting_escape";
            float duck = FishingController.EscapeDuck;
            bool landed = false;
            if (kind == "escape") ctl.DebugRelease();
            else if (ctl.State == FishingController.S.Fighting) landed = ctl.DebugLand();
            else landed = ctl.State == FishingController.S.Landing || ctl.State == FishingController.S.Result;
            if (landed)
            {
                bool rare = hooked.rarity == Rarity.Rare || hooked.rarity == Rarity.Epic;
                sting = rare ? "sting_rare" : "sting_catch";
                duck = rare ? FishingController.RareDuck : FishingController.CatchDuck;
            }
            yield return StingCheck($"{kind}: {(landed ? "landed " + hooked.id + " (" + hooked.rarity + ")" : "off the hook")}", sting, duck);
            // the catch card, then the sting out and the fight stem down (FightDown s)
            for (float w = 0f; w < 4f && landed && !HasButton("판매"); w += Time.unscaledDeltaTime) yield return null;
            if (HasButton("판매"))
            {
                yield return new WaitForSecondsRealtime(0.3f);
                Click("판매");
            }
            for (float w = 0f; w < 10f && Music.StingNow != null; w += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            while (Time.realtimeSinceStartup - tEnd < FishingController.FightDown + 0.4f) yield return null;
            CueCheck($"{kind}: after the fight, the fight stem down and the stage back up", cue, ("night", 0.95f, 1f), ("fight", 0f, 0.02f));
        }

        static float Corr(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            float ma = a.Take(n).Average(), mb = b.Take(n).Average(), sab = 0f, saa = 0f, sbb = 0f;
            for (int i = 0; i < n; i++)
            {
                sab += (a[i] - ma) * (b[i] - mb);
                saa += (a[i] - ma) * (a[i] - ma);
                sbb += (b[i] - mb) * (b[i] - mb);
            }
            return saa <= 1e-9f || sbb <= 1e-9f ? 0f : sab / Mathf.Sqrt(saa * sbb);
        }

        /// <summary>After an encounter's end (its sting played out): the stage's night stem alone, nothing else left.</summary>
        IEnumerator MusicStageBack(FishingController ctl, string cue, string what)
        {
            for (float w = 0f; w < 12f && Music.StingNow != null; w += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            CueCheck($"{what}: the stage back", cue, ("night", 0.95f, 1f), ("day", 0f, 0.02f), ("fight", 0f, 0.02f));
            MCheck($"{what}: the encounter's and the legend's decks freed", Music.DeckCount <= 1, $"{Music.DeckCount} decks");
        }

        /// <summary>
        /// Follows the encounters (while <see cref="musEncOn"/>), checking each phase's music at a moment its fades have
        /// settled: the omen, the eyes (lurk), the approach, the tease, the hook window (silence), the strike, a turn
        /// away; a legend fight's cue once sting_hook is over, then it is landed at once (sting_legend).
        /// </summary>
        IEnumerator MusicEncWatch(FishingController ctl, string stageCue)
        {
            LegendEncounter seen = null;
            LegendEncounter.Phase? ph = null;
            var done = new HashSet<string>();
            float phaseAt = 0f, teaseAt = -1f;
            bool hasEnc = Music.Has("encounter");
            while (musEncOn)
            {
                var e = ctl.Encounter;
                float now = Time.realtimeSinceStartup;
                if (ctl.State == FishingController.S.Encounter && e != null)
                {
                    if (e != seen)
                    {
                        seen = e;
                        ph = null;
                        teaseAt = -1f;
                        done.Clear();
                        Log($"[MUSIC] encounter with {e.Sp.id} (repeat {e.Repeat})");
                    }
                    if (e.Ph != ph)
                    {
                        ph = e.Ph;
                        phaseAt = now;
                        if (teaseAt < 0f && e.Ph == LegendEncounter.Phase.Tease) teaseAt = now;
                    }
                    float inPh = now - phaseAt;
                    switch (e.Ph)
                    {
                        case LegendEncounter.Phase.Omen:
                            if (inPh >= 0.3f && done.Add("omen"))
                            {
                                if (Music.HasSting("sting_omen")) MCheck("the omen: sting_omen", Music.StingNow == "sting_omen");
                                MCheck("the omen: the encounter's deck, silent", hasEnc ? Music.Current == "encounter" && Music.Gain("lurk") <= 0.02f : Music.Current == null);
                            }
                            break;
                        case LegendEncounter.Phase.Eyes:
                            if (inPh >= e.PhaseLen - 0.15f && done.Add("eyes"))
                            {
                                if (hasEnc) CueCheck("the eyes: lurk alone", "encounter", ("lurk", e.Repeat ? 0.5f : 0.75f, 1f), ("approach", 0f, 0.02f), ("tease", 0f, 0.02f));
                                MCheck("the eyes: the stage faded out and freed", Music.DeckCount <= 1, $"{Music.DeckCount} decks");
                            }
                            break;
                        case LegendEncounter.Phase.Approach:
                            if (hasEnc && inPh >= e.PhaseLen - 0.4f && done.Add("approach"))
                                CueCheck("the approach: lurk + approach", "encounter", ("lurk", 0.95f, 1f), ("approach", 0.8f, 1f), ("tease", 0f, 0.02f));
                            break;
                        case LegendEncounter.Phase.Tease:
                        case LegendEncounter.Phase.NoseIn:
                            if (hasEnc && teaseAt >= 0f && now - teaseAt >= 1.7f && done.Add("tease"))
                                CueCheck($"the {e.Ph}: lurk + approach + tease", "encounter", ("lurk", 0.95f, 1f), ("approach", 0.95f, 1f), ("tease", 0.8f, 1f));
                            break;
                        case LegendEncounter.Phase.HookWindow:
                            if (done.Add("window"))
                                MCheck("the hook window: silence", Music.DuckNow <= 0.05f && Music.Gain("lurk") <= 0.05f, string.Format(CIm, "duck {0:0.00}", Music.DuckNow));
                            break;
                        case LegendEncounter.Phase.Hooked:
                            if (inPh >= 0.2f && done.Add("hooked"))
                            {
                                if (Music.HasSting("sting_hook")) MCheck("hooked: sting_hook", Music.StingNow == "sting_hook");
                                // (stopped; or already the legend's cue, or the stage's, when a sting / a track is missing)
                                MCheck("hooked: the window's deck stopped", Music.Current != "encounter");
                            }
                            break;
                        case LegendEncounter.Phase.TurnAway:
                            if (inPh >= 0.3f && done.Add("turnaway"))
                            {
                                if (Music.HasSting("sting_fail")) MCheck("turned away: sting_fail", Music.StingNow == "sting_fail");
                                MCheck("turned away: the stage asked back, under the sting (the encounter's deck silent too)", Music.Wanted == stageCue
                                    && (!Music.HasSting("sting_fail") || (Music.DuckNow <= 0.05f && Music.DyingGain <= 0.05f)),
                                    string.Format(CIm, "wanted {0}, duck {1:0.00}, the decks fading out at {2:0.00}", Music.Wanted, Music.DuckNow, Music.DyingGain));
                            }
                            break;
                    }
                }
                else if (ctl.State == FishingController.S.Fighting && ctl.Hooked != null && ctl.Hooked.Sp.encounter != null && done.Add("legend"))
                    yield return MusicLegendFight(ctl, stageCue);
                yield return null;
            }
        }

        /// <summary>A legend on the line: its cue once sting_hook is over (or the stage's fight stem full), then landed at once.</summary>
        IEnumerator MusicLegendFight(FishingController ctl, string stageCue)
        {
            var sp = ctl.Hooked.Sp;
            for (float w = 0f; w < 6f && Music.StingNow != null; w += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(Music.StingRelease + FishingController.LegendFade);   // (the sting's duck back up, the cue's fade-in)
            if (ctl.State != FishingController.S.Fighting)
            {
                Log($"[MUSIC] the legend fight was over before its check ({ctl.State})");
                yield break;
            }
            string lc = "legend_" + sp.id;
            if (Music.Has(lc)) CueCheck("the legend fight", lc, ("main", 0.95f, 1f));
            else CueCheck($"the legend fight ({lc} not in the manifest yet: the stage's fight stem full)", stageCue, ("fight", 0.95f, 1f));
            yield return SyncCheck("the legend fight");
            ctl.DebugLand();
            yield return StingCheck($"the legend landed ({sp.id})", "sting_legend", 0f, 3f);
            // (the fight's deck, fading out under the sting, is under its duck 0 too: not heard over the fanfare)
            if (Music.HasSting("sting_legend"))
                MCheck("the legend landed: its fight music silent under the sting", Music.DyingGain <= 0.05f,
                    string.Format(CIm, "the decks fading out at {0:0.00}", Music.DyingGain));
        }
    }
}
