using System;
using System.IO;
using System.Linq;
using System.Text;
using HideAndShoot.Lobby;
using HideAndShoot.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Object=UnityEngine.Object;

public static class Ui1366
{
    private const string Menu="Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
    private const string Game="Assets/HideAndShoot/Scenes/GameScene.unity";
    private const string Folder="Assets/HideAndShoot/QA/CompactUI";
    public static object Apply()
    {
        if(EditorApplication.isPlaying)throw new Exception("Apply layout in edit mode");
        var active=SceneManager.GetActiveScene();
        var menu=SceneManager.GetSceneByPath(Menu);
        bool closeMenu=!menu.IsValid() || !menu.isLoaded;
        if(closeMenu)menu=EditorSceneManager.OpenScene(Menu,OpenSceneMode.Additive);
        if(menu.isDirty)throw new Exception("Menu has unsaved changes");
        var menuRoot=menu.GetRootGameObjects().First(g=>g.name=="MenuLobbyCanvas");
        menuRoot.GetComponent<MenuLobbyUI>().BakeEditableLayout();
        Quiet(menuRoot);
        EditorSceneManager.MarkSceneDirty(menu);EditorSceneManager.SaveScene(menu);
        var game=SceneManager.GetSceneByPath(Game);
        bool closeGame=!game.IsValid() || !game.isLoaded;
        if(closeGame)game=EditorSceneManager.OpenScene(Game,OpenSceneMode.Additive);
        if(game.isDirty)throw new Exception("Game has unsaved changes");
        var hud=game.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<GameHUDPresenter>(true)).Single();
        Replace(hud.transform,"GameHUD_Canvas");hud.BakeEditableLayout();
        var modal=game.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RoundMenuPresenter>(true)).Single();
        Replace(modal.transform,"RoundMenuCanvas");modal.Build();
        var podium=game.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MatchPodiumPresenter>(true)).Single();
        Replace(podium.transform,"PodiumOverlay");podium.BakeEditableOverlay();
        foreach(var root in game.GetRootGameObjects())Quiet(root);
        PlayerSettings.defaultScreenWidth=1366;PlayerSettings.defaultScreenHeight=768;
        EditorSceneManager.MarkSceneDirty(game);EditorSceneManager.SaveScene(game);AssetDatabase.SaveAssets();
        if(closeGame)EditorSceneManager.CloseScene(game,true);
        if(closeMenu)EditorSceneManager.CloseScene(menu,true);
        if(active.IsValid() && active.isLoaded)SceneManager.SetActiveScene(active);
        return new {applied=true,resolution="1366x768",menu=Menu,game=Game};
    }
    private static void Replace(Transform parent,string child)
    {var old=parent.Find(child);if(old!=null)Object.DestroyImmediate(old.gameObject);}
    private static void Quiet(GameObject root)
    {
        foreach(var frame in root.GetComponentsInChildren<TacticalPanelGraphic>(true))
        {frame.corner=2;frame.color=new Color(.035f,.055f,.07f,.88f);frame.borderColor=new Color(.36f,.44f,.48f,.20f);frame.SetAllDirty();}
        foreach(var item in root.GetComponentsInChildren<Transform>(true))
            if(new[]{"AccentRail","AccentFoot","ButtonEdge"}.Contains(item.name))item.gameObject.SetActive(false);
    }
    public static object Verify()
    {
        Directory.CreateDirectory(Folder);
        var report=new StringBuilder("Compact UI 1366x768 verification\n");
        var active=SceneManager.GetActiveScene();
        var menu=SceneManager.GetSceneByPath(Menu);bool closeMenu=!menu.IsValid() || !menu.isLoaded;
        if(closeMenu)menu=EditorSceneManager.OpenScene(Menu,OpenSceneMode.Additive);
        var game=SceneManager.GetSceneByPath(Game);bool closeGame=!game.IsValid() || !game.isLoaded;
        if(closeGame)game=EditorSceneManager.OpenScene(Game,OpenSceneMode.Additive);
        var temp=new GameObject("CompactUI_QA_TEMP");temp.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var source=menu.GetRootGameObjects().First(g=>g.name=="MenuLobbyCanvas");
            var camera=new GameObject("QA_Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(temp.transform);camera.enabled=false;
            var background=menu.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if(background!=null){camera.CopyFrom(background);camera.transform.SetPositionAndRotation(background.transform.position,background.transform.rotation);}
            camera.enabled=false;
            var clone=Object.Instantiate(source,temp.transform);var ui=clone.GetComponent<MenuLobbyUI>();ui.InitializeUI();
            var canvas=clone.GetComponent<Canvas>();
            var guide=clone.GetComponentInChildren<ControlsGuideToggle>(true);guide.Initialize();
            Check(guide.IsExpanded,"Menu starts with discoverable controls including Tab",report);
            Save(canvas,camera,"Menu_Open",1366,768,report,true);
            guide.GetComponentInChildren<Button>().onClick.Invoke();
            Check(!guide.IsExpanded && !guide.transform.Find("GuideContent").gameObject.activeSelf,
                "Guide button folds content",report);
            Save(canvas,camera,"Menu_Folded",1366,768,report,true);
            var originalVisibility=clone.transform.Find("MenuControlsPanel").Cast<Transform>().ToDictionary(t=>t.name,t=>t.gameObject.activeSelf);
            ui.DisplayRoom("AB12CD",true);
            var names=Enumerable.Range(0,8).Select(i=>i==1?new string('W',20):new string((char)('가'+i),20)).ToArray();
            ui.DisplayRoster(names,Enumerable.Range(0,8).Select(i=>i==0?"Host":"99999 ms").ToArray(),
                Enumerable.Range(0,8).Select(i=>i==0).ToArray(),8,8,true,false);
            Check(clone.GetComponentsInChildren<Button>(true).Single(b=>b.name=="HostPlayButton").interactable,
                "Eight-player host can start",report);
            foreach(var size in new[]{new Vector2Int(1366,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
                Save(canvas,camera,"Lobby_"+size.x+"x"+size.y,size.x,size.y,report,true);
            ui.DisplayRoom("AB12CD",false);ui.DisplayRoster(names,new string[8],new bool[8],8,8,false,false);
            Check(!clone.GetComponentsInChildren<Button>(true).Single(b=>b.name=="HostPlayButton").gameObject.activeSelf,
                "Client keeps host-only start hidden",report);
            Save(canvas,camera,"Lobby_Client",1366,768,report,true);
            ui.HideRoom();
            Check(!guide.IsExpanded,"Folded guide survives entering and leaving a room",report);
            foreach(Transform child in clone.transform.Find("MenuControlsPanel"))
                if(originalVisibility.TryGetValue(child.name,out bool value))Check(child.gameObject.activeSelf==value,
                    "Room leave restores "+child.name,report);
            guide.SetExpanded(true);clone.SetActive(false);
            var authored=game.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<GameHUDPresenter>(true)).Single();
            var hud=Object.Instantiate(authored,temp.transform);
            var hudCanvas=hud.GetComponentInChildren<Canvas>(true);
            foreach(var size in new[]{new Vector2Int(1366,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            foreach(var state in new[]{"active","spectator","scoreboard","toast","sensitivity","help","winner","countdown"})
            {
                hud.Preview(state);
                Save(hudCanvas,camera,"HUD_"+state+"_"+size.x+"x"+size.y,size.x,size.y,report,true);
                var visible=hudCanvas.transform.Cast<Transform>().Where(t=>t.gameObject.activeSelf && t.name!="ADSCrosshair").OfType<RectTransform>().ToArray();
                for(int i=0;i<visible.Length;i++)for(int j=i+1;j<visible.Length;j++)
                    Check(!Bounds(hudCanvas.transform,visible[i]).Overlaps(Bounds(hudCanvas.transform,visible[j])),
                        state+" separates "+visible[i].name+" and "+visible[j].name,report);
                Check(!hudCanvas.transform.Find("RankedScore").gameObject.activeSelf || state=="scoreboard",
                    state+" shows scoreboard only when requested",report);
            }
            hud.gameObject.SetActive(false);
            var modal=Object.Instantiate(game.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RoundMenuPresenter>(true)).Single(),temp.transform);
            var modalCanvas=modal.transform.Find("RoundMenuCanvas").GetComponent<Canvas>();foreach(var other in modal.GetComponentsInChildren<Canvas>(true))if(other!=modalCanvas)other.enabled=false;modalCanvas.gameObject.SetActive(true);
            Save(modalCanvas,camera,"ESC",1366,768,report,true);modal.gameObject.SetActive(false);
            report.AppendLine("PASS | Scene references and actual authored dimensions verified. Render previews use CanvasScaler's equivalent width/height match; live Game View capture follows separately.");
        }
        catch(Exception error){report.AppendLine("FAIL | "+error);throw;}
        finally
        {
            Object.DestroyImmediate(temp);
            if(closeGame)EditorSceneManager.CloseScene(game,true);
            if(closeMenu)EditorSceneManager.CloseScene(menu,true);
            if(active.IsValid() && active.isLoaded)SceneManager.SetActiveScene(active);
            File.WriteAllText(Folder+"/Verification.txt",report.ToString());
        }
        return new {passed=true,report=Folder+"/Verification.txt"};
    }
    public static object CapturePodium()
    {
        var active=SceneManager.GetActiveScene();
        var game=SceneManager.GetSceneByPath(Game);bool close=!game.IsValid() || !game.isLoaded;
        if(close)game=EditorSceneManager.OpenScene(Game,OpenSceneMode.Additive);
        var temp=new GameObject("PodiumUI_QA_TEMP");temp.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var source=game.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MatchPodiumPresenter>(true)).Single();
            var podium=Object.Instantiate(source,temp.transform);
            podium.ShowPodium(Enumerable.Range(0,3).Select(i=>new HideAndShoot.Networking.RelayLobbySession.ScoreRow(
                (ulong)i,new string((char)('가'+i),20),3-i,i,i,true,true)).ToArray());
            var canvas=podium.transform.Find("PodiumOverlay").GetComponent<Canvas>();foreach(var other in podium.GetComponentsInChildren<Canvas>(true))if(other!=canvas)other.enabled=false;
            foreach(var item in canvas.GetComponentsInChildren<Transform>(true))item.gameObject.layer=5;
            var camera=podium.GetComponentInChildren<Camera>(true);camera.enabled=false;
            var report=new StringBuilder();Save(canvas,camera,"Podium",1366,768,report,true);
            File.AppendAllText(Folder+"/Verification.txt",report.ToString());
            return new {passed=true,capture=Folder+"/Podium.png"};
        }
        finally
        {Object.DestroyImmediate(temp);if(close)EditorSceneManager.CloseScene(game,true);if(active.IsValid() && active.isLoaded)SceneManager.SetActiveScene(active);}
    }
    private static Rect Bounds(Transform canvas,RectTransform rect)
    {var b=RectTransformUtility.CalculateRelativeRectTransformBounds(canvas,rect);return new Rect(b.min.x,b.min.y,b.size.x,b.size.y);}
    private static void Check(bool good,string message,StringBuilder report)
    {if(!good)throw new Exception(message);report.AppendLine("PASS | "+message);}
    private static void Save(Canvas canvas,Camera camera,string name,int width,int height,StringBuilder report,bool check)
    {
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.enabled=false;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        float w=width/scaler.referenceResolution.x,h=height/scaler.referenceResolution.y;
        canvas.scaleFactor=Mathf.Pow(w,1-scaler.matchWidthOrHeight)*Mathf.Pow(h,scaler.matchWidthOrHeight);
        var previous=RenderTexture.active;var original=camera.targetTexture;
        var target=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.aspect=(float)width/height;
            foreach(var group in canvas.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
            Canvas.ForceUpdateCanvases();
            foreach(var frame in canvas.GetComponentsInChildren<TacticalPanelGraphic>(true))
            {frame.SetAllDirty();frame.Rebuild(CanvasUpdate.PreRender);}
            camera.Render();camera.Render();
            if(check)
            {
                foreach(var text in canvas.GetComponentsInChildren<TextMeshProUGUI>(false))
                {
                    if(string.IsNullOrEmpty(text.text))continue;
                    text.ForceMeshUpdate(true,true);
                    Check(!text.isTextTruncated,name+" text fits "+text.name,report);
                    var corners=new Vector3[4];text.rectTransform.GetWorldCorners(corners);
                    bool onScreen=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(camera,c)).All(p=>p.x>=-.5f && p.x<=width+.5f && p.y>=-.5f && p.y<=height+.5f);
                    Check(onScreen,name+" text stays on screen "+text.name,report);
                }
            }
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
            File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG());
        }
        finally{camera.targetTexture=original;RenderTexture.active=previous;Object.DestroyImmediate(texture);Object.DestroyImmediate(target);}
    }
}
