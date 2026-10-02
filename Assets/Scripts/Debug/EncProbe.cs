using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// -fkencprobe &lt;m&gt; (with -fkauto encounter and -fkshots &lt;dir&gt;): in each encounter's approach the first frame the
    /// legend is within &lt;m&gt; of the lure is saved as enc_&lt;model&gt;_2a_far&lt;n&gt;.png, and [ENCPROBE] lines log the fish's
    /// distance and its span across the window (snout to tail bone) then, at the fake-out's middle and the nose-in's largest.
    /// -fkenccm &lt;cm&gt;: the legend is shown at this length whatever it rolled (the same fish for before / after shots).
    /// </summary>
    public class EncProbe : MonoBehaviour
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        float dist;
        string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int c = Array.IndexOf(args, "-fkenccm");
            if (c >= 0 && c + 1 < args.Length && float.TryParse(args[c + 1], NumberStyles.Float, CI, out float cm)) EncounterView.DebugCm = cm;
            int i = Array.IndexOf(args, "-fkencprobe");
            if (i < 0 || i + 1 >= args.Length || !float.TryParse(args[i + 1], NumberStyles.Float, CI, out float d)) return;
            int j = Array.IndexOf(args, "-fkshots");
            var go = new GameObject("EncProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<EncProbe>();
            p.dist = d;
            p.dir = j >= 0 && j + 1 < args.Length ? args[j + 1] : null;
        }

        IEnumerator Start()
        {
            FishingController ctl = null;
            LegendEncounter seen = null;
            bool shot = false, fake = false, noseDone = false;
            float noseMax = 0f;
            int n = 0;
            while (true)
            {
                yield return AutoShot.Frame();   // (captures at its end: the test label hidden)
                if (ctl == null)
                {
                    ctl = FindAnyObjectByType<FishingController>();
                    if (ctl == null) continue;
                }
                var e = ctl.Encounter;
                var v = ctl.EncounterView;
                if (e == null || v == null || ctl.State != FishingController.S.Encounter) continue;
                if (e != seen)
                {
                    seen = e;
                    shot = fake = noseDone = false;
                    noseMax = 0f;
                    n++;
                }
                string id = e.Def.model ?? "legend";
                var ph = e.Ph;
                if (!shot && ph == LegendEncounter.Phase.Approach && v.FishLureDist > 0f && v.FishLureDist <= dist)
                {
                    shot = true;
                    string name = $"enc_{id}_2a_far{n}";
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        var tex = AutoShot.Texture();
                        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                        Destroy(tex);
                    }
                    Debug.Log(string.Format(CI, "[ENCPROBE] {0} approach t={1:0.00} lureDist={2:0.00} camDist={3:0.00} span={4:0.00} crop={5}",
                        name, e.PhaseT, v.FishLureDist, v.FishCamDist, v.FishSpanK, v.Crop));
                }
                if (ph == LegendEncounter.Phase.NoseIn) noseMax = Mathf.Max(noseMax, v.FishSpanK);
                if (!fake && e.InFakeOut && e.FakeOutK >= 0.5f)
                {
                    fake = true;
                    Debug.Log(string.Format(CI, "[ENCPROBE] {0} #{1} fakeout k={2:0.00} lureDist={3:0.00} camDist={4:0.00} span={5:0.00}",
                        id, n, e.FakeOutK, v.FishLureDist, v.FishCamDist, v.FishSpanK));
                }
                if (!noseDone && ph > LegendEncounter.Phase.NoseIn && noseMax > 0f)
                {
                    noseDone = true;
                    Debug.Log(string.Format(CI, "[ENCPROBE] {0} #{1} nose-in largest span={2:0.00}", id, n, noseMax));
                }
            }
        }
    }
}
