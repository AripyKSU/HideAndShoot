using System;
using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.NPC;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HideAndShoot.Player
{
    /// <summary>
    /// Owner-only presentation; not a networked actor. DeathFall (1.5s) then
    /// grounded ghost FPS; winner presentation follows the winner from a fixed
    /// frontal offset. No damage, ammo, or physics interactions are owned here.
    /// Camera distances/speed remain provisional until Part06 multi-client QA.
    /// </summary>
    public sealed class LocalSpectatorRig : MonoBehaviour
    {
        private enum SpectateMode { None, DeathFall, Ghost, WinnerFollow, OwnerWin }

        private const float DeathFallSeconds=1.5f;
        private const float GhostWalkSpeed=6.5f;
        private const float GhostSprintSpeed=8f;
        private const float GhostGravity=-18f;

        private HostPlayerLife life;
        private PlayerLocalMotor motor;
        private PistolCombatController pistol;
        private RelayLobbySession session;
        private GameObject spectatorRoot;
        private CharacterController ghostController;
        private Camera spectatorCamera;
        private AudioListener spectatorListener;
        private Transform followTarget;
        private SpectateMode mode;
        private Vector3 frontalDirection;
        private float deathFallUntil;
        private float pitch;
        private float verticalSpeed;
        private int observedRoundId=-1;
        private float nextCollisionRefresh;
        public bool IsGhost=>mode==SpectateMode.Ghost || mode==SpectateMode.DeathFall;

        private void Awake()
        {
            life=GetComponent<HostPlayerLife>();
            motor=GetComponent<PlayerLocalMotor>();
            pistol=GetComponent<PistolCombatController>();
            BuildSpectatorCamera();
        }

        private void OnEnable()
        {
            session=RelayLobbySession.Instance;
            if(session!=null)session.RoundWinnerAnnounced+=OnRoundWinner;
        }

        private void OnDisable()
        {
            if(session!=null)session.RoundWinnerAnnounced-=OnRoundWinner;
        }

        private void BuildSpectatorCamera()
        {
            spectatorRoot=new GameObject("Local_GroundedGhost");
            spectatorRoot.layer=LayerMask.NameToLayer("Ignore Raycast");
            ghostController=spectatorRoot.AddComponent<CharacterController>();
            ghostController.height=1.7f;
            ghostController.radius=.32f;
            ghostController.center=new Vector3(0,.85f,0);
            ghostController.stepOffset=.22f;
            ghostController.slopeLimit=45f;
            var view=new GameObject("Local_SpectatorCamera");
            view.transform.SetParent(spectatorRoot.transform,false);
            view.transform.localPosition=new Vector3(0,1.62f,0);
            spectatorCamera=view.AddComponent<Camera>();
            spectatorCamera.fieldOfView=75f;
            spectatorCamera.nearClipPlane=.04f;
            spectatorCamera.cullingMask=-1;
            int viewLayer=LayerMask.NameToLayer("FPSViewModel");
            if(viewLayer>=0)spectatorCamera.cullingMask &= ~(1<<viewLayer);
            spectatorListener=view.AddComponent<AudioListener>();
            spectatorRoot.SetActive(false);
        }

        public void OnOwnerLifeChanged(bool dead)
        {
            if(dead)
            {
                mode=SpectateMode.DeathFall;
                deathFallUntil=Time.unscaledTime+DeathFallSeconds;
                ActivateSpectator(true);
                ghostController.enabled=false;
                FollowDeathBody();
            }
            else RestoreOwnerView();
        }

        private void ActivateSpectator(bool active)
        {
            if(spectatorRoot!=null)spectatorRoot.SetActive(active);
            var camera=motor!=null?motor.PlayerCamera:null;
            if(camera!=null)
            {
                camera.enabled=!active;
                var listener=camera.GetComponent<AudioListener>();
                if(listener!=null)listener.enabled=!active;
            }
            motor?.SetFirstPersonViewModelVisible(!active);
            if(spectatorCamera!=null)spectatorCamera.enabled=active;
            if(spectatorListener!=null)spectatorListener.enabled=active;
        }

        private void RestoreOwnerView()
        {
            mode=SpectateMode.None;
            followTarget=null;
            if(ghostController!=null)ghostController.enabled=false;
            ActivateSpectator(false);
        }

        private void FollowDeathBody()
        {
            if(spectatorRoot==null)return;
            Vector3 target=transform.position+Vector3.up*.95f;
            Vector3 position=target-transform.forward*2.9f+Vector3.up*1.3f;
            spectatorRoot.transform.position=position;
            spectatorRoot.transform.rotation=Quaternion.LookRotation(
                (target-position).normalized,Vector3.up);
            spectatorCamera.transform.localPosition=Vector3.zero;
            spectatorCamera.transform.localRotation=Quaternion.identity;
        }

        private void EnterGhost()
        {
            if(life==null || !life.IsDead.Value || spectatorRoot==null)return;
            mode=SpectateMode.Ghost;
            ghostController.enabled=false;
            spectatorRoot.transform.SetPositionAndRotation(
                transform.position,Quaternion.Euler(0,transform.eulerAngles.y,0));
            spectatorCamera.transform.localPosition=new Vector3(0,1.62f,0);
            spectatorCamera.transform.localRotation=Quaternion.identity;
            pitch=0f;
            verticalSpeed=0f;
            ghostController.enabled=true;
            IgnoreOtherActors();
        }

        private void IgnoreOtherActors()
        {
            if(ghostController==null || !ghostController.enabled)return;
            nextCollisionRefresh=Time.unscaledTime+1f;
            foreach(var obj in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                if(!obj.IsSpawned ||
                   (obj.GetComponent<NpcWanderBrain>()==null &&
                    obj.GetComponent<HostPlayerLife>()==null))continue;
                foreach(var hitbox in obj.GetComponentsInChildren<Collider>(true))
                    if(hitbox!=null && hitbox!=ghostController)
                        Physics.IgnoreCollision(ghostController,hitbox,true);
            }
        }

        private void Update()
        {
            if(life==null || !life.IsOwner)return;
            if(pistol!=null && pistol.RoundId.Value!=observedRoundId)
            {
                observedRoundId=pistol.RoundId.Value;
                if(observedRoundId>0 && !life.IsDead.Value)
                    RestoreOwnerView();
            }

            if(mode==SpectateMode.DeathFall)
            {
                FollowDeathBody();
                if(Time.unscaledTime>=deathFallUntil)EnterGhost();
            }
            else if(mode==SpectateMode.Ghost)
                MoveGroundedGhost();
            else if(mode==SpectateMode.WinnerFollow)
                UpdateWinnerCamera();
        }

        private void MoveGroundedGhost()
        {
            if(session!=null && session.MatchPhase==
               (int)HideAndShoot.World.HostRoundSetupCoordinator.HostPhase.Podium)return;
            if(ghostController==null || !ghostController.enabled)return;
            if(Time.unscaledTime>=nextCollisionRefresh)IgnoreOtherActors();
            if(session!=null && session.RoundMenuVisible)return;
            var kb=Keyboard.current;
            var mouse=Mouse.current;
            if(kb==null || mouse==null)return;
            Vector2 look=mouse.delta.ReadValue()*.225f; // sensitivity 3 provisional
            spectatorRoot.transform.Rotate(Vector3.up*look.x,Space.World);
            pitch=Mathf.Clamp(pitch-look.y,-80f,80f);
            spectatorCamera.transform.localRotation=Quaternion.Euler(pitch,0,0);
            Vector2 input=Vector2.zero;
            if(kb.wKey.isPressed)input.y++;
            if(kb.sKey.isPressed)input.y--;
            if(kb.aKey.isPressed)input.x--;
            if(kb.dKey.isPressed)input.x++;
            input=Vector2.ClampMagnitude(input,1);
            float speed=kb.leftShiftKey.isPressed||kb.rightShiftKey.isPressed
                ?GhostSprintSpeed:GhostWalkSpeed;
            Vector3 horizontal=spectatorRoot.transform.TransformDirection(
                new Vector3(input.x,0,input.y))*speed;
            if(ghostController.isGrounded && verticalSpeed<0)verticalSpeed=-1f;
            verticalSpeed+=GhostGravity*Time.deltaTime;
            ghostController.Move(
                (horizontal+Vector3.up*verticalSpeed)*Time.deltaTime);
        }

        private void OnRoundWinner(ulong winner,int score,bool matchOver,int roundId)
        {
            if(life==null || !life.IsOwner || pistol==null ||
               roundId!=pistol.RoundId.Value)return;

            if(winner==life.OwnerClientId && !life.IsDead.Value)
            {
                // Winner stays in first person. HostPistol movement flag alone
                // authorizes locomotion; shooting and pickup are disabled.
                RestoreOwnerView();
                mode=SpectateMode.OwnerWin;
                return;
            }

            HostPlayerLife winnerLife=null;
            foreach(var other in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                if(other.IsSpawned && other.OwnerClientId==winner)
                {
                    winnerLife=other;
                    break;
                }
            if(winnerLife==null)return;
            followTarget=winnerLife.transform;
            frontalDirection=followTarget.forward;
            frontalDirection.y=0f;
            if(frontalDirection.sqrMagnitude<.01f)frontalDirection=Vector3.forward;
            frontalDirection.Normalize();
            if(ghostController!=null)ghostController.enabled=false;
            ActivateSpectator(true);
            mode=SpectateMode.WinnerFollow;
            UpdateWinnerCamera();
        }

        private void UpdateWinnerCamera()
        {
            if(followTarget==null || spectatorRoot==null)return;
            Vector3 lookAt=followTarget.position+Vector3.up*1.05f;
            Vector3 desired=followTarget.position+
                frontalDirection*2.5f+Vector3.up*2.15f;
            spectatorRoot.transform.position=desired;
            spectatorRoot.transform.rotation=Quaternion.LookRotation(
                (lookAt-desired).normalized,Vector3.up);
            spectatorCamera.transform.localPosition=Vector3.zero;
            spectatorCamera.transform.localRotation=Quaternion.identity;
        }

        private void OnDestroy()
        {
            if(session!=null)session.RoundWinnerAnnounced-=OnRoundWinner;
            if(spectatorRoot!=null)Destroy(spectatorRoot);
        }
    }
}
