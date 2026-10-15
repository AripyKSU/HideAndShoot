using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// Separate first-person Kyle arms SkinnedMeshRenderer. Keeps the full Kyle
    /// bone hierarchy/Avatar but renders only weighted arm triangles.
    /// Visually calibrate grip/camera offset in the later integrated QA phase.
    /// </summary>
    [InitializeOnLoad]
    public static class FirstPersonArmsInstaller
    {
        private const string PlayerPath="Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab";
        private const string WorldPath="Assets/HideAndShoot/Prefabs/RobotKyleWorldVisual.prefab";
        private const string MeshFolder="Assets/HideAndShoot/Animations";
        private const string MeshPrefix="FPSKyle_Arms_";
        private const string KyleModelPath=
            "Assets/HideAndShoot/ThirdParty/RobotKyle/Models/Robot Kyle.fbx";
        private static bool ran;
        static FirstPersonArmsInstaller() => EditorApplication.update += TryInstall;

        private static void TryInstall()
        {
            if(ran || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            ran=true;EditorApplication.update-=TryInstall;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Build Separate Kyle FP Arms")]
        public static void Install()
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath);
            if(source==null) return;
            Directory.CreateDirectory(MeshFolder);

            // Extracting arm-only triangles requires CPU-accessible mesh data.
            // ModelImporter defaults to Read/Write disabled for this imported FBX.
            // Set it before opening prefab contents, reimport once, then defer
            // generation to Editor's next update after the import is complete.
            var importer=AssetImporter.GetAtPath(KyleModelPath) as ModelImporter;
            if(importer==null)
            {
                Debug.LogError("[HideAndShoot/FP Arms] Kyle source FBX missing.");
                return;
            }
            if(!importer.isReadable)
            {
                importer.isReadable=true;
                importer.SaveAndReimport();
                EditorApplication.delayCall+=Install;
                return;
            }

            var player=PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var holder=player.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(x=>x.name=="FPSArmsRig_TO_BUILD");
                if(holder==null)return;
                if(holder.Find("FPS_KyleArmsSkinned")!=null)return;
                var visual=PrefabUtility.InstantiatePrefab(source,holder) as GameObject;
                if(visual==null)return;
                visual.name="FPS_KyleArmsSkinned";
                visual.transform.localPosition=new Vector3(0,-1.1f,.32f);
                visual.transform.localRotation=Quaternion.identity;
                visual.transform.localScale=Vector3.one;

                var animator=visual.GetComponentInChildren<Animator>(true);
                if(animator==null)
                {
                    Debug.LogError("[HideAndShoot] Cannot build FP arms: Robot Kyle Animator not found.");
                    UnityEngine.Object.DestroyImmediate(visual);
                    return;
                }

                Transform left=SafeBone(animator,HumanBodyBones.LeftUpperArm);
                Transform right=SafeBone(animator,HumanBodyBones.RightUpperArm);
                if(left==null || right==null)
                {
                    Debug.LogError("[HideAndShoot] Humanoid upper-arm bones missing; Avatar repair needed.");
                    UnityEngine.Object.DestroyImmediate(visual);
                    return;
                }

                int drawnTriangles=0;
                int meshIndex=0;
                foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    if(renderer is SkinnedMeshRenderer skinned)
                    {
                        var mesh=skinned.sharedMesh;
                        if(mesh==null || !mesh.isReadable) continue;
                        var weights=mesh.boneWeights;
                        var bones=skinned.bones;
                        if(weights.Length!=mesh.vertexCount || bones.Length==0)
                            continue;

                        bool[] isArmBone=new bool[bones.Length];
                        for(int b=0;b<bones.Length;b++)
                            isArmBone[b]=bones[b]!=null &&
                                (bones[b]==left || bones[b].IsChildOf(left) ||
                                 bones[b]==right || bones[b].IsChildOf(right));

                        float[] armWeight=new float[weights.Length];
                        for(int v=0;v<weights.Length;v++)
                        {
                            var w=weights[v];
                            float sum=0;
                            if(w.boneIndex0<isArmBone.Length && isArmBone[w.boneIndex0])sum+=w.weight0;
                            if(w.boneIndex1<isArmBone.Length && isArmBone[w.boneIndex1])sum+=w.weight1;
                            if(w.boneIndex2<isArmBone.Length && isArmBone[w.boneIndex2])sum+=w.weight2;
                            if(w.boneIndex3<isArmBone.Length && isArmBone[w.boneIndex3])sum+=w.weight3;
                            armWeight[v]=sum;
                        }

                        var dst=UnityEngine.Object.Instantiate(mesh);
                        dst.name=MeshPrefix+(meshIndex++);
                        bool hasArms=false;
                        for(int sub=0;sub<mesh.subMeshCount;sub++)
                        {
                            int[] tris=mesh.GetTriangles(sub);
                            var selected=new List<int>();
                            for(int t=0;t+2<tris.Length;t+=3)
                            {
                                int n=0;
                                for(int k=0;k<3;k++)
                                    if(armWeight[tris[t+k]]>.5f)n++;
                                if(n>=2)
                                {
                                    selected.Add(tris[t]);
                                    selected.Add(tris[t+1]);
                                    selected.Add(tris[t+2]);
                                }
                            }
                            if(selected.Count>0){hasArms=true;drawnTriangles+=selected.Count/3;}
                            dst.SetTriangles(selected,sub);
                        }
                        if(hasArms)
                        {
                            var asset=MeshFolder+"/"+dst.name+".asset";
                            if(AssetDatabase.LoadAssetAtPath<Mesh>(asset)!=null)
                                AssetDatabase.DeleteAsset(asset);
                            AssetDatabase.CreateAsset(dst,asset);
                            skinned.sharedMesh=dst;
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(dst);
                            renderer.enabled=false;
                        }
                    }
                    else
                    {
                        // Pistol (MeshRenderer) remains visible under this prefab.
                        // Other mesh-only body elements are hidden to keep true arms view.
                        bool pistolRenderer=renderer.GetComponentsInParent<Transform>(true)
                            .Any(t=>t.name.IndexOf("Pistol",StringComparison.OrdinalIgnoreCase)>=0);
                        if(!pistolRenderer) renderer.enabled=false;
                    }
                }

                if(drawnTriangles==0)
                {
                    Debug.LogError("[HideAndShoot/FP Arms] No arm triangles found with readable Kyle FBX. Check Humanoid Avatar, SkinnedMeshRenderer.bones mapping and arm weights. Player prefab unchanged.");
                    UnityEngine.Object.DestroyImmediate(visual);
                    return;
                }

                int layer=LayerMask.NameToLayer("FPSViewModel");
                if(layer<0)
                    layer=LayerMask.NameToLayer("Default");
                SetLayer(visual,layer);
                var camera=player.GetComponentInChildren<Camera>(true);
                if(camera!=null && layer>=0)
                    camera.cullingMask|=(1<<layer);
                PrefabUtility.SaveAsPrefabAsset(player,PlayerPath);
                Debug.Log("[HideAndShoot] Kyle first-person arms created, rendered triangles="+drawnTriangles+". Inspect visual placement later.");
            }
            finally {PrefabUtility.UnloadPrefabContents(player);}
        }

        private static Transform SafeBone(Animator a,HumanBodyBones b)
        {
            if(a.avatar==null || !a.avatar.isHuman)return null;
            try{return a.GetBoneTransform(b);}
            catch{return null;}
        }

        private static void SetLayer(GameObject go,int layer)
        {
            go.layer=layer;
            foreach(Transform t in go.transform)SetLayer(t.gameObject,layer);
        }
    }
}
