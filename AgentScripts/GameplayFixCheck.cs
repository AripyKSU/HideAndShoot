using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HideAndShoot.Combat;
using HideAndShoot.NPC;
using HideAndShoot.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class GameplayFixCheck
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    const string Output="docs/GameplayFixQA_2026-10-14";
    static readonly StringBuilder Report=new StringBuilder("Gameplay fix verification, connected Unity Editor\n");
    static void Check(bool good,string message)
    { if(!good)throw new Exception(message); Report.AppendLine("PASS | "+message); }
    static void Call(object target,string method,params object[] args)
    { target.GetType().GetMethod(method,Private).Invoke(target,args); }
    static void Step(Animator animator,float seconds)
    { for(int i=0;i<Mathf.CeilToInt(seconds/.05f);i++)animator.Update(.05f); }
    public static object Run()
    {
        Directory.CreateDirectory(Output);
        var active=SceneManager.GetActiveScene();
        var preview=EditorSceneManager.NewPreviewScene();
        var game=SceneManager.GetSceneByPath("Assets/HideAndShoot/Scenes/GameScene.unity");
        bool opened=!game.IsValid() || !game.isLoaded;
        try
        {
            Check(!EditorApplication.isPlaying,"Checks run without changing Play mode or network sessions");
            foreach(string name in new[]{"NPCActor","PlayerAvatar"})
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HideAndShoot/Prefabs/"+name+".prefab");
                var actor=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(actor,preview);
                try
                {
                    var component=name=="NPCActor"?(Component)actor.GetComponent<NpcWanderBrain>():actor.GetComponent<HostPlayerLife>();
                    Call(component,"Awake");
                    var animator=actor.transform.Find("WorldVisual").GetComponentInChildren<Animator>(true);
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();animator.Update(0f);
                    var hitbox=actor.transform.Find("DamageCapsule_ONE_HITBOX").GetComponent<Collider>();
                    for(int cycle=0;cycle<8;cycle++)
                    {
                        Call(component,"OnDeathChanged",false,true);Step(animator,1.8f);
                        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Death") && !hitbox.enabled,
                            name+" cycle "+cycle+": corpse enters Death and disables hitbox");
                        if(name=="PlayerAvatar")Call(component,"SetWorldRenderersVisible",false);
                        bool inactive=cycle%2==1;
                        if(inactive)actor.SetActive(false);
                        Call(component,"OnDeathChanged",true,false);
                        if(inactive)actor.SetActive(true);
                        Step(animator,.2f);
                        Check(!animator.GetBool("Dead") && animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && hitbox.enabled,
                            name+" cycle "+cycle+": "+(inactive?"inactive":"active")+" revive restores locomotion and hitbox");
                        if(name=="PlayerAvatar")
                        {
                            var renderers=(Renderer[])typeof(HostPlayerLife).GetField("visibleWorldRenderers",Private).GetValue(component);
                            Check(renderers.All(r=>r.enabled),"Player corpse renderers restored on revive");
                        }
                    }
                    animator.SetBool("Dead",true);Step(animator,.3f);
                    animator.SetBool("Dead",false);Step(animator,.2f);
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"),name+": controller alone exits Death when Dead clears");
                }
                finally{Object.DestroyImmediate(actor);}
            }
            if(opened)game=EditorSceneManager.OpenScene("Assets/HideAndShoot/Scenes/GameScene.unity",OpenSceneMode.Additive);
            var authored=game.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameHUDPresenter>(true)).Single();
            var hud=Object.Instantiate(authored);SceneManager.MoveGameObjectToScene(hud.gameObject,preview);
            var camera=new GameObject("GameplayFixQA_Camera",typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject,preview);
            camera.scene=preview;camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.1f,.13f);
            camera.cullingMask=-1;
            hud.Preview("active");
            var canvas=hud.transform.Find("GameHUD_Canvas").GetComponent<Canvas>();
            foreach(var other in hud.GetComponentsInChildren<Canvas>(true))if(other!=canvas)other.gameObject.SetActive(false);
            var label=canvas.transform.Find("Ammo/Text").GetComponent<TextMeshProUGUI>();
            label.color=TacticalUI.White;
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1366,768),new Vector2Int(1920,1080)})
            {
                foreach(int count in new[]{0,9,10})
                {
                    label.text=count+" / 10";
                    Render(canvas,camera,label,size,count==10);
                }
            }
            Check(Resources.Load<AudioClip>("Audio/Ambience")!=null,"Arena ambience clip resolves from Resources");
            Report.AppendLine("Audio target changed from 0.18 to 0.36 (2x gain); listening and multiplayer transport require a live match.");
            File.WriteAllText(Output+"/Verification.txt",Report.ToString());
            return new {passed=true,report=Output+"/Verification.txt",captures=3,deathCycles=16};
        }
        catch(Exception ex){Report.AppendLine("FAIL | "+ex);File.WriteAllText(Output+"/Verification.txt",Report.ToString());throw;}
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if(opened && game.IsValid() && game.isLoaded)EditorSceneManager.CloseScene(game,true);
            if(active.IsValid() && active.isLoaded)SceneManager.SetActiveScene(active);
        }
    }
    static void Render(Canvas canvas,Camera camera,TextMeshProUGUI label,Vector2Int size,bool save)
    {
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.enabled=false;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1f;
        canvas.scaleFactor=Mathf.Pow(size.x/scaler.referenceResolution.x,1-scaler.matchWidthOrHeight)*Mathf.Pow(size.y/scaler.referenceResolution.y,scaler.matchWidthOrHeight);
        var previous=RenderTexture.active;
        var target=new RenderTexture(size.x,size.y,24);
        var texture=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.aspect=(float)size.x/size.y;
            foreach(var group in canvas.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1f;
            Canvas.ForceUpdateCanvases();label.ForceMeshUpdate(true,true);
            foreach(var panel in canvas.GetComponentsInChildren<TacticalPanelGraphic>(true)){panel.SetAllDirty();panel.Rebuild(CanvasUpdate.PreRender);}
            camera.Render();camera.Render();label.ForceMeshUpdate(true,true);
            var area=label.rectTransform.rect;
            Check(label.textInfo.lineCount==1 && label.renderedWidth<=area.width+.5f && label.renderedHeight<=area.height+.5f,
                size+" "+label.text+": glyphs fit inside text rectangle ("+label.renderedWidth+" / "+area.width+")");
            var corners=new Vector3[4];label.rectTransform.GetWorldCorners(corners);
            Check(corners.Select(c=>RectTransformUtility.WorldToScreenPoint(camera,c)).All(p=>p.x>=0 && p.x<=size.x && p.y>=0 && p.y<=size.y),
                size+" "+label.text+": ammo stays on screen");
            if(save){RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,size.x,size.y),0,0);texture.Apply();File.WriteAllBytes(Output+"/Ammo10_"+size.x+"x"+size.y+".png",texture.EncodeToPNG());}
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(texture);Object.DestroyImmediate(target);}
    }
}