using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;
using HideAndShoot.Networking;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class InitialPrefabBuilder
    {
        private const string Root = "Assets/HideAndShoot";
        private const string Inputs = Root + "/ThirdParty";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string MaterialRoot = Root + "/Materials";
        private const string KyleFBX = Inputs + "/RobotKyle/Models/Robot Kyle.fbx";
        private const string KyleColor = Inputs + "/RobotKyle/Textures/Robot_Color.tga";
        private const string KyleNormal = Inputs + "/RobotKyle/Textures/Robot_Normal.tga";
        private const string PistolFBX = Inputs + "/Quaternius/Pistols/Pistol_1.fbx";
        private const string AmmoFBX = Inputs + "/AmmoPickup/Models/Ammo_Pack_Pistol.fbx";
        private const string AmmoPalette = Inputs + "/AmmoPickup/Textures/T_PCL_Palette_col.png";
        private const string WorldVisualPath = PrefabRoot + "/RobotKyleWorldVisual.prefab";
        private const string PlayerPath = PrefabRoot + "/PlayerAvatar.prefab";
        private const string NPCPath = PrefabRoot + "/NPCActor.prefab";
        private const string AmmoPath = PrefabRoot + "/AmmoPickup.prefab";

        static InitialPrefabBuilder() => EditorApplication.update += OnEditorUpdate;

        private static void OnEditorUpdate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(WorldVisualPath) && File.Exists(PlayerPath) &&
                File.Exists(NPCPath) && File.Exists(AmmoPath))
            {
                EditorApplication.update -= OnEditorUpdate;
                return;
            }
            if (!File.Exists(Root + "/Scenes/GameScene.unity") ||
                AssetDatabase.LoadAssetAtPath<GameObject>(KyleFBX) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(PistolFBX) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(AmmoFBX) == null)
                return;
            EditorApplication.update -= OnEditorUpdate;
            try { BuildMissingPrefabs(); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError("[HideAndShoot] Prefab generation paused; run Tools/HideAndShoot/Generate Missing Prefabs after correcting issue.");
            }
        }

        [MenuItem("Tools/HideAndShoot/Generate Missing Prefabs")]
        public static void BuildMissingPrefabs()
        {
            Directory.CreateDirectory(PrefabRoot);
            Directory.CreateDirectory(MaterialRoot);
            int hitLayer = GetOrAddLayer("HitCapsule");
            int visualLayer = GetOrAddLayer("WorldActor");
            int pickupLayer = GetOrAddLayer("AmmoPickup");

            var kyle = AssetDatabase.LoadAssetAtPath<GameObject>(KyleFBX);
            var pistol = AssetDatabase.LoadAssetAtPath<GameObject>(PistolFBX);
            var ammo = AssetDatabase.LoadAssetAtPath<GameObject>(AmmoFBX);
            if (kyle == null || pistol == null || ammo == null)
                throw new InvalidOperationException("Source FBX has not finished importing.");

            Material kyleMat = PrepareMaterial("RobotKyle_LowMetallic.mat",
                AssetDatabase.LoadAssetAtPath<Texture2D>(KyleColor),
                AssetDatabase.LoadAssetAtPath<Texture2D>(KyleNormal),
                .15f, .35f, Color.white);
            Material pistolMat = PrepareMaterial("LowPolyPistol_MutedGrey.mat",null,null,
                .12f,.35f,new Color(.30f,.33f,.37f));
            Material ammoMat = PrepareMaterial("AmmoPickup_SubtleEmission.mat",
                AssetDatabase.LoadAssetAtPath<Texture2D>(AmmoPalette),null,
                .02f,.25f,Color.white);
            if(ammoMat.HasProperty("_EmissionColor"))
            {
                ammoMat.EnableKeyword("_EMISSION");
                ammoMat.SetColor("_EmissionColor",new Color(.25f,.15f,.025f));
                EditorUtility.SetDirty(ammoMat);
            }

            if(!File.Exists(WorldVisualPath))
                MakeWorldVisual(kyle,pistol,kyleMat,pistolMat,visualLayer);
            if(!File.Exists(PlayerPath))
                MakePlayer(hitLayer);
            if(!File.Exists(NPCPath))
                MakeNPC(hitLayer);
            if(!File.Exists(AmmoPath))
                MakeAmmo(ammo,ammoMat,pickupLayer);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[HideAndShoot] WorldVisual/PlayerAvatar/NPCActor/AmmoPickup prefab shells generated. " +
                      "Gameplay and FP skinned arms are separate implementation tasks.");
        }

        private static Material PrepareMaterial(string file, Texture2D color, Texture2D normal,
            float metallic,float smooth,Color tint)
        {
            string path = MaterialRoot+"/"+file;
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null)
            {
                var shader=Shader.Find("Universal Render Pipeline/Lit");
                if(shader==null)shader=Shader.Find("Standard");
                mat=new Material(shader){name=Path.GetFileNameWithoutExtension(file)};
                AssetDatabase.CreateAsset(mat,path);
            }
            if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",tint);
            if(mat.HasProperty("_Color"))mat.SetColor("_Color",tint);
            if(mat.HasProperty("_BaseMap") && color!=null)mat.SetTexture("_BaseMap",color);
            if(mat.HasProperty("_MainTex") && color!=null)mat.SetTexture("_MainTex",color);
            if(mat.HasProperty("_BumpMap") && normal!=null)
            {
                mat.SetTexture("_BumpMap",normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",metallic);
            if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",smooth);
            if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",smooth);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetLayerRecursively(GameObject go,int layer)
        {
            go.layer=layer;
            foreach(Transform c in go.transform)
                SetLayerRecursively(c.gameObject,layer);
        }

        private static GameObject InstantiateModel(GameObject model,Transform parent)
        {
            var obj=PrefabUtility.InstantiatePrefab(model,parent) as GameObject;
            if (obj==null)throw new InvalidOperationException("Could not instantiate "+model.name);
            obj.transform.localPosition=Vector3.zero;
            obj.transform.localRotation=Quaternion.identity;
            obj.transform.localScale=Vector3.one;
            return obj;
        }

        private static void SetMaterial(GameObject go,Material mat)
        {
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats=r.sharedMaterials;
                if(mats.Length==0)mats=new Material[1];
                for(int i=0;i<mats.Length;i++)mats[i]=mat;
                r.sharedMaterials=mats;
            }
        }

        private static void MakeWorldVisual(GameObject kyle,GameObject pistol,
            Material kyleMat,Material pistolMat,int visualLayer)
        {
            var root=new GameObject("RobotKyleWorldVisual");
            try
            {
                var body=InstantiateModel(kyle,root.transform);
                body.name="RobotKyle";
                SetMaterial(body,kyleMat);
                var animator=body.GetComponentInChildren<Animator>(true);
                if(animator==null)
                    animator=body.AddComponent<Animator>();
                if(animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    Debug.LogWarning("[HideAndShoot] RobotKyle's Humanoid Avatar needs validation in Rig settings.");

                Transform hand=null;
                if(animator.avatar!=null && animator.avatar.isValid && animator.avatar.isHuman)
                {
                    try { hand=animator.GetBoneTransform(HumanBodyBones.RightHand); }
                    catch(Exception) { }
                }
                if(hand==null)
                    hand=body.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t=>t.name.Contains("Right_Wrist_Joint"));

                // Final grip offsets to be calibrated after visual animation preview.
                if(hand!=null)
                {
                    var socket=new GameObject("RightHandSocket");
                    socket.transform.SetParent(hand,false);
                    socket.transform.localPosition=new Vector3(.03f,-.02f,.08f);
                    socket.transform.localRotation=Quaternion.Euler(90,0,0);
                    var model=InstantiateModel(pistol,socket.transform);
                    model.name="Pistol_ProvisionalCandidate";
                    SetMaterial(model,pistolMat);
                }
                else
                    Debug.LogWarning("[HideAndShoot] Robot Kyle right wrist not found; pistol will be attached after Avatar audit.");

                SetLayerRecursively(root,visualLayer);
                PrefabUtility.SaveAsPrefabAsset(root,WorldVisualPath);
                Debug.Log("[HideAndShoot] Robot Kyle WorldVisual ready. Hand socket "+(hand!=null?"created":"pending"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void AddDamageCapsule(Transform parent,int layer)
        {
            var hit=new GameObject("DamageCapsule_ONE_HITBOX");
            hit.transform.SetParent(parent,false);
            hit.transform.localPosition=new Vector3(0,1.0f,0);
            hit.layer=layer;
            var capsule=hit.AddComponent<CapsuleCollider>();
            capsule.center=Vector3.zero;
            capsule.height=2f;
            capsule.radius=.33f;
            capsule.direction=1;
            capsule.isTrigger=true;
        }

        private static void AddVisual(GameObject root)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(WorldVisualPath);
            if(prefab==null)throw new InvalidOperationException("WorldVisual prefab missing");
            var visual=PrefabUtility.InstantiatePrefab(prefab,root.transform) as GameObject;
            visual.name="WorldVisual";
        }

        private static void MakePlayer(int hitLayer)
        {
            var root=new GameObject("PlayerAvatar");
            try
            {
                root.AddComponent<NetworkObject>();
                var controller=root.AddComponent<CharacterController>();
                controller.center=new Vector3(0,1,0);
                controller.height=2f;
                controller.radius=.33f;
                root.AddComponent<OwnerNetworkTransform>();
                AddDamageCapsule(root.transform,hitLayer);
                AddVisual(root);
                var fp=new GameObject("FPSRig_OWNER_ONLY");
                fp.transform.SetParent(root.transform,false);
                fp.transform.localPosition=new Vector3(0,1.7f,0);
                var camera=new GameObject("FirstPersonCamera");
                camera.transform.SetParent(fp.transform,false);
                camera.transform.localPosition=Vector3.zero;
                var cameraComponent=camera.AddComponent<Camera>();
                cameraComponent.enabled=false;
                cameraComponent.fieldOfView=75f;
                // Render only from a local controller and exclude the full body using culling mask.
                cameraComponent.cullingMask &= ~(1<<LayerMask.NameToLayer("WorldActor"));
                var arms=new GameObject("FPSArmsRig_TO_BUILD");
                arms.transform.SetParent(fp.transform,false);
                var viewPistol=new GameObject("PistolViewModel_TO_BUILD");
                viewPistol.transform.SetParent(fp.transform,false);
                var death=new GameObject("DeathCameraAnchor");
                death.transform.SetParent(root.transform,false);
                death.transform.localPosition=new Vector3(0,2.4f,2.5f);
                PrefabUtility.SaveAsPrefabAsset(root,PlayerPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void MakeNPC(int hitLayer)
        {
            var root=new GameObject("NPCActor");
            try
            {
                root.AddComponent<NetworkObject>();
                var agent=root.AddComponent<NavMeshAgent>();
                agent.speed=2.2f;agent.angularSpeed=180;agent.acceleration=7.5f;
                agent.radius=.33f;agent.height=2f;
                root.AddComponent<NetworkTransform>(); // Server-authoritative by default.
                AddDamageCapsule(root.transform,hitLayer);
                AddVisual(root);
                PrefabUtility.SaveAsPrefabAsset(root,NPCPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void MakeAmmo(GameObject model,Material material,int layer)
        {
            var root=new GameObject("AmmoPickup");
            try
            {
                root.layer=layer;
                root.AddComponent<NetworkObject>();
                root.AddComponent<NetworkTransform>();
                var trigger=root.AddComponent<SphereCollider>();
                trigger.isTrigger=true;
                trigger.radius=.46f;
                trigger.center=new Vector3(0,.30f,0);
                var visual=InstantiateModel(model,root.transform);
                visual.name="AmmoBoxVisual";
                visual.transform.localScale=Vector3.one*2.5f;
                SetMaterial(visual,material);
                SetLayerRecursively(visual,layer);
                PrefabUtility.SaveAsPrefabAsset(root,AmmoPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static int GetOrAddLayer(string name)
        {
            int layer=LayerMask.NameToLayer(name);
            if(layer>=0)return layer;
            var target=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if(target==null)throw new InvalidOperationException("TagManager not accessible");
            var obj=new SerializedObject(target);
            var prop=obj.FindProperty("layers");
            for(int i=8;i<32;i++)
            {
                var item=prop.GetArrayElementAtIndex(i);
                if(string.IsNullOrEmpty(item.stringValue))
                {
                    item.stringValue=name;
                    obj.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                    return i;
                }
            }
            throw new InvalidOperationException("No free User layer for "+name);
        }
    }
}
