var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
HideAndShoot.Combat.HostPlayerLife host=null,target=null;
foreach(var life in UnityEngine.Object.FindObjectsByType<HideAndShoot.Combat.HostPlayerLife>(UnityEngine.FindObjectsSortMode.None))
{if(life.IsOwner)host=life;else if(life.OwnerClientId==101)target=life;}
UnityEngine.Camera camera;
if(host.IsDead.Value)
{
 var rig=host.GetComponent<HideAndShoot.Player.LocalSpectatorRig>();
 camera=(UnityEngine.Camera)typeof(HideAndShoot.Player.LocalSpectatorRig).GetField("spectatorCamera",flags).GetValue(rig);
 camera.transform.position=new UnityEngine.Vector3(0,1001.6f,0);
 camera.transform.rotation=UnityEngine.Quaternion.LookRotation(target.transform.position+UnityEngine.Vector3.up*1.4f-camera.transform.position);
}
else camera=host.GetComponent<HideAndShoot.Player.PlayerLocalMotor>().PlayerCamera;
var hud=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.UI.GameHUDPresenter>();hud.SendMessage("Update");
var canvas=hud.transform.Find("GameHUD_Canvas").GetComponent<UnityEngine.Canvas>();
var mode=canvas.renderMode;var previousCamera=canvas.worldCamera;float plane=canvas.planeDistance;
var oldTarget=camera.targetTexture;var oldActive=UnityEngine.RenderTexture.active;
var rt=new UnityEngine.RenderTexture(1280,720,24);rt.Create();
var image=new UnityEngine.Texture2D(1280,720,UnityEngine.TextureFormat.RGB24,false);
try
{
 camera.targetTexture=rt;canvas.renderMode=UnityEngine.RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;
 UnityEngine.Canvas.ForceUpdateCanvases();camera.Render();camera.Render();
 UnityEngine.RenderTexture.active=rt;image.ReadPixels(new UnityEngine.Rect(0,0,1280,720),0,0);image.Apply();
 string path="AgentReports/CombatPresentation/"+(host.IsDead.Value?"Play_Spectator":"Play_Alive")+".png";
 System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
 var label=target.transform.Find("SpectatorNickname");
 if(host.IsDead.Value && (label==null || !label.gameObject.activeSelf))throw new System.Exception("Spectator name is not visible");
 return new { capture=path, spectator=host.IsDead.Value, targetNameVisible=label!=null && label.gameObject.activeSelf, targetRevealSeconds=target.RevealSecondsRemaining };
}
finally
{
 camera.targetTexture=oldTarget;UnityEngine.RenderTexture.active=oldActive;
 canvas.renderMode=mode;canvas.worldCamera=previousCamera;canvas.planeDistance=plane;
 UnityEngine.Object.Destroy(image);rt.Release();UnityEngine.Object.Destroy(rt);
}