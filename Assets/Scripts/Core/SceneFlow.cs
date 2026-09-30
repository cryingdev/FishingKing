using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>Scene switching with a pixel "iris" fade. Also carries the selected stage between scenes.</summary>
    public class SceneFlow : MonoBehaviour
    {
        public static SceneFlow I { get; private set; }
        public static string PendingStage;

        Canvas canvas;
        Image cover;
        bool busy;

        void Awake()
        {
            I = this;
            var go = new GameObject("[Fade]", typeof(RectTransform));
            go.transform.SetParent(transform);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            cover = new GameObject("Cover", typeof(RectTransform)).AddComponent<Image>();
            cover.transform.SetParent(go.transform, false);
            cover.color = new Color(0.05f, 0.04f, 0.08f, 0f);
            cover.rectTransform.anchorMin = Vector2.zero;
            cover.rectTransform.anchorMax = Vector2.one;
            cover.rectTransform.offsetMin = cover.rectTransform.offsetMax = Vector2.zero;
            cover.raycastTarget = false;
        }

        public static void Go(string scene)
        {
            if (I == null) { SceneManager.LoadScene(scene); return; }
            if (I.busy) return;
            I.StartCoroutine(I.GoCo(scene));
        }

        public static void Fishing(string stageId)
        {
            PendingStage = stageId;
            Game.Data.lastStage = stageId;
            Game.I.Save();
            Go("Fishing");
        }

        IEnumerator GoCo(string scene)
        {
            busy = true;
            cover.raycastTarget = true;
            yield return Fade(0f, 1f, 0.25f);
            var op = SceneManager.LoadSceneAsync(scene);
            while (!op.isDone) yield return null;
            yield return null;
            yield return Fade(1f, 0f, 0.3f);
            cover.raycastTarget = false;
            busy = false;
        }

        IEnumerator Fade(float a, float b, float time)
        {
            float e = 0;
            while (e < time)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / time);
                // quantized alpha steps feel more "pixel"
                var c = cover.color;
                c.a = Mathf.Round(Mathf.Lerp(a, b, k) * 6f) / 6f;
                cover.color = c;
                yield return null;
            }
            var cc = cover.color;
            cc.a = b;
            cover.color = cc;
        }
    }
}
