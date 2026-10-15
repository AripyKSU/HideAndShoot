using HideAndShoot.Networking;
using HideAndShoot.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HideAndShoot.UI
{
    /// <summary>Conceals every actor while the round is being placed and counted down.</summary>
    public sealed class RoundTransitionCurtain : MonoBehaviour
    {
        private Canvas curtain;
        private TextMeshProUGUI countdown;
        private int expectedRound=1;

        private void Awake()
        {
            curtain=transform.Find("RoundTransitionCanvas")?.GetComponent<Canvas>();
            countdown=curtain!=null?curtain.GetComponentInChildren<TextMeshProUGUI>(true):null;
        }

        public static void Begin(int roundId)
        {
            var view=FindFirstObjectByType<RoundTransitionCurtain>();
            if(view==null)return;
            view.expectedRound=Mathf.Max(view.expectedRound,roundId);
            if(view.curtain!=null)view.curtain.enabled=true;
        }

        private void LateUpdate()
        {
            if(curtain==null)return;
            var session=RelayLobbySession.Instance;
            bool placed=session!=null && session.MatchRoundId>=expectedRound &&
                (session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Active ||
                 session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Result ||
                 session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Podium);
            curtain.enabled=session!=null && session.IsConnected && session.IsMatchInProgress && !placed;
            if(countdown!=null)
                countdown.text=session!=null && session.MatchRoundId>=expectedRound &&
                    session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Countdown
                    ?Mathf.Clamp(session.CountdownRemaining,1,5).ToString():"라운드 준비 중…";
        }

#if UNITY_EDITOR
        public void BakeEditableLayout()
        {
            var rect=TacticalUI.Rect(transform,"RoundTransitionCanvas",new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            curtain=rect.GetComponent<Canvas>();
            if(curtain==null)curtain=rect.gameObject.AddComponent<Canvas>();
            curtain.renderMode=RenderMode.ScreenSpaceOverlay;curtain.sortingOrder=150;
            var scaler=rect.GetComponent<CanvasScaler>()??rect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1366,768);
            var shade=TacticalUI.Stripe(rect,"OpaqueBackdrop",new Vector2(.5f,.5f),Vector2.zero,Vector2.zero,
                new Color(.015f,.025f,.035f,1f));
            TacticalUI.Stretch(shade.rectTransform);
            countdown=TacticalUI.Text(rect,"Countdown","라운드 준비 중…",Vector2.zero,new Vector2(900,140),
                64,TacticalUI.White,TextAnchor.MiddleCenter);
            TacticalUI.Text(rect,"Hint","군중 속 상대를 찾아 살아남으세요.",new Vector2(0,-110),
                new Vector2(900,50),20,TacticalUI.Muted,TextAnchor.MiddleCenter);
            curtain.enabled=false;
        }
#endif
    }
}
