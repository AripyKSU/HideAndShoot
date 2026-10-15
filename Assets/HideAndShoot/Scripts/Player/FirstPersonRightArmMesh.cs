using System;
using UnityEngine;

namespace HideAndShoot.Player
{
    /// <summary>
    /// Narrows a CPU-readable first-person humanoid arm mesh to the RIGHT arm.
    /// Only changes the local FP mesh instance, never a network/world mesh.
    /// A persisted edit-time asset can replace this fallback.
    /// </summary>
    public static class FirstPersonRightArmMesh
    {
        public static int Restrict(Animator animator,Transform visualRoot)
        {
            if(animator==null || animator.avatar==null || !animator.avatar.isHuman ||
               visualRoot==null)return 0;
            var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if(upper==null)return 0;
            int allTriangles=0;
            foreach(var skin in visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var original=skin.sharedMesh;
                if(original==null)continue;
                if(original.name.StartsWith("FPS_RightArm_",StringComparison.Ordinal))
                {
                    allTriangles+=original.triangles.Length/3;
                    continue;
                }
                if(!original.isReadable)
                {
                    Debug.LogError("[HideAndShoot/FPS] Arm mesh not readable: "+
                                   original.name+" (fix in editor installer)");
                    continue;
                }
                var bones=skin.bones;
                var weights=original.boneWeights;
                if(weights.Length!=original.vertexCount || bones.Length==0)continue;
                var isRight=new bool[bones.Length];
                int rightBones=0;
                for(int b=0;b<bones.Length;b++)
                    if(bones[b]!=null &&
                       (bones[b]==upper || bones[b].IsChildOf(upper)))
                    {
                        isRight[b]=true;
                        rightBones++;
                    }
                if(rightBones==0)
                {
                    Debug.LogError("[HideAndShoot/FPS] SkinnedMeshRenderer has no right-arm bone mappings.");
                    continue;
                }
                var rightWeight=new float[weights.Length];
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    float sum=0f;
                    if(w.boneIndex0<isRight.Length && isRight[w.boneIndex0])sum+=w.weight0;
                    if(w.boneIndex1<isRight.Length && isRight[w.boneIndex1])sum+=w.weight1;
                    if(w.boneIndex2<isRight.Length && isRight[w.boneIndex2])sum+=w.weight2;
                    if(w.boneIndex3<isRight.Length && isRight[w.boneIndex3])sum+=w.weight3;
                    rightWeight[i]=sum;
                }
                var mesh=UnityEngine.Object.Instantiate(original);
                mesh.name="FPS_RightArm_"+skin.name;
                int triangles=0;
                for(int submesh=0;submesh<original.subMeshCount;submesh++)
                {
                    var source=original.GetTriangles(submesh);
                    var selected=new System.Collections.Generic.List<int>(source.Length/2);
                    for(int t=0;t+2<source.Length;t+=3)
                    {
                        int selectedVertices=0;
                        for(int v=0;v<3;v++)
                            if(rightWeight[source[t+v]]>.45f)selectedVertices++;
                        if(selectedVertices<2)continue;
                        selected.Add(source[t]);
                        selected.Add(source[t+1]);
                        selected.Add(source[t+2]);
                    }
                    triangles+=selected.Count/3;
                    mesh.SetTriangles(selected,submesh);
                }
                if(triangles==0)
                {
                    if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                    skin.enabled=false;
                    continue;
                }
                skin.sharedMesh=mesh;
                skin.localBounds=mesh.bounds;
                allTriangles+=triangles;
            }
            return allTriangles;
        }
    }
}
