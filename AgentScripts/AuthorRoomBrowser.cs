var menuScene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/HideAndShoot/Scenes/MenuLobbyScene.unity");
if(!menuScene.IsValid() || !menuScene.isLoaded)throw new System.Exception("Open MenuLobbyScene first.");
if(menuScene.isDirty)throw new System.Exception("Preserve pending scene edits before authoring.");
var ui=menuScene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<HideAndShoot.Lobby.MenuLobbyUI>(true)).First();
UnityEditor.Undo.RegisterFullObjectHierarchyUndo(ui.gameObject,"Add public room browser");
ui.BakeRoomBrowserLayout();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(menuScene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(menuScene);
var gamePath="Assets/HideAndShoot/Scenes/GameScene.unity";
var gameScene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(gamePath);
bool opened=!gameScene.IsValid() || !gameScene.isLoaded;
if(opened)gameScene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(gamePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
try
{
    if(gameScene.isDirty)throw new System.Exception("Preserve pending GameScene edits before authoring.");
    var services=gameScene.GetRootGameObjects().First(x=>x.name=="GameplayServices");
    UnityEditor.Undo.RegisterFullObjectHierarchyUndo(services,"Conceal round placement");
    var curtain=services.GetComponent<HideAndShoot.UI.RoundTransitionCurtain>()??UnityEditor.Undo.AddComponent<HideAndShoot.UI.RoundTransitionCurtain>(services);
    curtain.BakeEditableLayout();
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameScene);
    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(gameScene);
}
finally{if(opened)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(gameScene,true);}
UnityEngine.SceneManagement.SceneManager.SetActiveScene(menuScene);
return "Saved authored room browser and round curtain; original menu scene remains open.";
