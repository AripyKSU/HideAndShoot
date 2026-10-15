using System;
using System.Collections.Generic;
using Unity.Netcode;
using HideAndShoot.Networking;
using HideAndShoot.Player;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndShoot.NPC
{
    /// <summary>
    /// Host-only wandering NPC: random NavMesh destinations weighted by current
    /// NPC zone density and that actor's recent 4 zones. Never reacts to gunfire,
    /// corpses, other deaths, or combat. No jump/run/shoot behavior.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NpcWanderBrain : NetworkBehaviour, IPooledActorReset
    {
        private enum NpcState { Pooled, Walking, Pausing, Dying }

        private NavMeshAgent agent;
        private Animator animator;
        private Collider damageCollider;
        public NetworkVariable<bool> ReplicatedDead = new NetworkVariable<bool>(
            false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        private NpcZonePopulation zones;
        private NpcState state=NpcState.Pooled;
        private bool movementReleased;
        private readonly Queue<int> recentZones=new Queue<int>(4);
        private float pauseEnd;
        private float glanceRate;
        private float nextZoneCheck;
        private float lastProgressCheck;
        private Vector3 lastProgressPosition;
        private int registeredZone=-1;
        private int roundId=-1;
        private int generation=-1;

        public int RoundId => roundId;
        public int Generation => generation;
        public bool IsAlive => state==NpcState.Walking || state==NpcState.Pausing;

        private void Awake()
        {
            agent=GetComponent<NavMeshAgent>();
            var damage=transform.Find("DamageCapsule_ONE_HITBOX");
            if(damage!=null) damageCollider=damage.GetComponent<Collider>();
            var visual=transform.Find("WorldVisual");
            if(visual!=null)
            {
                animator=visual.GetComponentInChildren<Animator>(true);
                SharedPistolVisual.EnsureWorldPistol(visual);
            }
        }

        public override void OnNetworkSpawn()
        {
            ReplicatedDead.OnValueChanged += OnDeathChanged;
            if(!IsServer && agent!=null)
                agent.enabled=false; // NetworkTransform supplies client position.
            OnDeathChanged(!ReplicatedDead.Value,ReplicatedDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            ReplicatedDead.OnValueChanged -= OnDeathChanged;
            if(IsServer) UnregisterZone();
            ResetAnimator();
        }

        private void OnDeathChanged(bool previous,bool dead)
        {
            if(dead) { if(animator!=null)animator.SetBool("Dead",true); }
            else ResetAnimator();
            if(damageCollider!=null)damageCollider.enabled=!dead;
        }

        public void ResetForRound(int newRoundId)
        {
            if(!IsServer)return;
            UnregisterZone();
            roundId=newRoundId;
            generation=-1;
            recentZones.Clear();
            state=NpcState.Pooled;
            movementReleased=false;
            ReplicatedDead.Value=false;
            if(damageCollider!=null)damageCollider.enabled=true;
            nextZoneCheck=0;
            lastProgressCheck=0;
            ResetAnimator();
            if(agent.enabled)
            {
                agent.isStopped=true;
                agent.ResetPath();
            }
        }

        public bool ResetForSpawn(int newGeneration,Vector3 position,NpcZonePopulation density)
        {
            if(!IsServer || density==null || !NavMesh.SamplePosition(position,out var nav,
                2.0f,NavMesh.AllAreas))return false;

            zones=density;
            generation=newGeneration;
            UnregisterZone();
            recentZones.Clear();
            transform.position=nav.position;
            if(!agent.enabled)agent.enabled=true;
            agent.Warp(nav.position);
            agent.ResetPath();
            agent.speed=2.2f;
            movementReleased=false;
            agent.isStopped=true;
            state=NpcState.Walking;
            ReplicatedDead.Value=false;
            if(damageCollider!=null)damageCollider.enabled=true;
            lastProgressPosition=nav.position;
            lastProgressCheck=Time.time;
            RegisterZone(zones.GetZone(transform.position));
            return true;
        }

        public void HostReleaseCountdownMovement()
        {
            if(!IsServer || movementReleased || state==NpcState.Pooled ||
               state==NpcState.Dying || agent==null || !agent.enabled)return;
            movementReleased=true;
            agent.isStopped=false;
            lastProgressPosition=transform.position;
            lastProgressCheck=Time.time;
            ChooseDestination();
        }

        public void HostFreezeCountdownMovement()
        {
            if(!IsServer)return;
            movementReleased=false;
            if(agent!=null && agent.enabled)
            {
                agent.isStopped=true;
                agent.ResetPath();
            }
            if(animator!=null)animator.SetFloat("MoveSpeed",0);
        }

        private void Update()
        {
            if(!IsServer || state==NpcState.Pooled || state==NpcState.Dying)return;
            if(!movementReleased)
            {
                if(agent!=null && agent.enabled && !agent.isStopped)
                    agent.isStopped=true;
                if(animator!=null)animator.SetFloat("MoveSpeed",0);
                return;
            }
            if(Time.time>=nextZoneCheck)
            {
                nextZoneCheck=Time.time+.25f;
                int next=zones.GetZone(transform.position);
                if(next!=registeredZone)
                {
                    RegisterZone(next);
                    TrackRecent(next);
                }
            }

            if(animator!=null)
                animator.SetFloat("MoveSpeed",agent.velocity.magnitude,.1f,Time.deltaTime);
            if(state==NpcState.Pausing)
            {
                if(Time.time>=pauseEnd)
                {
                    state=NpcState.Walking;
                    agent.isStopped=false;
                    ChooseDestination();
                }
                else if(Time.time>=glanceRate)
                {
                    glanceRate=Time.time+UnityEngine.Random.Range(.45f,.8f);
                    transform.Rotate(Vector3.up,UnityEngine.Random.Range(-15f,15f));
                }
                return;
            }

            if(!agent.pathPending && agent.remainingDistance<=agent.stoppingDistance+.35f)
            {
                OnDestinationReached();
            }
            else if(Time.time-lastProgressCheck>=3f)
            {
                // Failed/stuck path: choose a different random destination;
                // never acquire perception or pursue other actors.
                if((transform.position-lastProgressPosition).sqrMagnitude<.3f)
                    ChooseDestination();
                lastProgressCheck=Time.time;
                lastProgressPosition=transform.position;
            }
        }

        private void OnDestinationReached()
        {
            TrackRecent(registeredZone);
            if(UnityEngine.Random.value<.35f)
            {
                state=NpcState.Pausing;
                agent.isStopped=true;
                agent.ResetPath();
                pauseEnd=Time.time+UnityEngine.Random.Range(.5f,2f);
                glanceRate=Time.time+.35f;
            }
            else ChooseDestination();
        }

        private void ChooseDestination()
        {
            if(zones==null || !agent.enabled)return;
            var candidates=new List<(Vector3 point,int zone,float weight)>(12);
            for(int i=0;i<12;i++)
            {
                var sample=new Vector3(UnityEngine.Random.Range(-34f,34f),
                    UnityEngine.Random.Range(0f,4.5f),UnityEngine.Random.Range(-34f,34f));
                if(!NavMesh.SamplePosition(sample,out var hit,3.5f,NavMesh.AllAreas))
                    continue;
                var path=new NavMeshPath();
                if(!agent.CalculatePath(hit.position,path) ||
                    path.status!=NavMeshPathStatus.PathComplete)
                    continue;
                int zone=zones.GetZone(hit.position);
                float populationFactor=1f/(1f+.24f*zones.GetDensity(zone));
                float visitFactor=recentZones.Contains(zone) ? .46f : 1f;
                candidates.Add((hit.position,zone,populationFactor*visitFactor));
            }
            if(candidates.Count==0)
            {
                // Stay briefly rather than teleport or forcefully redistribute.
                state=NpcState.Pausing;
                agent.isStopped=true;
                pauseEnd=Time.time+.8f;
                return;
            }
            float total=0;
            foreach(var item in candidates)total+=item.weight;
            float choice=UnityEngine.Random.value*total;
            Vector3 dest=candidates[candidates.Count-1].point;
            foreach(var item in candidates)
            {
                choice-=item.weight;
                if(choice<=0){dest=item.point;break;}
            }
            agent.isStopped=false;
            agent.SetDestination(dest);
        }

        private void TrackRecent(int zone)
        {
            if(zone<0 || (recentZones.Count>0 && LastRecent()==zone))return;
            if(recentZones.Count>=4)recentZones.Dequeue();
            recentZones.Enqueue(zone);
        }

        private int LastRecent()
        {
            int last=-1;
            foreach(int x in recentZones)last=x;
            return last;
        }

        private void RegisterZone(int zone)
        {
            if(zones==null || zone==registeredZone)return;
            UnregisterZone();
            zones.Add(zone);
            registeredZone=zone;
        }

        private void UnregisterZone()
        {
            if(zones!=null && registeredZone>=0)zones.Remove(registeredZone);
            registeredZone=-1;
        }

        public void HostApprovedDeath(int expectedRound,int expectedGeneration)
        {
            if(!IsServer || !IsAlive || roundId!=expectedRound ||
               generation!=expectedGeneration)return;
            state=NpcState.Dying;
            ReplicatedDead.Value=true;
            if(damageCollider!=null)damageCollider.enabled=false;
            UnregisterZone();
            agent.isStopped=true;
            agent.ResetPath();
            if(animator!=null)animator.SetBool("Dead",true);
            // Network-controlled death timing / corpse persistence is managed
            // by HostRoundManager, NOT by an automatic same-round respawn.
        }

        public void ReturnToPool()
        {
            if(!IsServer)return;
            UnregisterZone();
            state=NpcState.Pooled;
            movementReleased=false;
            recentZones.Clear();
            if(agent.enabled)
            {
                agent.isStopped=true;
                agent.ResetPath();
            }
            ResetAnimator();
        }

        public void ResetAfterNetworkDespawn()
        {
            // Called by NGO's client-side instance handler, after NetworkObject is gone.
            // No ResetForRound call here: only Host assigns gameplay generations.
            if(agent!=null && !IsServer && agent.enabled) agent.enabled=false;
            if(damageCollider!=null)damageCollider.enabled=true;
            ResetAnimator();
        }

        private void ResetAnimator()
        {
            if(animator==null)return;
            animator.SetBool("Dead",false);
            animator.SetBool("Aim",false);
            animator.ResetTrigger("FireOnce");
            animator.SetFloat("MoveSpeed",0);
            // Restore both state and pose when the replicated death flag clears,
            // including a reused client instance that spawned with stale death data.
            if(animator.isActiveAndEnabled)
            {
                animator.Play("Locomotion",0,0f);
                animator.Update(0f);
            }
        }

        public override void OnDestroy(){UnregisterZone();base.OnDestroy();}
    }

    public sealed class NpcZonePopulation : MonoBehaviour
    {
        // 72m square divided into 4x4 logical density zones, independent from NavMesh.
        private const int Columns=4;
        private const float HalfExtent=36f;
        private readonly int[] density=new int[Columns*Columns];

        public int GetZone(Vector3 world)
        {
            int x=Mathf.Clamp(Mathf.FloorToInt((world.x+HalfExtent)/(2*HalfExtent/Columns)),0,Columns-1);
            int z=Mathf.Clamp(Mathf.FloorToInt((world.z+HalfExtent)/(2*HalfExtent/Columns)),0,Columns-1);
            return z*Columns+x;
        }
        public int GetDensity(int zone)=>zone>=0 && zone<density.Length?density[zone]:0;
        public void Add(int zone){if(zone>=0 && zone<density.Length)density[zone]++;}
        public void Remove(int zone){if(zone>=0 && zone<density.Length)density[zone]=Mathf.Max(0,density[zone]-1);}
        public void ResetAll()=>Array.Clear(density,0,density.Length);
    }
}
