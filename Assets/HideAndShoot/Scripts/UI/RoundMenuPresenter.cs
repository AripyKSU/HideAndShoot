using TMPro;
using HideAndShoot.Networking;
using HideAndShoot.World;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace HideAndShoot.UI
{
    public sealed class RoundMenuPresenter : MonoBehaviour
    {
        private GameObject root;
        private TextMeshProUGUI voteCaption;
        // Editor-only initial creation. Once baked, this canvas is a real
        // editable GameObject under GameplayServices.
        public void Build()
        {
            if(root!=null)return;
            root=new GameObject("RoundMenuCanvas",typeof(RectTransform),typeof(Canvas),
                typeof(CanvasScaler),typeof(GraphicRaycaster));
            root.transform.SetParent(transform,false);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=180;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1366,768);scaler.matchWidthOrHeight=.5f;
            var shade=TacticalUI.Rect(root.transform,"ModalShade",new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            TacticalUI.Stretch(shade);
            shade.gameObject.AddComponent<Image>().color=new Color(.015f,.035f,.05f,.82f);
            var panel=TacticalUI.Rect(root.transform,"RoundMenuPanel",new Vector2(.5f,.5f),
                Vector2.zero,new Vector2(560,352));
            TacticalUI.Frame(panel,TacticalUI.Gold,true);
            TacticalUI.Text(panel,"MenuEyebrow","",new Vector2(0,132),
                new Vector2(472,28),15,TacticalUI.Cyan);
            TacticalUI.Text(panel,"MenuTitle","게임 메뉴",new Vector2(0,90),
                new Vector2(472,52),32,TacticalUI.White).fontStyle=FontStyles.Bold;
            var vote=CreateButton(panel,"RedoVoteButton","라운드 다시하기",new Vector2(0,13),false);
            voteCaption=vote.GetComponentInChildren<TextMeshProUGUI>();
            vote.onClick.AddListener(()=>RelayLobbySession.Instance?.VoteFromRoundMenu());
            var resume=CreateButton(panel,"ResumeButton","계속 플레이",new Vector2(0,-55),true);
            resume.onClick.AddListener(()=>RelayLobbySession.Instance?.ResumeFromRoundMenu());
            TacticalUI.Text(panel,"EscapeHint","ESC  돌아가기  ·  다시하기는 참가자 투표로 결정됩니다.",
                new Vector2(0,-126),new Vector2(472,40),15,TacticalUI.Muted,TextAnchor.MiddleCenter);
            root.SetActive(false);
        }
        private static Button CreateButton(Transform parent,string name,string label,Vector2 position,bool primary)
        {
            var rect=TacticalUI.Rect(parent,name,new Vector2(.5f,.5f),position,new Vector2(472,50));
            rect.gameObject.AddComponent<Image>();
            var button=rect.gameObject.AddComponent<Button>();
            TacticalUI.Text(rect,"Text",label,Vector2.zero,new Vector2(448,46),23,TacticalUI.White,TextAnchor.MiddleCenter);
            TacticalUI.Button(button,primary);return button;
        }
        private void Start()
        {
            root=transform.Find("RoundMenuCanvas")?.gameObject;
            var captionTransform=root!=null?
                root.transform.Find("RoundMenuPanel/RedoVoteButton/Text"):null;
            voteCaption=captionTransform!=null?
                captionTransform.GetComponent<TextMeshProUGUI>():null;
            var panel=root?.transform.Find("RoundMenuPanel");
            var vote=panel?.Find("RedoVoteButton")?.GetComponent<Button>();
            var resume=panel?.Find("ResumeButton")?.GetComponent<Button>();
            if(root==null || voteCaption==null || vote==null || resume==null)
            {
                Debug.LogError("[HideAndShoot/RoundMenu] Missing authored RoundMenuCanvas.");
                enabled=false;return;
            }
            vote.onClick.RemoveAllListeners();
            vote.GetComponent<HideAndShoot.UI.TacticalButtonFeedback>()?.BindClick();
            resume.onClick.RemoveAllListeners();
            resume.GetComponent<HideAndShoot.UI.TacticalButtonFeedback>()?.BindClick();
            vote.onClick.AddListener(()=>RelayLobbySession.Instance?.VoteFromRoundMenu());
            resume.onClick.AddListener(()=>RelayLobbySession.Instance?.ResumeFromRoundMenu());
        }
        private void Update()
        {
            if(root==null)return;
            var session=RelayLobbySession.Instance;
            bool visible=session!=null && session.IsMatchInProgress && session.RoundMenuVisible &&
                session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Podium;
            if(root.activeSelf!=visible)root.SetActive(visible);
            if(!visible)return;
            voteCaption.text="라운드 다시하기  ·  "+session.RedoYesCount+" / "+session.RedoEligibleCount+"표";
            if(EventSystem.current==null)
                new GameObject("GameUIEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
        }
    }
}
