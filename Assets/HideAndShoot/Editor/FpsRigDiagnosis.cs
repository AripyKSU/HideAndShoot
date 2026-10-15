using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>Safe edit-time prefab rig audit, no Play invocation or mutation.</summary>
    [InitializeOnLoad]
    public static class FpsRigDiagnosis
    {
        private const string Prefab="Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab";
        private const string Report="Assets/HideAndShoot/QA/FPS_RIG_DIAGNOSIS_2026-10-13.txt";
        static FpsRigDiagnosis()=>EditorApplication.update+=TryAudit;
        private static void TryAudit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isUpdating || EditorApplication.isCompiling)return;
            EditorApplication.update-=TryAudit;
            Audit();
        }

        [MenuItem("Tools/HideAndShoot/Inspect FPS Rig and Pistol")]
        public static void Audit()
        {
            var sb=new StringBuilder();
            sb.AppendLine("FPS RIG DIAGNOSIS (EDIT-TIME PREFAB, NOT PLAY)");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if(source==null){sb.AppendLine("ERROR no PlayerAvatar.prefab");Save(sb);return;}
            var player=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var rig=player.transform.Find("FPSRig_OWNER_ONLY");
                var arms=rig?.Find("FPSArmsRig_TO_BUILD/FPS_KyleArmsSkinned");
                var gunMount=rig?.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t=>t.name=="PistolViewModel_TO_BUILD");
                var gun=gunMount?.Find("FPS_QuaterniusPistol");
                var camera=rig?.GetComponentInChildren<Camera>(true);
                var controller=arms?.GetComponentInChildren<Animator>(true);
                sb.AppendLine($"FPS rig={rig!=null}, visible={rig?.gameObject.activeSelf}");
                sb.AppendLine($"Camera={camera!=null}, mask={camera?.cullingMask}, near={camera?.nearClipPlane}");
                sb.AppendLine($"Arms={arms!=null}, local={arms?.localPosition}, holder={arms?.parent?.localPosition}");
                sb.AppendLine($"Gun={gun!=null}, local={gunMount?.localPosition}, rotation={gunMount?.localEulerAngles}, layer={gun?.gameObject.layer}");
                sb.AppendLine($"Arms Animator={controller!=null}, avatarIsHuman={controller?.avatar?.isHuman}, controller={controller?.runtimeAnimatorController?.name}");
                if(controller!=null && controller.avatar!=null && controller.avatar.isHuman)
                {
                    foreach(var human in new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand})
                    {
                        Transform bone=null;
                        try{bone=controller.GetBoneTransform(human);}catch{}
                        sb.AppendLine(human+" bone="+(bone!=null?bone.name:"NULL")+
                            " position="+(bone!=null?bone.position.ToString("F3"):"null"));
                    }
                }
                foreach(var root in new[]{arms,gun})
                    if(root!=null)
                        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                        {
                            sb.AppendLine("Renderer "+renderer.name+
                                " enabled="+renderer.enabled+" layer="+renderer.gameObject.layer+
                                " mat="+String.Join(",",renderer.sharedMaterials.Select(m=>m?.name??"NULL"))+
                                " center="+renderer.bounds.center+" size="+renderer.bounds.size+
                                " localBounds="+(renderer is SkinnedMeshRenderer skin?skin.localBounds.ToString():"n/a"));
                            if(renderer is SkinnedMeshRenderer sm && sm.sharedMesh!=null)
                                sb.AppendLine("SkinnedMesh "+sm.sharedMesh.name+
                                    " vertices="+sm.sharedMesh.vertexCount+
                                    " triangles="+sm.sharedMesh.triangles.Length/3+
                                    " bones="+sm.bones.Length);
                        }
                foreach(var name in new[]{"Idle_ReadyPistol","Locomotion_Forward_Walk","Locomotion_Forward_Run","Fire_Handgun","Aim_Idle"})
                {
                    var path="Assets/HideAndShoot/ThirdParty/Mixamo/"+name+".fbx";
                    var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                        .Where(c=>!c.name.StartsWith("__preview",StringComparison.OrdinalIgnoreCase));
                    foreach(var clip in clips)
                        sb.AppendLine("CLIP "+name+": "+clip.name+" length="+clip.length+
                                      " looping="+clip.isLooping+
                                      " humanMotion="+clip.humanMotion+" legacy="+clip.legacy);
                }
                var controllerAsset=AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    "Assets/HideAndShoot/Animations/AC_HumanoidShared.controller");
                if(controllerAsset!=null)
                {
                    foreach(var layer in controllerAsset.layers)
                        foreach(var child in layer.stateMachine.states)
                        {
                            sb.AppendLine("STATE "+child.state.name+
                                " motion="+child.state.motion?.name);
                            if(child.state.motion is BlendTree tree)
                                foreach(var item in tree.children)
                                    sb.AppendLine("BLEND "+item.threshold+": "+
                                        item.motion?.name+
                                        " looping="+(item.motion is AnimationClip c &&
                                                     c.isLooping));
                        }
                }
            }
            catch(Exception e){sb.AppendLine("ERROR "+e);}
            finally{PrefabUtility.UnloadPrefabContents(player);}
            Save(sb);
        }
        private static void Save(StringBuilder sb)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report,sb.ToString());
            Debug.Log("[HideAndShoot/FPS] Rig diagnosis saved to "+Report);
        }
    }
}
