using System;
using System.IO;
using System.Linq;
using HideAndShoot.Player;
using HideAndShoot.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HideAndShoot.EditorTools
{
    /// <summary>Authoring-only player prefab and shared Kyle locomotion configuration.</summary>
    [InitializeOnLoad]
    public static class PlayerPrefabInstaller
    {
        private const string PlayerPrefab="Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab";
        private const string WorldPrefab="Assets/HideAndShoot/Prefabs/RobotKyleWorldVisual.prefab";
        private const string AnimDir="Assets/HideAndShoot/Animations";
        private const string ControllerPath=AnimDir+"/AC_HumanoidShared.controller";
        private const string ClipRoot="Assets/HideAndShoot/ThirdParty/Mixamo/";

        static PlayerPrefabInstaller() => EditorApplication.update += TryInstall;

        private static void TryInstall()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
               EditorApplication.isUpdating) return;
            EditorApplication.update -= TryInstall;
            Install();
        }

        [MenuItem("Tools/HideAndShoot/Install Player Movement and Shared Animations")]
        public static void Install()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab)==null) return;
            Directory.CreateDirectory(AnimDir);
            int layer=GetOrAddLayer("LocalHiddenBody");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if(controller==null) controller=BuildSharedController();
            if(controller!=null)
            {
                EnsureVictoryState(controller);
                InstallAnimator(controller);
            }

            var root=PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                bool updated=false;
                if(root.GetComponent<PlayerLocalMotor>()==null)
                {
                    root.AddComponent<PlayerLocalMotor>();
                    updated=true;
                }
                if(root.GetComponent<HostPlayerLife>()==null)
                {
                    root.AddComponent<HostPlayerLife>();
                    updated=true;
                }
                if(root.GetComponent<RemoteMotionAnimator>()==null)
                {
                    root.AddComponent<RemoteMotionAnimator>();
                    updated=true;
                }
                var combat=root.GetComponent<PistolCombatController>();
                if(combat==null)
                {
                    combat=root.AddComponent<PistolCombatController>();
                    updated=true;
                }
                var values=new SerializedObject(combat);
                var fire=values.FindProperty("pistolShot");
                var empty=values.FindProperty("emptyClick");
                var fireClip=AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/HideAndShoot/Resources/Audio/Pistol.wav");
                var emptyClip=AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/HideAndShoot/ThirdParty/Audio/SFX_Pistol_Empty_Source.wav");
                if(fire!=null && fireClip!=null && fire.objectReferenceValue!=fireClip)
                {
                    fire.objectReferenceValue=fireClip;
                    updated=true;
                }
                if(empty!=null && emptyClip!=null && empty.objectReferenceValue!=emptyClip)
                {
                    empty.objectReferenceValue=emptyClip;
                    updated=true;
                }
                values.ApplyModifiedPropertiesWithoutUndo();
                // Keep regenerated player prefabs at the same restrained firing level.
                var audioSource=root.GetComponent<AudioSource>();
                if(audioSource==null)
                {
                    audioSource=root.AddComponent<AudioSource>();
                    audioSource.playOnAwake=false;audioSource.volume=.38f;
                    audioSource.minDistance=1.5f;audioSource.maxDistance=32f;
                    audioSource.rolloffMode=AudioRolloffMode.Logarithmic;audioSource.dopplerLevel=0f;
                    updated=true;
                }
                var camera=root.GetComponentsInChildren<Camera>(true)
                    .FirstOrDefault(x=>x.name=="FirstPersonCamera");
                if(camera!=null)
                {
                    int mask=camera.cullingMask;
                    if(LayerMask.NameToLayer("WorldActor")>=0)
                        mask |= 1<<LayerMask.NameToLayer("WorldActor");
                    mask &= ~(1<<layer);
                    if(camera.cullingMask!=mask)
                    {
                        camera.cullingMask=mask;
                        updated=true;
                    }
                }
                if(updated)PrefabUtility.SaveAsPrefabAsset(root,PlayerPrefab);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("[HideAndShoot] PlayerLocalMotor and Kyle shared controller installed.");
        }

        /// <summary>Retrofit the already-generated controller, without replacing it.</summary>
        private static void EnsureVictoryState(AnimatorController controller)
        {
            if(controller==null || controller.layers.Length==0)return;
            var clip=LoadClip("Victory_LookAround");
            if(clip==null)return;
            if(!controller.parameters.Any(p=>p.name=="Victory"))
                controller.AddParameter("Victory",AnimatorControllerParameterType.Trigger);
            var machine=controller.layers[0].stateMachine;
            var state=machine.states.Select(child=>child.state)
                .FirstOrDefault(item=>item!=null && item.name=="Victory");
            if(state==null)
            {
                state=machine.AddState("Victory");
                state.motion=clip;
                var enter=machine.AddAnyStateTransition(state);
                enter.hasExitTime=false;
                enter.duration=.12f;
                enter.AddCondition(AnimatorConditionMode.If,0,"Victory");
            }
            else if(state.motion!=clip)state.motion=clip;
            AssetDatabase.SaveAssets();
        }

        private static AnimatorController BuildSharedController()
        {
            var clips=new [] {
                "Idle_ReadyPistol","Locomotion_Forward_Walk","Locomotion_Forward_Run",
                "Aim_Idle","Fire_Handgun","Death_Fall_Back","Victory_LookAround"
            }.Select(LoadClip).ToArray();
            if(clips[0]==null || clips[1]==null || clips[2]==null)
            {
                Debug.LogWarning("[HideAndShoot] Mixamo clips are not ready; controller remains pending.");
                return null;
            }

            var controller=AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("MoveSpeed",AnimatorControllerParameterType.Float);
            controller.AddParameter("Aim",AnimatorControllerParameterType.Bool);
            controller.AddParameter("FireOnce",AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead",AnimatorControllerParameterType.Bool);

            var sm=controller.layers[0].stateMachine;
            var locomotion=sm.AddState("Locomotion");
            var tree=new BlendTree
            {
                name="Movement_Idle_Walk_Run",
                blendType=BlendTreeType.Simple1D,
                blendParameter="MoveSpeed",
                useAutomaticThresholds=false
            };
            AssetDatabase.AddObjectToAsset(tree,controller);
            tree.AddChild(clips[0],0f);
            tree.AddChild(clips[1],2.2f);
            tree.AddChild(clips[2],5f);
            locomotion.motion=tree;
            sm.defaultState=locomotion;

            if(clips[3]!=null)
            {
                var aim=sm.AddState("Aim");
                aim.motion=clips[3];
                var enter=locomotion.AddTransition(aim);
                enter.hasExitTime=false; enter.duration=.12f;
                enter.AddCondition(AnimatorConditionMode.If,0,"Aim");
                var leave=aim.AddTransition(locomotion);
                leave.hasExitTime=false; leave.duration=.12f;
                leave.AddCondition(AnimatorConditionMode.IfNot,0,"Aim");
            }

            if(clips[4]!=null)
            {
                var fire=sm.AddState("Fire");
                fire.motion=clips[4];
                var transition=sm.AddAnyStateTransition(fire);
                transition.hasExitTime=false; transition.duration=.04f;
                transition.AddCondition(AnimatorConditionMode.If,0,"FireOnce");
                var done=fire.AddTransition(locomotion);
                done.hasExitTime=true; done.exitTime=.98f; done.duration=.08f;
            }

            if(clips[5]!=null)
            {
                var death=sm.AddState("Death");
                death.motion=clips[5];
                var transition=sm.AddAnyStateTransition(death);
                transition.hasExitTime=false;transition.duration=.08f;
                transition.AddCondition(AnimatorConditionMode.If,0,"Dead");
                // Revived or reused actors must leave Death even when their
                // explicit reset ran while the Animator was inactive or culled.
                var revived=death.AddTransition(locomotion);
                revived.hasExitTime=false;revived.duration=0f;
                revived.AddCondition(AnimatorConditionMode.IfNot,0,"Dead");
            }

            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip LoadClip(string name)
        {
            var path=ClipRoot+name+".fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c=>!c.name.StartsWith("__preview",StringComparison.OrdinalIgnoreCase));
        }

        private static void InstallAnimator(AnimatorController controller)
        {
            var root=PrefabUtility.LoadPrefabContents(WorldPrefab);
            try
            {
                var animator=root.GetComponentInChildren<Animator>(true);
                if(animator==null)
                {
                    Debug.LogWarning("[HideAndShoot] No Animator found in RobotKyleWorldVisual");
                    return;
                }
                if(animator.runtimeAnimatorController!=controller)
                {
                    animator.runtimeAnimatorController=controller;
                    animator.applyRootMotion=false;
                    PrefabUtility.SaveAsPrefabAsset(root,WorldPrefab);
                }
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }

        private static int GetOrAddLayer(string name)
        {
            int layer=LayerMask.NameToLayer(name);
            if(layer>=0)return layer;
            var target=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var serialized=new SerializedObject(target);
            var layers=serialized.FindProperty("layers");
            for(int i=8;i<32;i++)
            {
                var element=layers.GetArrayElementAtIndex(i);
                if(!string.IsNullOrEmpty(element.stringValue))continue;
                element.stringValue=name;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            throw new InvalidOperationException("No free user layers");
        }
    }
}
