using System;
using System.Collections.Generic;
using HideAndShoot.Combat;
using HideAndShoot.NPC;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.Networking
{
    /// <summary>
    /// Host-only 30Hz x 32-frame position history, shared by up to 80 actors
    /// (8 players + 72 NPC). Never accepts a client-chosen target without
    /// validating historical capsule geometry and earlier obstructions.
    ///
    /// Ticks/capsule margin are PROVISIONAL. Future ping/interpolation
    /// calibration and final-two pre-death shot proof remain pending.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class HostShotHistory : MonoBehaviour
    {
        public const int BufferSize = 32;
        public const int FramesPerSecond = 30;
        private const float MaxRange = 125f;
        private const float Tolerance = .18f;
        private const float ShooterOriginTolerance = 1.4f;

        public readonly struct ApprovedShot
        {
            public readonly ulong ShooterClientId;
            public readonly ulong TargetNetworkId;
            public readonly int RoundId;
            public readonly int TargetGeneration;
            public readonly int FireTick;
            public readonly int RenderTick;

            public ApprovedShot(ulong shooter, ulong target, int round,
                int generation, int fireTick, int renderTick)
            {
                ShooterClientId=shooter;
                TargetNetworkId=target;
                RoundId=round;
                TargetGeneration=generation;
                FireTick=fireTick;
                RenderTick=renderTick;
            }
        }

        private struct CapsuleFrame
        {
            public int Tick;
            public int Round;
            public int Generation;
            public bool Alive;
            public Vector3 Center;
            public Vector3 Forward;
            public float Radius;
            public float HalfSegment;
        }

        private sealed class ActorRecord
        {
            public NetworkObject Actor;
            public CapsuleCollider Capsule;
            public PistolCombatController Pistol;
            public HostPlayerLife PlayerLife;
            public NpcWanderBrain Npc;
            public int Round;
            public int Generation;
            public int Head;
            public int Count;
            public bool Active;
            public readonly CapsuleFrame[] Frames=new CapsuleFrame[BufferSize];
        }

        private readonly Dictionary<ulong,ActorRecord> actors=new Dictionary<ulong,ActorRecord>(80);
        private NetworkManager manager;
        private float nextCapture;
        private int lastTick=-1;
        public event Action<ApprovedShot> HostApprovedShot;

        public int TrackedCount=>actors.Count;

        /// <summary>
        /// Clear old round/generation records before any new round spawns.
        /// Prevents stale player frames and pooled NPC identities carrying over,
        /// and keeps history memory bounded across many rounds.
        /// </summary>
        public void BeginNewRound()
        {
            if(manager==null)manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsServer)return;
            foreach(var pair in actors)
                if(pair.Value.Pistol!=null)
                    pair.Value.Pistol.HostShotCandidateReceived-=OnCandidate;
            actors.Clear();
            lastTick=-1;
            nextCapture=0f;
        }

        private void Awake()
        {
            manager=NetworkManager.Singleton;
        }

        /// <summary>Call immediately after Spawn and Round/Generation setup.</summary>
        public void RegisterActor(NetworkObject actor,int round,int generation)
        {
            if(manager==null)manager=NetworkManager.Singleton;
            if(manager==null || !manager.IsServer || actor==null || !actor.IsSpawned)return;
            var capsule=actor.GetComponentInChildren<CapsuleCollider>(true);
            if(capsule==null)
            {
                Debug.LogWarning("[HideAndShoot/History] Actor lacks CapsuleCollider: "+actor.name);
                return;
            }

            ulong id=actor.NetworkObjectId;
            if(!actors.TryGetValue(id,out var record))
            {
                record=new ActorRecord();
                actors.Add(id,record);
            }
            if(record.Pistol!=null)
                record.Pistol.HostShotCandidateReceived-=OnCandidate;

            record.Actor=actor;
            record.Capsule=capsule;
            record.Pistol=actor.GetComponent<PistolCombatController>();
            record.PlayerLife=actor.GetComponent<HostPlayerLife>();
            record.Npc=actor.GetComponent<NpcWanderBrain>();
            record.Round=round;
            record.Generation=generation;
            record.Head=0;
            record.Count=0;
            record.Active=true;
            if(record.Pistol!=null)
                record.Pistol.HostShotCandidateReceived+=OnCandidate;

            Capture(record,manager.ServerTime.Tick);
        }

        public bool TryResolveActor(ulong networkId,int round,int generation,
            out NetworkObject actor)
        {
            actor=null;
            if(!actors.TryGetValue(networkId,out var record) ||
               record.Round!=round || record.Generation!=generation ||
               record.Actor==null || !record.Actor.IsSpawned)return false;
            actor=record.Actor;
            return true;
        }

        /// <summary>Keep past samples until aged out; never use new NPC generation's frames.</summary>
        public void UnregisterActor(NetworkObject actor)
        {
            if(actor==null || !actors.TryGetValue(actor.NetworkObjectId,out var record))return;
            record.Active=false;
            if(record.Pistol!=null)
                record.Pistol.HostShotCandidateReceived-=OnCandidate;
        }

        private void LateUpdate()
        {
            if(manager==null || !manager.IsServer)return;
            if(Time.unscaledTime<nextCapture)return;
            nextCapture=Time.unscaledTime+1f/FramesPerSecond;
            int tick=manager.ServerTime.Tick;
            if(lastTick==tick)return;
            lastTick=tick;

            // No per-frame allocations or reflection; each tracked actor has a fixed
            // preallocated 32-element history ring.
            foreach(var item in actors)
                if(item.Value.Active)Capture(item.Value,tick);
        }

        private static void Capture(ActorRecord record,int tick)
        {
            if(record.Actor==null || record.Capsule==null || !record.Actor.IsSpawned)return;
            var collider=record.Capsule;
            var transform=collider.transform;
            var scale=transform.lossyScale;
            float radius=collider.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
            float halfHeight=collider.height*.5f*Mathf.Abs(scale.y);
            record.Frames[record.Head]=new CapsuleFrame
            {
                Tick=tick,
                Round=record.Round,
                Generation=record.Generation,
                Alive=(record.Npc==null || record.Npc.IsAlive) &&
                      (record.PlayerLife==null || !record.PlayerLife.IsDead.Value),
                Center=transform.TransformPoint(collider.center),
                Forward=record.Actor.transform.forward,
                Radius=radius,
                HalfSegment=Mathf.Max(0,halfHeight-radius)
            };
            record.Head=(record.Head+1)%BufferSize;
            record.Count=Mathf.Min(record.Count+1,BufferSize);
        }

        private static bool TryGetAt(ActorRecord record,int tick,out CapsuleFrame frame)
        {
            frame=default;
            int afterIndex=-1;
            int beforeIndex=-1;
            int oldest=Int32.MaxValue;
            int newest=Int32.MinValue;
            for(int i=0;i<record.Count;i++)
            {
                var candidate=record.Frames[i];
                if(candidate.Round!=record.Round ||
                    candidate.Generation!=record.Generation)continue;

                if(candidate.Tick<=tick && candidate.Tick>newest)
                {
                    newest=candidate.Tick;
                    beforeIndex=i;
                }
                if(candidate.Tick>=tick && candidate.Tick<oldest)
                {
                    oldest=candidate.Tick;
                    afterIndex=i;
                }
            }
            if(beforeIndex<0 || afterIndex<0)return false;
            var a=record.Frames[beforeIndex];
            var b=record.Frames[afterIndex];
            if(a.Alive!=b.Alive) return false; // never interpolate across death
            if(a.Tick==b.Tick){frame=a;return true;}
            float t=(tick-a.Tick)/(float)(b.Tick-a.Tick);
            frame=new CapsuleFrame
            {
                Tick=tick,Round=a.Round,Generation=a.Generation,Alive=a.Alive,
                Center=Vector3.Lerp(a.Center,b.Center,t),
                Forward=Vector3.Slerp(a.Forward,b.Forward,t),
                Radius=Mathf.Lerp(a.Radius,b.Radius,t),
                HalfSegment=Mathf.Lerp(a.HalfSegment,b.HalfSegment,t)
            };
            return true;
        }

        /// <summary>Analytic ray vs upright capsule (body/arm has only one hitbox).</summary>
        private static bool RayCapsule(Ray ray,CapsuleFrame cap,out float distance)
        {
            distance=float.MaxValue;
            Vector3 delta=ray.origin-cap.Center;
            Vector3 direction=ray.direction.normalized;
            float radius=cap.Radius+Tolerance;

            // Infinite vertical cylinder clipped to segment height.
            float a=direction.x*direction.x+direction.z*direction.z;
            float b=2f*(delta.x*direction.x+delta.z*direction.z);
            float c=delta.x*delta.x+delta.z*delta.z-radius*radius;
            if(a>1e-7f)
            {
                float discr=b*b-4f*a*c;
                if(discr>=0)
                {
                    float q=Mathf.Sqrt(discr);
                    float t1=(-b-q)/(2f*a);
                    float t2=(-b+q)/(2f*a);
                    if(t1>=0 && Mathf.Abs(delta.y+t1*direction.y)<=cap.HalfSegment)
                        distance=Mathf.Min(distance,t1);
                    if(t2>=0 && Mathf.Abs(delta.y+t2*direction.y)<=cap.HalfSegment)
                        distance=Mathf.Min(distance,t2);
                }
            }

            // Hemispherical ends: full spheres with positive t are equivalent
            // when choosing earliest ray intersection alongside the cylinder.
            TestSphere(delta-Vector3.up*cap.HalfSegment,direction,radius,ref distance);
            TestSphere(delta+Vector3.up*cap.HalfSegment,direction,radius,ref distance);
            return distance<float.MaxValue && distance<=MaxRange;
        }

        private static void TestSphere(Vector3 offset,Vector3 direction,
            float radius,ref float nearest)
        {
            float b=Vector3.Dot(offset,direction);
            float c=Vector3.Dot(offset,offset)-radius*radius;
            float discr=b*b-c;
            if(discr<0)return;
            float root=Mathf.Sqrt(discr);
            float first=-b-root;
            float second=-b+root;
            if(first>=0) nearest=Mathf.Min(first,nearest);
            else if(second>=0)nearest=Mathf.Min(second,nearest);
        }

        private void OnCandidate(ulong shooterId,Vector3 origin,Vector3 direction,
            ulong targetId,bool hasTarget,int fireTick,int renderTick)
        {
            if(manager==null || !manager.IsServer || !hasTarget)return;
            int now=manager.ServerTime.Tick;
            if(fireTick<0 || renderTick<0 ||
                fireTick>now+2 || now-fireTick>BufferSize-1 ||
                renderTick>fireTick || fireTick-renderTick>20 ||
                now-renderTick>BufferSize-1) return;

            ActorRecord shooter=null;
            foreach(var item in actors)
            {
                if(item.Value.Active && item.Value.Pistol!=null &&
                   item.Value.Actor!=null &&
                   item.Value.Actor.OwnerClientId==shooterId)
                {
                    shooter=item.Value;
                    break;
                }
            }
            if(shooter==null || !shooter.Active ||
               !TryGetAt(shooter,fireTick,out var originFrame) ||
               !originFrame.Alive || !actors.TryGetValue(targetId,out var target) ||
               !TryGetAt(target,renderTick,out var targetFrame) || !targetFrame.Alive ||
               target.Round!=shooter.Round ||
               target.Actor==null || shooter.Actor==null ||
               target.Actor==shooter.Actor) return;

            if((origin-originFrame.Center).sqrMagnitude>
               ShooterOriginTolerance*ShooterOriginTolerance)return;
            var forward=originFrame.Forward;
            var horizontalDirection=new Vector3(direction.x,0,direction.z);
            if(horizontalDirection.sqrMagnitude>.03f &&
               Vector3.Dot(forward.normalized,horizontalDirection.normalized)<.55f)
                return;

            Ray ray=new Ray(origin,direction.normalized);
            if(!RayCapsule(ray,targetFrame,out float distance))return;

            // Static structures are sampled in the live physics scene; only static
            // map geometry is assumed immutable. Dynamic actors use history below.
            RaycastHit[] blockers=Physics.RaycastAll(ray,distance,~0,QueryTriggerInteraction.Ignore);
            foreach(var blocker in blockers)
            {
                if(blocker.collider==null || blocker.distance>=distance-Tolerance)continue;
                if(blocker.collider.GetComponentInParent<NetworkObject>()!=null)continue;
                if(blocker.collider.gameObject.layer==LayerMask.NameToLayer("WorldActor"))continue;
                // Terrain, building, roof, wall: cannot penetrate.
                return;
            }

            // Check every other historically alive actor for closer obstruction.
            foreach(var entry in actors)
            {
                if(entry.Key==targetId || entry.Value==shooter)continue;
                var other=entry.Value;
                if(other.Round!=shooter.Round || !TryGetAt(other,renderTick,out var body) ||
                   !body.Alive)continue;
                if(RayCapsule(ray,body,out float earlier) &&
                   earlier<distance-Tolerance)return;
            }

            HostApprovedShot?.Invoke(new ApprovedShot(shooterId,targetId,
                target.Round,target.Generation,fireTick,renderTick));
        }

        private void OnDestroy()
        {
            foreach(var item in actors)
                if(item.Value.Pistol!=null)
                    item.Value.Pistol.HostShotCandidateReceived-=OnCandidate;
            actors.Clear();
        }
    }
}
