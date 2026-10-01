using UnityEngine;

namespace FishingKing
{
    /// <summary>Scene bootstrap: builds the pixel view, the selected stage and the fishing controller.</summary>
    public class FishingScene : MonoBehaviour
    {
        void Start()
        {
            string id = SceneFlow.PendingStage ?? Game.Data.lastStage ?? "lake";
            if (GameDatabase.GetStage(id) == null || !Game.I.IsUnlocked(id)) id = "lake";
            var pv = PixelView.Create(Color.black);
            var stage = StageView.Build(id);
            var ctl = new GameObject("Fishing").AddComponent<FishingController>();
            ctl.Init(stage, pv);   // (the stage's music too: FishingController.Music.cs)
            Toast.Init();
            if (!Game.Data.tutorialDone)
            {
                Game.Data.tutorialDone = true;
                Game.I.Save();
                Dialog.Info("낚시 방법",
                    "1. 화면을 <color=#b0441a>아래로 당겼다가</color> 던질 쪽으로 <color=#b0441a>위로 튕기며 놓으면</color> 캐스팅!\n(세게 튕길수록 멀리, 튕긴 방향으로 날아가요)\n" +
                    "2. 찌가 <color=#b0441a>쑥 들어가면 탭</color>해서 챔질!\n" +
                    $"3. 화면에 <color=#b0441a>원을 그리면 릴이 감겨요</color>\n({CircleGesture.WindWay} 감기 · {CircleGesture.GiveWay} 줄 풀기)\n" +
                    "4. 장력이 빨개지면 잠깐 멈추세요!\n" +
                    "5. 채비가 물에 있을 때 <color=#b0441a>좌우로 밀면</color> 낚싯대가 기울어요\n(감으면 그쪽으로 휘어 와요 · 파이트 중엔 물고기가 달리는 <color=#b0441a>반대쪽으로</color> · 키보드 A/D)", "알겠어요!");
            }
        }
    }
}
