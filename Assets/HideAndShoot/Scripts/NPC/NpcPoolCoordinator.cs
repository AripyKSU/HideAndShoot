using System;
using System.Collections.Generic;
using HideAndShoot.World;
using HideAndShoot.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndShoot.NPC
{
    /// <summary>
    /// Host-only round population manager. Initial target rises by
    /// 16,14,12,10,8,6,4,2 for participants 1..8;
    /// killed NPCs NEVER respawn within the current round.
    ///
    /// Uses Despawn(false) and inactive host objects for reuse next round.
    /// Client-side objects are reused by PooledNetworkInstanceHandler registered
    /// with NGO before connection. Both pools keep generations separated.
    /// </summary>
    public sealed class NpcPoolCoordinator : MonoBehaviour
    {
        [SerializeField] private NpcZonePopulation density;
        [SerializeField] private float corpseAnimationSeconds = 1.8f; // provisional clip length
        [SerializeField] private int sampleAttemptsPerSpawn = 35;

        private readonly Stack<NetworkObject> hostInactive = new Stack<NetworkObject>();
        private readonly List<NetworkObject> currentRound = new List<NetworkObject>();
        private readonly Dictionary<NetworkObject,float> corpsePending = new Dictionary<NetworkObject,float>();
        private readonly List<NetworkObject> expired = new List<NetworkObject>();
        private HideAndShoot.Combat.HostPlayerLife[] spawnPlayers=
            Array.Empty<HideAndShoot.Combat.HostPlayerLife>();

        private GameObject prefab;
        private NetworkManager network;
        private int roundId = -1;
        private int nextGeneration = 1;
        private int totalCreated;

        public int RoundId => roundId;
        public int SpawnedCount => currentRound.Count;
        public int LiveCount
        {
            get
            {
                int live=0;
                foreach(var obj in currentRound)
                    if(obj!=null && obj.IsSpawned &&
                       obj.TryGetComponent<NpcWanderBrain>(out var brain) &&
                       brain.IsAlive) live++;
                return live;
            }
        }
        public int TotalCreated => totalCreated;

        /// <summary>Dense small lobbies, tapering additions through eight players.</summary>
        public static int TargetNpcCount(int participantCount)
        {
            return participantCount>=1 && participantCount<=8
                ? participantCount*(17-participantCount) : 0;
        }

        private void Awake()
        {
            if(density == null) density = GetComponent<NpcZonePopulation>();
        }

        private bool EnsureInitialized()
        {
            network=NetworkManager.Singleton;
            if(network == null || !network.IsServer) return false;
            if(density==null)
                density=FindFirstObjectByType<NpcZonePopulation>();
            var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            prefab=catalog!=null?catalog.NPCActor:null;
            if(prefab==null || density==null)
            {
                Debug.LogError("[HideAndShoot/NPC] Missing NPC prefab catalog or density component.");
                return false;
            }
            return true;
        }

        /// <summary>Round controller alone may call this at RoundSetup.</summary>
        public bool BeginRound(int newRoundId,int participantCount)
        {
            if(!EnsureInitialized() || participantCount<2 || participantCount>8 ||
                newRoundId<=roundId) return false;

            ReleaseWholeRound();
            roundId=newRoundId;
            density.ResetAll();
            spawnPlayers=FindObjectsByType<HideAndShoot.Combat.HostPlayerLife>(
                FindObjectsSortMode.None);

            int target=TargetNpcCount(participantCount);
            int created=0;
            var random=new System.Random(unchecked(Environment.TickCount+newRoundId*7919));
            for(int i=0;i<target;i++)
            {
                if(!TryCreateNpc(random))
                {
                    Debug.LogWarning("[HideAndShoot/NPC] Could not sample a valid NavMesh location for NPC "+i);
                    continue;
                }
                created++;
            }

            if(created!=target)
            {
                Debug.LogError("[HideAndShoot/NPC] Population incomplete: "+created+"/"+target+
                    ". Retiring partial population before aborting setup.");
                ReleaseWholeRound();
            }
            else
                Debug.Log("[HideAndShoot/NPC] Round "+newRoundId+": "+created+" roaming NPCs.");
            return created==target;
        }

        private bool TryCreateNpc(System.Random random)
        {
            NetworkObject obj=null;
            Vector3 position=Vector3.zero;
            bool found=false;
            for(int attempt=0;attempt<sampleAttemptsPerSpawn;attempt++)
            {
                float x=-34f+(float)random.NextDouble()*68f;
                float z=-34f+(float)random.NextDouble()*68f;
                float y=random.Next(0,2)==0?0f:3.2f;
                if(!NavMesh.SamplePosition(new Vector3(x,y,z),out NavMeshHit hit,3.0f,NavMesh.AllAreas))
                    continue;

                if(!float.IsNaN(hit.position.x) && !float.IsNaN(hit.position.y)
                   && SpawnSpaceAvailable(hit.position))
                {
                    position=hit.position;
                    found=true;
                    break;
                }
            }
            if(!found)return false;
            while(hostInactive.Count>0 && obj==null)
                obj=hostInactive.Pop();

            if(obj==null)
            {
                var go=Instantiate(prefab,position,Quaternion.identity);
                obj=go.GetComponent<NetworkObject>();
                if(obj==null){Destroy(go);return false;}
                totalCreated++;
            }
            obj.transform.SetPositionAndRotation(position,Quaternion.Euler(0,random.Next(0,360),0));
            obj.gameObject.SetActive(true);

            // Init movement AFTER NGO Spawn so NetworkBehaviour.IsServer is true.
            obj.Spawn(true);
            var brain=obj.GetComponent<NpcWanderBrain>();
            if(brain==null)
            {
                RetireActor(obj);
                return false;
            }
            brain.ResetForRound(roundId);
            if(!brain.ResetForSpawn(nextGeneration++,position,density))
            {
                RetireActor(obj);
                return false;
            }
            var history=FindFirstObjectByType<HostShotHistory>();
            if(history!=null)history.RegisterActor(obj,roundId,brain.Generation);
            currentRound.Add(obj);
            return true;
        }

        /// <summary>
        /// Initial NPCs must not occupy the same capsule as an existing NPC
        /// or a round participant. No artificial density separation after spawn.
        /// </summary>
        private bool SpawnSpaceAvailable(Vector3 point)
        {
            const float minDistanceSqr=1.0f;
            foreach(var existing in currentRound)
            {
                if(existing==null || !existing.IsSpawned)continue;
                Vector3 delta=existing.transform.position-point;
                delta.y=0;
                if(delta.sqrMagnitude<minDistanceSqr &&
                    Mathf.Abs(existing.transform.position.y-point.y)<1.8f)
                    return false;
            }
            foreach(var player in spawnPlayers)
            {
                if(!player.IsSpawned)continue;
                Vector3 delta=player.transform.position-point;
                delta.y=0;
                if(delta.sqrMagnitude<minDistanceSqr &&
                   Mathf.Abs(player.transform.position.y-point.y)<1.8f)
                    return false;
            }
            return true;
        }

        public void ReleaseCountdownMovement(int activeRoundId)
        {
            if(network==null || !network.IsServer || activeRoundId!=roundId)return;
            int resumed=0;
            foreach(var obj in currentRound)
                if(obj!=null && obj.IsSpawned &&
                   obj.TryGetComponent<NpcWanderBrain>(out var npc))
                {
                    npc.HostReleaseCountdownMovement();
                    resumed++;
                }
            Debug.Log("[HideAndShoot/NPC] Countdown ended; released movement for "+
                      resumed+" NPCs in round "+activeRoundId);
        }

        public void FreezeMovement()
        {
            if(network==null || !network.IsServer)return;
            foreach(var obj in currentRound)
                if(obj!=null && obj.IsSpawned &&
                   obj.TryGetComponent<NpcWanderBrain>(out var npc))
                    npc.HostFreezeCountdownMovement();
        }

        /// <summary>Called only after Host approves a hit/death.</summary>
        public bool ApproveDeath(NetworkObject actor,int expectedRound,int expectedGeneration)
        {
            if(network==null || !network.IsServer || actor==null ||
                !actor.IsSpawned || !currentRound.Contains(actor) ||
                corpsePending.ContainsKey(actor)) return false;

            var npc=actor.GetComponent<NpcWanderBrain>();
            if(npc==null || !npc.IsAlive ||
                npc.RoundId!=expectedRound || npc.Generation!=expectedGeneration)
                return false;

            npc.HostApprovedDeath(expectedRound,expectedGeneration);
            corpsePending[actor]=Time.time+Mathf.Max(.4f,corpseAnimationSeconds);
            return true;
        }

        private void Update()
        {
            if(network==null || !network.IsServer || corpsePending.Count==0)return;
            expired.Clear();
            foreach(var kv in corpsePending)
                if(Time.time>=kv.Value)expired.Add(kv.Key);
            foreach(var obj in expired)
            {
                corpsePending.Remove(obj);
                currentRound.Remove(obj);
                RetireActor(obj);
            }
        }

        private void RetireActor(NetworkObject obj)
        {
            if(obj==null)return;
            var history=FindFirstObjectByType<HostShotHistory>();
            if(history!=null)history.UnregisterActor(obj);
            var brain=obj.GetComponent<NpcWanderBrain>();
            // Do not reset the dying Animator before corpse animation is complete.
            brain?.ReturnToPool();
            if(obj.IsSpawned) obj.Despawn(false);
            obj.gameObject.SetActive(false);
            hostInactive.Push(obj);
        }

        /// <summary>Only after the previous round's result/celebration completes.</summary>
        public void ReleaseWholeRound()
        {
            if(network!=null && !network.IsServer)return;
            foreach(var obj in currentRound)
                RetireActor(obj);
            currentRound.Clear();
            corpsePending.Clear();
            expired.Clear();
            spawnPlayers=Array.Empty<HideAndShoot.Combat.HostPlayerLife>();
            if(density!=null)density.ResetAll();
        }

        private void OnDestroy()
        {
            if(network != null && network.IsServer)
                ReleaseWholeRound();
            foreach(var obj in hostInactive)
                if(obj!=null) Destroy(obj.gameObject);
            hostInactive.Clear();
        }
    }
}
