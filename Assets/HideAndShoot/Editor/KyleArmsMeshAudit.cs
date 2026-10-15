using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>One-time static asset inspection; never enters Unity Play mode.</summary>
    public static class KyleArmsMeshAudit
    {
        private const string Report="Assets/HideAndShoot/QA/KyleArmsMeshAudit.txt";

        // Explicit Editor-only audit. No automatic Play/Editor state changes.
        [MenuItem("Tools/HideAndShoot/Audit Kyle FP Arm Mesh")]
        public static void Run()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogWarning("[HideAndShoot/Kyle] Editor busy; audit not run.");
                return;
            }
            if(File.Exists(Report))return;
            var path="Assets/HideAndShoot/Prefabs/RobotKyleWorldVisual.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)return;
            var root=PrefabUtility.LoadPrefabContents(path);
            var log=new StringBuilder(4096);
            try
            {
                var animator=root.GetComponentInChildren<Animator>(true);
                log.AppendLine("Root: "+root.name);
                log.AppendLine("Animator: "+(animator!=null));
                if(animator!=null)
                {
                    log.AppendLine("Avatar: "+(animator.avatar!=null?
                        animator.avatar.name:"NULL"));
                    log.AppendLine("IsHuman: "+(animator.avatar!=null &&
                        animator.avatar.isHuman));
                }
                Transform left=null,right=null;
                if(animator!=null && animator.avatar!=null && animator.avatar.isHuman)
                {
                    try
                    {
                        left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    }
                    catch(Exception ex){log.AppendLine(ex.ToString());}
                }
                log.AppendLine("LeftUpperArm="+(left!=null?left.name:"NULL"));
                log.AppendLine("RightUpperArm="+(right!=null?right.name:"NULL"));
                foreach(var meshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh=meshRenderer.sharedMesh;
                    log.AppendLine("Skinned: "+meshRenderer.name+
                        " mesh="+(mesh!=null?mesh.name:"NULL"));
                    if(mesh==null)continue;
                    log.AppendLine("Vertices="+mesh.vertexCount+
                        " readable="+mesh.isReadable+" subMeshes="+mesh.subMeshCount+
                        " boneTransforms="+meshRenderer.bones.Length);
                    if(!mesh.isReadable)continue;
                    var weights=mesh.boneWeights;
                    var bones=meshRenderer.bones;
                    log.AppendLine("BoneWeights="+weights.Length);
                    int armBones=0;
                    var flags=new bool[bones.Length];
                    for(int b=0;b<bones.Length;b++)
                    {
                        var bone=bones[b];
                        flags[b]=bone!=null &&
                            ((left!=null && (bone==left || bone.IsChildOf(left))) ||
                             (right!=null && (bone==right || bone.IsChildOf(right))));
                        if(flags[b])armBones++;
                        if(b<60)log.AppendLine("  bone "+b+"="+
                            (bone!=null?bone.name:"NULL")+" arm="+flags[b]);
                    }
                    log.AppendLine("Matching arm bones="+armBones);
                    int strong=0,loose=0;
                    foreach(var weight in weights)
                    {
                        float sum=0;
                        if(weight.boneIndex0<flags.Length && flags[weight.boneIndex0])sum+=weight.weight0;
                        if(weight.boneIndex1<flags.Length && flags[weight.boneIndex1])sum+=weight.weight1;
                        if(weight.boneIndex2<flags.Length && flags[weight.boneIndex2])sum+=weight.weight2;
                        if(weight.boneIndex3<flags.Length && flags[weight.boneIndex3])sum+=weight.weight3;
                        if(sum>.5f)strong++;
                        if(sum>.1f)loose++;
                    }
                    log.AppendLine("Arm-weight vertices >0.5="+strong+" >0.1="+loose);
                }
                foreach(var meshRenderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    log.AppendLine("Static MeshRenderer: "+meshRenderer.name);
            }
            catch(Exception ex){log.AppendLine("AUDIT ERROR: "+ex);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report,log.ToString(),Encoding.UTF8);
            AssetDatabase.ImportAsset(Report);
            Debug.Log("[HideAndShoot/Kyle] Static arms mesh audit saved.");
        }
    }
}
