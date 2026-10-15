using UnityEngine;
namespace HideAndShoot.UI
{
    public sealed class TacticalPanelEntrance : MonoBehaviour
    {
        private CanvasGroup group;
        private float shownAt;
        private void OnEnable()
        {
            group=GetComponent<CanvasGroup>()??gameObject.AddComponent<CanvasGroup>();
            shownAt=Time.unscaledTime;
            group.alpha=0;
        }
        private void Update()
        {
            float t=Mathf.Clamp01((Time.unscaledTime-shownAt)/.18f);
            group.alpha=t*t*(3f-2f*t);
        }
        private void OnDisable(){if(group!=null)group.alpha=1;}
    }
}
