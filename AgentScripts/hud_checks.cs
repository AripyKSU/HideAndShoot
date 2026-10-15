var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
string path="Assets/HideAndShoot/Scenes/GameScene.unity";
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
if(scene.IsValid() && scene.isLoaded)throw new System.Exception("Expected closed GameScene to preserve unsaved state");
scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path,UnityEditor.SceneManagement.OpenSceneMode.Additive);
UnityEngine.Camera camera=null;UnityEngine.RenderTexture rt=null;UnityEngine.Texture2D image=null;
var oldRT=UnityEngine.RenderTexture.active;
try
{
 HideAndShoot.UI.GameHUDPresenter hud=null;
 foreach(var root in scene.GetRootGameObjects()){hud=root.GetComponentInChildren<HideAndShoot.UI.GameHUDPresenter>(true);if(hud!=null)break;}
 var ui=hud.transform.Find("GameHUD_Canvas");var canvas=ui.GetComponent<UnityEngine.Canvas>();
 var go=new UnityEngine.GameObject("HudEditModeCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
 camera=go.AddComponent<UnityEngine.Camera>();camera.enabled=false;camera.transform.position=new UnityEngine.Vector3(10000,1000,0);
 camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;camera.backgroundColor=new UnityEngine.Color(.05f,.08f,.1f,1);camera.nearClipPlane=.01f;camera.farClipPlane=10;
 camera.allowHDR=false;canvas.renderMode=UnityEngine.RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
 rt=new UnityEngine.RenderTexture(1280,720,24);rt.Create();camera.targetTexture=rt;
 image=new UnityEngine.Texture2D(1280,720,UnityEngine.TextureFormat.RGB24,false);
 void Capture(string name)
 {
  UnityEngine.Canvas.ForceUpdateCanvases();foreach(var text in ui.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))text.ForceMeshUpdate(true);
  camera.Render();UnityEngine.RenderTexture.active=rt;image.ReadPixels(new UnityEngine.Rect(0,0,1280,720),0,0);image.Apply();
  System.IO.File.WriteAllBytes("AgentReports/CombatPresentation/"+name+".png",image.EncodeToPNG());
 }
 hud.Preview("scoreboard");
 int overflow=0;
 for(int i=0;i<8;i++)
 {
  ui.Find("RankedScore/Row"+i+"/Kills").GetComponent<TMPro.TextMeshProUGUI>().text=(27-i*3).ToString();
  var name=ui.Find("RankedScore/Row"+i+"/Nickname").GetComponent<TMPro.TextMeshProUGUI>();name.ForceMeshUpdate(true);if(name.isTextOverflowing)overflow++;
 }
 Capture("HUD_Scoreboard");
 hud.Preview("toast");
 var warning=ui.Find("NpcRevealWarning");warning.gameObject.SetActive(true);warning.Find("Text").GetComponent<TMPro.TextMeshProUGUI>().text="NPC를 잘못 사격했습니다. 10초 동안 모습이 노출됩니다.";
 for(int i=0;i<5;i++)
 {
  var entry=ui.Find("KillFeed/Entry"+i).GetComponent<TMPro.TextMeshProUGUI>();entry.gameObject.SetActive(true);
  entry.text=i==0?new string('가',20)+"  ->  "+new string('나',20):"Player_"+(i+1)+"  ->  Player_"+(i+2);
  entry.alpha=1;
 }
 Capture("HUD_KillFeed_Penalty");
 if(overflow!=0)throw new System.Exception("Long scoreboard names overflow: "+overflow);
 return new {mode="EditMode",scoreRows=8,feedRows=5,longNameOverflow=overflow,captures=2};
}
finally
{
 UnityEngine.RenderTexture.active=oldRT;
 if(image!=null)UnityEngine.Object.DestroyImmediate(image);
 if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
 UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
 UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
}