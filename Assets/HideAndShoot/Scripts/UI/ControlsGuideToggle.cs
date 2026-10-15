using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using HideAndShoot.Networking;

namespace HideAndShoot.UI
{
    public sealed class ControlsGuideToggle : MonoBehaviour
    {
        [SerializeField] private Button toggle;
        [SerializeField] private GameObject content;
        [SerializeField] private TextMeshProUGUI caption;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Vector2 expandedSize;
        [SerializeField] private Vector2 collapsedSize;
        [SerializeField] private bool expanded=true;
        [SerializeField] private bool keyboardShortcut;
        public bool IsExpanded=>expanded;
        public void Configure(Button button,GameObject body,RectTransform container,
            Vector2 openSize,Vector2 closedSize,bool initiallyExpanded,bool useKeyboard=false)
        {
            toggle=button;content=body;panel=container;
            caption=button.GetComponentInChildren<TextMeshProUGUI>(true);
            expandedSize=openSize;collapsedSize=closedSize;
            keyboardShortcut=useKeyboard;expanded=initiallyExpanded;
            Bind();Apply();
        }
        private void Start()=>Initialize();
        public void Initialize(){Bind();Apply();}
        private void Bind()
        {
            if(toggle==null)return;
            toggle.onClick.RemoveAllListeners();
            toggle.GetComponent<HideAndShoot.UI.TacticalButtonFeedback>()?.BindClick();
            toggle.onClick.AddListener(Toggle);
        }
        public void Toggle(){SetExpanded(!expanded);}
        public void SetExpanded(bool value){expanded=value;Apply();}
        private void Apply()
        {
            if(content!=null)content.SetActive(expanded);
            if(panel!=null)panel.sizeDelta=expanded?expandedSize:collapsedSize;
            if(caption!=null)caption.text=keyboardShortcut?
                (expanded?"H  안내 접기 · Tab 순위 · Esc 메뉴":"H  조작법 펼치기 · Tab 순위 · Esc 메뉴"):
                (expanded?"조작법  -  접기":"조작법  +  펼치기");
        }
        private void Update()
        {
            if(!keyboardShortcut || Keyboard.current==null || !Keyboard.current.hKey.wasPressedThisFrame)return;
            var canvas=GetComponentInParent<Canvas>();
            if(canvas==null || !canvas.enabled || !canvas.gameObject.activeInHierarchy)return;
            if(RelayLobbySession.Instance!=null && RelayLobbySession.Instance.RoundMenuVisible)return;
            Toggle();
        }
        public static void CreateRows(Transform parent,int fontSize=18)
        {
            string[] keys={"W A S D","Shift","오른쪽 클릭","왼쪽 클릭","Tab","Esc","[ / ]","H"};
            string[] actions={"이동","달리기","조준","조준 중 사격","순위 보기","게임 메뉴","마우스 감도","플레이 중 조작법"};
            for(int i=0;i<keys.Length;i++)
            {
                float y=-20-i*38;
                TacticalUI.Text(parent,"Key"+i,keys[i],new Vector2(-100,y),new Vector2(132,32),fontSize-2,
                    TacticalUI.Muted,TextAnchor.MiddleLeft,new Vector2(.5f,1));
                TacticalUI.Text(parent,"Action"+i,actions[i],new Vector2(70,y),new Vector2(190,32),fontSize,
                    TacticalUI.White,TextAnchor.MiddleLeft,new Vector2(.5f,1));
            }
        }
    }
}
