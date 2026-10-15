using System;
using System.IO;
using System.Linq;
using HideAndShoot.Lobby;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Editor stays running. Opt-in via Temp/QA_RelayHost.request.
    /// Content "HOLD" holds a Host session and exposes its join code for a standalone test client.
    /// Temp/QA_RelayPlay.request sends Host Play, Temp/QA_RelayFinish.request exits the test.
    /// </summary>
    [InitializeOnLoad]
    internal static class RelayHostSmokeRunner
    {
        private const string Prefix = "HideAndShoot.RelayHostQA.";
        private const string StageKey = Prefix+"Stage";
        private const string SceneKey = Prefix+"PreviousScene";
        private const string ResultKey = Prefix+"Result";
        private const string TimeKey = Prefix+"Deadline";
        private const string HoldKey = Prefix+"Hold";
        private const string Report = "Assets/HideAndShoot/QA/RelayHostSmoke.txt";
        private const string MenuScene = "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";
        private static readonly string Temp = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp");
        private static string Request => Path.Combine(Temp, "QA_RelayHost.request");
        private static string HostCode => Path.Combine(Temp, "QA_RelayHost.code");
        private static string PlayRequest => Path.Combine(Temp, "QA_RelayPlay.request");
        private static string FinishRequest => Path.Combine(Temp, "QA_RelayFinish.request");
        private static double restoreWaitStart=-1;

        static RelayHostSmokeRunner() => EditorApplication.update += Tick;

        [MenuItem("Tools/HideAndShoot/QA/Smoke Test Host Relay (Keep Editor Open)")]
        public static void StartManually()
        {
            EditorPrefs.SetBool(HoldKey,false);
            Prepare();
        }

        private static void Tick()
        {
            string stage=EditorPrefs.GetString(StageKey,"");
            if (stage == "")
            {
                if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                   EditorApplication.isPlayingOrWillChangePlaymode) return;
                string requested=File.ReadAllText(Request).Trim();
                File.Delete(Request);
                EditorPrefs.SetBool(HoldKey,string.Equals(requested,"HOLD",StringComparison.OrdinalIgnoreCase));
                Prepare();
            }
            else if (stage=="entering" && EditorApplication.isPlaying)
            {
                var ui=UnityEngine.Object.FindFirstObjectByType<MenuLobbyUI>();
                if(ui == null) return;
                var button=ui.GetComponentsInChildren<Button>(true).FirstOrDefault(x=>x.name=="CreateRoomButton");
                if(button == null || !button.interactable)
                {
                    Finish("FAIL: Missing/disabled CreateRoomButton.");
                    return;
                }
                EditorPrefs.SetString(StageKey,"waiting");
                SetDeadline(45);
                button.onClick.Invoke();
                Debug.Log("[HideAndShoot/QA] Relay Host create requested in Play Mode.");
            }
            else if (stage=="waiting" && EditorApplication.isPlaying)
            {
                var ui=UnityEngine.Object.FindFirstObjectByType<MenuLobbyUI>();
                var network=UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
                if(network!=null && network.IsHost && ui!=null && ui.IsInRoom &&
                   !string.IsNullOrEmpty(ui.RoomCode))
                {
                    if(EditorPrefs.GetBool(HoldKey,false))
                    {
                        File.WriteAllText(HostCode,ui.RoomCode);
                        EditorPrefs.SetString(StageKey,"holding");
                        SetDeadline(120);
                        Debug.Log("[HideAndShoot/QA] Host held open for client. Join code saved in Temp.");
                        return;
                    }
                    var leave=ui.GetComponentsInChildren<Button>(true)
                        .FirstOrDefault(x=>x.name=="LeaveRoomButton");
                    if(leave==null)
                    {
                        Finish("FAIL: Host joined but LeaveRoomButton missing.");
                        return;
                    }
                    leave.onClick.Invoke();
                    Finish("PASS: Unity Services anonymous sign-in, Relay allocation/code, NGO Host, UI, Leave.");
                    return;
                }
                if(DeadlinePassed())
                    Finish("FAIL: Relay Host start timed out (45s). Check Editor.log for UGS error.");
            }
            else if(stage=="holding" && EditorApplication.isPlaying)
            {
                if(File.Exists(FinishRequest))
                {
                    File.Delete(FinishRequest);
                    Finish("PASS: Relay Host created and held, client test signaled completion.");
                    return;
                }
                if(File.Exists(PlayRequest))
                {
                    File.Delete(PlayRequest);
                    var ui=UnityEngine.Object.FindFirstObjectByType<MenuLobbyUI>();
                    var play=ui==null?null:ui.GetComponentsInChildren<Button>(true)
                        .FirstOrDefault(x=>x.name=="HostPlayButton");
                    if(play==null || !play.interactable)
                    {
                        Finish("FAIL: Host Play button not enabled after waiting for second connected player.");
                        return;
                    }
                    EditorPrefs.SetString(StageKey,"game");
                    SetDeadline(30);
                    play.onClick.Invoke();
                    Debug.Log("[HideAndShoot/QA] Host Play invoked via enabled button.");
                    return;
                }
                if(DeadlinePassed())
                    Finish("FAIL: Host hold timed out waiting for client/play/finish signal.");
            }
            else if(stage=="game" && EditorApplication.isPlaying)
            {
                var net=UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
                if(SceneManager.GetActiveScene().name=="GameScene" && net!=null && net.IsHost)
                {
                    File.WriteAllText(Path.Combine(Temp,"QA_RelayGameScene.ready"),"Host GameScene ready");
                    EditorPrefs.SetString(StageKey,"game-ready");
                    SetDeadline(45);
                    Debug.Log("[HideAndShoot/QA] Host NGO GameScene is active.");
                }
                if(DeadlinePassed())
                    Finish("FAIL: NGO GameScene did not become active within 30 seconds.");
            }
            else if(stage=="game-ready" && EditorApplication.isPlaying)
            {
                if(File.Exists(FinishRequest))
                {
                    File.Delete(FinishRequest);
                    Finish("PASS: Relay host + second client; Host Play and GameScene transition reached. Verify client-side scene log separately.");
                }
                else if(DeadlinePassed())
                    Finish("FAIL: Host scene switched but client-side confirmation signal timed out.");
            }
            else if(stage=="restore" &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                // Unity needs a short window to restore its backed-up edit-mode scene.
                if(restoreWaitStart<0)
                {
                    restoreWaitStart=EditorApplication.timeSinceStartup;
                    return;
                }
                if(EditorApplication.timeSinceStartup-restoreWaitStart<2.0) return;
                restoreWaitStart=-1;

                var path=EditorPrefs.GetString(SceneKey,"");
                var result=EditorPrefs.GetString(ResultKey,"FAIL: Missing test result");
                EditorPrefs.DeleteKey(StageKey);
                EditorPrefs.DeleteKey(SceneKey);
                EditorPrefs.DeleteKey(ResultKey);
                EditorPrefs.DeleteKey(TimeKey);
                EditorPrefs.DeleteKey(HoldKey);
                try
                {
                    if(!string.IsNullOrEmpty(path) && SceneManager.GetActiveScene().path!=path)
                        EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                }
                catch(Exception ex)
                {
                    result+="\nNOTE: Could not restore original scene: "+ex.Message;
                }
                var output=Path.Combine(Application.dataPath,"HideAndShoot","QA","RelayHostSmoke.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\n"+result+"\n");
                AssetDatabase.ImportAsset(Report);
                Debug.Log("[HideAndShoot/QA] "+result+" | Editor process remained open.");
            }
        }

        private static void SetDeadline(int seconds) =>
            EditorPrefs.SetString(TimeKey,DateTime.UtcNow.AddSeconds(seconds).Ticks.ToString());

        private static bool DeadlinePassed() =>
            long.TryParse(EditorPrefs.GetString(TimeKey,"0"),out long until) &&
            DateTime.UtcNow.Ticks>until;

        private static void Prepare()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("[HideAndShoot/QA] Run Host smoke from edit mode.");
                return;
            }
            var current=SceneManager.GetActiveScene();
            if(!current.IsValid() || current.isDirty)
            {
                Debug.LogWarning("[HideAndShoot/QA] Current scene is invalid/dirty; not changing scenes.");
                return;
            }
            EditorPrefs.SetString(SceneKey,current.path);
            EditorPrefs.SetString(StageKey,"entering");
            try
            {
                EditorSceneManager.OpenScene(MenuScene,OpenSceneMode.Single);
                EditorApplication.isPlaying=true;
            }
            catch(Exception ex)
            {
                EditorPrefs.SetString(ResultKey,"FAIL: Cannot start smoke: "+ex.Message);
                EditorPrefs.SetString(StageKey,"restore");
            }
        }

        private static void Finish(string message)
        {
            EditorPrefs.SetString(ResultKey,message);
            EditorPrefs.SetString(StageKey,"restore");
            EditorApplication.isPlaying=false;
        }
    }
}
