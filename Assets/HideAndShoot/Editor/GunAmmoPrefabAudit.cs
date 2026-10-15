using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class GunAmmoPrefabAudit
    {
        static GunAmmoPrefabAudit()=>EditorApplication.update+=RunOnce;
        private static void RunOnce()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode ||
               EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            EditorApplication.update-=RunOnce;
            Audit();
        }
        [MenuItem("Tools/HideAndShoot/Audit Pistol and Ammo Network Prefabs")]
        public static void Audit()
        {
            var sb=new StringBuilder("PREFAB NETWORK IDs and PISTOL VISUAL 2026-10-13\n");
            foreach(var name in new[]{"PlayerAvatar","NPCActor","AmmoPickup"})
            {
                var path="Assets/HideAndShoot/Prefabs/"+name+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var network=prefab?.GetComponent<NetworkObject>();
                var hashField=typeof(NetworkObject).GetField("GlobalObjectIdHash",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                var hashProperty=typeof(NetworkObject).GetProperty("GlobalObjectIdHash",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                object hash=network!=null ?
                    (hashField!=null ? hashField.GetValue(network) :
                    hashProperty!=null?hashProperty.GetValue(network):null):null;
                sb.AppendLine(name+" GUID="+AssetDatabase.AssetPathToGUID(path)+
                    " NetworkGlobalObjectIdHash="+(hash?.ToString()??"not exposed"));
                if(prefab==null)continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var visuals=root.transform.Find("WorldVisual");
                    if(visuals!=null)
                    {
                        var animator=visuals.GetComponentInChildren<Animator>(true);
                        var hand=animator!=null && animator.avatar!=null && animator.avatar.isHuman?
                            animator.GetBoneTransform(HumanBodyBones.RightHand):null;
                        sb.AppendLine(name+" world animator="+(animator!=null)+
                            " hand="+(hand!=null?hand.name:"null"));
                        foreach(var ren in visuals.GetComponentsInChildren<MeshRenderer>(true))
                            if(ren.name.Contains("Pistol"))
                                sb.AppendLine("world gun renderer="+ren.name+
                                    " enabled="+ren.enabled+" bounds="+ren.bounds.size+
                                    " active="+ren.gameObject.activeInHierarchy);
                    }
                    var gun=root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t=>t.name=="FPS_QuaterniusPistol");
                    if(gun!=null)
                    {
                        foreach(var ren in gun.GetComponentsInChildren<Renderer>(true))
                            sb.AppendLine("owner gun="+ren.name+" enabled="+ren.enabled+
                                " bounds="+ren.bounds.size+" active="+ren.gameObject.activeInHierarchy);
                    }
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            var dest="Assets/HideAndShoot/QA/GUN_AMMO_NETWORK_PREFAB_AUDIT.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllText(dest,sb.ToString());
            Debug.Log("[HideAndShoot/Audit] Prefab hashes and pistol visual written.");
        }
    }
}
