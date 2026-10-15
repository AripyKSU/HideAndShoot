using System;
using System.Linq;
using HideAndShoot.Player;
using HideAndShoot.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class FirstPersonPistolInstaller
    {
        static FirstPersonPistolInstaller()=>EditorApplication.update+=TryInstall;
        private static void TryInstall()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
               EditorApplication.isUpdating)return;
            EditorApplication.update-=TryInstall;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Install FPS Pistol and Camera Layers")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            InstallGripLayer();
            foreach(var name in new[]{"PlayerAvatar","NPCActor"})
            {
                string path="Assets/HideAndShoot/Prefabs/"+name+".prefab";
                var avatar=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var world=avatar.transform.Find("WorldVisual");
                    if(world!=null)
                    {
                        var mount=SharedPistolVisual.EnsureWorldPistol(world);
                        SharedPistolVisual.PoseGrip(mount,world.GetComponentInChildren<Animator>(true),avatar.transform.rotation);
                    }
                    var rig=avatar.transform.Find("FPSRig_OWNER_ONLY");
                    if(rig!=null)
                    {
                        var animator=rig.GetComponentInChildren<Animator>(true);
                        var camera=rig.GetComponentInChildren<Camera>(true);
                        var mount=rig.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t=>t.name=="PistolViewModel_TO_BUILD");
                        var hand=animator!=null && animator.isHuman?
                            animator.GetBoneTransform(HumanBodyBones.RightHand):null;
                        mount=SharedPistolVisual.EnsureOwnerGun(camera,mount,hand);
                        SharedPistolVisual.PoseGrip(mount,animator,camera!=null?camera.transform.rotation:Quaternion.identity);
                        if(camera!=null)
                        {
                            camera.cullingMask|=1<<LayerMask.NameToLayer("FPSViewModel");
                            camera.nearClipPlane=.025f;
                        }
                        if(animator!=null)animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    }
                    PrefabUtility.SaveAsPrefabAsset(avatar,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(avatar);}
            }

            string ammoPath="Assets/HideAndShoot/Prefabs/AmmoPickup.prefab";
            var ammo=PrefabUtility.LoadPrefabContents(ammoPath);
            try
            {
                ammo.GetComponent<AmmoPickupActor>().PrepareVisual();
                var material=AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/HideAndShoot/Materials/AmmoPickup_SubtleEmission.mat");
                if(material!=null)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor",new Color(.07f,.35f,.40f));
                    EditorUtility.SetDirty(material);
                    foreach(var renderer in ammo.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled=true;
                        var materials=renderer.sharedMaterials;
                        for(int i=0;i<materials.Length;i++)materials[i]=material;
                        renderer.sharedMaterials=materials;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(ammo,ammoPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(ammo);}
            AssetDatabase.SaveAssets();
            Debug.Log("[HideAndShoot] Palm-mounted pistols, right-arm grip layer and visible ammo saved.");
        }

        private static void InstallGripLayer()
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/HideAndShoot/Animations/AC_HumanoidShared.controller");
            if(controller==null || controller.layers.Any(l=>l.name=="PistolRightArm"))return;
            var clip=AssetDatabase.LoadAllAssetsAtPath(
                "Assets/HideAndShoot/ThirdParty/Mixamo/Idle_ReadyPistol.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
            if(clip==null)throw new InvalidOperationException("Idle pistol pose missing.");
            var mask=new AvatarMask {name="PistolRightArmMask"};
            for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm,true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers,true);
            var machine=new AnimatorStateMachine {name="PistolRightArm"};
            AssetDatabase.AddObjectToAsset(mask,controller);
            AssetDatabase.AddObjectToAsset(machine,controller);
            var grip=machine.AddState("Grip");
            grip.motion=clip;
            var released=machine.AddState("Released");
            machine.defaultState=grip;
            var dead=grip.AddTransition(released);
            dead.hasExitTime=false; dead.duration=.1f;
            dead.AddCondition(AnimatorConditionMode.If,0,"Dead");
            var alive=released.AddTransition(grip);
            alive.hasExitTime=false; alive.duration=.1f;
            alive.AddCondition(AnimatorConditionMode.IfNot,0,"Dead");
            controller.AddLayer(new AnimatorControllerLayer {
                name="PistolRightArm",defaultWeight=1f,avatarMask=mask,
                blendingMode=AnimatorLayerBlendingMode.Override,stateMachine=machine
            });
            EditorUtility.SetDirty(controller);
        }
    }
}
