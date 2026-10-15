using System;
using HideAndShoot.Player;
using HideAndShoot.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HideAndShoot.Combat
{
    /// <summary>
    /// FPS client feel and host ammo accounting. Aim/fire input and immediate SFX are
    /// locally responsive. Stage07 HostShotArbiter MUST validate historical capsules
    /// before ANY reported target receives damage. This component never kills actors.
    /// </summary>
    public sealed class PistolCombatController : NetworkBehaviour
    {
        public enum LocalAimState { Hip, AimEntering, AimReady, FireRecovery, Dead }

        [Header("Temporary tunables (Part02/03)")]
        [SerializeField] private float aimDuration = .15f;
        [SerializeField] private float firingRecovery = .45f;
        [SerializeField] private float maxHitDistance = 125f;
        [SerializeField] private float adsFov = 43f;
        [SerializeField] private AudioClip pistolShot;
        [SerializeField] private AudioClip emptyClick;

        public NetworkVariable<int> ServerAmmo = new NetworkVariable<int>(
            5, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> RoundActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> RoundId = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> WinnerMovementAllowed = new NetworkVariable<bool>(
            false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);

        public event Action<ulong, Vector3, Vector3, ulong, bool, int, int> HostShotCandidateReceived;

        private PlayerLocalMotor motor;
        private AudioSource audioPlayer;
        private Animator sharedAnimator;
        private Camera cameraRig;
        private LocalAimState state;
        private float stateUntil;
        private bool releasedSinceShot = true;
        private int localSequence;
        private int lastApprovedSequence;
        private float serverAimStartedAt;
        private int serverAimStartedTick;
        private bool serverIsAiming;
        private float hostRecoveryUntil;
        private int predictedAmmo;
        private float defaultFov;
        private float aimStartFov;
        private float AimDuration => Mathf.Max(.04f,aimDuration);
        private bool initialized;
        private bool fireRequested;
        private string feedback;
        private float feedbackUntil;

        public LocalAimState State => state;
        public int DisplayAmmo => predictedAmmo;
        public bool IsAiming => state == LocalAimState.AimEntering || state == LocalAimState.AimReady;
        public string LocalFeedback => Time.unscaledTime<feedbackUntil?feedback:string.Empty;

        private void Awake()
        {
            motor = GetComponent<PlayerLocalMotor>();
            var body=transform.Find("WorldVisual");
            if(body!=null) sharedAnimator=body.GetComponentInChildren<Animator>(true);
            audioPlayer=GetComponent<AudioSource>();
            if(audioPlayer==null) audioPlayer=gameObject.AddComponent<AudioSource>();
            audioPlayer.playOnAwake=false;
            audioPlayer.spatialBlend=0;
        }

        public override void OnNetworkSpawn()
        {
            predictedAmmo=ServerAmmo.Value;
            // Other people's shots should be heard spatially; the owner's click
            // is immediate and local-only. Final distance settings remain tunable.
            audioPlayer.spatialBlend=IsOwner?0f:1f;
            ServerAmmo.OnValueChanged+=OnAmmoChanged;
            RoundActive.OnValueChanged+=OnRoundActiveChanged;
            WinnerMovementAllowed.OnValueChanged+=OnWinnerMovementChanged;
            if(IsOwner)
            {
                InitializeOwnerView();
                UpdateOwnerMovementLock();
            }
        }

        public override void OnNetworkDespawn()
        {
            ServerAmmo.OnValueChanged-=OnAmmoChanged;
            RoundActive.OnValueChanged-=OnRoundActiveChanged;
            WinnerMovementAllowed.OnValueChanged-=OnWinnerMovementChanged;
            motor?.SetAimActive(false);
        }

        private void InitializeOwnerView()
        {
            if(motor==null)return;
            cameraRig=motor.PlayerCamera;
            if(cameraRig==null)return;
            defaultFov=cameraRig.fieldOfView;
            state=LocalAimState.Hip;
            initialized=true;
        }

        private void OnAmmoChanged(int oldAmmo, int authoritative)
        {
            // Local predictions are reconciled when Host's ammo count arrives.
            predictedAmmo=authoritative;
            if(IsOwner && RoundActive.Value && authoritative>oldAmmo)
            {
                HideAndShoot.Audio.GameAudio.PlayAmmoPickup();
                feedback="탄약 +"+(authoritative-oldAmmo);
                feedbackUntil=Time.unscaledTime+2f;
            }
        }

        private void OnRoundActiveChanged(bool previous, bool current)
        {
            UpdateOwnerMovementLock();
            if(!current)
            {
                state=LocalAimState.Hip;
                motor?.SetAimActive(false);
            }
        }

        private void OnWinnerMovementChanged(bool previous,bool current)
        {
            UpdateOwnerMovementLock();
        }

        private void UpdateOwnerMovementLock()
        {
            if(!IsOwner || motor==null)return;
            var life=GetComponent<HostPlayerLife>();
            bool dead=life!=null && life.IsDead.Value;
            motor.SetMovementLocked(dead ||
                (!RoundActive.Value && !WinnerMovementAllowed.Value));
        }

        public void HostAllowWinnerMovement(bool allowed)
        {
            if(!IsServer)return;
            var life=GetComponent<HostPlayerLife>();
            WinnerMovementAllowed.Value=allowed && (life==null || !life.IsDead.Value);
        }

        /// <summary>Only a Host round coordinator may call this.</summary>
        public void HostSetRound(int roundId, bool isActive)
        {
            if(!IsServer)return;
            int previousRound=RoundId.Value;
            if(previousRound!=roundId)WinnerMovementAllowed.Value=false;
            RoundId.Value=roundId;
            RoundActive.Value=isActive;
            if(!isActive)
            {
                serverIsAiming=false;
                lastApprovedSequence=0;
                hostRecoveryUntil=0;
                // Only a NEW round reloads to five. End-of-round celebration
                // must not alter the previous round's remaining ammo.
                if(previousRound!=roundId)ServerAmmo.Value=5;
            }
        }

        /// <summary>Host pickup manager provides verified ammo, never directly on the client.</summary>
        public void HostGrantAmmo(int amount)
        {
            if(!IsServer || amount<=0)return;
            ServerAmmo.Value=Mathf.Clamp(ServerAmmo.Value+amount,0,10);
        }

        private void Update()
        {
            if(!IsSpawned || !IsOwner || !initialized) return;
            if(cameraRig==null || motor==null)return;
            var ownerLife=GetComponent<HostPlayerLife>();
            if(ownerLife!=null && ownerLife.IsDead.Value)
            {
                ResetAim();
                state=LocalAimState.Dead;
                return;
            }
            if(state==LocalAimState.Dead)state=LocalAimState.Hip;
            if(RelayLobbySession.Instance!=null &&
               RelayLobbySession.Instance.RoundMenuVisible)
            {
                ResetAim();
                return;
            }

            var mouse=Mouse.current;
            if(mouse==null)return;
            if(!RoundActive.Value)
            {
                ResetAim();
                return;
            }

            if(mouse.rightButton.wasReleasedThisFrame)
            {
                releasedSinceShot=true;
                if(state==LocalAimState.AimEntering || state==LocalAimState.AimReady)
                    ResetAim();
            }

            if(state==LocalAimState.FireRecovery && Time.time>=stateUntil)
                state=LocalAimState.Hip;

            if(state==LocalAimState.Hip && mouse.rightButton.wasPressedThisFrame && releasedSinceShot)
            {
                releasedSinceShot=false;
                state=LocalAimState.AimEntering;
                aimStartFov=cameraRig.fieldOfView;
                stateUntil=Time.time+AimDuration;
                motor.SetAimActive(true);
                AimChangedRpc(true);
                // Keep world locomotion running while aiming. The old
                // full-body Aim state freezes feet and causes sliding.
            }
            // Finish the lens transition before evaluating this frame's fire input.
            UpdateAimZoom(mouse.rightButton.isPressed,Time.time,Time.deltaTime);

            if(state==LocalAimState.AimReady && mouse.leftButton.wasPressedThisFrame)
            {
                if(predictedAmmo<=0)
                {
                    PlayLocalSound(emptyClick);
                    feedback="탄약이 없습니다 · 청록색 탄약 상자를 찾아보세요";
                    feedbackUntil=Time.unscaledTime+2f;
                }
                else
                {
                    fireRequested=true;
                }
            }

        }

        private void UpdateAimZoom(bool aimHeld,float now,float deltaTime)
        {
            if(state==LocalAimState.AimEntering)
            {
                if(!aimHeld){ResetAim();return;}
                // Interpolate from the fixed starting FOV, not last frame's FOV.
                // The lens reaches its exact target at the finite ADS deadline.
                float progress=Mathf.Clamp01(1f-(stateUntil-now)/AimDuration);
                cameraRig.fieldOfView=Mathf.Lerp(aimStartFov,adsFov,progress);
                if(now>=stateUntil)
                {
                    cameraRig.fieldOfView=adsFov;
                    state=LocalAimState.AimReady;
                }
                return;
            }
            if(state==LocalAimState.AimReady)
            {cameraRig.fieldOfView=adsFov;return;}
            float speed=Mathf.Abs(defaultFov-adsFov)/AimDuration;
            cameraRig.fieldOfView=Mathf.MoveTowards(cameraRig.fieldOfView,defaultFov,speed*deltaTime);
        }

        private void LateUpdate()
        {
            if(!fireRequested)return;
            fireRequested=false;
            // Motor look and ADS lens updates have finished for the rendered frame.
            if(IsSpawned && IsOwner && state==LocalAimState.AimReady && RoundActive.Value)
                if(RelayLobbySession.Instance==null || !RelayLobbySession.Instance.RoundMenuVisible)FireOnce();
        }

        private void ResetAim()
        {
            bool wasAiming=IsAiming;
            if(state!=LocalAimState.Hip && state!=LocalAimState.FireRecovery)
                state=LocalAimState.Hip;
            if(wasAiming && IsSpawned && IsOwner && RoundActive.Value)
                AimChangedRpc(false);
            motor?.SetAimActive(false);
            if(cameraRig!=null)cameraRig.fieldOfView=defaultFov;
            fireRequested=false;
            releasedSinceShot=Mouse.current==null || !Mouse.current.rightButton.isPressed;
            if(sharedAnimator!=null)sharedAnimator.SetBool("Aim",false);
        }

        private void FireOnce()
        {
            Vector3 direction=cameraRig.ViewportPointToRay(new Vector3(.5f,.5f,0)).direction;
            // Start at the eye, so a wall inside the camera near plane still blocks a shot.
            Vector3 origin=cameraRig.transform.position;
            var ray=new Ray(origin,direction);
            ulong targetObject=0;
            bool hit=PistolAimUtility.TryFirstHit(ray,maxHitDistance,transform,out var first);
            Vector3 endpoint=hit?first.point:ray.GetPoint(maxHitDistance);
            var target=hit && first.collider.gameObject.layer==LayerMask.NameToLayer("HitCapsule")?
                first.collider.GetComponentInParent<NetworkObject>():null;
            bool hasTarget=target!=null && target.IsSpawned && target!=NetworkObject;
            if(hasTarget)targetObject=target.NetworkObjectId;

            predictedAmmo=Mathf.Max(0,predictedAmmo-1);
            localSequence++;
            int tick=NetworkManager.ServerTime.Tick;
            int approximateRenderTick=tick-2; // provisional; Stage07 history calibrates exact client offset
            PlayLocalSound(pistolShot);
            PistolLaserTracer.Show(motor.PistolMuzzle!=null?motor.PistolMuzzle.position:origin,endpoint);
            // Fire_Handgun is a 5.3-second full-body Mixamo clip that looks
            // like a reload. It must not interrupt player locomotion.
            if(sharedAnimator!=null)sharedAnimator.SetBool("Aim",false);
            motor.TriggerFirstPersonFire();
            motor.SetAimActive(false);
            state=LocalAimState.FireRecovery;
            stateUntil=Time.time+firingRecovery;
            releasedSinceShot=false;

            SubmitShotRpc(RoundId.Value,localSequence,origin,direction,
                targetObject,hasTarget,tick,approximateRenderTick);
        }

        private void PlayLocalSound(AudioClip clip)
        {
            if(clip!=null)audioPlayer.PlayOneShot(clip);
        }

        [Rpc(SendTo.Server)]
        private void AimChangedRpc(bool aiming, RpcParams rpc = default)
        {
            if(!RoundActive.Value || rpc.Receive.SenderClientId != OwnerClientId)return;
            var life=GetComponent<HostPlayerLife>();
            if(life!=null && life.IsDead.Value)return;
            serverIsAiming=aiming;
            // Aim stays a server-side combat validation flag. Full-body
            // Animator Aim must stay off until a layered upper-body rig exists.
            SyncRemoteAimRpc(false);
            if(aiming)
            {
                serverAimStartedAt=Time.time;
                serverAimStartedTick=NetworkManager.ServerTime.Tick;
            }
        }

        [Rpc(SendTo.Server)]
        private void SubmitShotRpc(int round,int sequence,Vector3 origin,Vector3 direction,
            ulong targetId,bool hasTarget,int fireTick,int targetRenderTick,RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId || !RoundActive.Value ||
                round!=RoundId.Value || sequence<=lastApprovedSequence ||
                !serverIsAiming || Time.time-serverAimStartedAt<aimDuration-.05f ||
                Time.time<hostRecoveryUntil || ServerAmmo.Value<=0)
                return;

            if(float.IsNaN(origin.x) || float.IsNaN(origin.y) || float.IsNaN(origin.z) ||
                float.IsInfinity(origin.x) || float.IsInfinity(origin.y) || float.IsInfinity(origin.z) ||
                float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z) ||
                float.IsInfinity(direction.x) || float.IsInfinity(direction.y) || float.IsInfinity(direction.z) ||
                direction.sqrMagnitude<.98f || direction.sqrMagnitude>1.02f)
                return;

            // Client timestamps are hints, not authority: bound them to
            // observed server time AND to this Host-observed ADS interval.
            int serverNow=NetworkManager.ServerTime.Tick;
            if(fireTick<0 || targetRenderTick<0 ||
               fireTick>serverNow+2 || serverNow-fireTick>31 ||
               targetRenderTick>fireTick || fireTick-targetRenderTick>20 ||
               fireTick<serverAimStartedTick-6)return;

            var life=GetComponent<HostPlayerLife>();
            if(life!=null && life.IsDead.Value &&
               (life.ServerDeathTick==int.MaxValue || fireTick>=life.ServerDeathTick))
                return;

            lastApprovedSequence=sequence;
            serverIsAiming=false;
            if(sharedAnimator!=null)sharedAnimator.SetBool("Aim",false);
            SyncRemoteAimRpc(false);
            hostRecoveryUntil=Time.time+firingRecovery;
            ServerAmmo.Value--;

            // Resolve before damage callbacks can disable the struck capsule.
            var ray=new Ray(origin,direction);
            bool hit=PistolAimUtility.TryFirstHit(ray,maxHitDistance,transform,out var first);
            Vector3 endpoint=hit?first.point:ray.GetPoint(maxHitDistance);
            // Never kill based only on the client claim. Stage07 attaches historical
            // target/sender/occlusion validation before requesting a Host death approval.
            HostShotCandidateReceived?.Invoke(OwnerClientId,origin,direction,targetId,
                hasTarget,fireTick,targetRenderTick);
            ApprovedFireRpc(endpoint);
        }

        /// <summary>Owner animation is immediate; server relays ADS to other observers.</summary>
        [Rpc(SendTo.NotOwner)]
        private void SyncRemoteAimRpc(bool aiming)
        {
            // Preserve locomotion during ADS rather than replacing the whole
            // body with the stationary Aim_Idle animation.
            if(sharedAnimator!=null)sharedAnimator.SetBool("Aim",false);
        }

        [Rpc(SendTo.NotOwner)]
        private void ApprovedFireRpc(Vector3 endpoint)
        {
            // Only the owner draws immediate prediction. Other peers receive
            // the laser on the host-approved fire event, never a fake hit.
            var muzzle=motor!=null?motor.PistolMuzzle:null;
            PistolLaserTracer.Show(muzzle!=null?muzzle.position:transform.position+Vector3.up*1.5f,endpoint);
            PlayLocalSound(pistolShot);
        }
    }
}
