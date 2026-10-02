using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// Test switch for the water movement (<see cref="WaterFx"/>): motion strips of the low-res world render (no UI).
    /// <code>
    /// -fkwaterstrip &lt;dir&gt;        visit the stages and save the strips into dir, then quit (use with -fkrich so every stage is unlocked)
    /// -fkwaterstages a,b,...      stages to visit (default: all)
    /// -fkstripn &lt;n&gt;             frames per strip (default 4)
    /// -fkstripdt &lt;seconds&gt;       time between frames (default 0.3)
    /// -fkstripwait &lt;seconds&gt;     settle time after entering a stage (default 3)
    /// </code>
    /// Per stage: &lt;stage&gt;_f&lt;i&gt;.png (one render-texture frame each, 1:1 pixels) and &lt;stage&gt;_strip.png (the frames
    /// stacked top to bottom, 2 px magenta gaps).
    /// </summary>
    public class WaterStrip : MonoBehaviour
    {
        string dir;
        string[] stages;
        int frames = 4;
        float dt = 0.3f, wait = 3f;

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string dir = Arg("-fkwaterstrip");
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("[WaterStrip]");
            DontDestroyOnLoad(go);
            var ws = go.AddComponent<WaterStrip>();
            ws.dir = dir;
            Directory.CreateDirectory(dir);
            string list = Arg("-fkwaterstages");
            if (!string.IsNullOrEmpty(list)) ws.stages = list.Split(',');
            else
            {
                ws.stages = new string[GameDatabase.Stages.Count];
                for (int i = 0; i < ws.stages.Length; i++) ws.stages[i] = GameDatabase.Stages[i].id;
            }
            if (int.TryParse(Arg("-fkstripn"), out int n)) ws.frames = Mathf.Clamp(n, 1, 32);
            if (float.TryParse(Arg("-fkstripdt"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d)) ws.dt = d;
            if (float.TryParse(Arg("-fkstripwait"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w)) ws.wait = w;
            ws.StartCoroutine(ws.Run());
        }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(1f);
            foreach (var id in stages)
            {
                SceneFlow.PendingStage = id;
                SceneFlow.Go("Fishing");
                yield return new WaitForSeconds(wait);
                Texture2D strip = null;
                int w = 0, h = 0;
                for (int i = 0; i < frames; i++)
                {
                    if (i > 0) yield return new WaitForSeconds(dt);
                    yield return new WaitForEndOfFrame();
                    var rt = PixelView.Current != null ? PixelView.Current.Target : null;
                    if (rt == null) break;
                    w = rt.width;
                    h = rt.height;
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var f = new Texture2D(w, h, TextureFormat.RGB24, false);
                    AutoShot.Read(f, new Rect(0, 0, w, h));
                    f.Apply(false);
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, $"{id}_f{i}.png"), f.EncodeToPNG());
                    if (strip == null)
                    {
                        strip = new Texture2D(w, frames * (h + 2) - 2, TextureFormat.RGB24, false);
                        var fill = new Color32[strip.width * strip.height];
                        for (int k = 0; k < fill.Length; k++) fill[k] = new Color32(255, 0, 255, 255);
                        strip.SetPixels32(fill);
                    }
                    strip.SetPixels(0, (frames - 1 - i) * (h + 2), w, h, f.GetPixels());
                    Destroy(f);
                    var sv = FindAnyObjectByType<StageView>();
                    Debug.Log($"[WaterStrip] {id} frame {i} t={Time.time:0.00} {(sv != null && sv.Water != null ? sv.Water.Stats : "-")}");
                }
                if (strip != null)
                {
                    strip.Apply(false);
                    File.WriteAllBytes(Path.Combine(dir, $"{id}_strip.png"), strip.EncodeToPNG());
                    Destroy(strip);
                }
            }
            Debug.Log("[WaterStrip] done");
            Application.Quit();
        }
    }
}
