using System;
using System.Collections.Generic;
using HideAndShoot.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndShoot.World
{
    /// <summary>
    /// Host-only timed ammo field. At RoundActive: zero pickups.
    /// Every five seconds at most one spawn attempt; no immediate replacement.
    /// Max live pickups equals participant count locked at round start.
    /// Skipped ticks are not queued or later burst-spawned.
    /// </summary>
    public sealed class AmmoSpawnDirector : MonoBehaviour
    {
        private const double AttemptPeriod=5d;
        [SerializeField] private SpawnPointRegistry anchorRegistry;
        private readonly Stack<NetworkObject> hostInactive=new Stack<NetworkObject>();
        private readonly Dictionary<AmmoPickupActor,int> active=new Dictionary<AmmoPickupActor,int>();
        private readonly HashSet<int> occupiedCandidates=new HashSet<int>();
        private readonly List<AmmoPickupActor> cleanup=new List<AmmoPickupActor>();

        private NetworkManager network;
        private GameObject prefab;
        private Transform[] candidates=Array.Empty<Transform>();
        private System.Random random;
        private double startServerTime;
        private int nextAttemptIndex;
        private int roundId=-1;
        private int cap;
        private int generation;
        private bool activeRound;

        public int ActiveCount=>active.Count;
        public int MaximumActive=>cap;
        public int AttemptCount=>Mathf.Max(0,nextAttemptIndex-1);
        public bool IsRoundActive=>activeRound;

        private bool Ready()
        {
            network=NetworkManager.Singleton;
            if(network==null || !network.IsServer) return false;
            if(anchorRegistry==null)
                anchorRegistry=FindFirstObjectByType<SpawnPointRegistry>();
            var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            prefab=catalog!=null?catalog.AmmoPickup:null;
            if(anchorRegistry==null || prefab==null)
            {
                Debug.LogError("[HideAndShoot/Ammo] Missing pickup prefab or map candidates.");
                return false;
            }
            var source=anchorRegistry.GetAmmoCandidates();
            candidates=new Transform[source.Count];
            for(int i=0;i<source.Count;i++) candidates[i]=source[i];
            if(candidates.Length<cap)
                Debug.LogError("[HideAndShoot/Ammo] Candidates="+candidates.Length+
                               " < player cap="+cap);
            return candidates.Length>=cap;
        }

        /// <summary>Call only when five-second countdown has finished.</summary>
        public bool BeginRound(int newRoundId,int participants)
        {
            if(participants<2 || participants>8 || newRoundId<=roundId)return false;
            cap=participants;
            if(!Ready())return false;

            EndRound();
            roundId=newRoundId;
            cap=participants;
            random=new System.Random(unchecked(Environment.TickCount+roundId*9011));
            // Current NGO server time, rather than DateTime or client clock.
            // Unscaled host monotonic clock also advances during NGO
            // transport hitches; do not depend on a late NetworkTime sample.
            startServerTime=Time.realtimeSinceStartupAsDouble;
            nextAttemptIndex=1;
            activeRound=true;
            Debug.Log("[HideAndShoot/Ammo] RoundActive="+roundId+
                ", candidate sites="+candidates.Length+", cap="+cap+
                ", first attempt at +5s; hostTime="+startServerTime.ToString("F2"));
            return true;
        }

        private void Update()
        {
            if(!activeRound || network==null || !network.IsServer)return;
            double now=Time.realtimeSinceStartupAsDouble;
            double firstNext=startServerTime+nextAttemptIndex*AttemptPeriod;
            if(now < firstNext)return;

            // At most one attempt per Update. If the editor/game stalls,
            // advance to the current 5s slot WITHOUT accumulating backlog.
            int elapsedSlots=Mathf.Max(1,(int)Math.Floor((now-startServerTime)/AttemptPeriod));
            nextAttemptIndex=elapsedSlots+1;
            if(active.Count>=cap)return;
            SpawnOne();
        }

        private void SpawnOne()
        {
            var possible=new List<int>(candidates.Length);
            for(int i=0;i<candidates.Length;i++)
                if(candidates[i]!=null && !occupiedCandidates.Contains(i)) possible.Add(i);
            if(possible.Count==0)
            {
                Debug.LogWarning("[HideAndShoot/Ammo] No free candidates at +"+
                    (Time.realtimeSinceStartupAsDouble-startServerTime).ToString("F1")+"s");
                return;
            }

            int index=possible[random.Next(possible.Count)];
            Vector3 position=candidates[index].position;
            // Candidates were generated at y=.22, including some near raised
            // walkways. Snap to an actually walkable NavMesh floor and lift the
            // pickup above it so it cannot hide under ground geometry.
            if(NavMesh.SamplePosition(position,out var nav,3f,NavMesh.AllAreas))
                position=nav.position+Vector3.up*.55f;
            else
                position.y+=.55f;
            Quaternion rotation=candidates[index].rotation;
            NetworkObject obj=null;
            while(hostInactive.Count>0 && obj==null)
                obj=hostInactive.Pop();
            if(obj==null)
            {
                var instance=Instantiate(prefab,position,rotation);
                obj=instance.GetComponent<NetworkObject>();
                if(obj==null)
                {
                    Destroy(instance);
                    Debug.LogError("[HideAndShoot/Ammo] Pickup prefab requires NetworkObject");
                    return;
                }
            }

            obj.transform.SetPositionAndRotation(position,rotation);
            obj.gameObject.SetActive(true);
            obj.Spawn(true);
            var actor=obj.GetComponent<AmmoPickupActor>();
            if(actor==null)
            {
                Recycle(obj);
                Debug.LogError("[HideAndShoot/Ammo] Pickup prefab requires AmmoPickupActor");
                return;
            }
            actor.PrepareServer(roundId,++generation,index,this);
            occupiedCandidates.Add(index);
            active[actor]=index;
            Debug.Log("[HideAndShoot/Ammo] SPAWN success round="+roundId+
                " slot="+nextAttemptIndex+" position="+position+
                " netId="+obj.NetworkObjectId+" active="+active.Count+"/"+cap);
        }

        /// <summary>Atomic Host-only first claimant wins; a full-mag touch preserves pickup (provisional).</summary>
        public bool TryCollect(AmmoPickupActor actor,PistolCombatController character)
        {
            if(!activeRound || network==null || !network.IsServer || actor==null ||
                character==null || !character.IsSpawned || actor.RoundId!=roundId ||
                !active.TryGetValue(actor,out int index) || actor.Claimed)
                return false;

            // Only living, participating players in this active round
            // can collect; spectators and stale-round avatars cannot.
            var life=character.GetComponent<HostPlayerLife>();
            if(!character.RoundActive.Value || character.RoundId.Value!=roundId ||
               (life!=null && (life.IsDead.Value || life.RoundId!=roundId)))
                return false;

            // Respect the authoritative cap. No client-supplied ammo or pickup identity.
            if(character.ServerAmmo.Value >= 10)
                return false;
            int generationAtContact=actor.Generation;
            if(!actor.TryClaim(roundId,generationAtContact))
                return false;

            character.HostGrantAmmo(5);
            active.Remove(actor);
            occupiedCandidates.Remove(index);
            Recycle(actor.NetworkObject);
            return true;
        }

        private void Recycle(NetworkObject obj)
        {
            if(obj==null)return;
            var actor=obj.GetComponent<AmmoPickupActor>();
            actor?.ClearServerState();
            if(obj.IsSpawned)obj.Despawn(false);
            obj.gameObject.SetActive(false);
            hostInactive.Push(obj);
        }

        public void EndRound()
        {
            activeRound=false;
            cleanup.Clear();
            foreach(var entry in active)cleanup.Add(entry.Key);
            foreach(var actor in cleanup)
                if(actor!=null)Recycle(actor.NetworkObject);
            active.Clear();
            occupiedCandidates.Clear();
            cleanup.Clear();
            nextAttemptIndex=0;
        }

        private void OnDestroy()
        {
            if(network!=null && network.IsServer)EndRound();
            foreach(var obj in hostInactive)
                if(obj!=null)Destroy(obj.gameObject);
            hostInactive.Clear();
        }
    }
}
