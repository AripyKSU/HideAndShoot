using System;
using System.IO;
using System.Linq;
using HideAndShoot.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// One-time integration for NGO runtime prefab registration and match scene
    /// component references. Modifies only generated HideAndShoot files.
    /// </summary>
    [InitializeOnLoad]
    public static class GameplaySceneInstaller
    {
        private const string CatalogPath="Assets/HideAndShoot/Resources/NetworkPrefabCatalog.asset";
        private const string GameScenePath="Assets/HideAndShoot/Scenes/GameScene.unity";
        private const string Prefabs="Assets/HideAndShoot/Prefabs/";

        static GameplaySceneInstaller() => EditorApplication.update += TryInstall;

        private static void TryInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= TryInstall;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Install Gameplay Runtime Bootstrap")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode ||
               EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Install;
                return;
            }

            if(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"PlayerAvatar.prefab")==null)
                return;

            Directory.CreateDirectory("Assets/HideAndShoot/Resources");
            var catalog=AssetDatabase.LoadAssetAtPath<NetworkPrefabCatalog>(CatalogPath);
            if(catalog==null)
            {
                catalog=ScriptableObject.CreateInstance<NetworkPrefabCatalog>();
                AssetDatabase.CreateAsset(catalog,CatalogPath);
            }

            var player=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"PlayerAvatar.prefab");
            var npc=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"NPCActor.prefab");
            var ammo=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"AmmoPickup.prefab");
            bool dirtyCatalog=catalog.PlayerAvatar!=player ||
                catalog.NPCActor!=npc || catalog.AmmoPickup!=ammo;
            if(dirtyCatalog)
            {
                catalog.PlayerAvatar=player;
                catalog.NPCActor=npc;
                catalog.AmmoPickup=ammo;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath)==null)return;
            Scene originallyActive=SceneManager.GetActiveScene();
            var scene=SceneManager.GetSceneByPath(GameScenePath);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(GameScenePath,OpenSceneMode.Additive);
            bool changed=false;
            try
            {
                var anchors=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="GameplayAnchors");
                if(anchors==null)
                    throw new InvalidOperationException("GameplayAnchors missing: GameScene structure has changed.");
                if(InitialSceneBuilder.ApplySpawnFacing(anchors))changed=true;
                var registry=anchors.GetComponent<SpawnPointRegistry>();
                if(registry==null)
                {
                    registry=anchors.AddComponent<SpawnPointRegistry>();
                    changed=true;
                }

                var services=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="GameplayServices");
                if(services==null)
                {
                    services=new GameObject("GameplayServices");
                    SceneManager.MoveGameObjectToScene(services,scene);
                    changed=true;
                }
                var bootstrap=services.GetComponent<MatchAvatarBootstrap>();
                if(bootstrap==null)
                {
                    bootstrap=services.AddComponent<MatchAvatarBootstrap>();
                    changed=true;
                }

                var serialized=new SerializedObject(bootstrap);
                var field=serialized.FindProperty("spawnPoints");
                if(field!=null && field.objectReferenceValue!=registry)
                {
                    field.objectReferenceValue=registry;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed=true;
                }
                if(changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Debug.Log("[HideAndShoot] Gameplay catalog and match-scene spawn bootstrap installed.");
            }
            catch(Exception ex) {Debug.LogException(ex);}
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
                if(originallyActive.IsValid() && originallyActive.isLoaded)
                    SceneManager.SetActiveScene(originallyActive);
            }
        }
    }
}
