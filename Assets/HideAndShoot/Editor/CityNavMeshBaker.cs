using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Ground-aware NavMesh baking for the 72m city. Keep the two intentionally
    /// accessible ramp rooftops, but don't bake ordinary building rooftops or
    /// decorative tree canopies. Manual rebake remains available via the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class CityNavMeshBaker
    {
        private const string ScenePath="Assets/HideAndShoot/Scenes/GameScene.unity";
        private const string Marker="Assets/HideAndShoot/Scenes/CityNavMeshBake.complete.txt";
        private const string RefreshRequest="Assets/HideAndShoot/QA/CityNavMeshTreeInstances_v2.request";
        private const string RefreshComplete="Assets/HideAndShoot/QA/CityNavMeshTreeInstances_v2.complete.txt";
        private static bool attempted;
        static CityNavMeshBaker() => EditorApplication.update += TryBake;

        private static void TryBake()
        {
            if (attempted || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath)) return;

            attempted=true;
            EditorApplication.update -= TryBake;
            bool requested=File.Exists(RefreshRequest) && !File.Exists(RefreshComplete);
            if(File.Exists(Marker) && !requested)return;
            try { Bake(); }
            catch(Exception ex) { Debug.LogException(ex); }
        }

        [MenuItem("Tools/HideAndShoot/Bake City NavMesh")]
        public static void Bake()
        {
            var active=SceneManager.GetActiveScene();
            Scene game=SceneManager.GetSceneByPath(ScenePath);
            bool opened=!game.IsValid() || !game.isLoaded;
            if(opened)game=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            try
            {
                var city=game.GetRootGameObjects().FirstOrDefault(root=>
                    root.name=="CityWorld_72x72_PrototypeLayout");
                NavMeshSurface surface=city!=null?city.GetComponent<NavMeshSurface>():null;
                if(surface==null)
                    throw new InvalidOperationException("CityWorld NavMeshSurface not found.");

                if(EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("NavMesh rebake is edit-mode only.");
                var roofColliders=new List<BoxCollider>();
                int blockedBuildings=0,treeObstacles=0;
                int nonWalkable=NavMesh.GetAreaFromName("Not Walkable");
                if(nonWalkable<0)
                    throw new InvalidOperationException("NavMesh 'Not Walkable' area is unavailable.");

                // Ordinary Kenney buildings have a bounding BoxCollider on their
                // root. Marking the roof-producing collider Not Walkable keeps its
                // physical obstacle without making the box's top a valid floor.
                // Do not alter NorthWestRoof_A / SouthEastRoof_B or their ramps.
                foreach(Transform child in city.transform)
                {
                    if(child.name=="MapGround" || child.name.StartsWith("Road_") ||
                       child.name=="CentralPark" || child.name=="NorthWestRoof_A" ||
                       child.name=="SouthEastRoof_B")
                        continue;

                    if(child.name.StartsWith("Boundary",StringComparison.Ordinal))
                    {
                        SetNonWalkable(child.gameObject,nonWalkable);
                        continue;
                    }

                    var collider=child.GetComponent<BoxCollider>();
                    if(collider!=null && !IsTreeName(child.name) &&
                       child.GetComponentInChildren<Renderer>(true)!=null)
                    {
                        SetNonWalkable(child.gameObject,nonWalkable);
                        roofColliders.Add(collider);
                        blockedBuildings++;
                    }
                }
                // Each mesh-model tree gets its own trunk footprint. Users can
                // duplicate and move tree prefabs inside an existing StreetTree
                // group; placing one volume on the parent group would be wrong.
                var treeRoots=new HashSet<Transform>();
                foreach(var renderer in city.GetComponentsInChildren<Renderer>(true))
                {
                    Transform root=renderer.transform;
                    while(root!=null && root!=city.transform && !IsTreeName(root.name))
                        root=root.parent;
                    if(root!=null && root!=city.transform && treeRoots.Add(root))
                    {
                        AddTreeObstacle(root,nonWalkable);
                        treeObstacles++;
                    }
                }

                surface.collectObjects=CollectObjects.Children;
                // Imported FBX render meshes can create walkable polygons on
                // decorative roofs/canopies. Use authored solid colliders instead.
                surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
                surface.BuildNavMesh();
                if(surface.navMeshData==null)
                    throw new InvalidOperationException("NavMeshSurface.BuildNavMesh did not create NavMeshData.");

                int roofLeaks=0;
                foreach(var roof in roofColliders)
                {
                    var bounds=roof.bounds;
                    var probe=new Vector3(bounds.center.x,bounds.max.y+.08f,bounds.center.z);
                    if(NavMesh.SamplePosition(probe,out var hit,.8f,NavMesh.AllAreas) &&
                       Mathf.Abs(hit.position.y-bounds.max.y)<.8f)
                    {
                        roofLeaks++;
                        Debug.LogError("[HideAndShoot/NavMesh] Unwanted roof still walkable: "+roof.name+
                            " (parent "+roof.transform.parent.name+"), hit="+hit.position);
                    }
                }
                if(roofLeaks>0)
                    throw new InvalidOperationException(roofLeaks+" unintended rooftops remain walkable.");

                int validGroundSpawns=0;
                var anchors=game.GetRootGameObjects().FirstOrDefault(r=>r.name=="GameplayAnchors");
                if(anchors!=null)
                {
                    foreach(var anchor in anchors.GetComponentsInChildren<WorldAnchor>(true))
                    {
                        if(anchor.Kind!=WorldAnchor.AnchorKind.PlayerSpawn)continue;
                        if(NavMesh.SamplePosition(anchor.transform.position,out var hit,1.5f,NavMesh.AllAreas) &&
                           Mathf.Abs(hit.position.y-anchor.transform.position.y)<1.0f)
                            validGroundSpawns++;
                    }
                }
                if(validGroundSpawns!=9)
                    throw new InvalidOperationException("Only "+validGroundSpawns+"/9 player ground spawn anchors have walkable NavMesh.");

                EditorSceneManager.MarkSceneDirty(game);
                if(!EditorSceneManager.SaveScene(game,ScenePath))
                    throw new InvalidOperationException("Unable to save baked NavMesh scene.");

                string result="City NavMesh baked "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+
                    "\nNavMeshData="+surface.navMeshData.name+
                    "\nScene="+ScenePath+
                    "\nGeometry=PhysicsColliders"+
                    "\nOrdinaryBuildingRoofsBlocked="+blockedBuildings+
                    "\nTreeTrunksBlocked="+treeObstacles+
                    "\nUnintendedRoofLeaks="+roofLeaks+
                    "\nGroundSpawnAnchors="+validGroundSpawns+"/9"+
                    "\nIntentionalRooftops=NorthWestRoof_A,SouthEastRoof_B (preserved)\n";
                File.WriteAllText(Marker,result);
                AssetDatabase.ImportAsset(Marker);
                if(File.Exists(RefreshRequest))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RefreshComplete));
                    File.WriteAllText(RefreshComplete,result);
                    AssetDatabase.ImportAsset(RefreshComplete);
                }
                Debug.Log("[HideAndShoot] City NavMesh baked and GameScene saved.");
            }
            finally
            {
                if(opened && game.IsValid() && game.isLoaded)
                    EditorSceneManager.CloseScene(game,true);
                if(active.IsValid() && active.isLoaded)
                    SceneManager.SetActiveScene(active);
            }
        }

        private static bool IsTreeName(string name)
        {
            return name.IndexOf("tree",StringComparison.OrdinalIgnoreCase)>=0 &&
                   !name.Equals("Trees",StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("TreeGroup",StringComparison.OrdinalIgnoreCase);
        }

        private static void SetNonWalkable(GameObject obj,int area)
        {
            var modifier=obj.GetComponent<NavMeshModifier>();
            if(modifier==null)modifier=obj.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild=false;
            modifier.overrideArea=true;
            modifier.area=area;
            modifier.applyToChildren=true;
        }

        private static void AddTreeObstacle(Transform root,int area)
        {
            // Tree crowns and imported mesh colliders cannot provide walkable
            // surfaces. The ground-level trunk hole remains as an obstacle.
            var modifier=root.GetComponent<NavMeshModifier>();
            if(modifier==null)modifier=root.gameObject.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild=true;
            modifier.applyToChildren=true;
            var volume=root.GetComponent<NavMeshModifierVolume>();
            if(volume==null)volume=root.gameObject.AddComponent<NavMeshModifierVolume>();
            volume.area=area;
            // These are world-meter trunk dimensions, even when the imported
            // tree prefab carries an FBX scale of 3x-5x.
            var scale=root.lossyScale;
            float x=Mathf.Max(Mathf.Abs(scale.x),.01f);
            float y=Mathf.Max(Mathf.Abs(scale.y),.01f);
            float z=Mathf.Max(Mathf.Abs(scale.z),.01f);
            volume.center=new Vector3(0f,1f/y,0f);
            volume.size=new Vector3(1.2f/x,3f/y,1.2f/z);
        }
    }
}
