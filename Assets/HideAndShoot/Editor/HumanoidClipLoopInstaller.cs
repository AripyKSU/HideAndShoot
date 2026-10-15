using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Ensure locomotion clips REPEAT. Old imported FBX clips had no explicit
    /// loop settings; those 0.5s / 0.8s clips could stop after their first cycle.
    /// Never loop the 5.3s Fire_Handgun or any Death clip.
    /// </summary>
    [InitializeOnLoad]
    public static class HumanoidClipLoopInstaller
    {
        private const string Root="Assets/HideAndShoot/ThirdParty/Mixamo/";
        private static readonly string[] LoopNames={
            "Idle_ReadyPistol","Locomotion_Forward_Walk","Locomotion_Forward_Run",
            "Locomotion_Backward_Walk","Locomotion_Strafe_Left",
            "Locomotion_Strafe_Right","Aim_Idle"
        };
        static HumanoidClipLoopInstaller()=>EditorApplication.update+=TryInstall;

        private static void TryInstall()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode ||
               EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            EditorApplication.update-=TryInstall;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Fix Looping Humanoid Movement Clips")]
        public static void Install()
        {
            var report=new List<string>();
            foreach(var name in LoopNames)
            {
                var path=Root+name+".fbx";
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(importer==null){report.Add("MISSING: "+name);continue;}
                var clips=importer.clipAnimations;
                if(clips==null || clips.Length==0)
                    clips=importer.defaultClipAnimations;
                if(clips==null || clips.Length==0)
                {
                    report.Add("NO_CLIPS: "+name);
                    continue;
                }
                bool updated=false;
                foreach(var clip in clips)
                {
                    if(!clip.loopTime || !clip.loopPose ||
                       !clip.lockRootRotation || !clip.lockRootHeightY ||
                       !clip.lockRootPositionXZ)
                    {
                        clip.loopTime=true;
                        clip.loopPose=true;
                        clip.lockRootRotation=true;
                        clip.lockRootHeightY=true;
                        clip.lockRootPositionXZ=true;
                        updated=true;
                    }
                }
                if(updated)
                {
                    importer.clipAnimations=clips;
                    importer.SaveAndReimport();
                }
                report.Add((updated?"UPDATED: ":"ALREADY_LOOPING: ")+name);
            }
            Directory.CreateDirectory("Assets/HideAndShoot/QA");
            File.WriteAllLines(
                "Assets/HideAndShoot/QA/ANIMATION_LOOP_IMPORT_AUDIT.txt",report);
            Debug.Log("[HideAndShoot/Animation] Loop flags audited: "+
                String.Join(" | ",report));
        }
    }
}
