using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HideAndShoot.UI
{
    public sealed class GameExplanationWindow : MonoBehaviour
    {
        [SerializeField] private GameObject window;
        [SerializeField] private GameObject[] pages;
        [SerializeField] private Button openButton,closeButton,previousButton,nextButton;
        [SerializeField] private TextMeshProUGUI pageNumber;
        private int page;
        private bool initialized;
        private void Start()=>Initialize();
        public void Initialize()
        {
            if(initialized || window==null)return;
            initialized=true;
            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(()=>window.SetActive(false));
            previousButton.onClick.AddListener(()=>ShowPage(page-1));
            nextButton.onClick.AddListener(()=>ShowPage(page+1));
            Open();
        }
        public void Open()
        {
            ShowPage(0);
            window.SetActive(true);
            window.transform.SetAsLastSibling();
        }
        public void ShowPage(int value)
        {
            page=Mathf.Clamp(value,0,pages.Length-1);
            for(int i=0;i<pages.Length;i++)pages[i].SetActive(i==page);
            pageNumber.text=(page+1)+" / "+pages.Length;
            previousButton.interactable=page>0;
            nextButton.interactable=page<pages.Length-1;
        }

#if UNITY_EDITOR
        private static RectTransform Area(Transform parent,string name,float x0,float y0,float x1,float y1)
        {
            var go=new GameObject(name,typeof(RectTransform));
            go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;
            rect.anchorMin=new Vector2(x0,y0);rect.anchorMax=new Vector2(x1,y1);
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            return rect;
        }
        private static TextMeshProUGUI Label(RectTransform rect,string value,int size)
        {
            var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=TacticalUI.Font;text.text=value;text.fontSize=size;
            text.color=TacticalUI.White;text.alignment=TextAlignmentOptions.Center;
            text.raycastTarget=false;text.richText=false;
            text.enableAutoSizing=true;text.fontSizeMin=12;text.fontSizeMax=size;
            return text;
        }
        private static Button MakeButton(RectTransform rect,string value)
        {
            rect.gameObject.AddComponent<Image>();
            var button=rect.gameObject.AddComponent<Button>();
            Label(Area(rect,"Label",0,0,1,1),value,25);
            TacticalUI.Button(button);
            return button;
        }
        private static void Picture(Transform parent,string resource,float x0,float x1,string caption)
        {
            var region=Area(parent,resource,x0,.28f,x1,1);
            var texture=Resources.Load<Texture2D>("GameExplanation/"+resource);
            if(texture==null)throw new System.InvalidOperationException("Missing guide image: "+resource);
            var rect=Area(region,"Image",0,0,1,1);
            var image=rect.gameObject.AddComponent<RawImage>();
            image.texture=texture;image.raycastTarget=false;
            var fitter=rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio=(float)texture.width/texture.height;
            Label(Area(parent,resource+"Caption",x0,0,x1,.24f),caption,23);
        }
        public void Bake()
        {
            var rules=transform.Find("RulesPanel");
            if(rules==null)throw new System.InvalidOperationException("RulesPanel missing");
            var oldGuide=rules.GetComponent<ControlsGuideToggle>();
            if(oldGuide!=null)DestroyImmediate(oldGuide);
            for(int i=rules.childCount-1;i>=0;i--)DestroyImmediate(rules.GetChild(i).gameObject);
            var old=transform.Find("GameExplanationModal");
            if(old!=null)DestroyImmediate(old.gameObject);
            var rulesRect=(RectTransform)rules;
            rulesRect.sizeDelta=new Vector2(380,66);
            var oldImage=rules.GetComponent<Image>();if(oldImage!=null)oldImage.color=Color.clear;
            openButton=MakeButton(Area(rules,"GameExplanationButton",0,0,1,1),"게임 설명");

            var modal=Area(transform,"GameExplanationModal",0,0,1,1);
            window=modal.gameObject;
            var dim=modal.gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.65f);
            var panel=Area(modal,"Window",.07f,.10f,.93f,.90f);
            var surface=panel.gameObject.AddComponent<Image>();surface.color=new Color(.035f,.065f,.085f,1);
            var outline=panel.gameObject.AddComponent<Outline>();
            outline.effectColor=TacticalUI.Cyan;outline.effectDistance=new Vector2(2,-2);
            closeButton=MakeButton(Area(panel,"Close",.935f,.89f,.985f,.98f),"X");
            previousButton=MakeButton(Area(panel,"Previous",.015f,.43f,.075f,.60f),"<");
            nextButton=MakeButton(Area(panel,"Next",.925f,.43f,.985f,.60f),">");
            pageNumber=Label(Area(panel,"PageNumber",.38f,.025f,.62f,.095f),"1 / 4",21);
            string[] titles={"NPC 사이에 숨어서 들키지 마세요.","다른 플레이어를 찾아 해치우세요.",
                "몰래 아이템을 먹으세요.","최후의 생존자가 되세요."};
            pages=new GameObject[4];
            for(int i=0;i<4;i++)
            {
                var root=Area(panel,"Page"+(i+1),.09f,.12f,.91f,.95f);
                pages[i]=root.gameObject;
                var title=Label(Area(root,"Title",0,.87f,1,1),titles[i],32);
                title.textWrappingMode=TextWrappingModes.NoWrap;
                var content=Area(root,"Content",0,0,1,.83f);
                switch(i)
                {
                    case 0:Picture(content,"has1",.08f,.92f,"WASD : 이동    Shift : 달리기\nNPC는 달리지 않습니다.");break;
                    case 1:
                        Picture(content,"has2",0,.48f,"우클릭 : 조준\n좌클릭 : 조준 중 사격");
                        Picture(content,"has4",.52f,1,"NPC 오사격 : 내 테두리가 잠깐 노출됩니다.");break;
                    case 2:
                        Picture(content,"has3",0,.48f,"탄약 상자 : 탄약 5발 보충\n최대 10발");
                        Picture(content,"has5",.52f,1,"선글라스 : 7초 동안 플레이어 식별");break;
                    case 3:Picture(content,"has6",.04f,.96f,"마지막까지 살아남아 라운드 승리!\n먼저 3승하면 최종 우승합니다.");break;
                }
            }
            ShowPage(0);
            window.SetActive(true);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}