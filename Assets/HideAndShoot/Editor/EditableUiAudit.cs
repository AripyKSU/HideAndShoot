using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HideAndShoot.Lobby;
using HideAndShoot.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class EditableUiAudit
    {
        private const string Request="Assets/HideAndShoot/QA/EditableUiAudit.request";
        private const string Report="Assets/HideAndShoot/QA/EditableUiAudit.report.txt";
        private const string MenuScene="Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
        private const string GameScene="Assets/HideAndShoot/Scenes/GameScene.unity";
        private static bool running;
        static EditableUiAudit()=>EditorApplication.update+=TryRun;
        private static void TryRun()
        {
            if(running || !File.Exists(Request) || File.Exists(Report) ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)return;
            running=true;
            EditorApplication.update-=TryRun;
            Run();
        }
        [MenuItem("Tools/HideAndShoot/UI/Audit Editable Scene UI")]
        public static void Run()
        {
            var log=new StringBuilder("Editable UI regression audit\n");
            var active=SceneManager.GetActiveScene();
            try
            {
                CheckMenu(log);
                CheckGame(log);
                log.AppendLine("PASS: Editor-authored UI can be modified in Hierarchy without runtime rebuilding.");
            }
            catch(Exception e)
            {
                log.AppendLine("FAIL: "+e);
                Debug.LogException(e);
            }
            finally
            {
                if(active.IsValid() && active.isLoaded)SceneManager.SetActiveScene(active);
                File.WriteAllText(Report,log.ToString());
                AssetDatabase.ImportAsset(Report);
                Debug.Log(log.ToString());
            }
        }

        private static void Require(bool condition,string name,StringBuilder log)
        {
            if(!condition)throw new IOException(name);
            log.AppendLine("PASS: "+name);
        }
        private static int CountObjects(Transform root)=>
            root.GetComponentsInChildren<Transform>(true).Length;
        private static void CheckMenu(StringBuilder log)
        {
            var scene=SceneManager.GetSceneByPath(MenuScene);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(MenuScene,OpenSceneMode.Additive);
            GameObject clone=null;
            try
            {
                var source=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="MenuLobbyCanvas");
                Require(source!=null,"MenuLobbyCanvas scene object exists",log);
                Require(source.GetComponentsInChildren<Text>(true).Length==0,
                    "No legacy Text in menu",log);
                Require(source.GetComponentsInChildren<TMP_InputField>(true).Length==2,
                    "Two editable TMP inputs in menu",log);
                Require(source.GetComponentsInChildren<Transform>(true).Count(
                    x=>x.name.StartsWith("Member") && x.name.Length==7)==8,
                    "Eight authored room roster rows",log);
                clone=UnityEngine.Object.Instantiate(source);
                clone.hideFlags=HideFlags.HideAndDontSave;
                var ui=clone.GetComponent<MenuLobbyUI>();
                int initial=CountObjects(clone.transform);
                ui.InitializeUI();
                Require(CountObjects(clone.transform)==initial,
                    "InitializeUI does not generate GameObjects",log);
                var activeStates=clone.transform.GetComponentsInChildren<Transform>(true)
                    .Where(t=>t.name=="ConnectionHint" || t.name=="CreateRoomButton" ||
                        t.name=="RoomCodeInput").ToDictionary(t=>t.name,t=>t.gameObject.activeSelf);
                for(int i=0;i<3;i++)
                {
                    ui.DisplayRoom("AB12CD",true);
                    ui.HideRoom();
                    Require(CountObjects(clone.transform)==initial,
                        "Three cycles of room enter/leave never rebuild UI: cycle "+(i+1),log);
                    foreach(var child in activeStates)
                    {
                        var found=clone.GetComponentsInChildren<Transform>(true)
                            .First(x=>x.name==child.Key);
                        Require(found.gameObject.activeSelf==child.Value,
                            child.Key+" visibility restored after cycle "+(i+1),log);
                    }
                }
            }
            finally
            {
                if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
            }
        }
        private static void CheckGame(StringBuilder log)
        {
            var scene=SceneManager.GetSceneByPath(GameScene);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(GameScene,OpenSceneMode.Additive);
            try
            {
                var services=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="GameplayServices");
                Require(services!=null,"GameplayServices exists",log);
                var hud=services.transform.Find("GameHUD_Canvas");
                var modal=services.transform.Find("RoundMenuCanvas");
                var podium=services.transform.Find("PodiumOverlay");
                Require(hud!=null && hud.GetComponent<Canvas>()!=null,
                    "Editable HUD canvas saved as a scene object",log);
                Require(modal!=null && modal.GetComponent<Canvas>()!=null,
                    "Editable round menu saved as a scene object",log);
                Require(podium!=null && podium.GetComponent<Canvas>()!=null,
                    "Editable podium overlay saved as a scene object",log);
                Require(scene.GetRootGameObjects().All(x=>x.name!="GameHUDCanvas"),
                    "Original prototype HUD removed",log);
                Require(scene.GetRootGameObjects().All(x=>
                    x.GetComponentsInChildren<Text>(true).Length==0),
                    "No legacy Text remains in GameScene",log);

                var presenter=services.GetComponent<GameHUDPresenter>();
                Require(presenter!=null,"GameHUDPresenter bound to authored HUD",log);
                var binding=presenter.GetType().GetMethod("BindAuthoredUI",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                int initial=CountObjects(services.transform);
                binding?.Invoke(presenter,null);
                Require(CountObjects(services.transform)==initial,
                    "HUD references bind without creating any UI GameObjects",log);
                var field=presenter.GetType().GetField("canvas",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                Require(field!=null && field.GetValue(presenter)!=null,
                    "HUD labels and indicators are wired correctly",log);
                Require(services.GetComponent<RoundMenuPresenter>()!=null &&
                    services.GetComponent<MatchPodiumPresenter>()!=null,
                    "Round menu and podium presenters exist",log);
            }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
}
