using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HideAndShoot.UI
{
    public static class TacticalUI
    {
        public static readonly Color Ink=new Color(.035f,.065f,.085f,.97f);
        public static readonly Color Panel=new Color(.055f,.10f,.125f,.96f);
        public static readonly Color White=new Color(.93f,.96f,.97f);
        public static readonly Color Muted=new Color(.52f,.66f,.70f);
        public static readonly Color Cyan=new Color(.26f,.91f,.92f);
        public static readonly Color Gold=new Color(1f,.72f,.29f);
        public static readonly Color Coral=new Color(1f,.40f,.34f);
        private static TMP_FontAsset font;
        public static TMP_FontAsset Font => font!=null?font:
            (font=Resources.Load<TMP_FontAsset>("Fonts/DNFBitBitv2_SDF"));

        public static TextAlignmentOptions Align(TextAnchor a)
        {
            switch(a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.MidlineLeft;
            }
        }

        public static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 position,
            Vector2 size,Vector2? pivot=null)
        {
            var existing=parent.Find(name);
            var rect=existing!=null?existing.GetComponent<RectTransform>():null;
            if(rect==null)
            {
                var go=new GameObject(name,typeof(RectTransform));
                go.transform.SetParent(parent,false);
                rect=(RectTransform)go.transform;
            }
            rect.anchorMin=rect.anchorMax=anchor;
            rect.pivot=pivot??new Vector2(.5f,.5f);
            rect.anchoredPosition=position;
            rect.sizeDelta=size;
            return rect;
        }

        public static TextMeshProUGUI Text(Transform parent,string name,string value,Vector2 position,
            Vector2 size,int fontSize,Color color,TextAnchor align=TextAnchor.MiddleLeft,
            Vector2? anchor=null)
        {
            var rect=Rect(parent,name,anchor??new Vector2(.5f,.5f),position,size);
            var text=rect.GetComponent<TextMeshProUGUI>()??rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=Font; text.fontSize=fontSize; text.color=color; text.alignment=Align(align);
            text.text=value; text.richText=false; text.raycastTarget=false;
            text.enableWordWrapping=true; text.overflowMode=TextOverflowModes.Truncate;
            return text;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;
        }

        public static Image Stripe(Transform parent,string name,Vector2 anchor,Vector2 position,
            Vector2 size,Color color,Vector2? pivot=null)
        {
            var rect=Rect(parent,name,anchor,position,size,pivot);
            var image=rect.GetComponent<Image>()??rect.gameObject.AddComponent<Image>();
            image.color=color; image.raycastTarget=false;
            return image;
        }

        public static void Frame(RectTransform root,Color accent,bool animated=false)
        {
            var image=root.GetComponent<Image>();if(image!=null)image.color=Color.clear;
            var rect=Rect(root,"TacticalFrame",new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            Stretch(rect);rect.SetAsFirstSibling();
            var frame=rect.GetComponent<TacticalPanelGraphic>()??rect.gameObject.AddComponent<TacticalPanelGraphic>();
            frame.color=new Color(.035f,.055f,.07f,.88f);
            frame.borderColor=new Color(.36f,.44f,.48f,.20f);frame.corner=2;
            frame.raycastTarget=false;frame.SetAllDirty();
            foreach(var name in new[]{"AccentRail","AccentFoot"})
            {var old=root.Find(name);if(old!=null)old.gameObject.SetActive(false);}
        }
        public static void Button(Button button,bool primary=false)
        {
            if(button==null)return;
            var image=button.GetComponent<Image>();
            if(image!=null)
            {
                image.color=Color.white;
                var colors=button.colors;
                colors.normalColor=primary?Gold:new Color(.10f,.14f,.17f);
                colors.highlightedColor=primary?new Color(1f,.84f,.43f):new Color(.18f,.24f,.28f);
                colors.pressedColor=primary?new Color(.80f,.49f,.13f):new Color(.07f,.10f,.12f);
                colors.selectedColor=colors.highlightedColor;
                colors.disabledColor=new Color(.11f,.15f,.17f);
                colors.fadeDuration=.12f;
                button.colors=colors;
                button.targetGraphic=image;
            }
            var text=button.GetComponentInChildren<TextMeshProUGUI>(true);
            if(text!=null)
            {
                text.font=Font; text.fontStyle=FontStyles.Bold; text.fontSize=23;
                text.alignment=TextAlignmentOptions.Midline; text.richText=false;
                text.color=button.interactable?(primary?Ink:White):Muted;
                text.enableAutoSizing=false;
                text.enableWordWrapping=true;
                text.raycastTarget=false;
                Stretch(text.rectTransform);
                text.rectTransform.offsetMin=new Vector2(12,4);
                text.rectTransform.offsetMax=new Vector2(-12,-4);
            }
            var feedback=button.GetComponent<TacticalButtonFeedback>()??
                button.gameObject.AddComponent<TacticalButtonFeedback>();
            feedback.primary=primary;
            var edge=button.transform.Find("ButtonEdge");if(edge!=null)edge.gameObject.SetActive(false);

        }

        public static void Input(TMP_InputField input)
        {
            input.GetComponent<Image>().color=new Color(.025f,.055f,.075f);
            var colors=input.colors;
            colors.normalColor=Color.white; colors.highlightedColor=new Color(.80f,1f,1f);
            colors.selectedColor=new Color(.75f,1f,1f); input.colors=colors;
            if(input.textComponent!=null)
            {
                input.textComponent.font=Font; input.textComponent.fontSize=25;
                input.textComponent.color=White; input.textComponent.richText=false;
            }
            if(input.placeholder is TextMeshProUGUI placeholder)
            {placeholder.font=Font;placeholder.fontSize=22;placeholder.color=Muted;}
            Stripe(input.transform,"InputUnderline",new Vector2(.5f,0),Vector2.zero,
                new Vector2(input.GetComponent<RectTransform>().sizeDelta.x-28,1),new Color(.4f,.5f,.55f,.35f),new Vector2(.5f,0));
        }

        public static void RefreshButtonLabel(Button button)
        {
            if(button==null)return;
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
            var feedback=button.GetComponent<TacticalButtonFeedback>();
            if(label!=null)label.color=button.interactable?
                feedback!=null && feedback.primary?Ink:White:Muted;
        }

        public static string Escape(string value)=>(value??string.Empty).Replace("<","＜").Replace(">","＞");
    }
}
