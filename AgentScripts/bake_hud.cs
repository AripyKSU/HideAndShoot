string path="Assets/HideAndShoot/Scenes/GameScene.unity";
string backup="AgentBackups/20261015-CombatPresentation/"+path;
if(!System.IO.File.Exists(backup)){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));System.IO.File.Copy(path,backup);}
var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
bool opened=!scene.IsValid() || !scene.isLoaded;
if(!opened && scene.isDirty)throw new System.InvalidOperationException("GameScene has unsaved changes; refusing to overwrite them.");
if(opened)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path,UnityEditor.SceneManagement.OpenSceneMode.Additive);
try
{
 HideAndShoot.UI.GameHUDPresenter hud=null;
 foreach(var root in scene.GetRootGameObjects())
 {hud=root.GetComponentInChildren<HideAndShoot.UI.GameHUDPresenter>(true);if(hud!=null)break;}
 if(hud==null)throw new System.Exception("Missing HUD presenter");
 hud.BakeCombatLayout();
 UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
 if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene))throw new System.Exception("Save failed");
 return new { saved=path, killsColumns=8, killFeedRows=5, penaltyWarning=true };
}
finally
{
 if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
 if(opened)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
}