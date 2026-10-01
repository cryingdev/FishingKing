using System;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The second set of synthesized sounds: footsteps per surface, the reel's drag slipping (a loop), keeping /
    /// releasing a fish, a small success chime, lure actions (the popper's chug, the frog's scurry on a pad, the jerkbait's
    /// rattle), and the per-stage ambience with its time-of-day layers (dawn birds, night insects), built lazily per stage.
    /// </summary>
    public partial class Sfx
    {
        public static AudioClip StepWood, StepStone, StepSnow, StepDeck, Keep, Release, Success, Pop, Scurry, Rattle, DragLoop;
        /// <summary>
        /// Recorded one-shots (Resources/Audio/Sfx/&lt;name&gt;): the rod swung forward in the cast, the float landing on the
        /// water. Null when the file is missing; the callers then use the synthesized Cast / Whoosh / Plop as before.
        /// </summary>
        public static AudioClip CastSwing, FloatLand;
        /// <summary>
        /// The reel's recorded clicks (Resources/Audio/Sfx/reel_click_1..N, as many as there are): click 1 starts a turn of
        /// the handle, then 2..N follow in order and wrap (<see cref="ReelClick"/>). Empty: the synthesized ReelTick.
        /// </summary>
        static readonly System.Collections.Generic.List<AudioClip> reelClicks = new System.Collections.Generic.List<AudioClip>();
        /// <summary>Seconds without a click after which the next one counts as starting to wind again (click 1).</summary>
        const float ReelRestart = 0.35f;
        static int reelNext;
        static float reelLast = -99f;

        /// <summary>Ambience volume (the old single loop's level) and the time-of-day layers' level at full weight.</summary>
        const float AmbBase = 0.35f, AmbLayer = 0.3f;
        /// <summary>Seconds the drag loop keeps sounding after the last <see cref="Drag"/> call, then fades over this too.</summary>
        const float DragHold = 0.12f;

        AudioSource dawnSrc, nightSrc, drag, thrash;
        /// <summary>The taut line twanging under load: one rubbery pluck (see <see cref="LineStrain"/>).</summary>
        public static AudioClip LineTing;
        static float lastTing = -99f;
        /// <summary>Seconds between tings at the bottom / the top of the strain (they come faster as it climbs).</summary>
        const float TingSlow = 0.9f, TingFast = 0.16f;
        /// <summary>The pitch's curve over the strain (1 = straight; 2.5: half the strain gives under a fifth of the rise).</summary>
        const float StrainPitchCurve = 2.5f;
        /// <summary>The hooked fish splashing at the surface: Resources/Audio/Sfx/fish_thrash (a loop) when present, else the synthesized splashing loop.</summary>
        public static AudioClip FishThrash;
        float thrashWant, thrashUntil = -1f;
        /// <summary>Seconds the thrash loop fades in / out over.</summary>
        const float ThrashFade = 0.25f;
        float dragWant, dragSeen = -1f, ambMult = 1f, ambGain = 1f, dawnW, nightW;
        /// <summary>
        /// The base ambience's level for a recorded loop (Resources/Audio/Ambience/amb_&lt;stage&gt;, meant to be normalized to
        /// -24 dBFS RMS) on top of AmbBase. (The synthesized loops are only peak-capped at 0.9 by Make, never raised.)
        /// </summary>
        const float RecordedAmbGain = 1.4f;
        static readonly System.Collections.Generic.Dictionary<string, AudioClip> loops = new System.Collections.Generic.Dictionary<string, AudioClip>();

        void InitFoley()
        {
            dawnSrc = Loop(0f);
            nightSrc = Loop(0f);
            drag = Loop(0f);
            thrash = Loop(0f);
            BuildFoley();
            drag.clip = DragLoop;
            CastSwing = Resources.Load<AudioClip>("Audio/Sfx/cast_swing");
            FloatLand = Resources.Load<AudioClip>("Audio/Sfx/float_land");
            FishThrash = Resources.Load<AudioClip>("Audio/Sfx/fish_thrash");
            if (FishThrash == null) FishThrash = BuildThrashLoop();
            thrash.clip = FishThrash;
            for (int k = 1; ; k++)
            {
                var c = Resources.Load<AudioClip>("Audio/Sfx/reel_click_" + k);
                if (c == null) break;
                reelClicks.Add(c);
            }
        }

        AudioSource Loop(float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.volume = vol;
            return s;
        }

        void Update()
        {
            if (audioReset)
            {
                // a new output device stopped every source: the loops that only start on a stage change play again
                // (drag / thrash restart on their next call, the chiptune and the music by Music)
                audioReset = false;
                foreach (var s in new[] { ambience, dawnSrc, nightSrc })
                    if (s != null && s.clip != null && !s.isPlaying) s.Play();
                if (rasp != null && raspWant > 0f && !rasp.isPlaying) rasp.Play();
            }
            TickAfterSting();
            // the drag loop follows Drag() calls and fades out on its own once they stop (the fight ended, the scene left)
            if (drag == null) return;
            float age = Time.unscaledTime - dragSeen;
            float want = (age <= DragHold ? dragWant : 0f) * AudioMix.Effects;
            drag.volume = Mathf.MoveTowards(drag.volume, want, Time.unscaledDeltaTime / DragHold);
            if (drag.volume <= 0.001f && drag.isPlaying) drag.Stop();
            // the thrash loop: on while Thrash() keeps it alive, then fades
            float tw = (Time.unscaledTime <= thrashUntil ? thrashWant : 0f) * AudioMix.Effects;
            thrash.volume = Mathf.MoveTowards(thrash.volume, tw, Time.unscaledDeltaTime / ThrashFade);
            if (thrash.volume <= 0.001f && thrash.isPlaying) thrash.Stop();
        }

        /// <summary>
        /// The line under high tension twanging: level 0..1 (0 = silent; the caller maps the tension to it). A rubbery twang
        /// every TingSlow .. TingFast seconds, louder and higher as it climbs (pitch x1.05 .. x1.9, a string being tightened,
        /// slowly at first and steeply near the break: <see cref="StrainPitchCurve"/>).
        /// Call it every frame; the tings stop when the calls do. The gap is measured from the last ting (not reset when
        /// the level drops to 0), so a tension dithering around the threshold twangs at most every TingSlow s, never once
        /// per upward crossing; after a quiet spell the first ting comes at once.
        /// </summary>
        public static void LineStrain(float level)
        {
            if (I == null || LineTing == null || level <= 0f) return;
            float now = Time.unscaledTime;
            level = Mathf.Clamp01(level);
            if (now - lastTing < Mathf.Lerp(TingSlow, TingFast, level)) return;
            // the pitch climbs slowly at first and steeply towards the break (level ^ StrainPitchCurve)
            float rise = Mathf.Pow(level, StrainPitchCurve);
            Play(LineTing, 0.12f + 0.33f * level, 1.05f + 0.85f * rise + UnityEngine.Random.Range(-0.015f, 0.015f));
            lastTing = now;
            TingCount++;
        }

        /// <summary>Tings played since boot (for the tests: the rate while the tension dithers at the threshold).</summary>
        internal static int TingCount;

        AudioClip afterClip;
        float afterVol, afterWait;
        /// <summary>A sound held back under a music sting waits at most this long.</summary>
        const float AfterStingMax = 3f;

        /// <summary>
        /// A fanfare-like one-shot (the level-up arpeggio) that would clash with a music sting sounding now: held until the
        /// sting is nearly out (<see cref="FishingKing.Music.StingLeft"/> under 0.3 s, at most <see cref="AfterStingMax"/> s), else played at once.
        /// </summary>
        public static void PlayAfterSting(AudioClip c, float vol = 1f)
        {
            if (I == null || c == null) return;
            if (FishingKing.Music.StingNow == null)
            {
                Play(c, vol);
                return;
            }
            I.afterClip = c;
            I.afterVol = vol;
            I.afterWait = 0f;
        }

        void TickAfterSting()
        {
            if (afterClip == null) return;
            afterWait += Time.unscaledDeltaTime;
            if (FishingKing.Music.StingNow != null && FishingKing.Music.StingLeft > 0.3f && afterWait < AfterStingMax) return;
            Play(afterClip, afterVol);
            afterClip = null;
        }

        /// <summary>
        /// The hooked fish thrashing at the surface: level 0..1 for the next `hold` seconds (call it every frame it splashes,
        /// or once with a longer hold for a burst, e.g. the hook set). Fades out by itself.
        /// </summary>
        public static void Thrash(float level, float hold = 0.15f)
        {
            if (I == null || I.thrash == null || FishThrash == null) return;
            float now = Time.unscaledTime;
            I.thrashWant = now <= I.thrashUntil ? Mathf.Max(I.thrashWant, Mathf.Clamp01(level)) : Mathf.Clamp01(level);
            I.thrashUntil = Mathf.Max(I.thrashUntil, now + hold);
            if (!I.thrash.isPlaying) I.thrash.Play();
        }

        // ------------------------------------------------------------------ API

        /// <summary>One click of the reel as it winds: the recorded sequence (click 1 after a pause, then 2..N in turn), else ReelTick.</summary>
        public static void ReelClick(float vol = 0.35f)
        {
            if (reelClicks.Count == 0)
            {
                PlayVar(ReelTick, vol, 0.15f);
                return;
            }
            float now = Time.unscaledTime;
            if (now - reelLast > ReelRestart) reelNext = 0;                                   // starting to wind: click 1
            else if (reelNext >= reelClicks.Count) reelNext = reelClicks.Count > 1 ? 1 : 0;   // wrap to click 2
            reelLast = now;
            PlayVar(reelClicks[reelNext], vol, 0.03f);
            reelNext++;
        }

        /// <summary>One footstep on this stage's ground (the pier's planks, rock, snow, the boat's deck).</summary>
        public static void Step(string stageId, float vol = 0.35f)
        {
            var c = stageId switch
            {
                "lake" or "swamp" => StepWood,
                "ice" => StepSnow,
                "ocean" => StepDeck,
                _ => StepStone,
            };
            PlayVar(c, vol, 0.1f);
        }

        /// <summary>The drag slipping this frame: amount 0..1 (louder and higher as the fish takes line); call it every frame it slips.</summary>
        public static void Drag(float amount)
        {
            if (I == null || I.drag == null) return;
            amount = Mathf.Clamp01(amount);
            I.dragWant = 0.25f + 0.35f * amount;
            I.drag.pitch = 0.85f + 0.35f * amount;
            I.dragSeen = Time.unscaledTime;
            if (!I.drag.isPlaying) I.drag.Play();
        }

        /// <summary>
        /// The stage's ambience: its own base loop plus the dawn and night layers it has (see <see cref="AmbienceLayers"/>).
        /// Stages without a loop of their own fall back to <see cref="Ambience"/>(kind).
        /// </summary>
        public static void StageAmbience(string stageId, string kind)
        {
            if (I == null) return;
            // a recorded loop for the stage wins over the synthesized one
            var b = Resources.Load<AudioClip>("Audio/Ambience/amb_" + stageId);
            float gain = b != null ? RecordedAmbGain : SynthAmbGain(stageId);
            if (b == null) b = StageLoop(stageId);
            if (b == null)
            {
                Ambience(kind);
                return;
            }
            I.ambGain = gain;
            I.ApplyAmbience();
            SetLoop(I.ambience, b);
            SetLoop(I.dawnSrc, DawnLoop(stageId));
            SetLoop(I.nightSrc, NightLoop(stageId));
        }

        /// <summary>The time-of-day layers' weights (0..1): dawn birds and night insects / frogs, where the stage has them.</summary>
        public static void AmbienceLayers(float dawn, float night)
        {
            if (I == null || I.dawnSrc == null) return;
            I.dawnW = Mathf.Clamp01(dawn);
            I.nightW = Mathf.Clamp01(night);
            I.ApplyAmbience();
        }

        /// <summary>The ambience and its layers at their levels: AmbBase x night dimming x a recorded loop's gain, AmbLayer x the layer's weight, all x the 환경음 slider.</summary>
        void ApplyAmbience()
        {
            if (ambience != null) ambience.volume = AmbBase * ambMult * ambGain * AudioMix.Amb;
            if (dawnSrc != null) dawnSrc.volume = AmbLayer * dawnW * ambMult * AudioMix.Amb;
            if (nightSrc != null) nightSrc.volume = AmbLayer * nightW * ambMult * AudioMix.Amb;
        }

        /// <summary>
        /// For the tests (Docs/music.md 1.2): the RMS (dBFS) of a stage's synthesized base loop, dawn and night layers as
        /// they play at their designed levels (x AmbBase / x AmbLayer), before the sliders; NaN where it has none.
        /// </summary>
        internal static (float bed, float dawn, float night) AmbienceRmsDb(string stageId)
        {
            static float Db(AudioClip c, float gain)
            {
                if (c == null) return float.NaN;
                var d = new float[c.samples * c.channels];
                c.GetData(d, 0);
                double s = 0;
                foreach (var v in d) s += v * v;
                return 20f * Mathf.Log10(Mathf.Max(1e-9f, Mathf.Sqrt((float)(s / Mathf.Max(1, d.Length))) * gain));
            }
            return (Db(StageLoop(stageId), AmbBase * SynthAmbGain(stageId)), Db(DawnLoop(stageId), AmbLayer), Db(NightLoop(stageId), AmbLayer));
        }

        /// <summary>The base ambience's AudioSource.volume now (for the tests).</summary>
        internal static float AmbienceVolumeNow => I != null && I.ambience != null ? I.ambience.volume : 0f;

        /// <summary>The drag loop's / the rasp's AudioSource.volume now (for the tests).</summary>
        internal static float DragVolumeNow => I != null && I.drag != null ? I.drag.volume : 0f;
        internal static float RaspVolumeNow => I != null && I.rasp != null && I.rasp.isPlaying ? I.rasp.volume : 0f;

        static void SetLoop(AudioSource s, AudioClip c)
        {
            if (s == null || (s.clip == c && (c == null || s.isPlaying))) return;
            s.clip = c;
            if (c != null) s.Play();
            else s.Stop();
        }

        /// <summary>
        /// A synthesized base loop's trim, so every stage's ambience sits about 4-6 dB under its stage bed (about -29 dBFS
        /// RMS, Docs/music.md 1.2). Measured at AmbBase (-fkauto music, [MIX] ambience): the stream's rushing water -21.2 dBFS
        /// (x 0.25: -33.2), the sea's waves -31.4 (x 0.74: -34.0); lake -34.1, swamp -39.6, ice -38.7, ocean -40.9, cave -41.1
        /// stay as they are (quiet textures under the bed's rests).
        /// </summary>
        static float SynthAmbGain(string id) => id switch
        {
            "stream" => 0.25f,
            "sea" => 0.74f,
            _ => 1f,
        };

        static AudioClip Cached(string key, Func<AudioClip> make)
        {
            if (!loops.TryGetValue(key, out var c)) loops[key] = c = make();
            return c;
        }

        static AudioClip StageLoop(string id) => id switch
        {
            "lake" => Cached("lake", BuildLakeLoop),
            "stream" => Cached("stream", BuildStreamLoop),
            "sea" => Cached("sea", BuildSeaLoop),
            "swamp" => Cached("swamp", BuildSwampLoop),
            "ocean" => Cached("ocean", BuildOceanLoop),
            "ice" => Cached("ice", BuildIceLoop),
            "cave" => ambCave,
            _ => null,
        };

        static AudioClip DawnLoop(string id) => id is "lake" or "stream" or "swamp" ? Cached("dawn", BuildDawnLoop) : null;

        static AudioClip NightLoop(string id) => id switch
        {
            "lake" or "stream" => Cached("crickets", () => BuildNightLoop(false)),
            "swamp" => Cached("frogs", () => BuildNightLoop(true)),
            _ => null,
        };

        // ------------------------------------------------------------------ one-shots

        static void BuildFoley()
        {
            // footsteps: a hollow plank (a low knock + a short scuff), rock (a bright grit burst), snow (a crunch of grains),
            // the boat's deck (lower and hollower than the pier)
            var d = Buf(0.12f); Tone(d, 0, 0.07f, 150, 90, 0.9f, Tri, 0.002f, 0.6f); Tone(d, 0, 0.06f, 330, 300, 0.25f, Sine, 0.002f, 0.6f);
            Noise(d, 0, 0.05f, 0.35f, 0.3f, 0.1f, 2f, 61); StepWood = Make("step_wood", d);
            d = Buf(0.1f); Noise(d, 0, 0.07f, 1f, 0.7f, 0.35f, 2.2f, 63); Tone(d, 0, 0.03f, 120, 80, 0.4f, Sine, 0.001f, 0.6f); StepStone = Make("step_stone", d);
            d = Buf(0.18f); Grains(d, 0, 0.15f, 70, 0.7f, 65); Noise(d, 0, 0.12f, 0.25f, 0.2f, 0.1f, 1.5f, 66); StepSnow = Make("step_snow", d);
            d = Buf(0.16f); Tone(d, 0, 0.1f, 110, 70, 1f, Tri, 0.003f, 0.5f); Tone(d, 0, 0.09f, 220, 200, 0.3f, Sine, 0.003f, 0.5f);
            Noise(d, 0, 0.04f, 0.2f, 0.2f, 0.1f, 2f, 67); StepDeck = Make("step_deck", d);

            // the fish into the bucket (a splash and the bucket's metal ring) / back into the water (a soft splash and a swish)
            d = Buf(0.6f); Noise(d, 0, 0.35f, 1f, 0.55f, 0.06f, 2f, 71); Tone(d, 0.02f, 0.5f, 820, 815, 0.22f, Sine, 0.002f, 0.35f);
            Tone(d, 0.02f, 0.45f, 1340, 1330, 0.14f, Sine, 0.002f, 0.4f); Tone(d, 0, 0.08f, 260, 110, 0.35f, Sine); Keep = Make("keep", d);
            d = Buf(0.6f); Noise(d, 0, 0.18f, 0.6f, 0.08f, 0.35f, 0.8f, 73); Noise(d, 0.16f, 0.4f, 1f, 0.45f, 0.05f, 2f, 74);
            Tone(d, 0.16f, 0.1f, 240, 100, 0.3f, Sine); Release = Make("release", d);

            // a small success: two bell notes (G6, C7) with a soft attack (bank shot, turned the fish, pulled it out, freed)
            d = Buf(0.55f); Bell(d, 0, 1568f, 0.5f, 0.35f); Bell(d, 0.09f, 2093f, 0.6f, 0.45f); Success = Make("success", d);

            // lures: the popper's chug (a low bloop with a burst of water), the frog's scurry (five quick pats), the jerkbait's
            // rattle (beads clicking)
            d = Buf(0.22f); Tone(d, 0, 0.09f, 420, 150, 0.9f, Sine, 0.002f, 0.6f); Noise(d, 0.01f, 0.12f, 0.6f, 0.5f, 0.08f, 2f, 81); Pop = Make("pop", d);
            d = Buf(0.24f);
            for (int k = 0; k < 5; k++) Noise(d, k * 0.042f, 0.02f, 1f - 0.12f * k, 0.55f, 0.25f, 2.5f, 83 + k);
            Scurry = Make("scurry", d);
            d = Buf(0.16f);
            float[] at = { 0f, 0.018f, 0.031f, 0.052f, 0.07f, 0.095f };
            for (int k = 0; k < at.Length; k++) Tone(d, at[k], 0.006f, 3300 + 240 * (k % 3), 3100, 0.7f - 0.07f * k, Sq, 0.0005f, 0.6f);
            Rattle = Make("rattle", d);

            DragLoop = BuildDragLoop();
            LineTing = BuildLineTing();
        }

        /// <summary>A decaying bell: a sine partial with two inharmonic overtones.</summary>
        static void Bell(float[] d, float start, float f, float dur, float vol)
        {
            Tone(d, start, dur, f, f, vol, Sine, 0.004f, 0.45f);
            Tone(d, start, dur * 0.6f, f * 2.76f, f * 2.76f, vol * 0.25f, Sine, 0.002f, 0.7f);
            Tone(d, start, dur * 0.4f, f * 5.4f, f * 5.4f, vol * 0.1f, Sine, 0.001f, 0.8f);
        }

        /// <summary>Random tiny clicks (snow crunch, gravel): `count` high-passed impulses over `dur`.</summary>
        static void Grains(float[] d, float start, float dur, int count, float vol, int seed)
        {
            var rnd = new System.Random(seed);
            int s0 = (int)(start * Rate), n = (int)(dur * Rate);
            for (int k = 0; k < count; k++)
            {
                int at = s0 + rnd.Next(n);
                float env = 1f - (float)(at - s0) / n;
                float a = vol * env * (0.4f + 0.6f * (float)rnd.NextDouble());
                float sign = rnd.Next(2) == 0 ? 1f : -1f;
                for (int i = 0; i < 12 && at + i < d.Length; i++) d[at + i] += sign * a * (i % 2 == 0 ? 1f : -0.8f) * (1f - i / 12f);
            }
        }

        /// <summary>
        /// One twang of the taut line, modelled on a recorded rubber band (measured: f0 ~113 Hz, a full harmonic series (here to the
        /// 24th) with the 3rd weaker than the 4th, the overtones dying 2-3x faster than the fundamental, ~0.3 s). Its pitch
        /// starts a little flat, rises ~6 % in the first 60 ms as the band snaps taut, then sags back; a low thump and a tiny
        /// click at the pluck.
        /// </summary>
        static AudioClip BuildLineTing()
        {
            var d = Buf(0.34f);
            const float f0 = 113f;
            // harmonic levels at the pluck (dB, measured) and their decay times (s)
            float[] db = { 0, -4, -16, -10, -16, -16, -21, -20, -27, -24, -28, -28, -29, -30, -31, -31, -33, -33, -34, -35, -36, -37, -38, -38 };
            double ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float rise = 1f - Mathf.Exp(-t / 0.018f);
                float sag = Mathf.Clamp01((t - 0.08f) / 0.17f);
                float f = f0 * (0.955f + 0.065f * rise - 0.045f * sag * sag * (3f - 2f * sag)) * (1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 7f * t));
                ph += f / Rate;
                float v = 0f;
                for (int k = 1; k <= db.Length; k++)
                {
                    float tau = k == 1 ? 0.075f : k <= 4 ? 0.04f : 0.028f;
                    v += Mathf.Pow(10f, db[k - 1] / 20f) * Mathf.Exp(-t / tau) * Mathf.Sin((float)(2.0 * Math.PI * k * ph));
                }
                float attack = Mathf.Min(1f, t / 0.0015f);
                d[i] = v * attack + 0.3f * Mathf.Exp(-t / 0.03f) * Mathf.Sin(2f * Mathf.PI * 42f * t);   // the thump
            }
            Noise(d, 0f, 0.004f, 0.15f, 0.9f, 0.7f, 2f, 99);   // the click of the pluck
            Noise(d, 0f, 0.12f, 0.12f, 0.45f, 0.3f, 3f, 98);   // the rubber's buzz against the fingers, dying fast
            int fo = (int)(0.03f * Rate);
            for (int i = 0; i < fo; i++) d[d.Length - fo + i] *= 1f - (float)i / fo;
            return Make("line_ting", d);
        }

        /// <summary>
        /// The drag slipping: a ratchet's clicks at 45 Hz over a thin whine of the spool, exactly one second, so it loops
        /// without a seam (490 samples a click, 1200 whole cycles of the whine).
        /// </summary>
        static AudioClip BuildDragLoop()
        {
            var d = Buf(1f);
            var rnd = new System.Random(91);
            const int period = 490;
            var click = new float[60];
            float y = 0f;
            for (int i = 0; i < click.Length; i++)
            {
                y += 0.8f * ((float)rnd.NextDouble() * 2f - 1f - y);
                click[i] = y * Mathf.Pow(1f - i / 60f, 2f);
            }
            for (int s = 0; s + click.Length < d.Length; s += period)
                for (int i = 0; i < click.Length; i++) d[s + i] += click[i];
            for (int i = 0; i < d.Length; i++) d[i] += 0.12f * Mathf.Sin(2f * Mathf.PI * 1200f * i / Rate);
            return Make("drag", d, true);
        }

        /// <summary>
        /// A fish splashing at the surface (the stand-in for a recorded fish_thrash): irregular bursts of bright water noise
        /// with a low plop each over a light churn, about 1.5 s, looping.
        /// </summary>
        static AudioClip BuildThrashLoop()
        {
            var d = Buf(1.75f);
            Bed(d, 0.08f, 0.25f, 191);
            var rnd = new System.Random(192);
            for (int k = 0; k < 9; k++)
            {
                float t = (float)rnd.NextDouble() * 1.55f;
                Noise(d, t, 0.12f + 0.12f * (float)rnd.NextDouble(), 0.5f + 0.5f * (float)rnd.NextDouble(), 0.7f, 0.12f, 1.8f, 193 + k);
                Tone(d, t, 0.06f, 260f, 110f, 0.2f, Sine);
            }
            return Make("thrash", Crossfade(d), true);
        }

        // ------------------------------------------------------------------ ambience loops

        /// <summary>Low-passed noise into d (one pole, coefficient a), times vol.</summary>
        static void Bed(float[] d, float a, float vol, int seed, Func<float, float> env = null)
        {
            var rnd = new System.Random(seed);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                y += a * ((float)rnd.NextDouble() * 2f - 1f - y);
                d[i] += y * vol * (env != null ? env(t) : 1f);
            }
        }

        /// <summary>Band noise (the difference of two one-pole low passes) into d.</summary>
        static void Band(float[] d, float hi, float lo, float vol, int seed, Func<float, float> env = null)
        {
            var rnd = new System.Random(seed);
            float y = 0f, z = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                y += hi * ((float)rnd.NextDouble() * 2f - 1f - y);
                z += lo * (y - z);
                d[i] += (y - z) * vol * (env != null ? env(t) : 1f);
            }
        }

        /// <summary>A bird's chirp: a fast sine sweep, with a second note after it.</summary>
        static void Chirp(float[] d, float t, float f, float vol)
        {
            Tone(d, t, 0.07f, f, f * 1.35f, vol, Sine, 0.004f, 0.5f);
            Tone(d, t + 0.09f, 0.06f, f * 1.2f, f * 0.95f, vol * 0.8f, Sine, 0.004f, 0.5f);
        }

        /// <summary>Gentle lapping at the pier (the old water loop's motion, softer) and two far-off birds.</summary>
        static AudioClip BuildLakeLoop()
        {
            var d = Buf(12.25f);
            Band(d, 0.06f, 0.004f, 1f, 101, t => 0.35f + 0.65f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 3f) * Mathf.Sin(t * 2f * Mathf.PI / 2f), 2f));
            var rnd = new System.Random(102);
            for (int k = 0; k < 6; k++) Tone(d, 0.6f + k * 1.9f + (float)rnd.NextDouble(), 0.05f, 900, 1500, 0.04f, Sine, 0.001f, 0.8f); // small drips at the posts
            Chirp(d, 3.1f, 2600f, 0.06f);
            Chirp(d, 8.4f, 3100f, 0.05f);
            return Make("amb_lake", Crossfade(d), true);
        }

        /// <summary>Rushing water: loud band noise with a slow churn, and bubbling.</summary>
        static AudioClip BuildStreamLoop()
        {
            var d = Buf(10.25f);
            Band(d, 0.45f, 0.03f, 1f, 111, t => 0.8f + 0.12f * Mathf.Sin(t * 2f * Mathf.PI * 0.7f) + 0.08f * Mathf.Sin(t * 2f * Mathf.PI * 1.3f));
            Bed(d, 0.03f, 0.9f, 112);
            var rnd = new System.Random(113);
            for (int k = 0; k < 90; k++)
            {
                float f = 500f + 900f * (float)rnd.NextDouble();
                Tone(d, (float)rnd.NextDouble() * 9.8f, 0.03f + 0.03f * (float)rnd.NextDouble(), f, f * 1.6f, 0.06f, Sine, 0.002f, 0.6f);
            }
            return Make("amb_stream", Crossfade(d), true);
        }

        /// <summary>Waves breaking on the tetrapods (a swell, a crash, the backwash) twice per loop, and gulls.</summary>
        static AudioClip BuildSeaLoop()
        {
            var d = Buf(13.25f);
            Bed(d, 0.02f, 0.8f, 121, t => 0.4f + 0.3f * Mathf.Sin(t * 2f * Mathf.PI / 6.5f));
            for (int w = 0; w < 2; w++)
            {
                float t0 = 1.2f + w * 6.5f;
                Noise(d, t0, 0.5f, 0.9f, 0.6f, 0.4f, 0.6f, 122 + w);          // the crash
                Noise(d, t0 + 0.3f, 3.5f, 0.6f, 0.25f, 0.06f, 1.4f, 124 + w); // the backwash hiss
            }
            void Gull(float t, float f)
            {
                Tone(d, t, 0.22f, f, f * 0.72f, 0.07f, Tri, 0.01f, 0.4f);
                Tone(d, t + 0.25f, 0.18f, f * 0.95f, f * 0.7f, 0.06f, Tri, 0.01f, 0.4f);
            }
            Gull(4.0f, 1900f);
            Gull(10.6f, 2100f);
            return Make("amb_sea", Crossfade(d), true);
        }

        /// <summary>Still murky water, a faint insect hum, and a few low frog croaks.</summary>
        static AudioClip BuildSwampLoop()
        {
            var d = Buf(12.25f);
            Bed(d, 0.01f, 0.7f, 131);
            Band(d, 0.9f, 0.6f, 0.04f, 132, t => 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 31f));
            foreach (float t in new[] { 1.5f, 5.8f, 9.1f }) Croak(d, t, 120f, 0.12f);
            return Make("amb_swamp", Crossfade(d), true);
        }

        /// <summary>A frog's croak: three quick pulses of a buzzy low tone.</summary>
        static void Croak(float[] d, float t, float f, float vol)
        {
            for (int k = 0; k < 3; k++) Tone(d, t + k * 0.085f, 0.06f, f * 1.1f, f, vol, x => Tri(x) * (0.6f + 0.4f * Sq(x * 0.19f)), 0.004f, 0.4f);
        }

        /// <summary>The open sea from the boat: a deep slow swell, wind over the water, and the hull's creaks.</summary>
        static AudioClip BuildOceanLoop()
        {
            var d = Buf(14.25f);
            Bed(d, 0.008f, 1f, 141, t => 0.55f + 0.45f * Mathf.Sin(t * 2f * Mathf.PI / 7f));
            Band(d, 0.5f, 0.2f, 0.05f, 142, t => 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI / 4.7f + 1f));
            void Creak(float t, float f)
            {
                Tone(d, t, 0.55f, f, f * 0.82f, 0.08f, x => Tri(x) * (0.5f + 0.5f * Sq(x * 0.06f)), 0.05f, 0.5f);
            }
            Creak(2.2f, 210f);
            Creak(9.3f, 180f);
            Tone(d, 5.6f, 0.15f, 120, 100, 0.12f, Sine, 0.003f, 0.6f); // a rope / hull knock
            return Make("amb_ocean", Crossfade(d), true);
        }

        /// <summary>The wind over the ice (as before) and, once per loop, the ice singing as it cracks.</summary>
        static AudioClip BuildIceLoop()
        {
            var d = Buf(12.25f);
            var rnd = new System.Random(151);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float gust = 0.4f + 0.6f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 6f), 2f);
                y += 0.015f * ((float)rnd.NextDouble() * 2f - 1f - y);
                d[i] = y * gust;
            }
            Noise(d, 7.2f, 0.04f, 0.15f, 0.9f, 0.6f, 2f, 152);
            Tone(d, 7.21f, 0.9f, 1200f, 180f, 0.05f, Sine, 0.001f, 0.5f); // the "pew" of a crack running through the ice
            return Make("amb_ice", Crossfade(d), true);
        }

        /// <summary>Dawn: a chorus of chirps and a short trill.</summary>
        static AudioClip BuildDawnLoop()
        {
            var d = Buf(11.25f);
            var rnd = new System.Random(161);
            for (int k = 0; k < 16; k++) Chirp(d, (float)rnd.NextDouble() * 10.6f, 2400f + 1800f * (float)rnd.NextDouble(), 0.08f + 0.06f * (float)rnd.NextDouble());
            for (int k = 0; k < 8; k++) Tone(d, 5f + k * 0.06f, 0.04f, 3600f, 3900f, 0.07f, Sine, 0.002f, 0.5f);
            return Make("amb_dawn", Crossfade(d), true);
        }

        /// <summary>Night: two crickets (trains of three chirps at 4.4 / 3.9 kHz), and on the swamp frogs as well.</summary>
        static AudioClip BuildNightLoop(bool frogs)
        {
            var d = Buf(10.25f);
            var rnd = new System.Random(frogs ? 171 : 172);
            foreach (float f in new[] { 4400f, 3900f })
            {
                float t = (float)rnd.NextDouble() * 0.5f;
                while (t < 9.9f)
                {
                    for (int k = 0; k < 3; k++) Tone(d, t + k * 0.03f, 0.018f, f, f, f > 4000f ? 0.07f : 0.05f, Sine, 0.002f, 0.3f);
                    t += 0.55f + 0.2f * (float)rnd.NextDouble();
                }
            }
            if (frogs)
                for (int k = 0; k < 6; k++) Croak(d, 0.7f + k * 1.6f + 0.4f * (float)rnd.NextDouble(), 140f + 40f * (k % 2), 0.1f);
            return Make(frogs ? "amb_night_frogs" : "amb_night", Crossfade(d), true);
        }
    }
}
