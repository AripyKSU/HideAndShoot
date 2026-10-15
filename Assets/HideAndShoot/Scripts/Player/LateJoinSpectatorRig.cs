using HideAndShoot.Combat;
using HideAndShoot.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HideAndShoot.Player
{
    /// <summary>
    /// Local-only grounded spectator for connected participants that arrived
    /// after the current round began. It cannot affect NGO gameplay state.
    /// Ownership of a PlayerAvatar at the next RoundSetup disables this camera.
    /// </summary>
    public sealed class LateJoinSpectatorRig : MonoBehaviour
    {
        private RelayLobbySession session;
        private GameObject root;
        private CharacterController controller;
        private Camera view;
        private AudioListener listener;
        private Camera overview;
        private AudioListener overviewListener;
        private bool watching;
        private float pitch;
        private float falling;
        private float nextIgnoreRefresh;
        private const float LookSensitivity=.225f;
        private const float Gravity=-18f;

        private void Start()
        {
            session=RelayLobbySession.Instance;
            root=new GameObject("LOCAL_LateJoinGroundObserver");
            root.transform.SetParent(transform,false);
            int ignore=LayerMask.NameToLayer("Ignore Raycast");
            if(ignore>=0)root.layer=ignore;
            controller=root.AddComponent<CharacterController>();
            controller.height=1.7f;
            controller.radius=.32f;
            controller.center=new Vector3(0,.85f,0);
            controller.stepOffset=.22f;
            var cameraRoot=new GameObject("LocalWaitingCamera");
            cameraRoot.transform.SetParent(root.transform,false);
            cameraRoot.transform.localPosition=new Vector3(0,1.62f,0);
            view=cameraRoot.AddComponent<Camera>();
            view.fieldOfView=75f;
            view.nearClipPlane=.04f;
            int viewLayer=LayerMask.NameToLayer("FPSViewModel");
            if(viewLayer>=0)view.cullingMask &= ~(1<<viewLayer);
            listener=cameraRoot.AddComponent<AudioListener>();
            overview=GameObject.Find("SceneOverviewCamera")?.GetComponent<Camera>();
            if(overview!=null)overviewListener=overview.GetComponent<AudioListener>();
            root.SetActive(false);
        }

        private bool OwnsAvatar()
        {
            var manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsClient)return false;
            foreach(var player in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                if(player.IsSpawned && player.IsOwner)return true;
            return false;
        }

        private void Activate()
        {
            if(root==null || watching)return;
            watching=true;
            Vector3 point=Vector3.zero;
            if(overview!=null)
            {
                var ray=new Ray(overview.transform.position,Vector3.down);
                if(Physics.Raycast(ray,out var hit,300f,~(1<<2)))
                    point=hit.point;
            }
            // Keep the grounded CharacterController active at a valid height.
            root.transform.position=point+Vector3.up*.08f;
            root.transform.rotation=Quaternion.identity;
            pitch=0;
            falling=0;
            root.SetActive(true);
            if(overview!=null)overview.enabled=false;
            if(overviewListener!=null)overviewListener.enabled=false;
            RefreshCollisionIgnores();
        }

        private void Deactivate()
        {
            if(!watching || root==null)return;
            watching=false;
            root.SetActive(false);
            if(view!=null)view.enabled=false;
            if(listener!=null)listener.enabled=false;
            // An avatar owner has their own FPS camera; never restore the
            // overview for it. The overview is restored only for no-match.
            if(session==null || !session.IsMatchInProgress)
            {
                if(overview!=null)overview.enabled=true;
                if(overviewListener!=null)overviewListener.enabled=true;
            }
        }

        private void RefreshCollisionIgnores()
        {
            if(!watching || controller==null)return;
            nextIgnoreRefresh=Time.unscaledTime+1f;
            foreach(var actor in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                if(!actor.IsSpawned)continue;
                if(actor.GetComponent<HostPlayerLife>()==null &&
                   actor.GetComponent<HideAndShoot.NPC.NpcWanderBrain>()==null)continue;
                foreach(var collider in actor.GetComponentsInChildren<Collider>(true))
                    if(collider!=null && collider!=controller)
                        Physics.IgnoreCollision(controller,collider,true);
            }
        }

        private void Update()
        {
            if(session==null || root==null)return;
            var manager=NetworkManager.Singleton;
            bool waiting=manager!=null && manager.IsClient &&
                session.IsMatchInProgress && session.MatchRoundId>0 &&
                !OwnsAvatar() &&
                session.MatchPhase!=(int)HideAndShoot.World.HostRoundSetupCoordinator.HostPhase.Podium;
            if(!waiting)
            {
                Deactivate();
                return;
            }
            if(!watching)
            {
                Activate();
                if(view!=null)view.enabled=true;
                if(listener!=null)listener.enabled=true;
            }
            if(session.RoundMenuVisible)return;
            if(Time.unscaledTime>=nextIgnoreRefresh)RefreshCollisionIgnores();
            var kb=Keyboard.current;
            var mouse=Mouse.current;
            if(kb==null || mouse==null)return;
            Vector2 delta=mouse.delta.ReadValue()*LookSensitivity;
            root.transform.Rotate(Vector3.up*delta.x,Space.World);
            pitch=Mathf.Clamp(pitch-delta.y,-80f,80f);
            view.transform.localRotation=Quaternion.Euler(pitch,0,0);
            Vector2 input=Vector2.zero;
            if(kb.wKey.isPressed)input.y++;
            if(kb.sKey.isPressed)input.y--;
            if(kb.aKey.isPressed)input.x--;
            if(kb.dKey.isPressed)input.x++;
            input=Vector2.ClampMagnitude(input,1);
            float speed=(kb.leftShiftKey.isPressed||kb.rightShiftKey.isPressed)?8f:6.5f;
            Vector3 horizontal=root.transform.TransformDirection(new Vector3(input.x,0,input.y))*speed;
            if(controller.isGrounded && falling<0)falling=-1f;
            falling+=Gravity*Time.deltaTime;
            controller.Move((horizontal+Vector3.up*falling)*Time.deltaTime);
        }

        private void OnDestroy()
        {
            Deactivate();
        }
    }
}
