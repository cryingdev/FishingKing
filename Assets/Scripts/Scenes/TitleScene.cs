using UnityEngine;
using UnityEngine.UI;

namespace FishingKing
{
    /// <summary>Title: the lake at rest behind the logo.</summary>
    public class TitleScene : MonoBehaviour
    {
        Image soundIcon;

        void RefreshSound()
        {
            if (soundIcon != null) soundIcon.sprite = Art.UI(Game.Data.soundOn ? "icon_sound" : "icon_mute");
        }

        void OnDestroy()
        {
            if (Game.I != null) Game.I.Changed -= RefreshSound;
        }

        void Start()
        {
            PixelView.Create(Color.black);
            var stage = StageView.Build("lake");
            var angler = Angler.Create(stage);
            angler.SetPose("idle");
            Toast.Init();
            Music.Play("title");

            var canvas = UIKit.CreateCanvas("TitleUI", 10);
            var root = (RectTransform)canvas.transform;
            // whole-number scale keeps the Galmuri pixel logo crisp
            var logo = UIKit.PixelImg(root, Art.UI("logo"), 1f, "Logo");
            logo.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -24), logo.rectTransform.sizeDelta, new Vector2(0.5f, 1));
            logo.gameObject.AddComponent<Bob>();
            Tween.Pop(logo.transform, 0.5f, 0.4f);
            var carp = UIKit.PixelImg(logo.transform, Art.Fish("golden_carp", 0), 2f, "GoldenCarp");
            carp.rectTransform.At(new Vector2(1, 1), new Vector2(40, 20), carp.rectTransform.sizeDelta, new Vector2(0.5f, 0.5f));
            carp.rectTransform.localRotation = Quaternion.Euler(0, 0, 18);
            carp.gameObject.AddComponent<FishWiggle>().Init("golden_carp");

            var start = UIKit.Button(root, "낚시하러 가기", "green", () => SceneFlow.Go("Map"), new Vector2(280, 76), 28);
            start.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(290, 96), new Vector2(280, 76), new Vector2(0.5f, 0));
            start.gameObject.AddComponent<Pulse>();

            var sound = UIKit.IconButton(root, Art.UI(Game.Data.soundOn ? "icon_sound" : "icon_mute"), "grey", null, 56);
            sound.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-14, -14), new Vector2(56, 56));
            soundIcon = sound.transform.Find("Icon").GetComponent<Image>();
            sound.onClick.AddListener(() => Game.I.SetSound(!Game.Data.soundOn));
            Game.I.Changed += RefreshSound; // the settings dialog can switch the sound too
            SettingsUI.CornerButton(root).GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-80, -14), new Vector2(96, 56));

            var d = Game.Data;
            string sub = d.totalCaught > 0 ? $"Lv.{d.level}  ·  잡은 물고기 {d.totalCaught}마리  ·  {UIKit.Num(d.coins)} 코인" : "전설의 물고기를 낚고 낚시왕이 되어 보세요!";
            var info = UIKit.Label(root, sub, 18, UIKit.Cream);
            info.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(760, 30), new Vector2(0.5f, 1));
        }
    }

    public class Bob : MonoBehaviour
    {
        Vector2 basePos;
        float t;

        void Start() => basePos = ((RectTransform)transform).anchoredPosition;

        void Update()
        {
            t += Time.unscaledDeltaTime;
            ((RectTransform)transform).anchoredPosition = basePos + new Vector2(0, Mathf.Round(Mathf.Sin(t * 1.6f) * 3f) * 2f);
        }
    }

    /// <summary>
    /// Attention pulse by brightness. Scaling would put the pixel text off the pixel grid (uneven glyphs), so the
    /// graphic breathes between slightly dimmed and full colour instead (vertex colours cannot exceed white).
    /// </summary>
    public class Pulse : MonoBehaviour
    {
        Graphic g;
        Color baseColor;
        float t;

        void Start()
        {
            g = GetComponent<Graphic>();
            if (g != null) baseColor = g.color;
        }

        void Update()
        {
            if (g == null) return;
            t += Time.unscaledDeltaTime;
            float k = Mathf.Max(0, Mathf.Sin(t * 3f));
            g.color = baseColor * Color.Lerp(new Color(0.84f, 0.84f, 0.84f, 1f), Color.white, k);
        }
    }
}
