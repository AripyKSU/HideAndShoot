using HideAndShoot.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HideAndShoot.Player
{
    /// <summary>
    /// Owner-only responsive ground locomotion and first-person camera.
    /// No jump/reload; combat calls SetAimActive / SetMovementLocked.
    /// NetworkTransform authority stays on the owning client (host audits later).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerLocalMotor : NetworkBehaviour
    {
        [Header("Prototype tunables")]
        [SerializeField] private float walkSpeed = 2.2f;
        [SerializeField] private float sprintSpeed = 5f;
        [SerializeField] private float aimWalkSpeed = 1.5f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float lookDegreesPerPixel = .075f;
        [SerializeField, Range(1,10)] private int sensitivity = 3;

        private CharacterController controller;
        private Camera ownerCamera;
        private Transform fpsRig;
        private Transform worldVisual;
        private Animator worldAnimator;
        private Animator firstPersonAnimator;
        private Transform viewModelRoot;
        private Transform viewModelRightHand;
        private Transform firstPersonGunSocket;
        private Transform worldGunSocket;
        private float weaponRecoil;
        private float weaponBob;
        private float weaponAimBlend;
        private bool weaponPrepared;
        private bool ownerWeaponVisible=true;
        private float pitch;
        private float fallVelocity;
        private Vector3 frameVelocity;
        private bool aimActive;
        private bool movementLocked;
        private float showSensitivityUntil;
        private int ownBodyLayer=-1;
        private bool prepared;
        private float nextMotionSync;
        private float lastSentMotionSpeed=-1f;
        private float footstepDistance;
        private bool wasWalking;
        private RemoteMotionAnimator proxyAnimation;

        public Camera PlayerCamera => ownerCamera;
        public Transform PistolMuzzle => IsOwner ? firstPersonGunSocket?.Find("Muzzle") : worldGunSocket?.Find("Muzzle");
        public bool IsAimActive => aimActive;
        public Vector3 WorldVelocity => frameVelocity;
        public int Sensitivity => sensitivity;
        public float SensitivityVisibleUntil => showSensitivityUntil;

        private void Awake()
        {
            controller=GetComponent<CharacterController>();
            proxyAnimation=GetComponent<RemoteMotionAnimator>();
            fpsRig=transform.Find("FPSRig_OWNER_ONLY");
            worldVisual=transform.Find("WorldVisual");
            if(fpsRig!=null)
                ownerCamera=fpsRig.GetComponentInChildren<Camera>(true);
            if(worldVisual!=null)
            {
                worldAnimator=worldVisual.GetComponentInChildren<Animator>(true);
                worldGunSocket=SharedPistolVisual.EnsureWorldPistol(worldVisual);
            }
            if(fpsRig!=null)
            {
                viewModelRoot=fpsRig.Find("FPSArmsRig_TO_BUILD");
                if(viewModelRoot!=null)
                    firstPersonAnimator=viewModelRoot.GetComponentInChildren<Animator>(true);
            }
        }

        private void EnsureFirstPersonPistol()
        {
            var mount=fpsRig!=null?fpsRig.Find("PistolViewModel_TO_BUILD"):null;
            if(mount==null)
                foreach(var child in fpsRig.GetComponentsInChildren<Transform>(true))
                    if(child.name=="PistolViewModel_TO_BUILD"){mount=child;break;}
            firstPersonGunSocket=SharedPistolVisual.EnsureOwnerGun(ownerCamera,mount,viewModelRightHand);
        }

        private void PrepareFirstPersonWeapon()
        {
            if(weaponPrepared || firstPersonAnimator==null || viewModelRoot==null)return;
            weaponPrepared=true;
            var visual=viewModelRoot.Find("FPS_KyleArmsSkinned");
            viewModelRightHand=firstPersonAnimator.avatar!=null &&
                firstPersonAnimator.avatar.isHuman ?
                firstPersonAnimator.GetBoneTransform(HumanBodyBones.RightHand):null;
            int tris=FirstPersonRightArmMesh.Restrict(firstPersonAnimator,visual);
            if(tris<=0)
                Debug.LogError("[HideAndShoot/FPS] No RIGHT-arm polygons produced.");

            // The first-person rig should never replay the third-person full
            // body locomotion / handgun firing (often looks like reloading).
            // Hold a coherent Aim_Idle pose; apply FPS bob and recoil manually.
            firstPersonAnimator.applyRootMotion=false;
            firstPersonAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            int worldGripLayer=firstPersonAnimator.GetLayerIndex("PistolRightArm");
            if(worldGripLayer>=0)firstPersonAnimator.SetLayerWeight(worldGripLayer,0f);
            firstPersonAnimator.Play("Aim",0,0.15f);
            firstPersonAnimator.Update(0f);
            firstPersonAnimator.speed=0f;
            if(visual!=null)
                foreach(var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                    renderer.enabled=false; // copied third-person pistol hidden
            EnsureFirstPersonPistol();
            RefreshOwnerWeaponVisibility();
            if(firstPersonGunSocket!=null)
                Debug.Log("[HideAndShoot/FPS] Right-hand gun mounted; triangles="+tris);
        }

        public override void OnNetworkSpawn()
        {
            PreparePresentation(IsOwner);
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                Cursor.visible=true;
                Cursor.lockState=CursorLockMode.None;
            }
            if(ownerCamera!=null)ownerCamera.enabled=false;
        }

        private void PreparePresentation(bool local)
        {
            if(prepared) return;
            prepared=true;
            if(ownerCamera!=null)
            {
                ownerCamera.enabled=local;
                if(local)
                {
                    ownerCamera.tag="MainCamera";
                    ownerCamera.fieldOfView=75f;
                    ownerCamera.nearClipPlane=.025f;
                    int fpsLayer=LayerMask.NameToLayer("FPSViewModel");
                    if(fpsLayer>=0)
                    {
                        ownerCamera.cullingMask|=(1<<fpsLayer);
                        // Both arms and gun are OWNER-ONLY and render on the
                        // dedicated layer. Remote avatars never enable FPSRig.
                        foreach(var item in fpsRig.GetComponentsInChildren<Transform>(true))
                            if(item!=fpsRig)item.gameObject.layer=fpsLayer;
                    }
                    ownBodyLayer=LayerMask.NameToLayer("LocalHiddenBody");
                    // Never hide all WorldActor models: only the local owner's world body.
                    int worldLayer=LayerMask.NameToLayer("WorldActor");
                    if (worldLayer>=0) ownerCamera.cullingMask|=(1<<worldLayer);
                    if(ownBodyLayer>=0 && worldVisual!=null)
                    {
                        SetLayerRecursive(worldVisual.gameObject,ownBodyLayer);
                        ownerCamera.cullingMask&=~(1<<ownBodyLayer);
                    }
                    var listener=ownerCamera.GetComponent<AudioListener>();
                    if(listener==null) listener=ownerCamera.gameObject.AddComponent<AudioListener>();
                    listener.enabled=true;
                    foreach(var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    {
                        if(cam!=ownerCamera && cam.name=="SceneOverviewCamera")
                        {
                            cam.enabled=false;
                            var otherListener=cam.GetComponent<AudioListener>();
                            if(otherListener!=null)otherListener.enabled=false;
                        }
                    }
                    Cursor.visible=false;
                    Cursor.lockState=CursorLockMode.Locked;
                }
                else
                {
                    var listener=ownerCamera.GetComponent<AudioListener>();
                    if(listener!=null)listener.enabled=false;
                }
            }
            if(fpsRig!=null)
            {
                fpsRig.gameObject.SetActive(local);
                if(local)PrepareFirstPersonWeapon();
                if(!local)
                    foreach(var renderer in fpsRig.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled=false;
            }
        }

        private static void SetLayerRecursive(GameObject root,int layer)
        {
            root.layer=layer;
            foreach(Transform child in root.transform)
                SetLayerRecursive(child.gameObject,layer);
        }

        /// <summary>
        /// Host chooses one of nine unique spawn anchors each round; owner applies
        /// the reposition locally because OwnerNetworkTransform uses owner authority.
        /// </summary>
        public void HostAssignRoundSpawn(Vector3 position,Quaternion rotation,int roundId)
        {
            if(!IsServer)return;
            ConcealRoundSpawnRpc(roundId);
            ApplyRoundSpawn(position,rotation);
            AssignRoundSpawnRpc(position,rotation);
        }

        [Rpc(SendTo.Everyone)]
        private void ConcealRoundSpawnRpc(int roundId)
        {
            HideAndShoot.UI.RoundTransitionCurtain.Begin(roundId);
        }

        [Rpc(SendTo.Owner)]
        private void AssignRoundSpawnRpc(Vector3 position,Quaternion rotation)
        {
            ApplyRoundSpawn(position,rotation);
        }

        private void ApplyRoundSpawn(Vector3 position,Quaternion rotation)
        {
            var cc=controller!=null?controller:GetComponent<CharacterController>();
            bool wasEnabled=cc!=null && cc.enabled;
            if(wasEnabled)cc.enabled=false;
            var sync=GetComponent<OwnerNetworkTransform>();
            if(sync!=null && sync.CanCommitToTransform)
                sync.Teleport(position,rotation,transform.localScale);
            else
                transform.SetPositionAndRotation(position,rotation);
            if(wasEnabled)cc.enabled=true;
            pitch=0;
            fallVelocity=0;
            frameVelocity=Vector3.zero;
            if(fpsRig!=null)fpsRig.localRotation=Quaternion.identity;
            weaponRecoil=0f;
            if(firstPersonAnimator!=null && weaponPrepared)
            {
                firstPersonAnimator.Play("Aim",0,0.15f);
                firstPersonAnimator.Update(0f);
                firstPersonAnimator.speed=0f;
            }
            SetMovementLocked(true);
        }

        public void SetAimActive(bool active)
        {
            aimActive=active;
            RefreshOwnerWeaponVisibility();
        }

        public void SetFirstPersonViewModelVisible(bool visible)
        {
            ownerWeaponVisible=visible;
            RefreshOwnerWeaponVisibility();
        }

        private void RefreshOwnerWeaponVisibility()
        {
            if(!IsOwner || fpsRig==null)return;
            var armRoot=viewModelRoot!=null ?
                viewModelRoot.Find("FPS_KyleArmsSkinned"):null;
            if(armRoot!=null)
            {
                // ADS hides ONLY the first-person arm, not the world actor.
                foreach(var renderer in armRoot.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled=ownerWeaponVisible &&
                        renderer is SkinnedMeshRenderer;
            }
            if(firstPersonGunSocket!=null)
                foreach(var renderer in firstPersonGunSocket.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled=ownerWeaponVisible;
        }

        public void TriggerFirstPersonFire()
        {
            // The old Fire_Handgun clip looked like an incorrect reload from
            // first person. A brief procedural weapon recoil replaces it.
            weaponRecoil=1f;
        }
        public void SetMovementLocked(bool locked)
        {
            movementLocked=locked;
            if(locked)frameVelocity=Vector3.zero;
        }

        private void Update()
        {
            if(!IsSpawned || !IsOwner) return;

            var kb=Keyboard.current;
            var mouse=Mouse.current;
            if(kb==null || mouse==null)return;

            if(kb.leftBracketKey.wasPressedThisFrame) SetSensitivity(sensitivity-1);
            if(kb.rightBracketKey.wasPressedThisFrame) SetSensitivity(sensitivity+1);

            // ESC vote menu is modal; don't move or turn beneath the menu.
            bool controlsLocked=movementLocked ||
                (RelayLobbySession.Instance!=null && RelayLobbySession.Instance.RoundMenuVisible);
            // FPS mouse aim; pitch cannot overturn. Mouse delta is already per-frame.
            if(!controlsLocked)
            {
                Vector2 delta=mouse.delta.ReadValue()*lookDegreesPerPixel*sensitivity;
                transform.Rotate(Vector3.up*delta.x,Space.World);
                pitch=Mathf.Clamp(pitch-delta.y,-80f,80f);
                if(fpsRig!=null)fpsRig.localRotation=Quaternion.Euler(pitch,0,0);
            }

            Vector2 input=Vector2.zero;
            if(!controlsLocked)
            {
                if(kb.wKey.isPressed)input.y+=1;
                if(kb.sKey.isPressed)input.y-=1;
                if(kb.dKey.isPressed)input.x+=1;
                if(kb.aKey.isPressed)input.x-=1;
            }
            input=Vector2.ClampMagnitude(input,1);
            bool running=!aimActive && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            float speed=aimActive?aimWalkSpeed:running?sprintSpeed:walkSpeed;
            Vector3 flat=transform.TransformDirection(new Vector3(input.x,0,input.y))*speed;
            frameVelocity=flat;

            // Movement animations on remote clients must not rely exclusively
            // on interpolated NetworkTransform positional deltas, which often
            // spend several frames at zero between received snapshots.
            float motionSpeed=flat.magnitude;
            if(Time.unscaledTime>=nextMotionSync ||
               Mathf.Abs(motionSpeed-lastSentMotionSpeed)>1.5f)
            {
                nextMotionSync=Time.unscaledTime+.12f;
                lastSentMotionSpeed=motionSpeed;
                SyncMovementRpc(motionSpeed);
            }

            // Preserve grounded collision: CharacterController.Move only, no jump code.
            if(controller.isGrounded && fallVelocity<0)
                fallVelocity=-1f;
            fallVelocity+=gravity*Time.deltaTime;
            Vector3 velocity=flat+Vector3.up*fallVelocity;
            controller.Move(velocity*Time.deltaTime);
            UpdateFootsteps(running,controlsLocked);

            if(worldAnimator!=null)
            {
                if(HasParameter(worldAnimator,"MoveSpeed"))
                    worldAnimator.SetFloat("MoveSpeed",flat.magnitude,.12f,Time.deltaTime);
                if(HasParameter(worldAnimator,"IsRunning"))
                    worldAnimator.SetBool("IsRunning",running && flat.sqrMagnitude>.001f);
            }
            // First-person right arm stays in a stable one-hand gun grip.
            // The world Kyle uses the walk/run BlendTree above.
        }

        private void UpdateFootsteps(bool running,bool locked)
        {
            var horizontal=controller.velocity;horizontal.y=0f;
            if(locked || !controller.isGrounded || horizontal.sqrMagnitude<.01f)
            {footstepDistance=0;wasWalking=false;return;}
            float stride=running?1.45f:.95f;
            if(!wasWalking)footstepDistance=stride*.75f;
            wasWalking=true;footstepDistance+=horizontal.magnitude*Time.deltaTime;
            if(footstepDistance>=stride)
            {
                footstepDistance%=stride;
                HideAndShoot.Audio.GameAudio.PlayFootstep(running);
            }
        }

        private void LateUpdate()
        {
            SharedPistolVisual.PoseGrip(worldGunSocket,worldAnimator,transform.rotation);
            if(!IsSpawned || !IsOwner || !weaponPrepared ||
               viewModelRightHand==null || viewModelRoot==null || ownerCamera==null ||
               !fpsRig.gameObject.activeSelf)return;

            // Reframe the *hand*, not a blind prefab-origin offset. This keeps
            // even retargeted Humanoid/Kyle skeletal variations in the FPS view.
            weaponAimBlend=Mathf.MoveTowards(weaponAimBlend,aimActive?1f:0f,Time.deltaTime*5f);
            float x=Mathf.Lerp(.70f,.55f,weaponAimBlend);
            float y=Mathf.Lerp(.28f,.18f,weaponAimBlend);
            var intended=ownerCamera.ViewportToWorldPoint(new Vector3(x,y,.62f));
            viewModelRoot.position+=Vector3.ClampMagnitude(
                intended-viewModelRightHand.position,3f);

            weaponRecoil=Mathf.MoveTowards(weaponRecoil,0f,
                Time.deltaTime*7f);
            float move01=Mathf.Clamp01(frameVelocity.magnitude/5f);
            weaponBob+=Time.deltaTime*(3f+move01*10f);
            float bob=move01*Mathf.Sin(weaponBob)*.007f;
            viewModelRoot.localPosition+=new Vector3(0f,bob,0f);

            if(firstPersonGunSocket!=null)
            {
                // Recoil moves arm and gun together; the grip never separates from the palm.
                viewModelRoot.position-=ownerCamera.transform.forward*(weaponRecoil*.025f);
                var ray=new Ray(ownerCamera.transform.position,
                    ownerCamera.ViewportPointToRay(new Vector3(.5f,.5f,0)).direction);
                bool hit=HideAndShoot.Combat.PistolAimUtility.TryFirstHit(ray,125f,transform,out var first);
                var point=hit?first.point:ray.GetPoint(125f);
                var direction=point-viewModelRightHand.position;
                SharedPistolVisual.PoseGrip(firstPersonGunSocket,firstPersonAnimator,
                    Quaternion.LookRotation(direction,ownerCamera.transform.up));
            }
        }

        [Rpc(SendTo.Server,Delivery=RpcDelivery.Unreliable)]
        private void SyncMovementRpc(float speed,RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId ||
               float.IsNaN(speed) || float.IsInfinity(speed))return;
            speed=Mathf.Clamp(speed,0f,5f);
            // Host updates its proxy immediately, then broadcasts to observers.
            proxyAnimation?.ApplyOwnerMotion(speed);
            DistributeMovementRpc(speed);
        }

        [Rpc(SendTo.NotOwner,Delivery=RpcDelivery.Unreliable)]
        private void DistributeMovementRpc(float speed)
        {
            proxyAnimation?.ApplyOwnerMotion(speed);
        }

        private static bool HasParameter(Animator a,string name)
        {
            foreach(var p in a.parameters)
                if(p.name==name)return true;
            return false;
        }

        private void SetSensitivity(int value)
        {
            sensitivity=Mathf.Clamp(value,1,10);
            showSensitivityUntil=Time.unscaledTime+1.5f;
        }

    }
}
