using System;
using System.IO;
using System.Linq;
using HideAndShoot.Player;
using HideAndShoot.NPC;
using HideAndShoot.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class NpcPrefabInstaller
    {
        private const string NpcPrefab="Assets/HideAndShoot/Prefabs/NPCActor.prefab";
        private const string AmmoPrefab="Assets/HideAndShoot/Prefabs/AmmoPickup.prefab";
        private const string GameScene="Assets/HideAndShoot/Scenes/GameScene.unity";
        static NpcPrefabInstaller() => EditorApplication.update += TryInstall;
        private static void TryInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= TryInstall;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Install NPC Wander Brain")]
        public static void Install()
        {
            if(AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefab)==null)return;
            var npc=PrefabUtility.LoadPrefabContents(NpcPrefab);
            try
            {
                bool npcChanged=false;
                if(npc.GetComponent<NpcWanderBrain>()==null)
                {
                    npc.AddComponent<NpcWanderBrain>();
                    npcChanged=true;
                }
                if(npc.GetComponent<RemoteMotionAnimator>()==null)
                {
                    npc.AddComponent<RemoteMotionAnimator>();
                    npcChanged=true;
                }
                if(npcChanged)PrefabUtility.SaveAsPrefabAsset(npc,NpcPrefab);
            }
            finally{PrefabUtility.UnloadPrefabContents(npc);}

            if(AssetDatabase.LoadAssetAtPath<GameObject>(AmmoPrefab)!=null)
            {
                var ammo=PrefabUtility.LoadPrefabContents(AmmoPrefab);
                try
                {
                    bool dirty=false;
                    if(ammo.GetComponent<AmmoPickupActor>()==null)
                    {
                        ammo.AddComponent<AmmoPickupActor>();
                        dirty=true;
                    }
                    var body=ammo.GetComponent<Rigidbody>();
                    if(body==null)
                    {
                        body=ammo.AddComponent<Rigidbody>();
                        dirty=true;
                    }
                    if(body.useGravity || !body.isKinematic)
                    {
                        body.useGravity=false;
                        body.isKinematic=true;
                        dirty=true;
                    }
                    if(dirty)PrefabUtility.SaveAsPrefabAsset(ammo,AmmoPrefab);
                }
                finally{PrefabUtility.UnloadPrefabContents(ammo);}
            }

            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene)==null)return;
            var scene=EditorSceneManager.OpenScene(GameScene,OpenSceneMode.Additive);
            try
            {
                var services=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="GameplayServices");
                if(services==null)
                {
                    services=new GameObject("GameplayServices");
                    SceneManager.MoveGameObjectToScene(services,scene);
                }
                bool updated=false;
                if(services.GetComponent<NpcZonePopulation>()==null)
                {
                    services.AddComponent<NpcZonePopulation>();
                    updated=true;
                }
                if(services.GetComponent<NpcPoolCoordinator>()==null)
                {
                    services.AddComponent<NpcPoolCoordinator>();
                    updated=true;
                }
                if(services.GetComponent<AmmoSpawnDirector>()==null)
                {
                    services.AddComponent<AmmoSpawnDirector>();
                    updated=true;
                }
                if(updated)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally {EditorSceneManager.CloseScene(scene,true);}
            Debug.Log("[HideAndShoot] NPC pool, wandering AI, density zones and ammo director configured.");
        }
    }
}
