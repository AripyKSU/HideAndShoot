using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace HideAndShoot.UI
{
    public sealed class TacticalButtonFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        public bool primary;
        private bool hover;
        private Button button;
        private void OnEnable()=>BindClick();
        public void BindClick()
        {
            button=GetComponent<Button>();
            if(button!=null){button.onClick.RemoveListener(PlayClick);button.onClick.AddListener(PlayClick);}
        }
        private void PlayClick()=>HideAndShoot.Audio.GameAudio.PlayUiClick();
        public void OnPointerEnter(PointerEventData data)=>hover=true;
        public void OnPointerExit(PointerEventData data)=>hover=false;
        private void OnDisable(){if(button!=null)button.onClick.RemoveListener(PlayClick);hover=false;transform.localScale=Vector3.one;}
        private void Update()
        {
            if(button==null)button=GetComponent<Button>();
            bool active=button!=null && button.interactable;
            transform.localScale=Vector3.one;
            var text=button!=null?button.GetComponentInChildren<TextMeshProUGUI>():null;
            if(text!=null)text.color=active?(primary?TacticalUI.Ink:TacticalUI.White):TacticalUI.Muted;
        }
    }
}
