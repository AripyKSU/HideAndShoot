using TMPro;
using System;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// One-time targeted polish of existing authored scenes.
    /// Does not rebuild or overwrite the world, preserves spawn anchors and all unrelated content.
    /// Runs after recompilation only when scene edits are safe.
    /// </summary>
    [InitializeOnLoad]
    public static class UiWorldPolishPass
    {
        private const string MenuPath = "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
        private const string GamePath = "Assets/HideAndShoot/Scenes/GameScene.unity";
        private const string Completed = "Assets/HideAndShoot/QA/UI_World_Polish_v1.applied";
        private const string Nature = "Assets/HideAndShoot/ThirdParty/Kenney/NatureKit/Models/";

        private static bool attempted;
        static UiWorldPolishPass() => EditorApplication.update += TryApply;

        private static void TryApply()
        {
            if(attempted || EditorApplication.isCompiling || EditorApplication.isUpdating ||
               EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(Completed))
                return;
            attempted=true;
            EditorApplication.update-=TryApply;
            Apply();
        }

        [MenuItem("Tools/HideAndShoot/Apply UI and Rooftop Polish")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
               EditorApplication.isUpdating)
            {
                Debug.LogWarning("[HideAndShoot] Finish Play/compilation before polishing scenes.");
                return;
            }
            if(File.Exists(Completed))
            {
                Debug.Log("[HideAndShoot] Requested UI/world polish has already been applied.");
                return;
            }

            Scene active=SceneManager.GetActiveScene();
            if(active.IsValid() && active.isDirty)
            {
                Debug.LogWarning("[HideAndShoot] Active scene has unsaved edits; polish postponed.");
                attempted=false;
                EditorApplication.update+=TryApply;
                return;
            }

            EditScene(MenuPath, ApplyMenu);
            EditScene(GamePath, ApplyWorld);

            File.WriteAllText(Completed,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+
                "\nPolished approved menu UI and both rooftop ramps; added 12 street trees and 6 shrubs.\n"+
                "NavMesh rebuilt (path connectivity logged separately).\n");
            AssetDatabase.ImportAsset(Completed);
            Debug.Log("[HideAndShoot] UI and rooftop polish applied; editor left running.");
        }

        private static void EditScene(string path,Action<Scene> action)
        {
            var scene=SceneManager.GetSceneByPath(path);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                action(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene,path))
                    throw new IOException("Could not save "+path);
            }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
            }
        }

        private static Transform Find(Transform root,string objectName)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t=>t.name==objectName);
        }

        private static GameObject Root(Scene scene,string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(g=>g.name==name);
        }

        private static void ApplyMenu(Scene scene)
        {
            var canvas=Root(scene,"MenuLobbyCanvas");
            if(canvas==null)throw new InvalidOperationException("MenuLobbyCanvas not found.");
            var left=Find(canvas.transform,"RulesPanel");
            var right=Find(canvas.transform,"MenuControlsPanel");
            if(left==null || right==null)
                throw new InvalidOperationException("Menu panels not found.");

            var title=Find(left,"RulesTitle")?.GetComponent<TextMeshProUGUI>();
            if(title!=null) title.text="조작 방법";
            var rules=Find(left,"RulesBody")?.GetComponent<TextMeshProUGUI>();
            if(rules!=null)
            {
                rules.text="WASD   이동\nShift   달리기\n오른쪽 클릭   조준\n왼쪽 클릭   사격\n\n"+
                    "NPC 사이에 숨은 상대를 찾아\n마지막까지 살아남으세요.\n\n"+
                    "3승을 먼저 거두면 우승!";
                rules.fontSize=23;
                rules.lineSpacing=1.35f;
                rules.alignment=HideAndShoot.UI.TacticalUI.Align(TextAnchor.MiddleLeft);
                var rt=(RectTransform)rules.transform;
                rt.sizeDelta=new Vector2(415,420);
                rt.anchoredPosition=new Vector2(0,-3);
            }

            var line=Find(right,"NicknameDivider");
            if(line==null)
            {
                var go=new GameObject("NicknameDivider",typeof(RectTransform),typeof(Image));
                go.transform.SetParent(right,false);
                var rect=(RectTransform)go.transform;
                rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
                rect.sizeDelta=new Vector2(332,2);
                rect.anchoredPosition=new Vector2(0,98);
                var img=go.GetComponent<Image>();
                img.color=new Color(.60f,.67f,.68f,1f);
                img.raycastTarget=false;
                go.transform.SetSiblingIndex(Mathf.Min(right.childCount-1,3));
            }

            var room=Find(right,"RoomOverlay");
            if(room==null) throw new InvalidOperationException("RoomOverlay not found.");
            var background=room.GetComponent<Image>();
            if(background==null)
                throw new InvalidOperationException("RoomOverlay Image not found.");
            background.color=new Color(.08f,.13f,.15f,1f);  // absolutely opaque
            background.raycastTarget=true;
            room.SetAsLastSibling();

            var status=Find(right,"NetworkSetupHint");
            if(status!=null)
            {
                status.name="MenuStatusText";
                var txt=status.GetComponent<TextMeshProUGUI>();
                if(txt!=null) txt.text=string.Empty; // no developer copy shown to players
            }
            Debug.Log("[HideAndShoot] Menu: short controls, nickname divider, opaque room overlay, developer hint removed.");
        }

        private static void ApplyWorld(Scene scene)
        {
            var city=Root(scene,"CityWorld_72x72_PrototypeLayout");
            if(city==null)throw new InvalidOperationException("CityWorld root not found.");

            ApplyRamp(city.transform,"NorthWestRoof_A",false);
            ApplyRamp(city.transform,"SouthEastRoof_B",true);

            var treePositions=new[]
            {
                new Vector3(-31,0,5), new Vector3(-24,0,30),
                new Vector3(-8,0,31), new Vector3(8,0,32),
                new Vector3(32,0,7), new Vector3(31,0,-8),
                new Vector3(8,0,-32), new Vector3(-8,0,-31),
                new Vector3(-31,0,-5), new Vector3(-24,0,-30),
                new Vector3(24,0,31), new Vector3(24,0,-31)
            };
            for(int i=0;i<treePositions.Length;i++)
            {
                string name="StreetTree_"+(i+1).ToString("00");
                AddKenneyDecoration(city.transform,name,treePositions[i],
                    i%2==0 ? "tree_default.fbx" : "tree_cone.fbx",3.15f);
            }
            var shrubs=new[]
            {
                new Vector3(-7,0,8),new Vector3(7,0,8),
                new Vector3(-7,0,-8),new Vector3(7,0,-8),
                new Vector3(30,0,0),new Vector3(-30,0,0)
            };
            for(int i=0;i<shrubs.Length;i++)
                AddKenneyDecoration(city.transform,"ParkShrub_"+(i+1).ToString("00"),
                    shrubs[i],"plant_bushSmall.fbx",1.25f);

            var surface=city.GetComponent<NavMeshSurface>();
            if(surface==null)throw new InvalidOperationException("NavMeshSurface missing from city.");
            surface.BuildNavMesh();
            if(surface.navMeshData==null)
                throw new InvalidOperationException("NavMeshData was not built.");

            CheckRooftopPath("NW",new Vector3(-15,0.15f,3.2f),new Vector3(-15,3.6f,15));
            CheckRooftopPath("SE",new Vector3(16,0.15f,-3.2f),new Vector3(16,3.6f,-15));

            Debug.Log("[HideAndShoot] World: two ramps redirected clear of adjacent buildings, 12 trees/6 shrubs added; NavMesh rebuilt.");
        }

        private static void ApplyRamp(Transform city,string name,bool approachFromNorth)
        {
            var site=Find(city,name);
            var ramp=site==null?null:Find(site,"WalkableRamp");
            if(ramp==null)throw new InvalidOperationException(name+" / WalkableRamp missing.");

            ramp.localPosition=new Vector3(0,1.70f,approachFromNorth?7.5f:-7.5f);
            ramp.localRotation=Quaternion.Euler(approachFromNorth?24.6f:-24.6f,0,0);
            ramp.localScale=new Vector3(2.5f,.28f,7.75f);
            ramp.gameObject.isStatic=true;
        }

        private static void AddKenneyDecoration(Transform city,string name,Vector3 location,
            string file,float targetWidth)
        {
            if(Find(city,name)!=null)return;
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Nature+file);
            if(model==null)throw new IOException("Decoration model missing: "+file);
            var root=new GameObject(name);
            root.transform.SetParent(city,false);
            root.transform.localPosition=location;
            var instance=PrefabUtility.InstantiatePrefab(model,root.transform) as GameObject;
            if(instance==null)throw new InvalidOperationException("Cannot spawn "+file);
            instance.transform.localPosition=Vector3.zero;
            instance.transform.localRotation=Quaternion.identity;
            var renderer=instance.GetComponentsInChildren<Renderer>();
            if(renderer.Length==0)throw new InvalidOperationException(file+" has no visible mesh");
            Bounds bounds=renderer[0].bounds;
            for(int i=1;i<renderer.Length;i++)bounds.Encapsulate(renderer[i].bounds);
            float width=Mathf.Max(bounds.size.x,bounds.size.z,.01f);
            instance.transform.localScale*=Mathf.Clamp(targetWidth/width,.05f,100f);
            renderer=instance.GetComponentsInChildren<Renderer>();
            bounds=renderer[0].bounds;
            for(int i=1;i<renderer.Length;i++)bounds.Encapsulate(renderer[i].bounds);
            instance.transform.position+=Vector3.up * (0.08f-bounds.min.y);

            // Decoration has no colliders and is omitted from the host physics hitboxes.
        }

        private static void CheckRooftopPath(string label,Vector3 foot,Vector3 top)
        {
            if(!NavMesh.SamplePosition(foot,out NavMeshHit from,.95f,NavMesh.AllAreas) ||
               !NavMesh.SamplePosition(top,out NavMeshHit to,1.0f,NavMesh.AllAreas))
            {
                Debug.LogWarning("[HideAndShoot] "+label+" roof NavMesh endpoint not found; inspect bake settings.");
                return;
            }
            var path=new NavMeshPath();
            bool found=NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,path);
            if(found && path.status==NavMeshPathStatus.PathComplete)
                Debug.Log("[HideAndShoot] "+label+" ramp NavMesh PathComplete.");
            else
                Debug.LogWarning("[HideAndShoot] "+label+" ramp NavMesh path incomplete. Inspect approach.");
        }
    }
}
