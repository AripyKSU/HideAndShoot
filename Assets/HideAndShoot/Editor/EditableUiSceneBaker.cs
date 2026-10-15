using System;
using System.IO;
using System.Linq;
using System.Text;
using HideAndShoot.Lobby;
using HideAndShoot.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// One-time conversion of the existing runtime-generated UI to scene-owned
    /// GameObjects. Future edits happen in Scene/Hierarchy/Inspector, NOT by
    /// rerunning the layout builder at runtime. Never overwrites baked layouts
    /// automatically after the completion marker exists.
    /// </summary>
    [InitializeOnLoad]
    public static class EditableUiSceneBaker
    {
        private const string MenuPath="Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
        private const string GamePath="Assets/HideAndShoot/Scenes/GameScene.unity";
        private const string Request="Assets/HideAndShoot/QA/EditableUiSceneBake.request";
        private const string Complete="Assets/HideAndShoot/QA/EditableUiSceneBake.complete.txt";
        private const string NicknameRequest="Assets/HideAndShoot/QA/EditableRosterAutosize.request";
        private const string NicknameDone="Assets/HideAndShoot/QA/EditableRosterAutosize.complete.txt";
        private static bool working;

        static EditableUiSceneBaker()=>EditorApplication.update+=TryBake;

        private static void TryBake()
        {
            if(working || EditorApplication.isCompiling || EditorApplication.isUpdating ||
               EditorApplication.isPlayingOrWillChangePlaymode)return;
            bool initial=File.Exists(Request) && !File.Exists(Complete);
            bool names=File.Exists(NicknameRequest) && !File.Exists(NicknameDone);
            if(!initial && !names)return;
            working=true;
            EditorApplication.update-=TryBake;
            try{if(initial)Bake(); if(names)BakeNicknameAutosizing();}
            catch(Exception ex){Debug.LogException(ex);working=false;}
        }

        [MenuItem("Tools/HideAndShoot/UI/Auto-size Editable Roster Names")]
        public static void BakeNicknameAutosizing()
        {
            var scene=SceneManager.GetSceneByPath(MenuPath);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(MenuPath,OpenSceneMode.Additive);
            int changed=0;
            try
            {
                var canvas=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="MenuLobbyCanvas");
                if(canvas==null)throw new IOException("MenuLobbyCanvas missing.");
                for(int i=0;i<8;i++)
                {
                    var row=canvas.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t=>t.name=="Member"+i);
                    var label=row?.Find("Nickname")?.GetComponent<TextMeshProUGUI>();
                    if(label==null)throw new IOException("Missing roster label "+i);
                    label.enableAutoSizing=true;
                    label.fontSizeMin=12;
                    label.fontSizeMax=20;
                    label.enableWordWrapping=false;
                    EditorUtility.SetDirty(label);
                    changed++;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene,MenuPath))
                    throw new IOException("Failed to save autosized roster labels.");
                File.WriteAllText(NicknameDone,
                    "Authored TMP labels with autosizing: "+changed+"\n");
                AssetDatabase.ImportAsset(NicknameDone);
                Debug.Log("[HideAndShoot/UI] Saved 8 editable nickname auto-size settings.");
            }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
            }
        }

        [MenuItem("Tools/HideAndShoot/UI/Bake Editable Scene UI")]
        public static void Bake()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating ||
               EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bake editable UI in edit mode only.");
            var active=SceneManager.GetActiveScene();
            var report=new StringBuilder("Editable UI authoring report\n");
            try
            {
                BakeMenu(report);
                BakeGame(report);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Complete,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+
                    "\n"+report);
                AssetDatabase.ImportAsset(Complete);
                Debug.Log("[HideAndShoot/UI] Authored scene UI saved.\n"+report);
            }
            finally
            {
                if(active.IsValid() && active.isLoaded)
                    SceneManager.SetActiveScene(active);
            }
        }

        private static void BakeMenu(StringBuilder report)
        {
            var scene=SceneManager.GetSceneByPath(MenuPath);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(MenuPath,OpenSceneMode.Additive);
            try
            {
                var root=scene.GetRootGameObjects().FirstOrDefault(go=>go.name=="MenuLobbyCanvas");
                if(root==null)throw new IOException("Missing MenuLobbyCanvas in "+MenuPath);
                var presenter=root.GetComponent<MenuLobbyUI>();
                if(presenter==null)throw new IOException("MenuLobbyUI component missing.");
                if(root.transform.Find("MenuVignette")==null ||
                   root.transform.Find("MenuTopRule")==null ||
                   root.GetComponentsInChildren<Transform>(true).All(t=>t.name!="Member7"))
                    presenter.BakeEditableLayout();

                // These disabled prototypes were retained from the first
                // procedural UI. Remove them so the Hierarchy shows only the
                // designed editable interface, including all 8 roster rows.
                foreach(var name in new[]{"RulesTitle","RulesBody","ConnectedPlayers","PingColumn"})
                {
                    var old=root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t=>t.name==name);
                    if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                }
                AssertComponent<TMP_InputField>(root.transform,"NicknameInput");
                AssertComponent<TMP_InputField>(root.transform,"RoomCodeInput");
                AssertComponent<Button>(root.transform,"LeaveRoomButton");
                AssertComponent<TextMeshProUGUI>(root.transform,"RoomCodeSameRow");
                for(int i=0;i<8;i++)AssertTransform(root.transform,"Member"+i);
                if(root.GetComponentsInChildren<Text>(true).Length!=0)
                    throw new IOException("Legacy Unity UI.Text remains in menu.");
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene,MenuPath))
                    throw new IOException("Could not save "+MenuPath);
                report.AppendLine("MenuLobbyScene: editable main menu, room panel, buttons and 8 roster rows");
            }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
            }
        }

        private static void BakeGame(StringBuilder report)
        {
            var scene=SceneManager.GetSceneByPath(GamePath);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(GamePath,OpenSceneMode.Additive);
            try
            {
                var services=scene.GetRootGameObjects().FirstOrDefault(go=>go.name=="GameplayServices");
                if(services==null)
                {
                    services=new GameObject("GameplayServices");
                    SceneManager.MoveGameObjectToScene(services,scene);
                }

                // Delete only the original scene's static prototype HUD, not
                // the authored GameHUD_Canvas created below.
                var legacy=scene.GetRootGameObjects().FirstOrDefault(go=>go.name=="GameHUDCanvas");
                if(legacy!=null)UnityEngine.Object.DestroyImmediate(legacy);

                var hud=services.GetComponent<GameHUDPresenter>();
                if(hud==null)hud=services.AddComponent<GameHUDPresenter>();
                hud.BakeEditableLayout();
                var roundMenu=services.GetComponent<RoundMenuPresenter>();
                if(roundMenu==null)roundMenu=services.AddComponent<RoundMenuPresenter>();
                if(services.transform.Find("RoundMenuCanvas")==null)roundMenu.Build();
                var podium=services.GetComponent<MatchPodiumPresenter>();
                if(podium==null)podium=services.AddComponent<MatchPodiumPresenter>();
                podium.BakeEditableOverlay();

                // An EventSystem is a scene object, not transient UI created
                // every time ESC is opened.
                if(scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EventSystem>(true))
                    .FirstOrDefault()==null)
                {
                    var eventSystem=new GameObject("GameUIEventSystem",typeof(EventSystem),
                        typeof(InputSystemUIInputModule));
                    SceneManager.MoveGameObjectToScene(eventSystem,scene);
                }

                var h=AssertTransform(services.transform,"GameHUD_Canvas");
                AssertTransform(h,"RankedScore");
                AssertTransform(h,"AliveCount");
                AssertTransform(h,"ADSCrosshair");
                AssertTransform(h,"Ammo");
                AssertTransform(h,"RoundCountdown");
                var modal=AssertTransform(services.transform,"RoundMenuCanvas");
                AssertComponent<Button>(modal,"RedoVoteButton");
                AssertComponent<Button>(modal,"ResumeButton");
                var result=AssertTransform(services.transform,"PodiumOverlay");
                for(int i=1;i<=3;i++)AssertTransform(result,"Rank_"+i);
                if(scene.GetRootGameObjects()
                    .Any(root=>root.GetComponentsInChildren<Text>(true).Length>0))
                    throw new IOException("Legacy Unity UI.Text remains in GameScene.");
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene,GamePath))
                    throw new IOException("Could not save "+GamePath);
                report.AppendLine("GameScene: editable GameHUD_Canvas, RoundMenuCanvas, PodiumOverlay, GameUIEventSystem");
            }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
            }
        }

        private static Transform AssertTransform(Transform root,string name)
        {
            var item=root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t=>t.name==name);
            if(item==null)throw new IOException("Missing editable UI object: "+name);
            return item;
        }
        private static T AssertComponent<T>(Transform root,string name) where T:Component
        {
            var item=AssertTransform(root,name);
            var component=item.GetComponent<T>();
            if(component==null)
                throw new IOException("Missing "+typeof(T).Name+" on "+name);
            return component;
        }
    }
}
