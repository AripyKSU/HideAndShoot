using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
namespace HideAndShoot.EditorTools
{
    // Repair source-prefab NGO GUID hashes through NGO's own editor OnValidate.
    [InitializeOnLoad]
    public static class NetworkPrefabHashRepair
    {
        static NetworkPrefabHashRepair()=>EditorApplication.update+=InstallOnReady;
        private static void InstallOnReady()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode ||
               EditorApplication.isUpdating || EditorApplication.isCompiling)return;
            EditorApplication.update-=InstallOnReady;
            Repair();
        }
        [MenuItem("Tools/HideAndShoot/Verify Unique NGO Prefab IDs")]
        public static void Repair()
        {
            var paths=new [] {
                "Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab",
                "Assets/HideAndShoot/Prefabs/NPCActor.prefab",
                "Assets/HideAndShoot/Prefabs/AmmoPickup.prefab"
            };
            var objs=paths.Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>(p))
                .Select(go=>go!=null?go.GetComponent<NetworkObject>():null).ToArray();
            var field=typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.NonPublic|BindingFlags.Instance);
            var validator=typeof(NetworkObject).GetMethod("OnValidate",
                BindingFlags.NonPublic|BindingFlags.Instance);
            if(objs.Any(x=>x==null) || field==null || validator==null)return;
            var before=objs.Select(o=>(uint)field.GetValue(o)).ToArray();
            if(before.Distinct().Count()!=objs.Length || before.Any(v=>v==0))
            {
                foreach(var obj in objs)
                {
                    validator.Invoke(obj,null);
                    EditorUtility.SetDirty(obj);
                }
                AssetDatabase.SaveAssets();
            }
            var after=objs.Select(o=>(uint)field.GetValue(o)).ToArray();
            var ok=after.Distinct().Count()==objs.Length && after.All(v=>v!=0);
            var report="BEFORE: "+String.Join(", ",before)+"\nAFTER: "+
                       String.Join(", ",after)+"\nUNIQUE: "+ok+
                       "\nORDER: PlayerAvatar, NPCActor, AmmoPickup\n";
            File.WriteAllText("Assets/HideAndShoot/QA/NGO_PREFAB_HASH_VERIFY.txt",report);
            Debug.Log("[HideAndShoot/NGO] "+report.Replace("\n"," | "));
        }
    }
}
