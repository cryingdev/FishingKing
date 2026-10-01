using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The fishing scene's music (Tools/Music/README.md 4, Docs/music.md). A director looks at the state every frame
    /// (<see cref="TickMusic"/>, from Update) and asks <see cref="Music"/> only for what changed; the stings come from the
    /// events (a catch, a fish off, a forced snag break) and from the encounter's phases. It keeps nothing but what it
    /// asked for last, so every way out of a fight, an encounter or a legend fight lands back on the stage cue.
    /// <list type="bullet">
    /// <item>between fish: stage_&lt;id&gt;, its day stem at dawn and by day, its night stem in the evening and at night,
    /// crossfaded over <see cref="PeriodFade"/> s when the period changes;</item>
    /// <item>a bite ducks it for the bite (<see cref="BiteDuck"/>); the legend's build-up dims it while the line trembles
    /// (its meter from 0.7 until it falls under 0.6: <see cref="BuildDuck"/>);</item>
    /// <item>fighting: the fight stem comes up and follows the line's smoothed tension (<see cref="FightMin"/>..1), down
    /// over <see cref="FightDown"/> s after;</item>
    /// <item>landed: sting_catch / sting_rare / sting_legend by its rarity; shaken off or the line broken (a forced snag
    /// break too): sting_escape;</item>
    /// <item>the encounter: the stage fades out under sting_omen, then the encounter cue: lurk (opening, eyes),
    /// + approach, + tease (tease, nose-in), silent through the lunge and the hook window; hooked: sting_hook, then
    /// legend_&lt;id&gt; (a legend without a track yet: the stage with its fight stem full); turned away: sting_fail with
    /// the stage coming back under it (without an encounter track the window is silent under its own drone and
    /// heartbeat, not the chiptune);</item>
    /// <item>a legend fight's end: its catch or escape sting, the stage coming back under it.</item>
    /// </list>
    /// </summary>
    public partial class FishingController
    {
        internal const float PeriodFade = 8f, FightDown = 3f, FightMin = 0.55f;
        /// <summary>The line's tension is smoothed over this long (s) before it sets the fight stem.</summary>
        internal const float TensionTau = 0.5f;
        /// <summary>The stage under a bite and under the legend's build-up; under the stings of a catch (common / uncommon,
        /// rare / epic), of a fish off and of a forced snag break (a legend's stings: 0, the stage comes back after them).</summary>
        internal const float BiteDuck = 0.55f, BuildDuck = 0.65f, CatchDuck = 0.35f, RareDuck = 0.25f, EscapeDuck = 0.4f, SnagDuck = 0.45f;
        /// <summary>The stage fading out at the omen, and back in under the sting that ended an encounter / a legend fight.</summary>
        const float OmenFade = 1.2f, StageBack = 1f;
        const float FightRise = 0.8f;      // s: the fight stem up at the hook set ...
        const float FightFollow = 0.25f;   // s: ... then after the tension, asked for again once it moves by FightStep
        const float FightStep = 0.02f;
        const float TensionLo = 0.2f, TensionHi = 0.9f;   // the smoothed tension that gives the fight stem FightMin / 1
        const float BuildOn = 0.7f, BuildOff = 0.6f;      // the legend's meter: the build-up dims the stage from BuildOn until under BuildOff ...
        const float BuildDim = 2.5f;       // s: ... going down this slowly
        const float BiteDown = 0.15f, BiteBack = 0.6f;    // s: a bite's duck down, and back up after it
        const float DuckBack = 1.5f;       // s: any other duck back up (the build-up eased off)
        const float EyesFade = 2.5f, ApproachFade = 2f, TeaseFade = 1.5f;   // s: the encounter's stems in by phase
        const float WindowDuck = 0.12f;    // s: the lunge and the hook window go silent this fast
        /// <summary>sting_hook hands over to the legend's cue this long (s) before its end; the cue fades in over LegendFade s.</summary>
        internal const float HookHandoff = 0.3f, LegendFade = 0.4f;
        const float LegendStage = 0.6f;    // s: a legend without a track: the stage (its fight stem full) back in

        enum Mus { Stage, Encounter, Legend }
        Mus musMode;
        string musStem;               // the stage's base stem asked for: day / night
        float musFight;               // the fight stem's level asked for
        float musTension;             // the line's tension, smoothed
        float musDuck = 1f;           // the duck asked for
        bool musBuild;                // the legend's build-up dims the stage
        LegendEncounter musEnc;       // the encounter whose phases were followed
        LegendEncounter.Phase? musPh;
        bool musLegendOn;             // the legend fight's cue has been started

        string StageCue => "stage_" + Stage.Def.id;

        static string PeriodStem => GameClock.Now == Period.Evening || GameClock.Now == Period.Night ? "night" : "day";

        static string OtherStem(string stem) => stem == "day" ? "night" : "day";

        void InitMusic()
        {
            musMode = Mus.Stage;
            musStem = PeriodStem;
            musFight = 0f;
            musDuck = 1f;
            Music.Duck(1f, 0f);
            PlayStage(Music.SceneFade);
        }

        /// <summary>The scene closes: nothing of it stays ducked (the next scene plays its own cue).</summary>
        void LeaveMusic() => Music.Duck(1f, 0.5f);

        void PlayStage(float fade) => Music.Play(StageCue, fade, (musStem, 1f), (OtherStem(musStem), 0f), ("fight", musFight));

        void SetDuck(float level, float seconds)
        {
            if (level == musDuck) return;
            musDuck = level;
            Music.Duck(level, seconds);
        }

        /// <summary>One frame of the director (real seconds).</summary>
        void TickMusic(float dt)
        {
            if (State == S.Encounter && Encounter != null) EncounterMusic();
            else if ((State == S.Fighting || State == S.Landing) && Hooked != null && Hooked.Sp.encounter != null) LegendMusic(Hooked.Sp);
            else StageMusic(dt);
        }

        void StageMusic(float dt)
        {
            if (musMode != Mus.Stage)
            {
                // back from the encounter or a legend fight (under the sting that ended it; a legend without a track of
                // its own played the stage's fight stem, which now goes down as after any fight)
                musMode = Mus.Stage;
                musFight = 0f;
                musStem = PeriodStem;
                SetDuck(1f, 0.3f);
                PlayStage(Music.Current == StageCue ? FightDown : StageBack);
            }
            string stem = PeriodStem;
            if (stem != musStem)
            {
                musStem = stem;
                Music.Stem(stem, 1f, PeriodFade);
                Music.Stem(OtherStem(stem), 0f, PeriodFade);
            }
            // the fight layer: up at the hook set, then after the line's tension; down over FightDown s after the fight
            float want = 0f, secs = FightDown;
            if (State == S.Fighting && Fight != null)
            {
                musTension += (Mathf.Clamp01(Fight.TensionRatio) - musTension) * (1f - Mathf.Exp(-dt / TensionTau));
                want = Mathf.Lerp(FightMin, 1f, Mathf.InverseLerp(TensionLo, TensionHi, musTension));
                secs = musFight < FightMin ? FightRise : FightFollow;
            }
            else musTension = 0f;
            if (want <= 0f ? musFight > 0f : Mathf.Abs(want - musFight) >= FightStep)
            {
                Music.Stem("fight", want, secs);
                musFight = want;
            }
            // a bite: down for the moment of the strike; the legend's build-up: dimmed until it eases off (or comes)
            float meter = Watch != null ? Watch.Meter : 0f;
            musBuild = meter >= BuildOn || (musBuild && meter >= BuildOff);
            if (State == S.Biting) SetDuck(BiteDuck, BiteDown);
            else if (musBuild && State != S.Fighting) SetDuck(BuildDuck, BuildDim);
            else SetDuck(1f, musDuck <= BiteDuck ? BiteBack : DuckBack);
        }

        void EncounterMusic()
        {
            var e = Encounter;
            if (musEnc != e)
            {
                // the omen: the stage fades out under sting_omen, the encounter's deck starts silent (its lurk comes up
                // with the window)
                musEnc = e;
                musPh = null;
                musMode = Mus.Encounter;
                musLegendOn = false;
                musBuild = false;
                musFight = 0f;
                SetDuck(1f, 0.5f);
                if (Music.Has("encounter")) Music.Play("encounter", OmenFade, ("lurk", 0f));
                else Music.Stop(OmenFade);
                Music.Sting("sting_omen", 1f);
            }
            if (musPh != e.Ph)
            {
                musPh = e.Ph;
                switch (e.Ph)
                {
                    case LegendEncounter.Phase.Open:
                    case LegendEncounter.Phase.Eyes:
                        EncounterStems(1f, 0f, 0f, EyesFade);
                        break;
                    case LegendEncounter.Phase.Approach:
                        EncounterStems(1f, 1f, 0f, ApproachFade);
                        break;
                    case LegendEncounter.Phase.Tease:
                    case LegendEncounter.Phase.NoseIn:
                        EncounterStems(1f, 1f, 1f, TeaseFade);
                        break;
                    case LegendEncounter.Phase.Lunge:
                    case LegendEncounter.Phase.HookWindow:
                        SetDuck(0f, WindowDuck);
                        break;
                    case LegendEncounter.Phase.Hooked:
                        // the strike: the window's music gone (silent as it was), sting_hook, the legend's cue after it
                        // (its clips loading under the sting, so it starts the moment it is asked for)
                        Music.Stop(0.3f);
                        SetDuck(1f, 0f);
                        Music.Sting("sting_hook", 0f);
                        Music.Preload("legend_" + e.Sp.id);
                        musMode = Mus.Legend;
                        musLegendOn = false;
                        break;
                    case LegendEncounter.Phase.TurnAway:
                        // turned away: sting_fail, the stage back up under it (the window's deck keeps the duck it was
                        // heard with, so PlayStage before SetDuck, and goes silent under the sting's)
                        Music.Sting("sting_fail", 0f);
                        musMode = Mus.Stage;
                        musFight = 0f;
                        musStem = PeriodStem;
                        PlayStage(StageBack);
                        SetDuck(1f, 0f);
                        break;
                }
            }
            if (musMode == Mus.Legend) LegendMusic(e.Sp);
        }

        void EncounterStems(float lurk, float approach, float tease, float seconds)
        {
            Music.Stem("lurk", lurk, seconds);
            Music.Stem("approach", approach, seconds);
            Music.Stem("tease", tease, seconds);
            SetDuck(1f, 0.4f);
        }

        /// <summary>A legend on the line: its own cue once sting_hook has (nearly) played out.</summary>
        void LegendMusic(FishSpecies sp)
        {
            if (musMode != Mus.Legend)
            {
                // (on the line without the encounter's strike, e.g. a test hook: its cue at once)
                musMode = Mus.Legend;
                musLegendOn = false;
                SetDuck(1f, 0.3f);
            }
            if (musLegendOn || Music.StingLeft > HookHandoff) return;
            musLegendOn = true;
            string cue = "legend_" + sp.id;
            if (Music.Has(cue)) Music.Play(cue, LegendFade);
            else
            {
                musFight = 1f;
                musStem = PeriodStem;
                PlayStage(LegendStage);
            }
        }

        /// <summary>Landed (the fish swung up, its card to come): the sting by its rarity (a legend's: the stage comes back under it).</summary>
        void CatchMusic(FishSpecies sp)
        {
            bool legend = sp.encounter != null || sp.rarity == Rarity.Legendary;
            bool rare = sp.rarity == Rarity.Rare || sp.rarity == Rarity.Epic;
            Music.Sting(legend ? "sting_legend" : rare ? "sting_rare" : "sting_catch", legend ? 0f : rare ? RareDuck : CatchDuck);
        }

        /// <summary>The fish is off (shaken off, the line broken): sting_escape (after a legend fight the stage comes back under it).</summary>
        void FishOffMusic() => Music.Sting("sting_escape", musMode == Mus.Legend ? 0f : EscapeDuck);

        /// <summary>A snag forced until the line broke (not the 끊기 button): the line lost, sting_escape.</summary>
        void SnagBreakMusic() => Music.Sting("sting_escape", SnagDuck);
    }
}
