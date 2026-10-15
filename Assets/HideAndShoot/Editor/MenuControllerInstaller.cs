using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using HideAndShoot.Lobby;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    internal static class MenuControllerInstaller
    {
        private const string MenuScene="Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
        static MenuControllerInstaller() { EditorApplication.delayCall += Install; }

        [MenuItem("Tools/HideAndShoot/Install Menu Controller")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Install;
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) == null) return;

            Scene loaded=SceneManager.GetActiveScene();
            Scene scene=SceneManager.GetSceneByPath(MenuScene);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(MenuScene,OpenSceneMode.Additive);
            try
            {
                GameObject canvas=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="MenuLobbyCanvas");
                if (canvas==null) throw new InvalidOperationException("Missing MenuLobbyCanvas in MenuLobbyScene.");
                if (canvas.GetComponent<MenuLobbyUI>()==null)
                {
                    canvas.AddComponent<MenuLobbyUI>();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("[HideAndShoot] MenuLobbyUI wired to MenuLobbyCanvas.");
                }
            }
            catch(Exception ex) { Debug.LogException(ex); }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                if(loaded.IsValid() && loaded.isLoaded) SceneManager.SetActiveScene(loaded);
            }
        }
    }
}
