if(UnityEditor.EditorApplication.isPlaying)throw new System.Exception("EditMode only");
var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
var previousRT=UnityEngine.RenderTexture.active;
UnityEngine.Material bodyMaterial=null,wallMaterial=null;
UnityEngine.RenderTexture rt=null;UnityEngine.Texture2D image=null;
HideAndShoot.Player.PlayerHighlightTarget target=null;
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
try
{
 var cameraGO=new UnityEngine.GameObject("OutlineEditModeCamera");
 var camera=cameraGO.AddComponent<UnityEngine.Camera>();camera.enabled=false;
 camera.transform.position=UnityEngine.Vector3.zero;camera.transform.rotation=UnityEngine.Quaternion.identity;
 camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;camera.backgroundColor=UnityEngine.Color.black;
 camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.fieldOfView=60;camera.cullingMask=1<<31;camera.allowHDR=false;camera.allowMSAA=false;
 var cameraData=cameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
 cameraData.SetRenderer(0);cameraData.renderPostProcessing=false;cameraData.requiresDepthTexture=true;
 rt=new UnityEngine.RenderTexture(512,288,24);rt.Create();camera.targetTexture=rt;
 var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab");
 var root=UnityEngine.Object.Instantiate(prefab);root.name="ActualSkinnedPlayer";root.transform.position=new UnityEngine.Vector3(0,-.9f,4);
 var body=root.transform.Find("WorldVisual");
 bodyMaterial=new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));bodyMaterial.SetColor("_BaseColor",new UnityEngine.Color(.5f,.02f,.02f,1));
 foreach(var renderer in body.GetComponentsInChildren<UnityEngine.Renderer>(true))
 {renderer.gameObject.layer=31;renderer.sharedMaterial=bodyMaterial;}
 var animator=body.GetComponentInChildren<UnityEngine.Animator>(true);if(animator!=null){animator.Rebind();animator.Update(0);}
 target=root.AddComponent<HideAndShoot.Player.PlayerHighlightTarget>();target.PreviewTarget=true;target.PreviewNickname="관전_Player";
 typeof(HideAndShoot.Player.PlayerHighlightTarget).GetMethod("Awake",flags).Invoke(target,null);
 typeof(HideAndShoot.Player.PlayerHighlightTarget).GetMethod("OnEnable",flags).Invoke(target,null);
 HideAndShoot.Player.PlayerHighlightTarget.PreviewCamera=camera;
 var wall=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);wall.name="Occluder";wall.layer=31;
 wall.transform.position=new UnityEngine.Vector3(0,0,2.5f);wall.transform.localScale=new UnityEngine.Vector3(3,3,.3f);
 wallMaterial=new UnityEngine.Material(bodyMaterial);wallMaterial.SetColor("_BaseColor",new UnityEngine.Color(.02f,.12f,.2f,1));wall.GetComponent<UnityEngine.Renderer>().sharedMaterial=wallMaterial;
 image=new UnityEngine.Texture2D(512,288,UnityEngine.TextureFormat.RGB24,false);
 System.IO.Directory.CreateDirectory("AgentReports/CombatPresentation");
 int Capture(string name)
 {
  UnityEngine.Physics.SyncTransforms();camera.Render();UnityEngine.RenderTexture.active=rt;image.ReadPixels(new UnityEngine.Rect(0,0,512,288),0,0);image.Apply();
  System.IO.File.WriteAllBytes("AgentReports/CombatPresentation/"+name+".png",image.EncodeToPNG());
  int white=0;foreach(var c in image.GetPixels32())if(c.r>160 && c.g>160 && c.b>160)white++;
  return white;
 }
 wall.SetActive(false);int visible=Capture("Skinned_Name_Visible");
 if(root.transform.Find("SpectatorNickname")==null || !root.transform.Find("SpectatorNickname").gameObject.activeSelf)throw new System.Exception("Visible name missing");
 wall.SetActive(true);int hidden=Capture("Skinned_Name_Hidden");
 if(root.transform.Find("SpectatorNickname").gameObject.activeSelf)throw new System.Exception("Name leaked behind wall");
 wall.transform.position=new UnityEngine.Vector3(-1.5f,0,2.5f);int partial=Capture("Skinned_Name_Partial");
 if(visible<50)throw new System.Exception("Missing outline: "+visible);
 if(hidden!=0)throw new System.Exception("Outline leaked through wall: "+hidden);
 if(partial<=0 || partial>=visible)throw new System.Exception("Partial occlusion failed: "+partial+" / "+visible);
 return new { mode="EditMode", visibleWhitePixels=visible, fullyOccludedWhitePixels=hidden, partialWhitePixels=partial, passed=3 };
}
finally
{
 HideAndShoot.Player.PlayerHighlightTarget.PreviewCamera=null;
 if(target!=null)typeof(HideAndShoot.Player.PlayerHighlightTarget).GetMethod("OnDisable",flags).Invoke(target,null);
 UnityEngine.RenderTexture.active=previousRT;
 if(image!=null)UnityEngine.Object.DestroyImmediate(image);
 if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
 if(bodyMaterial!=null)UnityEngine.Object.DestroyImmediate(bodyMaterial);
 if(wallMaterial!=null)UnityEngine.Object.DestroyImmediate(wallMaterial);
 UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
 UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
}