using Unity.Netcode;
using UnityEngine;
using HideAndShoot.Player;

namespace HideAndShoot.Combat
{
    /// <summary>Host-authorized instant death only; no client may set IsDead.</summary>
    public sealed class HostPlayerLife : NetworkBehaviour
    {
        public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
            false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);

        public const double NpcRevealSeconds=10d;
        public NetworkVariable<double> RevealedUntil=new NetworkVariable<double>(
            0d,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public double RevealSecondsRemaining=>IsSpawned && !IsDead.Value && NetworkManager!=null
            ? System.Math.Max(0d,RevealedUntil.Value-NetworkManager.ServerTime.Time):0d;
        public void HostRevealForNpcHit(int expectedRound)
        {
            if(!IsServer || IsDead.Value || expectedRound!=roundId)return;
            RevealedUntil.Value=NetworkManager.ServerTime.Time+NpcRevealSeconds;
        }
        public void HostClearReveal(){if(IsServer)RevealedUntil.Value=0d;}

        private Animator worldAnimator;
        private Renderer[] visibleWorldRenderers;
        private float hideCorpseAt=float.PositiveInfinity;
        private bool corpseHidden;
        private CapsuleCollider hitbox;
        private PlayerLocalMotor motor;
        private int serverDeathTick = int.MaxValue;
        private int roundId = -1;

        public int ServerDeathTick=>serverDeathTick;
        public int RoundId=>roundId;

        private void Awake()
        {
            motor=GetComponent<PlayerLocalMotor>();
            var body=transform.Find("WorldVisual");
            if(body!=null)
            {
                worldAnimator=body.GetComponentInChildren<Animator>(true);
                // Cache only originally enabled parts; avoid un-hiding
                // intentionally invisible renderers on subsequent rounds.
                var renderers=body.GetComponentsInChildren<Renderer>(true);
                var visible=new System.Collections.Generic.List<Renderer>(renderers.Length);
                foreach(var renderer in renderers)
                    if(renderer!=null && renderer.enabled) visible.Add(renderer);
                visibleWorldRenderers=visible.ToArray();
            }
            var capsule=transform.Find("DamageCapsule_ONE_HITBOX");
            if(capsule!=null)hitbox=capsule.GetComponent<CapsuleCollider>();
        }

        public override void OnNetworkSpawn()
        {
            if(GetComponent<PlayerHighlightTarget>()==null)gameObject.AddComponent<PlayerHighlightTarget>();
            if(IsOwner && GetComponent<LocalSpectatorRig>()==null)
                gameObject.AddComponent<LocalSpectatorRig>();
            IsDead.OnValueChanged+=OnDeathChanged;
            OnDeathChanged(!IsDead.Value,IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            IsDead.OnValueChanged-=OnDeathChanged;
        }
        private void Update()
        {
            if(!IsSpawned || !IsDead.Value || corpseHidden ||
               Time.unscaledTime<hideCorpseAt)return;
            SetWorldRenderersVisible(false);
        }

        private void SetWorldRenderersVisible(bool visible)
        {
            if(visibleWorldRenderers!=null)
                foreach(var renderer in visibleWorldRenderers)
                    if(renderer!=null)renderer.enabled=visible;
            corpseHidden=!visible;
        }

        public void HostResetForRound(int round)
        {
            if(!IsServer)return;
            roundId=round;
            RevealedUntil.Value=0d;
            serverDeathTick=int.MaxValue;
            IsDead.Value=false;
            OnDeathChanged(true,false);
        }

        public bool HostKill(int expectedRound,int authoritativeDeathTick)
        {
            if(!IsServer || IsDead.Value || expectedRound!=roundId)return false;
            serverDeathTick=authoritativeDeathTick;
            RevealedUntil.Value=0d;
            IsDead.Value=true;
            OnDeathChanged(false,true);
            return true;
        }

        private void OnDeathChanged(bool previous,bool dead)
        {
            if(hitbox!=null)hitbox.enabled=!dead;
            if(dead)
            {
                // Play the full 1.5s death collapse, then remove visible body
                // as the player becomes a grounded ghost spectator.
                hideCorpseAt=Time.unscaledTime+1.5f;
            }
            else
            {
                hideCorpseAt=float.PositiveInfinity;
                SetWorldRenderersVisible(true);
            }
            if(IsOwner)motor?.SetFirstPersonViewModelVisible(!dead);
            if(worldAnimator!=null)
            {
                worldAnimator.SetBool("Dead",dead);
                if(dead)worldAnimator.SetBool("Aim",false);
                else
                {
                    worldAnimator.SetBool("Aim",false);
                    worldAnimator.ResetTrigger("FireOnce");
                    worldAnimator.SetFloat("MoveSpeed",0f);
                    if(worldAnimator.isActiveAndEnabled)
                    {
                        worldAnimator.Play("Locomotion",0,0f);
                        worldAnimator.Update(0f);
                    }
                }
            }
            if(dead && IsOwner && motor!=null)
                motor.SetMovementLocked(true);
            if(IsOwner)
                GetComponent<LocalSpectatorRig>()?.OnOwnerLifeChanged(dead);
        }
    }
}
