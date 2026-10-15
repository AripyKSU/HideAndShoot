using HideAndShoot.NPC;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.Player
{
    /// <summary>
    /// Presentation-only animation for non-owning player and NPC proxies.
    /// Local player locomotion uses immediate input; server NPC locomotion
    /// uses NavMeshAgent velocity. Proxies derive motion from synced position,
    /// avoiding extra RPC traffic and changing no gameplay authority.
    /// </summary>
    public sealed class RemoteMotionAnimator : NetworkBehaviour
    {
        private Animator worldAnimator;
        private NpcWanderBrain npc;
        private Vector3 previousPosition;
        private bool hasSample;
        private float lastOwnerSpeed=-1f;
        private float lastOwnerUpdate;

        private void Awake()
        {
            var visual=transform.Find("WorldVisual");
            if(visual!=null)
                worldAnimator=visual.GetComponentInChildren<Animator>(true);
            npc=GetComponent<NpcWanderBrain>();
        }

        public override void OnNetworkSpawn()
        {
            previousPosition=transform.position;
            hasSample=false;
            lastOwnerSpeed=-1f;
        }

        public void ApplyOwnerMotion(float speed)
        {
            if(npc!=null || !IsSpawned || IsOwner)return;
            lastOwnerSpeed=Mathf.Clamp(speed,0f,5f);
            lastOwnerUpdate=Time.unscaledTime;
        }

        private void LateUpdate()
        {
            if(!IsSpawned || worldAnimator==null)return;
            var hand=worldAnimator.isHuman?worldAnimator.GetBoneTransform(HumanBodyBones.RightHand):null;
            SharedPistolVisual.PoseGrip(hand!=null?hand.Find("Pistol_WorldVisible"):null,
                worldAnimator,transform.rotation);
            // The owning FPS motor or Host NavMeshAgent drives authoritative
            // animation locally; never overwrite it with transform deltas.
            if(npc!=null ? IsServer : IsOwner)return;
            Vector3 position=transform.position;
            if(!hasSample)
            {
                hasSample=true;
                previousPosition=position;
                return;
            }
            Vector3 delta=position-previousPosition;
            previousPosition=position;
            delta.y=0f;
            // Round reposition is a teleport, never a sprint animation.
            float speed=delta.sqrMagnitude>9f ? 0f :
                delta.magnitude/Mathf.Max(.005f,Time.deltaTime);
            if(npc==null && lastOwnerSpeed>=0f &&
               Time.unscaledTime-lastOwnerUpdate<.45f)
                speed=lastOwnerSpeed;
            worldAnimator.SetFloat("MoveSpeed",Mathf.Clamp(speed,0f,5f),
                .12f,Time.deltaTime);
        }
    }
}
