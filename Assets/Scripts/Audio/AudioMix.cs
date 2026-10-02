using UnityEngine;

namespace FishingKing
{
    /// <summary>The four volume sliders of 설정 → 음량 (<see cref="AudioMix"/>).</summary>
    public enum AudioChannel { Master, Music, Effects, Ambience }

    /// <summary>
    /// The player's volume sliders (Docs/music.md 1.2, 4) as gains: each one a multiplier on top of the calibrated mix
    /// (100 % = the level the mix was designed at, 1.0 exactly; never used to re-balance it). The curve is (p/100)^2:
    /// 70 % about -6 dB, 50 % -12 dB, 10 % -40 dB, 0 silent. Cached here (refreshed by <see cref="Apply"/> whenever a
    /// setting changes) so the sources read a float, not the save, every frame:
    /// <list type="bullet">
    /// <item><see cref="Master"/>: AudioListener.volume (0 while 소리 is off: the master mute);</item>
    /// <item><see cref="Bgm"/>: every music deck, sting (one being cut too) and the chiptune (<see cref="Music"/>);</item>
    /// <item><see cref="Effects"/>: every one-shot (the pool's sources, live), the rasp, drag and thrash loops, the line's twang,
    /// the encounter's drone and heartbeat (<see cref="Sfx"/>);</item>
    /// <item><see cref="Amb"/>: the stage ambience and its dawn / night layers.</item>
    /// </list>
    /// </summary>
    public static class AudioMix
    {
        public const int Max = 100, Step = 10;

        public static float Master { get; private set; } = 1f;
        public static float Bgm { get; private set; } = 1f;
        public static float Effects { get; private set; } = 1f;
        public static float Amb { get; private set; } = 1f;

        /// <summary>A slider's percent (0..100) as a gain: (p/100)^2.</summary>
        public static float Curve(int percent)
        {
            float p = Mathf.Clamp(percent, 0, Max) / (float)Max;
            return p * p;
        }

        /// <summary>A slider value as saved: 0..100 in steps of 10.</summary>
        public static int Snap(int percent) => Mathf.Clamp(Mathf.RoundToInt(percent / (float)Step) * Step, 0, Max);

        public static int Get(SaveData d, AudioChannel c) => c switch
        {
            AudioChannel.Master => d.masterVol,
            AudioChannel.Music => d.musicVol,
            AudioChannel.Effects => d.sfxVol,
            _ => d.ambVol,
        };

        public static void Set(SaveData d, AudioChannel c, int percent)
        {
            percent = Snap(percent);
            switch (c)
            {
                case AudioChannel.Master: d.masterVol = percent; break;
                case AudioChannel.Music: d.musicVol = percent; break;
                case AudioChannel.Effects: d.sfxVol = percent; break;
                default: d.ambVol = percent; break;
            }
        }

        /// <summary>-fkmute: every sound off for the run (many test clients side by side), whatever the save says.</summary>
        public static bool Muted => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fkmute") >= 0;

        /// <summary>Reads the save's sliders and mute, sets the listener and the effect / ambience sources (the music reads its gain every frame).</summary>
        public static void Apply()
        {
            var d = Game.I != null ? Game.Data : null;
            if (d == null) return;
            Master = Curve(d.masterVol);
            Bgm = Curve(d.musicVol);
            Effects = Curve(d.sfxVol);
            Amb = Curve(d.ambVol);
            AudioListener.volume = d.soundOn && !Muted ? Master : 0f;
            if (Muted) Debug.Log($"[AUDIO] -fkmute: the listener at {AudioListener.volume}");
            Sfx.ApplyMix();
        }
    }
}
