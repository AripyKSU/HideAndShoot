using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using HideAndShoot.Lobby;
using HideAndShoot.Networking;
using HideAndShoot.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class UiPodiumRegression
    {
        private const string Report="Assets/HideAndShoot/QA/UiPodiumRegression.txt";
        private const string ReturnRequest="Assets/HideAndShoot/QA/MenuRoomReturn.request";
        private const string ReturnComplete="Assets/HideAndShoot/QA/MenuRoomReturn.complete.txt";
        private static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static UiPodiumRegression()=>EditorApplication.update+=Ready;
        private static void Ready()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)return;
            EditorApplication.update-=Ready;
            if(File.Exists(ReturnRequest) && !File.Exists(ReturnComplete) ||
                !File.Exists(Report) || File.ReadAllText(Report).Contains("FAIL |") ||
                !File.ReadAllText(Report).Contains("VERIFIED VISIBLE PODIUM OVERLAY"))Run();
        }
        private static void Check(bool valid,string description,StringBuilder report)
        {
            if(!valid)throw new InvalidOperationException(description);
            report.AppendLine("PASS | "+description);
        }
        private static T Named<T>(Transform root,string name) where T:Component=>
            root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name).GetComponent<T>();

        [MenuItem("Tools/HideAndShoot/QA/Verify Styled UI and Eight-player Lobby")]
        public static void Run()
        {
            var report=new StringBuilder("Unity UI, eight-slot lobby and distinct podium animations\n");
            var root=new GameObject("UiPodiumRegression_TEMP");root.hideFlags=HideFlags.HideAndDontSave;
            var original=SceneManager.GetActiveScene();
            var menu=SceneManager.GetSceneByPath("Assets/HideAndShoot/Scenes/MenuLobbyScene.unity");
            bool opened=!menu.IsValid() || !menu.isLoaded;
            try
            {
                PodiumAnimationInstaller.Install();
                if(opened)menu=EditorSceneManager.OpenScene("Assets/HideAndShoot/Scenes/MenuLobbyScene.unity",OpenSceneMode.Additive);
                var authored=menu.GetRootGameObjects().First(g=>g.name=="MenuLobbyCanvas");
                var sourceCamera=menu.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true))
                    .FirstOrDefault(c=>c.name=="MenuBackgroundCamera");
                var camera=new GameObject("UI_QA_Camera",typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(root.transform,false);camera.enabled=false;
                if(sourceCamera!=null)
                {
                    camera.CopyFrom(sourceCamera);
                    camera.transform.SetPositionAndRotation(sourceCamera.transform.position,sourceCamera.transform.rotation);
                }
                else
                {camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=TacticalUI.Ink;}
                camera.enabled=false;
                var clone=UnityEngine.Object.Instantiate(authored,root.transform);
                var ui=clone.GetComponent<MenuLobbyUI>();ui.InitializeUI();
                var canvas=clone.GetComponent<Canvas>();
                SaveCanvas(canvas,camera,"Menu_Styled_1920",1920,1080);
                // Regression for create-room -> leave-room: restored menu must
                // remain code-styled without re-enabling intentionally hidden UI.
                var controls=Named<RectTransform>(clone.transform,"MenuControlsPanel");
                var hint=Named<TextMeshProUGUI>(controls,"ConnectionHint");
                var originalHintVisibility=hint.gameObject.activeSelf;
                hint.gameObject.SetActive(false);
                var priorStates=controls.Cast<Transform>().ToDictionary(
                    child=>child.name,child=>child.gameObject.activeSelf);
                ui.DisplayRoom("AB12CD",true);
                ui.DisplayRoom("AB12CD",true); // repeated host roster binds
                Check(ui.IsInRoom,"Room overlay activates when a room is created",report);
                ui.HideRoom("방에서 나왔습니다.");
                Check(!ui.IsInRoom,"Room overlay is hidden after leaving",report);
                foreach(Transform child in controls)
                    if(priorStates.TryGetValue(child.name,out bool prior))
                        Check(child.gameObject.activeSelf==prior,
                            "Leave room restores '"+child.name+"' original visibility",report);
                Check(!hint.gameObject.activeSelf,
                    "Intentionally hidden menu text stays hidden after room exit",report);
                hint.gameObject.SetActive(originalHintVisibility);
                var title=Named<TextMeshProUGUI>(clone.transform,"GameTitle");
                Check(title.text=="HIDE AND SHOOT" && title.font==TacticalUI.Font &&
                    title.fontStyle==FontStyles.Bold,"Code-authored title and TMP font survive room exit",report);
                Check(Named<TMP_InputField>(controls,"NicknameInput").interactable,
                    "Nickname TMP input restored after room exit",report);
                Check(Named<Button>(controls,"CreateRoomButton").interactable,
                    "Create-room button is usable again after room exit",report);
                SaveCanvas(canvas,camera,"Menu_AfterRoomLeave_1920",1920,1080);
                ui.DisplayRoom("AB12CD",true);
                var names=new string[8];var pings=new string[8];var hosts=new bool[8];
                for(int i=0;i<8;i++)
                {
                    names[i]=new string((char)('가'+i),NicknameRules.MaxCharacters);
                    pings[i]=i==0?"Host":(20+i*18)+" ms";hosts[i]=i==0;
                    Check(NicknameRules.TryNormalize(names[i],out _,out _),"20-character Korean nickname "+(i+1)+" accepted",report);
                }
                ui.DisplayRoster(names,pings,hosts,8,8,true,false);
                Check(Named<Button>(clone.transform,"HostPlayButton").interactable,"Eight-player host can start",report);
                var grid=Named<RectTransform>(clone.transform,"RosterGrid");
                Check(grid.childCount==8,"Exactly eight dedicated member rows",report);
                foreach(var resolution in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1280,1024)})
                {
                    SaveCanvas(canvas,camera,"Lobby_8Players_"+resolution.x+"x"+resolution.y,
                        resolution.x,resolution.y,()=>CheckRoster(grid,canvas,report,resolution));
                }
                names[1]="<b>Nickname</b>";
                names[2]=new string('W',20);pings[2]="99999 ms";
                ui.DisplayRoster(names,pings,hosts,8,8,true,false);
                var text=Named<TextMeshProUGUI>(grid.GetChild(1),"Nickname");
                Check(!text.richText && text.text==names[1],"Nickname markup remains literal",report);
                Check(Named<TextMeshProUGUI>(grid.GetChild(2),"Ping").text=="999+ ms","Large ping stays within its separate column",report);
                var wideName=Named<TextMeshProUGUI>(grid.GetChild(2),"Nickname");
                wideName.ForceMeshUpdate(true,true);
                Check(wideName.enableAutoSizing && !wideName.isTextTruncated &&
                    wideName.GetRenderedValues(false).x<=wideName.rectTransform.rect.width+.5f,
                    "Twenty wide Latin characters fit without truncation",report);
                ui.DisplayRoom("AB12CD",false);ui.DisplayRoster(names,pings,hosts,8,8,false,false);
                Check(!Named<Button>(clone.transform,"HostPlayButton").gameObject.activeSelf,
                    "Client lobby keeps host-only start hidden",report);
                SaveCanvas(canvas,camera,"Lobby_Client_8Players",1280,720);
                ui.DisplayRoom("AB12CD",true);ui.DisplayRoster(names,pings,hosts,1,8,true,false);
                Check(!Named<Button>(clone.transform,"HostPlayButton").interactable,
                    "One-player lobby keeps start disabled",report);
                clone.SetActive(false);
                RenderHud(root.transform,camera,report);
                RenderRoundMenu(root.transform,camera,report);
                RenderPodium(root.transform,report);
                report.AppendLine("Rendered layout/animation checks use editor previews; no online session was opened.");
            }
            catch(Exception error)
            {report.AppendLine("FAIL | "+error);Debug.LogException(error);}
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if(opened && menu.IsValid() && menu.isLoaded)EditorSceneManager.CloseScene(menu,true);
                if(original.IsValid() && original.isLoaded)SceneManager.SetActiveScene(original);
                Directory.CreateDirectory("Assets/HideAndShoot/QA");
                File.WriteAllText(Report,report.ToString());
                if(File.Exists(ReturnRequest) && !report.ToString().Contains("FAIL |"))
                    File.WriteAllText(ReturnComplete,"Room leave UI restore verified "+
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Debug.Log(report.ToString());
            }
        }

        private static void CheckRoster(RectTransform grid,Canvas canvas,StringBuilder report,Vector2Int resolution)
        {
            for(int i=0;i<8;i++)
            {
                var row=(RectTransform)grid.GetChild(i);
                var name=Named<TextMeshProUGUI>(row,"Nickname");
                Check(name.preferredWidth<=name.rectTransform.rect.width+.5f &&
                    name.preferredHeight<=name.rectTransform.rect.height+.5f,
                    resolution+" row "+(i+1)+" preserves full nickname in one line",report);
                var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(grid,row);
                Check(bounds.min.y>=grid.rect.yMin-.5f && bounds.max.y<=grid.rect.yMax+.5f,
                    resolution+" row "+(i+1)+" fits roster viewport",report);
                var corners=new Vector3[4];name.rectTransform.GetWorldCorners(corners);
                bool onScreen=true;
                foreach(var corner in corners)
                {
                    var point=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,corner);
                    onScreen&=point.x>=-.5f && point.x<=resolution.x+.5f &&
                        point.y>=-.5f && point.y<=resolution.y+.5f;
                }
                Check(onScreen,resolution+" row "+(i+1)+" text stays on screen",report);
            }
        }

        private static void SaveCanvas(Canvas canvas,Camera camera,string name,int width,int height,Action verify=null)
        {
            var scaler=canvas.GetComponent<CanvasScaler>();
            if(scaler!=null)scaler.enabled=false;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;canvas.planeDistance=1;
            canvas.scaleFactor=Mathf.Sqrt(width/1366f*height/768f);
            Save(camera,canvas,name,width,height,verify);
        }
        private static void Save(Camera camera,Canvas canvas,string name,int width,int height,Action verify)
        {
            var previous=RenderTexture.active;var original=camera.targetTexture;
            var target=new RenderTexture(width,height,24);
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.aspect=(float)width/height;
                canvas.gameObject.SetActive(false);canvas.gameObject.SetActive(true);
                foreach(var group in canvas.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
                foreach(var graphic in canvas.GetComponentsInChildren<Graphic>(true))graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                foreach(var frame in canvas.GetComponentsInChildren<TacticalPanelGraphic>(true))
                {frame.SetAllDirty();frame.Rebuild(CanvasUpdate.PreRender);}
                camera.Render();
                camera.Render(); // Settle newly-created camera and skinned renderer state.
                verify?.Invoke();
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                if(name.StartsWith("Podium_"))
                {
                    var pixels=texture.GetPixels32();var background=pixels[0];int visible=0;
                    for(int i=0;i<pixels.Length;i+=8)
                        if(Mathf.Abs(pixels[i].r-background.r)+Mathf.Abs(pixels[i].g-background.g)+
                            Mathf.Abs(pixels[i].b-background.b)>30)visible++;
                    if(visible<1000)throw new InvalidOperationException("Podium render is blank: "+name);
                    int heading=0;
                    for(int y=height-185;y<height-30;y+=2)
                    for(int x=width/4;x<width*3/4;x+=2)
                    {
                        var pixel=pixels[y*width+x];
                        if(pixel.r+pixel.g+pixel.b>200)heading++;
                    }
                    if(heading<100)throw new InvalidOperationException("Podium result overlay is missing: "+name);
                }
                File.WriteAllBytes("Assets/HideAndShoot/QA/"+name+".png",texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=original;RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void RenderHud(Transform root,Camera camera,StringBuilder report)
        {
            var hud=new GameObject("Compact_HUD_QA").AddComponent<GameHUDPresenter>();
            hud.transform.SetParent(root,false);hud.BakeEditableLayout();
            var canvas=hud.GetComponentInChildren<Canvas>(true);
            hud.Preview("toast");SaveCanvas(canvas,camera,"HUD_Styled_QA",1366,768);
            hud.Preview("scoreboard");
            foreach(var label in canvas.transform.Find("RankedScore").GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                label.ForceMeshUpdate(true,true);
                Check(!label.isTextTruncated,"Compact scoreboard preserves "+label.name,report);
            }
            SaveCanvas(canvas,camera,"HUD_Scoreboard_8Players",1366,768);
            hud.gameObject.SetActive(false);
        }
        private static void RenderRoundMenu(Transform root,Camera camera,StringBuilder report)
        {
            var presenter=new GameObject("RoundMenu_QA").AddComponent<RoundMenuPresenter>();
            presenter.transform.SetParent(root,false);presenter.Build();
            var canvas=presenter.GetComponentInChildren<Canvas>(true);canvas.gameObject.SetActive(true);
            SaveCanvas(canvas,camera,"RoundMenu_Styled_QA",1280,720);
            Check(canvas.GetComponent<GraphicRaycaster>()!=null,"Styled ESC menu supports UGUI pointer input",report);
            presenter.gameObject.SetActive(false);
        }

        private static void RenderPodium(Transform root,StringBuilder report)
        {
            var set=Resources.Load<PodiumAnimationSet>("PodiumAnimationSet");
            Check(set!=null && set.clips.Length>=3 && set.clips.All(c=>c!=null && c.isLooping),
                "Podium has at least three native looping clips",report);
            Check(set.clips.Select(PodiumAnimationInstaller.MotionHash).Distinct().Count()==set.clips.Length,
                "Podium clips have distinct motion curves",report);
            var presenter=new GameObject("Podium_QA").AddComponent<MatchPodiumPresenter>();
            presenter.transform.SetParent(root,false);
            presenter.BakeEditableOverlay();
            var rows=Enumerable.Range(0,3).Select(i=>new RelayLobbySession.ScoreRow((ulong)i,
                new string((char)('가'+i),20),3-i,i,i,true,true)).ToArray();
            presenter.ShowPodium(rows);
            var celebrations=presenter.GetComponentsInChildren<PodiumCelebration>(true);
            Check(celebrations.Length==3 && celebrations.Select(c=>c.SelectedClip).Distinct().Count()==3,
                "First, second and third use different clips simultaneously",report);
            for(int i=0;i<3;i++)report.AppendLine("Rank "+(i+1)+" clip="+celebrations[i].SelectedClip.name);
            foreach(var celebration in celebrations)celebration.SampleTime(.65f);
            var canvas=presenter.GetComponentInChildren<Canvas>(true);
            var camera=presenter.GetComponentInChildren<Camera>(true);camera.enabled=false;
            foreach(var item in canvas.GetComponentsInChildren<Transform>(true))item.gameObject.layer=5;
            SaveCanvas(canvas,camera,"Podium_ThreeAnimations_QA",1920,1080);
            var animator=celebrations[0].GetComponent<Animator>();
            var before=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation;
            foreach(var celebration in celebrations)celebration.SampleTime(1.65f);
            var after=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation;
            Check(Quaternion.Angle(before,after)>1,"Celebration advances animated arm pose",report);
            SaveCanvas(canvas,camera,"Podium_ThreeAnimations_Frame2",1920,1080);
            report.AppendLine("VERIFIED VISIBLE PODIUM OVERLAY");
        }
    }
}
