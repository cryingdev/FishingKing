using UnityEditor;
using UnityEngine;

namespace FishingKing.EditorTools
{
    /// <summary>
    /// Import settings for the background music under Resources/Audio/Music (Tools/Music/fk_music.py: 44.1 kHz stereo
    /// OGG stems and stings, played by Music): Vorbis kept compressed in memory and decoded as it plays (a stage's three
    /// stems run at once, a full decode would cost tens of MB), not loaded with the scene but in the background when a
    /// cue asks for it (Music waits for every stem before the sample-locked start), the sample rate and the stereo kept.
    /// Every platform gets the same settings, so a stem is never resampled or re-encoded differently from its partners.
    /// <para>The recorded ambience loops under Resources/Audio/Ambience (Sfx.StageAmbience) get the same, except that they
    /// load with the clip (they start playing the moment the stage opens).</para>
    /// <para>Recorded one-shots under Resources/Audio/Sfx (Sfx.Foley: the cast, the float landing, the reel's clicks, the
    /// thrash loop) are short and timing-critical: decompressed on load as PCM (no codec delay, the transients intact),
    /// forced to mono (the game's effects are mono), loaded with the clip, not in the background.</para>
    /// </summary>
    public class MusicImporter : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Audio/Music/", AmbienceRoot = "Assets/Resources/Audio/Ambience/", SfxRoot = "Assets/Resources/Audio/Sfx/";
        /// <summary>
        /// Vorbis quality (0..1). The sources are oggenc -q 2 (about 96 kb/s) and Unity encodes them again: at 0.6 that
        /// second generation stays clean on the soft pads and long reverb tails, at roughly a megabyte a minute of stereo.
        /// </summary>
        const float Quality = 0.6f;

        // bump whenever the settings below change, so Unity reimports the affected assets
        public override uint GetVersion() => 3;

        void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (path.StartsWith(SfxRoot))
            {
                var si = (AudioImporter)assetImporter;
                si.forceToMono = true;
                si.ambisonic = false;
                si.loadInBackground = false;
                var ss = si.defaultSampleSettings;
                ss.loadType = AudioClipLoadType.DecompressOnLoad;
                ss.compressionFormat = AudioCompressionFormat.PCM;
                ss.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                ss.preloadAudioData = true;
                si.defaultSampleSettings = ss;
                foreach (var platform in new[] { "Standalone", "Android", "iOS", "WebGL" })
                    if (si.ContainsSampleSettingsOverride(platform)) si.ClearSampleSettingOverride(platform);
                return;
            }
            bool ambience = path.StartsWith(AmbienceRoot);
            if (!ambience && !path.StartsWith(Root)) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = false;
            ai.ambisonic = false;
            ai.loadInBackground = !ambience;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = Quality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = ambience;
            ai.defaultSampleSettings = s;
            foreach (var platform in new[] { "Standalone", "Android", "iOS", "WebGL" })
                if (ai.ContainsSampleSettingsOverride(platform)) ai.ClearSampleSettingOverride(platform);
        }
    }
}
