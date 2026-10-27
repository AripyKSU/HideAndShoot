using TMPro;
using System.Collections.Generic;
using HideAndShoot.Networking;
using HideAndShoot.World;
using UnityEngine;
using UnityEngine.UI;

namespace HideAndShoot.UI
{
    public sealed class MatchPodiumPresenter : MonoBehaviour
    {
        private RelayLobbySession session;
        private GameObject stage;
        private Camera podiumCamera;
        private Canvas overlay;
        private RectTransform backdrop;
        private float backdropAspect;
        private Material stageMaterial;
        private readonly List<Material> materials=new List<Material>();
        private readonly float[] podiumX={0,-3,3};
        private readonly float[] podiumHeights={1.65f,1.12f,.78f};
        private void Start()
        {
            session=RelayLobbySession.Instance;
            overlay=transform.Find("PodiumOverlay")?.GetComponent<Canvas>();
            if(session!=null)session.MatchPodiumAnnounced+=ShowPodium;
        }

        private static void Layer(Transform target,int layer)
        {target.gameObject.layer=layer;foreach(Transform child in target)Layer(child,layer);}

        private GameObject Block(string name,Vector3 position,Vector3 size,Color color)
        {
            var block=GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name=name;block.transform.SetParent(stage.transform,false);
            block.transform.localPosition=position;block.transform.localScale=size;
            block.GetComponent<Collider>().enabled=false;
            var renderer=block.GetComponent<Renderer>();
            // The primitive default material is editor-dependent in URP.
            // Clone a Resources asset so its shader and emission variant survive builds.
            var material=new Material(stageMaterial);
            material.color=color;
            if(material.HasProperty("_EmissionColor"))
            {material.SetColor("_EmissionColor",color*.10f);}
            renderer.sharedMaterial=material;materials.Add(material);
            Layer(block.transform,5);
            return block;
        }

        private void CreateBackdrop()
        {
            var texture=Resources.Load<Texture2D>("PodiumArenaBackground");
            if(texture==null){Debug.LogError("[HideAndShoot/Podium] Missing arena background.");return;}
            var background=new GameObject("ArenaBackground",typeof(RectTransform),typeof(Canvas),typeof(RawImage));
            background.transform.SetParent(podiumCamera.transform,false);
            background.layer=5;
            backdrop=background.GetComponent<RectTransform>();
            backdrop.localPosition=new Vector3(0,0,50);
            backdrop.localRotation=Quaternion.identity;
            backdrop.localScale=Vector3.one;
            var canvas=background.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.WorldSpace;
            canvas.worldCamera=podiumCamera;
            canvas.sortingOrder=-100;
            var image=background.GetComponent<RawImage>();
            image.texture=texture;
            image.raycastTarget=false;
            backdropAspect=(float)texture.width/texture.height;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if(backdrop==null || podiumCamera==null)return;
            // Cover the camera view with the original proportions, cropping only the excess.
            float height=2*50*Mathf.Tan(podiumCamera.fieldOfView*.5f*Mathf.Deg2Rad);
            height*=Mathf.Max(1,podiumCamera.aspect/backdropAspect);
            backdrop.sizeDelta=new Vector2(height*backdropAspect,height);
        }
        public void ShowPodium(RelayLobbySession.ScoreRow[] standings)
        {
            if(stage!=null || standings==null || standings.Length==0)return;
            var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            var source=catalog!=null && catalog.PlayerAvatar!=null?
                catalog.PlayerAvatar.transform.Find("WorldVisual"):null;
            if(source==null){Debug.LogError("[HideAndShoot/Podium] World visual missing.");return;}
            int variation=0;
            unchecked
            {
                foreach(var row in standings)variation=(variation*31+(int)row.ClientId+
                    row.Score+(int)row.LastScoredOrder)&int.MaxValue;
            }
            stageMaterial=Resources.Load<Material>("PodiumStageMaterial");
            if(stageMaterial==null || stageMaterial.shader==null || !stageMaterial.shader.isSupported)
            {
                Debug.LogError("[HideAndShoot/Podium] Missing or unsupported PodiumStageMaterial.");
                return;
            }
            stage=new GameObject("MatchPodium_3D");
            stage.transform.SetParent(transform,false);stage.transform.position=new Vector3(0,500,0);
            Block("StageFloor",new Vector3(0,-.18f,0),new Vector3(12,.2f,6),new Color(.045f,.09f,.12f));
            for(int i=0;i<3;i++)
            {
                Color accent=i==0?TacticalUI.Gold:i==1?TacticalUI.Cyan:TacticalUI.Coral;
                Block("Podium_"+(i+1),new Vector3(podiumX[i],podiumHeights[i]*.5f,0),
                    new Vector3(2.4f,podiumHeights[i],2.15f),new Color(.10f,.17f,.21f));
                Block("RankAccent_"+i,new Vector3(podiumX[i],podiumHeights[i]-.04f,-1.09f),
                    new Vector3(2.25f,.055f,.045f),accent);
                Block("StageRail_"+i,new Vector3(podiumX[i],.005f,-1.65f),
                    new Vector3(2.4f,.025f,.09f),accent);
                if(i>=standings.Length)continue;
                var character=Instantiate(source.gameObject,stage.transform);
                character.name="Kyle_Podium_"+(i+1);
                character.transform.localPosition=new Vector3(podiumX[i],podiumHeights[i],0);
                character.transform.localRotation=Quaternion.Euler(0,180,0);
                Layer(character.transform,5);
                foreach(var collider in character.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                var animator=character.GetComponentInChildren<Animator>(true);
                if(animator!=null)
                {
                    var celebration=animator.GetComponent<PodiumCelebration>()??
                        animator.gameObject.AddComponent<PodiumCelebration>();
                    celebration.PlayRank(i,variation);
                }
            }
            var cameraObj=new GameObject("PodiumCamera");
            cameraObj.transform.SetParent(stage.transform,false);
            cameraObj.transform.localPosition=new Vector3(0,4.8f,-13);
            cameraObj.transform.LookAt(stage.transform.position+Vector3.up*1.65f);
            podiumCamera=cameraObj.AddComponent<Camera>();
            podiumCamera.depth=100;podiumCamera.clearFlags=CameraClearFlags.SolidColor;
            podiumCamera.backgroundColor=TacticalUI.Ink;podiumCamera.cullingMask=1<<5;
            podiumCamera.fieldOfView=47;podiumCamera.nearClipPlane=.1f;podiumCamera.farClipPlane=60;
            CreateBackdrop();
            var lightObj=new GameObject("PodiumKeyLight");
            lightObj.transform.SetParent(stage.transform,false);
            lightObj.transform.localRotation=Quaternion.Euler(40,310,0);
            var light=lightObj.AddComponent<Light>();
            light.type=LightType.Directional;light.intensity=1.8f;light.cullingMask=1<<5;
            var fillObj=new GameObject("PodiumRimLight");
            fillObj.transform.SetParent(stage.transform,false);
            fillObj.transform.localRotation=Quaternion.Euler(20,140,0);
            light=fillObj.AddComponent<Light>();light.type=LightType.Directional;
            light.intensity=.6f;light.color=TacticalUI.Cyan;light.cullingMask=1<<5;
            overlay=transform.Find("PodiumOverlay")?.GetComponent<Canvas>();
            if(overlay==null)
            {
                Debug.LogError("[HideAndShoot/Podium] Missing editable PodiumOverlay.");
                return;
            }
            string[] names={"Nickname","Score"};
            for(int i=0;i<3;i++)
            {
                var rank=overlay.transform.Find("Rank_"+(i+1));
                if(rank==null)continue;
                var name=rank.Find(names[0])?.GetComponent<TextMeshProUGUI>();
                var score=rank.Find(names[1])?.GetComponent<TextMeshProUGUI>();
                if(name!=null)name.text=i<standings.Length?standings[i].Nickname:"참가자 없음";
                if(score!=null)score.text=i<standings.Length?standings[i].Score+"승 · "+standings[i].Kills+"킬":"—";
            }
            overlay.gameObject.SetActive(true);
            HideAndShoot.Audio.GameAudio.PlaySettlement();
        }

#if UNITY_EDITOR
        public void BakeEditableOverlay()
        {
            if(transform.Find("PodiumOverlay")!=null)return;
            var ui=new GameObject("PodiumOverlay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            ui.transform.SetParent(transform,false);
            overlay=ui.GetComponent<Canvas>();overlay.renderMode=RenderMode.ScreenSpaceOverlay;overlay.sortingOrder=200;
            var scaler=ui.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1366,768);scaler.matchWidthOrHeight=.5f;
            TacticalUI.Text(ui.transform,"ResultEyebrow","",new Vector2(0,-48),
                new Vector2(1000,40),19,TacticalUI.Cyan,TextAnchor.MiddleCenter,new Vector2(.5f,1));
            TacticalUI.Text(ui.transform,"PodiumTitle","경기 결과",new Vector2(0,-68),
                new Vector2(800,68),40,TacticalUI.White,TextAnchor.MiddleCenter,new Vector2(.5f,1)).fontStyle=FontStyles.Bold;
            TacticalUI.Stripe(ui.transform,"ResultRule",new Vector2(.5f,1),new Vector2(0,-112),
                new Vector2(120,1),TacticalUI.Gold);
            float[] x={0,-360,360};
            string[] ranks={"1위","2위","3위"};
            for(int i=0;i<3;i++)
            {
                Color accent=i==0?TacticalUI.Gold:i==1?TacticalUI.Cyan:TacticalUI.Coral;
                var card=TacticalUI.Rect(ui.transform,"Rank_"+(i+1),new Vector2(.5f,0),
                    new Vector2(x[i],86),new Vector2(330,104));
                TacticalUI.Frame(card,accent,true);
                TacticalUI.Text(card,"RankBadge",ranks[i],new Vector2(0,30),new Vector2(290,24),15,accent);
                var nickname=TacticalUI.Text(card,"Nickname","참가자 없음",
                    new Vector2(0,0),new Vector2(290,34),23,TacticalUI.White);
                nickname.fontStyle=FontStyles.Bold;nickname.enableAutoSizing=true;
                nickname.fontSizeMin=12;nickname.fontSizeMax=23;
                TacticalUI.Text(card,"Score","—",
                    new Vector2(0,-32),new Vector2(290,30),16,accent);
            }
            ui.SetActive(false);
        }
#endif
        private void OnDestroy()
        {
            if(session!=null)session.MatchPodiumAnnounced-=ShowPodium;
            if(stage!=null){if(Application.isPlaying)Destroy(stage);else DestroyImmediate(stage);}
            foreach(var material in materials)
                if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
        }
    }
}
