using System;
using System.IO;
using System.Reflection;
using System.Text;
using HideAndShoot.Combat;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class AimZoomCheck
{
    public static object Run()
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var type=typeof(PistolCombatController);
        var update=type.GetMethod("UpdateAimZoom",flags);
        var root=new GameObject("AimZoomCheck_TEMP");root.hideFlags=HideFlags.HideAndDontSave;
        var camera=root.AddComponent<Camera>();camera.enabled=false;
        var combat=root.AddComponent<PistolCombatController>();
        void Set(string name,object value)=>type.GetField(name,flags).SetValue(combat,value);
        void Step(bool held,float now,float delta)=>update.Invoke(combat,new object[]{held,now,delta});
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var report=new StringBuilder("Finite ADS zoom verification\n");
        try
        {
            Set("cameraRig",camera);Set("defaultFov",60f);Set("adsFov",43f);Set("aimDuration",.15f);
            foreach(int fps in new[]{20,30,60,144})
            {
                camera.fieldOfView=60;Set("aimStartFov",60f);Set("stateUntil",.15f);
                Set("state",PistolCombatController.LocalAimState.AimEntering);
                for(int i=0;i<=Mathf.CeilToInt(.15f*fps)+1;i++)
                {
                    float now=i/(float)fps;Step(true,now,1f/fps);
                    if(now<.15f)Check(combat.State==PistolCombatController.LocalAimState.AimEntering,
                        "Fire became ready before zoom completion");
                    else Check(combat.State==PistolCombatController.LocalAimState.AimReady && camera.fieldOfView==43f,
                        "Zoom did not reach exact FOV before AimReady");
                }
                report.AppendLine("PASS | "+fps+"fps: no early readiness; exact 43-degree FOV at first frame on/after 0.15s");
            }
            Set("state",PistolCombatController.LocalAimState.AimEntering);Set("stateUntil",.15f);
            Set("aimStartFov",60f);camera.fieldOfView=60;Step(true,.075f,.016f);
            Check(Mathf.Abs(camera.fieldOfView-51.5f)<.001f && combat.State==PistolCombatController.LocalAimState.AimEntering,
                "Mid-zoom readiness/progress mismatch");
            Step(false,.1f,.016f);
            Check(combat.State==PistolCombatController.LocalAimState.Hip && camera.fieldOfView==60f,
                "Release did not cancel zoom");report.AppendLine("PASS | Releasing ADS cancels without entering AimReady");
            Set("state",PistolCombatController.LocalAimState.FireRecovery);camera.fieldOfView=43;
            for(int i=0;i<5;i++)Step(false,i/30f,1/30f);
            Check(camera.fieldOfView==60,"Zoom-out still has an asymptotic tail");
            report.AppendLine("PASS | Recovery zoom-out reaches exact normal FOV");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab");
            Check(Mathf.Abs(new SerializedObject(prefab.GetComponent<PistolCombatController>()).FindProperty("aimDuration").floatValue-.15f)<.0001f,
                "Player prefab still uses old ADS timing");report.AppendLine("PASS | Authored player prefab duration=0.15s");
            string current=File.ReadAllText("Assets/HideAndShoot/Scripts/Combat/PistolCombatController.cs");
            string previous=File.ReadAllText("Temp/AimZoomFix_Backup_20261014/PistolCombatController.cs");
            const string rpc="        [Rpc(SendTo.Server)]";
            Check(current.Substring(current.IndexOf(rpc))==previous.Substring(previous.IndexOf(rpc)),
                "Server shot validation/relay code changed");report.AppendLine("PASS | Server validation and laser relay method bodies unchanged");
            report.AppendLine("Editor compilation passed. This checks the actual zoom transition method; no online session or build was started.");
            File.WriteAllText("docs/AIM_ZOOM_FIX_2026-10-14.txt",report.ToString());
            return new {passed=true,duration=.15f,report="docs/AIM_ZOOM_FIX_2026-10-14.txt"};
        }
        finally{Object.DestroyImmediate(root);}
    }
}
