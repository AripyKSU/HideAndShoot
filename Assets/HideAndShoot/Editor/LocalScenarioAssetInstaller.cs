using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// One-shot repair for the local Multiplayer Play Mode Scenario configs.
    ///
    /// Create and serialize through Unity's AssetDatabase. Never overwrite the
    /// existing NewPlayModeScenario, which may be referenced by PlayModeManager.
    /// Exposes a menu command so the user can rebuild without restarting Unity.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalScenarioAssetInstaller
    {
        private const string DirectoryPath = "Assets/Settings/PlayMode";
        private const string ThreePlayerPath =
            DirectoryPath + "/HideAndShoot_Local3Players.asset";
        private const string FourPlayerPath =
            DirectoryPath + "/HideAndShoot_Local4Players.asset";
        private const string StartScene =
            "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity";

        static LocalScenarioAssetInstaller()
        {
            // Once Unity imports this script, process at the next safe editor
            // update. Unlike delayCall, don't lose the request while importing.
            EditorApplication.update += InstallOnEditorReady;
        }

        private static void InstallOnEditorReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= InstallOnEditorReady;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Repair Local Multiplayer Scenarios")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogWarning(
                    "[HideAndShoot/Scenario] Wait for Unity to finish compiling; " +
                    "the installer does not modify scenarios during Play.");
                return;
            }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScene);
            if (scene == null)
            {
                Debug.LogError("[HideAndShoot/Scenario] MenuLobbyScene missing.");
                return;
            }

            // ScenarioConfig and PlayModeManager are internal to MPP 1.6.2.
            // Reflection here avoids a source dependency on their assemblies.
            var scenarioType = Type.GetType(
                "Unity.Multiplayer.PlayMode.Scenarios.Editor.ScenarioConfig, " +
                "Unity.Multiplayer.PlayMode.Scenarios.Editor");
            if (scenarioType == null || !typeof(ScriptableObject).IsAssignableFrom(scenarioType))
            {
                Debug.LogError("[HideAndShoot/Scenario] MPP ScenarioConfig type unavailable.");
                return;
            }

            var three = CreateScenario(scenarioType, scene, ThreePlayerPath, 2);
            var four = CreateScenario(scenarioType, scene, FourPlayerPath, 3);
            AssetDatabase.SaveAssets();

            // A previously selected scenario may have been invalidated by a
            // file-system overwrite. Rebind the live PlayModeManager, but only
            // in a stable, non-Play state.
            if (three != null)
                TrySelectScenario(three);

            Debug.Log("[HideAndShoot/Scenario] Local scenarios ready: " +
                      (three != null ? ThreePlayerPath : "(3-player unavailable)") +
                      ", " +
                      (four != null ? FourPlayerPath : "(4-player unavailable)") +
                      ". Existing NewPlayModeScenario is preserved.");
        }

        private static UnityEngine.Object CreateScenario(
            Type type, SceneAsset scene, string path, int extraEditors)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing != null)
            {
                if (existing.GetType() != type)
                {
                    Debug.LogError("[HideAndShoot/Scenario] Name conflict: " + path);
                    return null;
                }
                return existing;
            }

            if (AssetDatabase.AssetPathExists(path))
            {
                Debug.LogError("[HideAndShoot/Scenario] Refusing overwrite at " + path);
                return null;
            }

            var asset = ScriptableObject.CreateInstance(type);
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            using (var so = new SerializedObject(asset))
            {
                var enabled = so.FindProperty("m_EnableEditors");
                var main = so.FindProperty("m_MainEditorInstance");
                var editors = so.FindProperty("m_EditorInstances");
                var locals = so.FindProperty("m_LocalInstances");
                var remotes = so.FindProperty("m_RemoteInstances");
                if (enabled == null || main == null || editors == null ||
                    locals == null || remotes == null)
                {
                    Debug.LogError("[HideAndShoot/Scenario] MPP schema differs; no asset written.");
                    UnityEngine.Object.DestroyImmediate(asset);
                    return null;
                }

                enabled.boolValue = true;
                var start = main.FindPropertyRelative("m_InitialScene");
                if (start != null)
                    start.objectReferenceValue = scene;
                var mainName = main.FindPropertyRelative("Name");
                if (mainName != null)
                    mainName.stringValue = "Main Editor";

                editors.arraySize = extraEditors;
                for (int i = 0; i < extraEditors; i++)
                {
                    var entry = editors.GetArrayElementAtIndex(i);
                    var name = entry.FindPropertyRelative("Name");
                    var initial = entry.FindPropertyRelative("m_InitialScene");
                    if (name == null || initial == null)
                    {
                        Debug.LogError("[HideAndShoot/Scenario] Missing Editor Instance fields.");
                        UnityEngine.Object.DestroyImmediate(asset);
                        return null;
                    }
                    name.stringValue = "Player " + (i + 2);
                    initial.objectReferenceValue = scene;
                    var tag = entry.FindPropertyRelative("m_PlayerTag");
                    if (tag != null) tag.stringValue = "";
                }

                locals.arraySize = 0;
                remotes.arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }

        private static void TrySelectScenario(UnityEngine.Object scenario)
        {
            try
            {
                var managerType = Type.GetType(
                    "Unity.Multiplayer.PlayMode.Configurations.Editor.PlayModeManager, " +
                    "Unity.Multiplayer.PlayMode.Configurations.Editor");
                if (managerType == null) return;

                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(managerType);
                var manager = singleton.GetProperty("instance", flags)?.GetValue(null);
                if (manager == null) return;

                var state = managerType.GetProperty("CurrentState",
                    BindingFlags.Instance | BindingFlags.Public);
                if (state?.GetValue(manager)?.ToString() != "NotRunning") return;

                var active = managerType.GetProperty("ActivePlayModeConfig",
                    BindingFlags.Instance | BindingFlags.Public);
                if (active?.CanWrite == true)
                    active.SetValue(manager, scenario);
            }
            catch (Exception ex)
            {
                // The saved scenario is still valid: only active UI selection may
                // need the Scenarios window reopened.
                Debug.LogWarning("[HideAndShoot/Scenario] Could not refresh active " +
                                 "MPP selection; reopen Play Mode Scenarios. " + ex.Message);
            }
        }
    }
}
