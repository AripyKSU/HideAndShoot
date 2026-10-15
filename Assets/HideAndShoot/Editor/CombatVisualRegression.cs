using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using HideAndShoot.Combat;
using HideAndShoot.Player;
using HideAndShoot.World;

namespace HideAndShoot.EditorTools
{
    [InitializeOnLoad]
    public static class CombatVisualRegression
    {
        static CombatVisualRegression()
        {
            EditorApplication.update+=RunWhenReady;
        }

        private static void RunWhenReady()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating)return;
            EditorApplication.update-=RunWhenReady;
            if(!File.Exists("Assets/HideAndShoot/QA/CombatVisualRegression.txt"))Verify();
        }

        private static void Check(bool condition,string message,StringBuilder report)
        {
            if(!condition)throw new InvalidOperationException(message);
            report.AppendLine("PASS | "+message);
        }

        [MenuItem("Tools/HideAndShoot/Verify Combat Visuals and Aim")]
        public static void Verify()
        {
            var report=new StringBuilder("Unity edit-mode physics and rendered visual regression\n");
            var root=new GameObject("CombatVisualRegression_TEMP");
            root.hideFlags=HideFlags.HideAndDontSave;
            root.transform.position=new Vector3(10000,1000,0);
            try
            {
                FirstPersonPistolInstaller.Install();
                var shooter=new GameObject("Shooter");
                shooter.transform.SetParent(root.transform,false);
                var self=GameObject.CreatePrimitive(PrimitiveType.Cube);
                self.transform.SetParent(shooter.transform,false);
                self.transform.localPosition=Vector3.forward*.25f;
                self.transform.localScale=Vector3.one*.1f;
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform,false);
                wall.transform.localPosition=Vector3.forward*.75f;
                wall.transform.localScale=new Vector3(1,1,.2f);
                var actor=new GameObject("Target",typeof(CapsuleCollider));
                actor.transform.SetParent(root.transform,false);
                actor.transform.localPosition=Vector3.forward*3f;
                actor.layer=LayerMask.NameToLayer("HitCapsule");
                actor.GetComponent<CapsuleCollider>().isTrigger=true;
                actor.GetComponent<CapsuleCollider>().radius=.25f;
                var ray=new Ray(root.transform.position,Vector3.forward);
                Physics.SyncTransforms();
                Check(PistolAimUtility.TryFirstHit(ray,125,shooter.transform,out var hit) &&
                    hit.collider.gameObject==wall && Mathf.Abs(hit.distance-.65f)<.001f,
                    "Near wall blocks target; exact endpoint below 2m; self ignored",report);
                var endpoint=hit.point;
                var muzzle=ray.origin+new Vector3(.33f,-.26f,.52f);
                PistolLaserTracer.Show(muzzle,endpoint);
                var tracer=UnityEngine.Object.FindFirstObjectByType<PistolLaserTracer>();
                tracer.transform.SetParent(root.transform,true);
                var line=tracer.GetComponent<LineRenderer>();
                Check((line.GetPosition(0)-muzzle).sqrMagnitude<.000001f &&
                    (line.GetPosition(1)-endpoint).sqrMagnitude<.000001f,
                    "Offset muzzle tracer ends at the same near-wall raycast point",report);
                UnityEngine.Object.DestroyImmediate(tracer.gameObject);
                UnityEngine.Object.DestroyImmediate(wall);
                var pickup=GameObject.CreatePrimitive(PrimitiveType.Cube);
                pickup.transform.SetParent(root.transform,false);
                pickup.transform.localPosition=Vector3.forward;
                pickup.layer=LayerMask.NameToLayer("AmmoPickup");
                pickup.GetComponent<Collider>().isTrigger=true;
                Physics.SyncTransforms();
                Check(PistolAimUtility.TryFirstHit(ray,125,shooter.transform,out hit) &&
                    hit.collider.gameObject==actor,"Ammo trigger ignored; capsule selected",report);
                UnityEngine.Object.DestroyImmediate(actor);
                Physics.SyncTransforms();
                Check(!PistolAimUtility.TryFirstHit(ray,125,shooter.transform,out hit),
                    "Empty aim uses full weapon range",report);
                UnityEngine.Object.DestroyImmediate(pickup);
                UnityEngine.Object.DestroyImmediate(shooter);
                RenderPlayer(root.transform,report);
                RenderAmmo(root.transform,report);
                RenderHud(root.transform,report);
                report.AppendLine("Live multiplayer input and lag compensation require a connected play session.");
            }
            catch(Exception error)
            {
                report.AppendLine("FAIL | "+error);
                Debug.LogException(error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Directory.CreateDirectory("Assets/HideAndShoot/QA");
                File.WriteAllText("Assets/HideAndShoot/QA/CombatVisualRegression.txt",report.ToString());
                Debug.Log(report.ToString());
            }
        }

        private static PlayableGraph Sample(Animator animator,string clipName)
        {
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/HideAndShoot/ThirdParty/Mixamo/"+clipName+".fbx")
                .OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
            var graph=PlayableGraph.Create("VisualRegressionPose");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable=AnimationClipPlayable.Create(graph,clip);
            playable.SetTime(.2f);
            var output=AnimationPlayableOutput.Create(graph,"Pose",animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);
            return graph;
        }

        private static Camera CameraFor(Transform root)
        {
            var camera=new GameObject("QA_Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root,false);
            camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.08f,.13f,.18f);
            camera.nearClipPlane=.01f;
            camera.farClipPlane=20;
            camera.fieldOfView=45;
            var light=new GameObject("QA_Light",typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root,false);
            light.type=LightType.Directional;
            light.intensity=2f;
            light.transform.rotation=Quaternion.Euler(40,-30,0);
            return camera;
        }

        private static void Save(Camera camera,string name)
        {
            var target=new RenderTexture(1280,720,24);
            var previous=RenderTexture.active;
            var originalTarget=camera.targetTexture;
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;
                camera.aspect=1280f/720;
                camera.Render();
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1280,720),0,0);
                texture.Apply();
                File.WriteAllBytes("Assets/HideAndShoot/QA/"+name+".png",texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=originalTarget;
                RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void RenderPlayer(Transform root,StringBuilder report)
        {
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab"),root,false);
            var fps=player.transform.Find("FPSRig_OWNER_ONLY");
            fps.gameObject.SetActive(false);
            var world=player.transform.Find("WorldVisual");
            var animator=world.GetComponentInChildren<Animator>(true);
            var graph=Sample(animator,"Idle_ReadyPistol");
            var mount=SharedPistolVisual.EnsureWorldPistol(world);
            animator.GetComponent<PistolFingerGrip>().ApplyGrip();
            SharedPistolVisual.PoseGrip(mount,animator,player.transform.rotation);
            var camera=CameraFor(root);
            camera.transform.localPosition=new Vector3(1.2f,1.5f,1.8f);
            camera.transform.LookAt(root.position+Vector3.up*1.3f);
            Save(camera,"Pistol_World_QA");
            SharedPistolVisual.TryLocalBounds(mount,mount,out var measured);
            report.AppendLine("Measured pistol size="+measured.size.ToString("F6"));
            Check(SharedPistolVisual.TryLocalBounds(mount,mount,out var bounds) &&
                Mathf.Abs(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)-.26f)<.002f &&
                bounds.size.z>bounds.size.x*2,"World pistol faces forward, max dimension 26cm",report);
            report.AppendLine("Pistol local size="+bounds.size.ToString("F4"));
            var firstScale=mount.GetChild(0).localScale;
            player.transform.Rotate(0,90,0);
            SharedPistolVisual.EnsureWorldPistol(world);
            Check((firstScale-mount.GetChild(0).localScale).sqrMagnitude<.000001f,
                "Pistol normalization stable across yaw and repeated setup",report);
            player.transform.localRotation=Quaternion.identity;
            graph.Destroy();
            world.gameObject.SetActive(false);
            fps.gameObject.SetActive(true);
            var ownerCamera=fps.GetComponentInChildren<Camera>(true);
            ownerCamera.enabled=false;
            ownerCamera.fieldOfView=75;
            var armRoot=fps.Find("FPSArmsRig_TO_BUILD");
            var fpAnimator=armRoot.GetComponentInChildren<Animator>(true);
            graph=Sample(fpAnimator,"Aim_Idle");
            var arm=armRoot.Find("FPS_KyleArmsSkinned");
            FirstPersonRightArmMesh.Restrict(fpAnimator,arm);
            var hand=fpAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            var fpMount=fps.GetComponentsInChildren<Transform>(true).First(t=>t.name=="PistolViewModel_TO_BUILD");
            fpMount=SharedPistolVisual.EnsureOwnerGun(ownerCamera,fpMount,hand);
            fpAnimator.GetComponent<PistolFingerGrip>().ApplyGrip();
            report.AppendLine("Right-hand grip muscles="+HumanTrait.MuscleName.Count(n=>n.StartsWith("Right ") && n.Contains("Stretched")));
            foreach(var renderer in arm.GetComponentsInChildren<MeshRenderer>(true))
                if(!renderer.transform.IsChildOf(fpMount))renderer.enabled=false;
            armRoot.position+=ownerCamera.ViewportToWorldPoint(new Vector3(.70f,.28f,.62f))-hand.position;
            SharedPistolVisual.PoseGrip(fpMount,fpAnimator,Quaternion.LookRotation(
                ownerCamera.transform.position+ownerCamera.transform.forward*125-hand.position,ownerCamera.transform.up));
            Save(ownerCamera,"Pistol_FPS_QA");
            ownerCamera.fieldOfView=43;
            armRoot.position+=ownerCamera.ViewportToWorldPoint(new Vector3(.55f,.18f,.62f))-hand.position;
            SharedPistolVisual.PoseGrip(fpMount,fpAnimator,Quaternion.LookRotation(
                ownerCamera.transform.position+ownerCamera.transform.forward*125-hand.position,ownerCamera.transform.up));
            Save(ownerCamera,"Pistol_ADS_QA");
            Check(fpMount.parent==hand && fpMount.Find("Muzzle")!=null,
                "Owner gun parented to wrist with model-derived muzzle",report);
            graph.Destroy();
            player.SetActive(false);
        }

        private static void RenderAmmo(Transform root,StringBuilder report)
        {
            var ammo=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/HideAndShoot/Prefabs/AmmoPickup.prefab"),root,false);
            var actor=ammo.GetComponent<AmmoPickupActor>();
            actor.PrepareVisual();
            var visual=ammo.transform.GetChild(0);
            Check(SharedPistolVisual.TryLocalBounds(visual,ammo.transform,out var bounds) &&
                Mathf.Abs(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)-.42f)<.002f &&
                bounds.center.magnitude<.002f,"Ammo box visible at 42cm and centered above floor",report);
            var scale=visual.localScale;
            actor.OnNetworkDespawn();
            actor.PrepareVisual();
            Check((scale-visual.localScale).sqrMagnitude<.000001f,
                "Pooled ammo keeps its size after visual reset",report);
            var camera=CameraFor(root);
            camera.transform.localPosition=new Vector3(.7f,.45f,.8f);
            camera.transform.LookAt(root.position);
            Save(camera,"Ammo_Model_QA");
            ammo.SetActive(false);
        }

        private static void RenderHud(Transform root,StringBuilder report)
        {
            var hud=new GameObject("QA_HUD").AddComponent<HideAndShoot.UI.GameHUDPresenter>();
            hud.transform.SetParent(root,false);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=hud.GetType();
            type.GetField("font",flags).SetValue(hud,HideAndShoot.UI.TacticalUI.Font);
            type.GetMethod("CreateCanvas",flags).Invoke(hud,null);
            var canvas=hud.GetComponentInChildren<Canvas>(true);
            var camera=CameraFor(root);
            camera.transform.localPosition=new Vector3(0,0,-3);
            canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;
            canvas.planeDistance=1;
            foreach(var text in canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if(text.name!="Text")continue;
                string panel=text.transform.parent.name;
                if(panel=="RoundInfo")text.text="라운드 2  ·  3승 먼저 달성";
                else if(panel=="AliveCount")text.text="3명 생존";
                else if(panel=="Ammo")text.text="탄약  2 <size=20>/ 10</size>";
                else if(panel=="ControlHints")text.text="오른쪽 클릭 조준  →  왼쪽 클릭 사격\nTab 순위  ·  Esc 메뉴  ·  [ / ] 감도";
                else if(panel=="AmmoFeedback")text.text="탄약 +5";
                else continue;
                text.transform.parent.gameObject.SetActive(true);
            }
            var reticle=(GameObject)type.GetField("reticle",flags).GetValue(hud);
            reticle.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Check(reticle.GetComponent<RectTransform>().anchoredPosition==Vector2.zero,
                "Reticle geometry anchored at exact viewport center",report);
            Save(camera,"HUD_QA");
        }
        public static void Inspect()
        {
            var report = new StringBuilder();
            foreach (var path in new[] {
                "Assets/HideAndShoot/Resources/PistolViewModel.fbx",
                "Assets/HideAndShoot/ThirdParty/AmmoPickup/Models/Ammo_Pack_Pistol.fbx",
                "Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab" })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) { report.AppendLine("Missing: " + path); continue; }
                var go = UnityEngine.Object.Instantiate(source);
                report.AppendLine(path);
                foreach (var mesh in go.GetComponentsInChildren<MeshFilter>(true))
                    report.AppendLine(mesh.name + " local bounds=" + mesh.sharedMesh.bounds + " rotation=" + mesh.transform.rotation.eulerAngles + " scale=" + mesh.transform.lossyScale);
                foreach (var animator in go.GetComponentsInChildren<Animator>(true))
                {
                    if (animator.avatar == null || !animator.avatar.isHuman) continue;
                    animator.Rebind();
                    animator.Play("Aim", 0, .15f);
                    animator.Update(0f);
                    var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                    report.AppendLine(animator.name + " hand pos=" + hand.position + " rotation=" + hand.rotation.eulerAngles + " fingers=" + (middle != null ? (middle.position - hand.position).ToString("F4") : "none"));
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory("Assets/HideAndShoot/QA");
            File.WriteAllText("Assets/HideAndShoot/QA/CombatVisualInspection.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
