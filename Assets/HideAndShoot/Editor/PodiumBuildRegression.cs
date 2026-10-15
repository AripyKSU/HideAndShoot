using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    public static class PodiumBuildRegression
    {
        [MenuItem("Tools/HideAndShoot/QA/Build Podium Shader Regression")]
        public static void Build()
        {
            const string materialPath="Assets/HideAndShoot/Resources/PodiumStageMaterial.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null || material.shader.name!="Universal Render Pipeline/Lit" ||
                !material.IsKeywordEnabled("_EMISSION"))
                throw new Exception("Build-safe URP podium material or emission variant missing");
            string output="Builds/QA_Podium/HideAndShoot.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes=new[]{"Assets/HideAndShoot/Scenes/MenuLobbyScene.unity","Assets/HideAndShoot/Scenes/GameScene.unity"},
                target=BuildTarget.StandaloneWindows64,
                locationPathName=output,
                options=BuildOptions.None,
                extraScriptingDefines=new[]{"HIDEANDSHOOT_PODIUM_BUILD_QA"}
            });
            var summary=report.summary;
            File.WriteAllText("Builds/QA_Podium/BuildReport.txt",
                "Result="+summary.result+"\nErrors="+summary.totalErrors+"\nWarnings="+summary.totalWarnings+
                "\nOptions=Release\nBytes="+summary.totalSize+"\n");
            if(summary.result!=BuildResult.Succeeded)throw new Exception("Podium QA build failed");
        }
    }
}