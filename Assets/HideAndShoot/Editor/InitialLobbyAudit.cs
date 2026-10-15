using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Text;
using HideAndShoot.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HideAndShoot.EditorTools
{
    /// <summary>Non-destructive edit-mode verification. Does not enter Play or touch current saved scene.</summary>
    internal static class InitialLobbyAudit
    {
        private const string MenuPath = "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
        private const string GamePath = "Assets/HideAndShoot/Scenes/GameScene.unity";
        private const string ReportPath = "Assets/HideAndShoot/QA/LobbyEditModeAudit.txt";
        [MenuItem("Tools/HideAndShoot/QA/Verify Lobby Edit Mode")]
        public static void Run()
        {
            var report = new StringBuilder("HideAndShoot / Lobby edit-mode verification\n");
            report.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            int passed=0,failed=0;
            Action<bool,string> check = (ok,description) =>
            {
                report.AppendLine((ok?"PASS: ":"FAIL: ")+description);
                if(ok)passed++;else failed++;
            };

            check(NicknameRules.Adjectives.Length==20 && NicknameRules.Animals.Length==20,
                "nickname tables are 20 x 20");
            check(!NicknameRules.TryNormalize(null,out _,out _) &&
                  !NicknameRules.TryNormalize("   ",out _,out _),
                "empty/whitespace nickname is rejected");
            check(NicknameRules.TryNormalize("Custom Name",out string name,out _) && name=="Custom Name",
                "user-defined nickname accepted");
            check(NicknameRules.Generate().Length>0, "generated default nickname is not empty");

            string[] scenePaths = EditorBuildSettings.scenes.Where(x=>x.enabled)
                .Select(x=>x.path).ToArray();
            check(scenePaths.Length==2 && scenePaths[0]==MenuPath && scenePaths[1]==GamePath,
                "build settings exactly MenuLobbyScene + GameScene");
            check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab")!=null &&
                  AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HideAndShoot/Prefabs/NPCActor.prefab")!=null,
                "PlayerAvatar / NPCActor prefabs available");
            bool opened=false;
            Scene menu=SceneManager.GetSceneByPath(MenuPath);
            try
            {
                if(!menu.IsValid() || !menu.isLoaded)
                {
                    menu=EditorSceneManager.OpenScene(MenuPath,OpenSceneMode.Additive);
                    opened=true;
                }
                var canvas=menu.GetRootGameObjects().FirstOrDefault(x=>x.name=="MenuLobbyCanvas");
                check(canvas!=null && canvas.GetComponent<MenuLobbyUI>()!=null,
                    "MenuLobbyCanvas has MenuLobbyUI");
                if(canvas!=null)
                {
                    var objs=canvas.GetComponentsInChildren<Transform>(true);
                    string[] required = { "NicknameInput","CreateRoomButton","RoomCodeInput",
                        "JoinRoomButton","RoomOverlay","ConnectedPlayers","CopyCodeButton","HostPlayButton" };
                    foreach(var nameRequired in required)
                        check(objs.Any(x=>x.name==nameRequired), "UI hierarchy has "+nameRequired);
                    check(objs.Any(x=>x.name=="RoomOverlay" && !x.gameObject.activeSelf),
                        "room overlay initially hidden");
                }
            }
            catch(Exception ex)
            {
                check(false, "menu additive inspection: "+ex.Message);
            }
            finally
            {
                if(opened && menu.IsValid() && menu.isLoaded)
                    EditorSceneManager.CloseScene(menu,true);
            }

            report.AppendLine($"Totals: {passed} PASS / {failed} FAIL.");
            var full = Path.Combine(Application.dataPath,"HideAndShoot","QA","LobbyEditModeAudit.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full,report.ToString(),new UTF8Encoding(false));
            Debug.Log("[HideAndShoot/QA] Lobby edit-mode result: "+passed+
                " pass / "+failed+" fail. See "+ReportPath);
            AssetDatabase.ImportAsset(ReportPath);
        }
    }
}
