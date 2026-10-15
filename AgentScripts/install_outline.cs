var shaderMask=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/HideAndShoot/Resources/Shaders/PlayerSilhouette.shader");
var shaderOutline=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/HideAndShoot/Resources/Shaders/PlayerOutline.shader");
if(shaderMask==null || shaderOutline==null)throw new System.Exception("Missing outline shaders");
foreach(var shader in new[]{shaderMask,shaderOutline})
 if(UnityEditor.ShaderUtil.ShaderHasError(shader))throw new System.Exception("Shader error: "+shader.name);
int installed=0;
foreach(string path in new[]{"Assets/Settings/PC_Renderer.asset","Assets/Settings/Mobile_Renderer.asset"})
{
 string backup="AgentBackups/20261015-CombatPresentation/"+path;
 if(!System.IO.File.Exists(backup)){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));System.IO.File.Copy(path,backup);}
 var data=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
 HideAndShoot.Player.PlayerOutlineRendererFeature feature=null;
 foreach(var existing in data.rendererFeatures)
  if(existing is HideAndShoot.Player.PlayerOutlineRendererFeature found){feature=found;break;}
 if(feature==null)
 {
  feature=UnityEngine.ScriptableObject.CreateInstance<HideAndShoot.Player.PlayerOutlineRendererFeature>();
  feature.name="Depth-tested Player Outline";
  UnityEditor.AssetDatabase.AddObjectToAsset(feature,data);data.rendererFeatures.Add(feature);
 }
 feature.maskShader=shaderMask;feature.outlineShader=shaderOutline;
 feature.outlinePixels=2;feature.SetActive(true);feature.Create();
 data.SetDirty();UnityEditor.EditorUtility.SetDirty(feature);UnityEditor.EditorUtility.SetDirty(data);
 UnityEditor.AssetDatabase.SaveAssetIfDirty(data);installed++;
}
return new { renderers=installed, shaderErrors=0, radiusPixels=2 };