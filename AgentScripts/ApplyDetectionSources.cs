using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;

public static class ApplyDetectionSources
{
    public static object Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
        const string stage="AgentScripts/DetectionItemStage";
        const string backup="AgentBackups/DetectionItem_2026-10-16";
        var states=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new {path=SceneManager.GetSceneAt(i).path,dirty=SceneManager.GetSceneAt(i).isDirty}).ToArray();
        var files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories);
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            foreach(var file in files)
            {
                string relative=file.Substring(stage.Length+1);
                Directory.CreateDirectory(Path.GetDirectoryName(relative));
                if(File.Exists(relative))
                {
                    string old=Path.Combine(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(old));
                    if(!File.Exists(old))File.Copy(relative,old);
                }
                File.Copy(file,relative,true);
            }
            const string modelDir="Assets/HideAndShoot/ThirdParty/DetectionSunglasses";
            Directory.CreateDirectory(modelDir);
            File.Copy("AgentDownloads/Eyewear/Sunglasses.fbx",modelDir+"/Sunglasses.fbx",true);
            File.WriteAllText(modelDir+"/SOURCE.md","# Detection sunglasses source\n\nCreator: iPoly3D\nModel: glasses12.001, 904 triangles, 2 materials\nSelected listing: https://poly.pizza/m/p5QgQxkMBE\nOriginal pack: https://opengameart.org/content/lowpoly-glasses-pack\nArchive: https://opengameart.org/sites/default/files/lowpoly_glasses.zip\nArchive entry: FBX/glasses12.001.fbx\nLicense: CC0 1.0 Universal (free/public domain)\nLicense reference: https://creativecommons.org/publicdomain/zero/1.0/\nVerified: 2026-10-16\n\nOnly this FBX is imported. Project materials recolor the lenses; geometry is unchanged.\n");
        }
        finally{AssetDatabase.AllowAutoRefresh();AssetDatabase.Refresh();}
        return new {sourceFiles=files.Length,model="glasses12.001.fbx",license="CC0 1.0",openScenesPreserved=states};
    }
}