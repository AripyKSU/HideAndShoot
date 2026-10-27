using System;
using System.Collections.Generic;
using HideAndShoot.Combat;
using HideAndShoot.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndShoot.World
{
    /// <summary>One world pickup. First spawn at +10s; replacement 10s after collection.</summary>
    public sealed class VisionSpawnDirector : MonoBehaviour
    {
        public const double SpawnDelaySeconds=10d;
        public const int MaximumActive=1;
        private readonly Stack<NetworkObject> inactive=new Stack<NetworkObject>();
        private NetworkManager network;
        private AmmoSpawnDirector ammo;
        private GameObject prefab;
        private Transform[] candidates=Array.Empty<Transform>();
        private System.Random random;
        private VisionPickupActor active;
        private int roundId=-1,generation;
        private bool activeRound;
        private double nextSpawnAt=double.PositiveInfinity;
        public int ActiveCount=>active!=null && active.IsSpawned?1:0;
        public bool IsRoundActive=>activeRound;
        public double SecondsUntilSpawn=>activeRound && ActiveCount==0?
            Math.Max(0d,nextSpawnAt-Time.realtimeSinceStartupAsDouble):double.PositiveInfinity;
        public bool IsCandidateOccupied(int index)=>ActiveCount>0 && active.CandidateIndex==index;

        public bool BeginRound(int round)
        {
            network=NetworkManager.Singleton;
            if(network==null || !network.IsServer || round<=roundId)return false;
            var registry=FindFirstObjectByType<SpawnPointRegistry>();
            var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            prefab=catalog!=null?catalog.VisionPickup:null;
            if(registry==null || prefab==null)return false;
            var source=registry.GetAmmoCandidates();
            if(source.Count==0)return false;
            candidates=new Transform[source.Count];
            for(int i=0;i<source.Count;i++)candidates[i]=source[i];
            EndRound();roundId=round;ammo=GetComponent<AmmoSpawnDirector>();
            random=new System.Random(unchecked(Environment.TickCount+round*21911));
            activeRound=true;nextSpawnAt=Time.realtimeSinceStartupAsDouble+SpawnDelaySeconds;
            return true;
        }
        private void Update()
        {
            if(!activeRound || network==null || !network.IsServer || ActiveCount>0 ||
               Time.realtimeSinceStartupAsDouble<nextSpawnAt)return;
            nextSpawnAt=Time.realtimeSinceStartupAsDouble+SpawnDelaySeconds;
            SpawnOne();
        }
        private bool SpawnOne()
        {
            if(!activeRound || network==null || !network.IsServer || ActiveCount>0)return false;
            var available=new List<int>(candidates.Length);
            for(int i=0;i<candidates.Length;i++)
                if(candidates[i]!=null && (ammo==null || !ammo.IsCandidateOccupied(i)))available.Add(i);
            if(available.Count==0)return false;
            int index=available[random.Next(available.Count)];
            Vector3 position=candidates[index].position;
            if(NavMesh.SamplePosition(position,out var hit,3f,NavMesh.AllAreas))
                position=hit.position+Vector3.up*.55f;
            else position.y+=.55f;
            NetworkObject obj=null;
            while(inactive.Count>0 && obj==null)obj=inactive.Pop();
            if(obj==null)obj=Instantiate(prefab).GetComponent<NetworkObject>();
            obj.transform.SetPositionAndRotation(position,candidates[index].rotation);
            obj.gameObject.SetActive(true);obj.Spawn(true);
            active=obj.GetComponent<VisionPickupActor>();
            active.PrepareServer(roundId,++generation,index,this);
            nextSpawnAt=double.PositiveInfinity;
            return true;
        }
        public bool TryCollect(VisionPickupActor actor,PistolCombatController character)
        {
            if(!activeRound || network==null || !network.IsServer || actor==null || actor!=active ||
               !actor.IsSpawned || actor.Claimed || actor.RoundId!=roundId || character==null ||
               !character.IsSpawned || (character.transform.position-actor.transform.position).sqrMagnitude>2.7225f)
                return false;
            var vision=character.GetComponent<PlayerDetectionVision>();
            if(vision==null || !vision.CanActivateForRound(roundId))return false;
            if(!actor.TryClaim(roundId,actor.Generation))return false;
            if(!vision.HostActivate(roundId))throw new InvalidOperationException("Validated vision grant failed");
            var obj=actor.NetworkObject;active=null;Recycle(obj);
            nextSpawnAt=Time.realtimeSinceStartupAsDouble+SpawnDelaySeconds;
            return true;
        }
        private void Recycle(NetworkObject obj)
        {
            if(obj==null)return;
            obj.GetComponent<VisionPickupActor>().ClearServerState();
            if(obj.IsSpawned)obj.Despawn(false);
            obj.gameObject.SetActive(false);inactive.Push(obj);
        }
        public void EndRound()
        {
            activeRound=false;nextSpawnAt=double.PositiveInfinity;
            if(active!=null){var obj=active.NetworkObject;active=null;Recycle(obj);}
        }
        private void OnDestroy()
        {
            if(network!=null && network.IsServer)EndRound();
            foreach(var obj in inactive)if(obj!=null)Destroy(obj.gameObject);
            inactive.Clear();
        }
    }
}
