using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Opt-in standalone development build for two-instance Relay/NGO tests.
    /// Trigger Temp/QA_BuildStandalone.request. The Unity Editor stays open.
    /// </summary>
    internal static class QaStandaloneBuilder
    {
        private const string BuildFolder = "Builds/QA_Standalone";
        private const string ReportFile = "Assets/HideAndShoot/QA/StandaloneBuildAudit.txt";
        private static string Root => Path.GetDirectoryName(Application.dataPath);
        private static string Request => Path.Combine(Root, "Temp", "QA_BuildStandalone.request");


        [MenuItem("Tools/HideAndShoot/QA/Build Standalone Relay Client")]
        public static void Build()
        {
            var scenePaths = new []
            {
                "Assets/HideAndShoot/Scenes/MenuLobbyScene.unity",
                "Assets/HideAndShoot/Scenes/GameScene.unity"
            };
            string output=Path.Combine(Root,BuildFolder,"HideAndShoot.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string result;
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes=scenePaths,
                    locationPathName=output,
                    target=BuildTarget.StandaloneWindows64,
                    options=BuildOptions.Development
                };
                BuildReport report=BuildPipeline.BuildPlayer(options);
                var summary=report.summary;
                result=$"Result={summary.result}\nErrors={summary.totalErrors}\nWarnings={summary.totalWarnings}\nBytes={summary.totalSize}\nOutput={output}\n";
                Debug.Log("[HideAndShoot/QA] Standalone build: "+summary.result+" errors="+summary.totalErrors);
            }
            catch(Exception ex)
            {
                result="Result=Exception\n"+ex+"\n";
                Debug.LogException(ex);
            }
            var reportPath=Path.Combine(Application.dataPath,"HideAndShoot","QA","StandaloneBuildAudit.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\n"+result);
            AssetDatabase.ImportAsset(ReportFile);
        }

    }
}
