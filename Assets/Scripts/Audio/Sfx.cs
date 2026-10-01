using System;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Procedurally synthesized sound effects, ambience and a small chiptune loop
    /// (no audio files needed). Lives on the persistent [Game] object. More sounds and the per-stage ambience: Sfx.Foley.cs.
    /// Every source honours the volume sliders live (<see cref="AudioMix"/>, <see cref="ApplyMix"/>): the one-shot pool's
    /// sources and the rasp / drag / thrash loops x <see cref="AudioMix.Effects"/>, the ambience and its layers x
    /// <see cref="AudioMix.Amb"/>, the chiptune x <see cref="AudioMix.Bgm"/>.
    /// </summary>
    public partial class Sfx : MonoBehaviour
    {
        const int Rate = 22050;
        static Sfx I;

        public static AudioClip Click, Cast, Splash, Plop, ReelTick, Bite, Nibble, Hook, Snap, Escape, Catch, Coin,
            LevelUp, Error, Warn, Bubble, Jump, Whoosh, Unlock,
            // the legend encounter (Docs/lures_legend_spec.md 4.3)
            Drone, Heartbeat, Glint, Chomp,
            // obstacles (Docs/obstacles_spec.md 10.3): a cast hitting rock / wood / a hull / crystal / leaves, the line
            // rubbing (a loop, see Rasp), a pad or weed pulled loose
            Tock, Knock, Thunk, Ting, Rustle, RaspLoop, Tear;

        /// <summary>One-shot voices. A busy voice taken again is re-pitched mid-sound, so an idle one is taken first.</summary>
        const int Voices = 16;
        AudioSource[] pool;
        int next;
        float[] poolStart;
        float raspWant, chipMult;
        /// <summary>The level the last one-shot was asked to play at, x the effects slider (for the tests).</summary>
        internal static float LastOneShotVol;
        // set by AudioSettings.OnAudioConfigurationChanged (a new output device stops every source), handled in Update
        volatile bool audioReset;
        AudioSource ambience, music, rasp;
        static AudioClip ambWater, ambWind, ambCave, musicLoop;

        void Awake()
        {
            I = this;
            pool = new AudioSource[Voices];
            poolStart = new float[Voices];
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i] = gameObject.AddComponent<AudioSource>();
                pool[i].playOnAwake = false;
            }
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfiguration;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.loop = true;
            ambience.volume = 0.35f;
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.volume = ChipBase;
            rasp = gameObject.AddComponent<AudioSource>();
            rasp.loop = true;
            rasp.playOnAwake = false;
            rasp.volume = 0f;
            Build();
            rasp.clip = RaspLoop;
            InitFoley();
            ApplyMix();
        }

        void OnDestroy() => AudioSettings.OnAudioConfigurationChanged -= OnAudioConfiguration;

        void OnAudioConfiguration(bool deviceWasChanged) => audioReset = true;

        /// <summary>
        /// The volume sliders changed (<see cref="AudioMix.Apply"/>): every source of ours at its level now. The one-shot
        /// voices carry the effects gain as their volume (PlayOneShot's level is x it), so sounds still playing follow too.
        /// </summary>
        public static void ApplyMix()
        {
            if (I == null || I.pool == null) return;
            foreach (var s in I.pool) s.volume = AudioMix.Effects;
            if (I.rasp != null && I.raspWant > 0f) I.rasp.volume = I.raspWant * AudioMix.Effects;
            if (I.music != null) I.music.volume = ChipBase * I.chipMult * AudioMix.Bgm;
            I.ApplyAmbience();
        }

        public static void Play(AudioClip c, float vol = 1f, float pitch = 1f)
        {
            if (I == null || c == null) return;
            // an idle voice (from the round-robin point on), else the one that started longest ago
            int n = I.pool.Length, pick = -1;
            for (int k = 0; k < n && pick < 0; k++)
            {
                int i = (I.next + k) % n;
                if (!I.pool[i].isPlaying) pick = i;
            }
            if (pick < 0)
            {
                pick = 0;
                for (int i = 1; i < n; i++)
                    if (I.poolStart[i] < I.poolStart[pick]) pick = i;
            }
            I.next = (pick + 1) % n;
            var s = I.pool[pick];
            I.poolStart[pick] = Time.unscaledTime;
            s.pitch = pitch;
            s.volume = AudioMix.Effects;
            LastOneShotVol = vol * s.volume;
            s.PlayOneShot(c, vol);
        }

        public static void PlayVar(AudioClip c, float vol = 1f, float spread = 0.08f) =>
            Play(c, vol, 1f + UnityEngine.Random.Range(-spread, spread));

        public static void Ambience(string kind)
        {
            if (I == null) return;
            AudioClip c = kind == "cave" ? ambCave : kind == "snow" ? ambWind : kind == "none" ? null : ambWater;
            SetLoop(I.dawnSrc, null);
            SetLoop(I.nightSrc, null);
            I.ambGain = 1f;
            I.ApplyAmbience();
            SetLoop(I.ambience, c);
        }

        /// <summary>The ambience's volume x this (the night is a little quieter).</summary>
        public static void AmbienceVolume(float mult)
        {
            if (I == null || I.ambience == null) return;
            I.ambMult = Mathf.Clamp01(mult);
            I.ApplyAmbience();
        }

        /// <summary>The line rubbing on structure: the rasp loop at this volume (0 stops it).</summary>
        public static void Rasp(float vol)
        {
            if (I == null || I.rasp == null) return;
            if (vol <= 0.001f)
            {
                I.raspWant = 0f;
                if (I.rasp.isPlaying) I.rasp.Stop();
                return;
            }
            I.raspWant = Mathf.Clamp01(vol);
            I.rasp.volume = I.raspWant * AudioMix.Effects;
            if (!I.rasp.isPlaying) I.rasp.Play();
        }

        /// <summary>The chiptune loop's volume x this (<see cref="FishingKing.Music"/> fades it in and out).</summary>
        public static void MusicVolume(float mult)
        {
            if (I == null || I.music == null) return;
            I.chipMult = Mathf.Clamp01(mult);
            I.music.volume = ChipBase * I.chipMult * AudioMix.Bgm;
        }

        /// <summary>The chiptune loop's designed level (Docs/music.md 1.2).</summary>
        const float ChipBase = 0.22f;

        /// <summary>The chiptune's AudioSource.volume now (for the tests).</summary>
        internal static float ChipVolume => I != null && I.music != null ? I.music.volume : 0f;

        public static void Music(bool on)
        {
            if (I == null) return;
            if (on && !I.music.isPlaying)
            {
                I.music.clip = musicLoop;
                I.music.Play();
            }
            else if (!on) I.music.Stop();
        }

        // ------------------------------------------------------------------ synthesis
        static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        static AudioClip Make(string name, float[] d, bool loop = false)
        {
            float peak = 0.0001f;
            foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            float g = Mathf.Min(1f, 0.9f / peak);
            for (int i = 0; i < d.Length; i++) d[i] *= g;
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static float Sq(float ph) => (ph % 1f) < 0.5f ? 1f : -1f;
        static float Tri(float ph) => 4f * Mathf.Abs((ph % 1f) - 0.5f) - 1f;
        static float Sine(float ph) => Mathf.Sin(ph * 2f * Mathf.PI);

        static void Tone(float[] d, float start, float dur, float f0, float f1, float vol, Func<float, float> wave, float attack = 0.005f, float release = 0.6f)
        {
            int s0 = (int)(start * Rate), n = (int)(dur * Rate);
            double ph = 0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(f0, f1, t);
                ph += f / Rate;
                float env = Mathf.Min(1f, i / (attack * Rate + 1)) * Mathf.Pow(1 - t, release * 3f);
                d[s0 + i] += wave((float)ph) * env * vol;
            }
        }

        static void Noise(float[] d, float start, float dur, float vol, float lp0, float lp1, float decay = 2f, int seed = 1)
        {
            var rnd = new System.Random(seed);
            int s0 = (int)(start * Rate), n = (int)(dur * Rate);
            float y = 0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = (float)i / n;
                float a = Mathf.Lerp(lp0, lp1, t);
                y += a * ((float)rnd.NextDouble() * 2 - 1 - y);
                float env = Mathf.Min(1f, i / (0.004f * Rate)) * Mathf.Pow(1 - t, decay);
                d[s0 + i] += y * env * vol;
            }
        }

        static void Build()
        {
            var d = Buf(0.05f); Tone(d, 0, 0.05f, 1400, 1100, 0.6f, Sq); Click = Make("click", d);
            d = Buf(0.35f); Noise(d, 0, 0.35f, 1f, 0.05f, 0.5f, 1.5f); Cast = Make("cast", d);
            d = Buf(0.25f); Noise(d, 0, 0.25f, 1f, 0.35f, 0.08f, 1.2f, 3); Whoosh = Make("whoosh", d);
            d = Buf(0.55f); Noise(d, 0, 0.5f, 1f, 0.6f, 0.05f, 2.2f, 5); Tone(d, 0, 0.12f, 300, 90, 0.5f, Sine); Splash = Make("splash", d);
            d = Buf(0.18f); Tone(d, 0, 0.12f, 700, 250, 0.7f, Sine); Noise(d, 0, 0.15f, 0.3f, 0.5f, 0.1f, 2f); Plop = Make("plop", d);
            d = Buf(0.03f); Noise(d, 0, 0.02f, 1f, 0.9f, 0.9f, 3f, 7); Tone(d, 0, 0.02f, 2600, 2400, 0.4f, Sq); ReelTick = Make("tick", d);
            d = Buf(0.4f); Tone(d, 0, 0.14f, 880, 880, 0.5f, Sq, 0.002f, 0.3f); Tone(d, 0.13f, 0.25f, 1320, 1320, 0.5f, Sq, 0.002f, 0.5f); Bite = Make("bite", d);
            d = Buf(0.12f); Tone(d, 0, 0.12f, 300, 200, 0.6f, Tri); Nibble = Make("nibble", d);
            d = Buf(0.3f); Noise(d, 0, 0.2f, 0.7f, 0.2f, 0.6f, 1f, 9); Tone(d, 0.05f, 0.2f, 160, 60, 0.9f, Tri); Hook = Make("hook", d);
            d = Buf(0.6f); Tone(d, 0, 0.5f, 1800, 120, 0.6f, Sq, 0.001f, 0.6f); Noise(d, 0, 0.1f, 1f, 0.9f, 0.3f, 2f, 11); Snap = Make("snap", d);
            d = Buf(0.7f); Tone(d, 0, 0.22f, 520, 480, 0.4f, Tri); Tone(d, 0.22f, 0.22f, 440, 400, 0.4f, Tri); Tone(d, 0.44f, 0.26f, 330, 300, 0.4f, Tri); Escape = Make("escape", d);
            d = Buf(1.1f);
            float[] notes = { 523, 659, 784, 1047 };
            for (int i = 0; i < notes.Length; i++) Tone(d, i * 0.11f, i == 3 ? 0.6f : 0.14f, notes[i], notes[i], 0.45f, Sq, 0.003f, i == 3 ? 0.4f : 0.2f);
            Tone(d, 0.33f, 0.7f, 523, 523, 0.25f, Tri, 0.003f, 0.5f);
            Catch = Make("catch", d);
            d = Buf(0.3f); Tone(d, 0, 0.08f, 988, 988, 0.4f, Sq, 0.002f, 0.2f); Tone(d, 0.07f, 0.22f, 1319, 1319, 0.4f, Sq, 0.002f, 0.5f); Coin = Make("coin", d);
            d = Buf(0.9f);
            float[] up = { 392, 523, 659, 784, 1047, 1319 };
            for (int i = 0; i < up.Length; i++) Tone(d, i * 0.08f, 0.2f, up[i], up[i], 0.4f, Sq, 0.002f, 0.4f);
            LevelUp = Make("levelup", d);
            d = Buf(0.25f); Tone(d, 0, 0.25f, 140, 110, 0.6f, Sq); Error = Make("error", d);
            d = Buf(0.09f); Tone(d, 0, 0.09f, 1760, 1760, 0.35f, Sq, 0.001f, 0.2f); Warn = Make("warn", d);
            d = Buf(0.1f); Tone(d, 0, 0.1f, 500, 1300, 0.5f, Sine); Bubble = Make("bubble", d);
            d = Buf(0.5f); Noise(d, 0, 0.45f, 1f, 0.8f, 0.1f, 1.6f, 13); Jump = Make("jump", d);
            d = Buf(0.6f); Tone(d, 0, 0.1f, 660, 660, 0.4f, Sq); Tone(d, 0.1f, 0.1f, 880, 880, 0.4f, Sq); Tone(d, 0.2f, 0.35f, 1320, 1320, 0.4f, Sq, 0.002f, 0.4f); Unlock = Make("unlock", d);
            // legend encounter: a low drone under the line, a heartbeat, the "ting" of the tell, the jaws closing
            d = Buf(1.5f); Tone(d, 0, 1.5f, 55, 50, 0.8f, Tri, 0.3f, 0.25f); Noise(d, 0, 1.5f, 0.25f, 0.03f, 0.02f, 1.2f, 21); Drone = Make("drone", d);
            d = Buf(0.35f); Tone(d, 0, 0.12f, 60, 45, 1f, Sine, 0.004f, 0.5f); Tone(d, 0, 0.08f, 120, 90, 0.3f, Tri, 0.004f, 0.6f);
            Tone(d, 0.17f, 0.14f, 50, 38, 0.85f, Sine, 0.004f, 0.5f); Tone(d, 0.17f, 0.08f, 100, 76, 0.25f, Tri, 0.004f, 0.6f); Heartbeat = Make("heartbeat", d);
            d = Buf(0.25f); Tone(d, 0, 0.25f, 2640, 2640, 0.5f, Sine, 0.001f, 0.5f); Glint = Make("glint", d);
            d = Buf(0.3f); Noise(d, 0, 0.12f, 1f, 0.6f, 0.2f, 2f, 31); Tone(d, 0.02f, 0.25f, 90, 60, 1f, Tri, 0.002f, 0.4f); Chomp = Make("chomp", d);

            // obstacles: a hard click with a noise tick (rock, concrete), a wooden knock, a hull's thunk with a light ring, a
            // crystal's ting with a shimmer, a leafy rustle, a short torn-weed burst, and the line's rasp (a loop)
            d = Buf(0.05f); Tone(d, 0, 0.03f, 1500, 1200, 0.6f, Sq, 0.001f, 0.4f); Noise(d, 0, 0.015f, 0.5f, 0.9f, 0.9f, 3f, 41); Tock = Make("tock", d);
            d = Buf(0.07f); Tone(d, 0, 0.06f, 700, 600, 0.8f, Tri, 0.001f, 0.5f); Knock = Make("knock", d);
            d = Buf(0.18f); Tone(d, 0, 0.15f, 260, 240, 0.9f, Sine, 0.002f, 0.6f); Tone(d, 0, 0.15f, 780, 760, 0.18f, Sine, 0.002f, 0.4f); Thunk = Make("thunk", d);
            d = Buf(0.32f); Tone(d, 0, 0.3f, 2800, 2800, 0.6f, Sine, 0.001f, 0.5f); Tone(d, 0.02f, 0.28f, 4200, 4150, 0.2f, Sine, 0.001f, 0.6f); Ting = Make("ting", d);
            d = Buf(0.13f); Noise(d, 0, 0.12f, 1f, 0.55f, 0.45f, 1.5f, 43); Rustle = Make("rustle", d);
            d = Buf(0.09f); Noise(d, 0, 0.08f, 1f, 0.15f, 0.08f, 1.5f, 47); Tear = Make("tear", d);
            RaspLoop = BuildRasp();
            ambWater = BuildWaterLoop();
            ambWind = BuildWindLoop();
            ambCave = BuildCaveLoop();
            musicLoop = BuildMusic();
        }

        /// <summary>Band noise (2-4 kHz-ish) amplitude-modulated at 18 Hz, one second, looping.</summary>
        static AudioClip BuildRasp()
        {
            var d = Buf(1f);
            var rnd = new System.Random(45);
            float y = 0f, lo = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                y += 0.85f * ((float)rnd.NextDouble() * 2f - 1f - y);
                lo += 0.25f * (y - lo);
                d[i] = (y - lo) * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 18f * t));
            }
            return Make("rasp", d, true);
        }

        static AudioClip BuildWaterLoop()
        {
            var d = Buf(6f);
            var rnd = new System.Random(21);
            float y = 0, y2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float lap = 0.5f + 0.5f * Mathf.Sin(t * 2 * Mathf.PI / 3f) * Mathf.Sin(t * 2 * Mathf.PI / 2f);
                y += 0.04f * ((float)rnd.NextDouble() * 2 - 1 - y);
                y2 += 0.3f * (y - y2);
                d[i] = y2 * (0.3f + 0.7f * lap);
            }
            d = Crossfade(d);
            return Make("amb_water", d, true);
        }

        static AudioClip BuildWindLoop()
        {
            var d = Buf(6f);
            var rnd = new System.Random(22);
            float y = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float gust = 0.4f + 0.6f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 2 * Mathf.PI / 6f), 2);
                y += 0.015f * ((float)rnd.NextDouble() * 2 - 1 - y);
                d[i] = y * gust;
            }
            d = Crossfade(d);
            return Make("amb_wind", d, true);
        }

        static AudioClip BuildCaveLoop()
        {
            var d = Buf(6f);
            var rnd = new System.Random(23);
            float y = 0;
            for (int i = 0; i < d.Length; i++)
            {
                y += 0.01f * ((float)rnd.NextDouble() * 2 - 1 - y);
                d[i] = y * 0.6f;
            }
            // water drips
            float[] drips = { 0.4f, 1.9f, 2.6f, 4.3f, 5.2f };
            foreach (var s in drips) Tone(d, s, 0.12f, 1400, 2200, 0.05f, Sine, 0.001f, 0.8f);
            d = Crossfade(d);
            return Make("amb_cave", d, true);
        }

        /// <summary>Blends the tail into the head and drops the tail so the clip loops seamlessly.</summary>
        static float[] Crossfade(float[] d)
        {
            int n = Rate / 4;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                d[i] = d[i] * k + d[d.Length - n + i] * (1 - k);
            }
            var o = new float[d.Length - n];
            Array.Copy(d, o, o.Length);
            return o;
        }

        /// <summary>Relaxed 8-bar chiptune loop in C major pentatonic.</summary>
        static AudioClip BuildMusic()
        {
            const float bpm = 96f;
            float beat = 60f / bpm;
            int bars = 8;
            var d = Buf(bars * 4 * beat);
            int[] chords = { 0, 5, 3, 4, 0, 5, 3, 4 }; // I vi IV V
            float[] roots = { 130.81f, 146.83f, 164.81f, 174.61f, 196.00f, 220.00f, 246.94f };
            int[] scale = { 0, 2, 4, 7, 9, 12, 14, 16 };
            var rnd = new System.Random(4);
            for (int b = 0; b < bars; b++)
            {
                float t0 = b * 4 * beat;
                float root = roots[chords[b]];
                // bass: root on 1 and 3, fifth on 2 and 4
                for (int q = 0; q < 4; q++)
                {
                    float f = q % 2 == 0 ? root / 2 : root / 2 * 1.5f;
                    Tone(d, t0 + q * beat, beat * 0.9f, f, f, 0.28f, Tri, 0.005f, 0.25f);
                }
                // arpeggio pad
                for (int e = 0; e < 8; e++)
                {
                    float f = root * (e % 4 == 0 ? 1f : e % 4 == 1 ? 1.25f : e % 4 == 2 ? 1.5f : 2f);
                    Tone(d, t0 + e * beat / 2, beat * 0.45f, f, f, 0.07f, Sq, 0.003f, 0.5f);
                }
                // melody
                for (int e = 0; e < 8; e++)
                {
                    if (rnd.NextDouble() < 0.35) continue;
                    int deg = scale[rnd.Next(scale.Length)];
                    float f = 523.25f * Mathf.Pow(2, deg / 12f) * (chords[b] == 3 ? 0.89f : 1f);
                    float len = rnd.NextDouble() < 0.3 ? beat : beat / 2;
                    Tone(d, t0 + e * beat / 2, len * 0.9f, f, f, 0.12f, x => Tri(x) * 0.6f + Sq(x) * 0.15f, 0.004f, 0.35f);
                }
            }
            return Make("music", d, true);
        }
    }
}
