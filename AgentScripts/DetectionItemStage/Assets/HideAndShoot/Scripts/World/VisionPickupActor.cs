using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.Player;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.World
{
    /// <summary>
    /// Networked trigger for the detection sunglasses. The server authorizes acquisition,
    /// enforcing one claimant. Client touch alone grants no detection effect.
    /// </summary>
    public sealed class VisionPickupActor : NetworkBehaviour, IPooledActorReset
    {
        private VisionSpawnDirector director;
        private int roundId=-1;
        private int generation=-1;
        private int candidateIndex=-1;
        private bool claimed;
        private Transform visual;
        private Transform glowShell;
        private Vector3 visualHome;
        private Quaternion visualRotation;
        private bool visualPrepared;
        private float nextProximityCheck;

        public int RoundId => roundId;
        public int Generation => generation;
        public int CandidateIndex => candidateIndex;
        public bool Claimed => claimed;

        public override void OnNetworkSpawn()
        {
            // Keep the sunglasses materials intact; its separate transparent shell owns the glow.
            PrepareVisual();
            if(glowShell!=null){glowShell.gameObject.SetActive(true);glowShell.localPosition=Vector3.zero;}
            visual.gameObject.SetActive(true);
            visual.localPosition=visualHome;
            visual.localRotation=visualRotation;
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))renderer.enabled=true;
        }

        public void PrepareVisual()
        {
            if(visualPrepared)return;
            visual=transform.Find("SunglassesVisual");
            if(visual==null && transform.childCount>0)visual=transform.GetChild(0);
            if(visual==null)
            {
                var box=GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name="SunglassesVisual";
                visual=box.transform;
                visual.SetParent(transform,false);
                var collider=box.GetComponent<Collider>();
                collider.enabled=false;
                Destroy(collider);
            }
            // Imported FBX dimensions are tiny; normalize in pickup-local space, once.
            SharedPistolVisual.NormalizeVisual(visual,transform,.48f);
            if(SharedPistolVisual.TryLocalBounds(visual,transform,out var bounds))
                visual.localPosition-=bounds.center;
            visualHome=visual.localPosition;
            visualRotation=visual.localRotation;
            int layer=LayerMask.NameToLayer("AmmoPickup");
            foreach(var item in visual.GetComponentsInChildren<Transform>(true))
                if(layer>=0)item.gameObject.layer=layer;
            glowShell=transform.Find("PickupGlowShell");
            visualPrepared=true;
        }

        public override void OnNetworkDespawn()
        {
            if(glowShell!=null){glowShell.gameObject.SetActive(false);glowShell.localPosition=Vector3.zero;}
            if(visual!=null)visual.localPosition=visualHome;
            if(visual!=null)visual.localRotation=visualRotation;
        }

        private void Update()
        {
            if(!IsSpawned || visual==null)return;
            visual.localPosition=visualHome+Vector3.up*
                (Mathf.Sin(Time.time*3f)*.1f);
            visual.Rotate(Vector3.up,Time.deltaTime*30f,Space.World);
            if(glowShell!=null)glowShell.localPosition=Vector3.up*(Mathf.Sin(Time.time*3f)*.1f);
        }

        public void PrepareServer(int round,int newGeneration,int positionIndex,VisionSpawnDirector owner)
        {
            if(!IsServer) return;
            director=owner;
            roundId=round;
            generation=newGeneration;
            candidateIndex=positionIndex;
            claimed=false;
        }

        private void FixedUpdate()
        {
            if(!IsServer || !IsSpawned || claimed || director==null ||
               Time.time<nextProximityCheck)return;
            nextProximityCheck=Time.time+.2f;
            // Owner-authority NetworkTransform updates positions on the host
            // without a CharacterController.Move call on the host; relying
            // only on OnTriggerEnter can miss a teleport/interpolation overlap.
            foreach(var collider in Physics.OverlapSphere(transform.position,
                .95f,~0,QueryTriggerInteraction.Collide))
                TryContact(collider);
        }

        private void OnTriggerStay(Collider collider)=>TryContact(collider);

        private void OnTriggerEnter(Collider collider)=>TryContact(collider);

        private void TryContact(Collider collider)
        {
            if(!IsServer || claimed || director==null || collider==null) return;
            var character=collider.GetComponentInParent<PistolCombatController>();
            if(character==null || !character.IsSpawned) return;
            director.TryCollect(this,character);
        }

        public bool TryClaim(int expectedRound,int expectedGeneration)
        {
            if(!IsServer || claimed || roundId!=expectedRound ||
                generation!=expectedGeneration)return false;
            claimed=true;
            return true;
        }

        public void ClearServerState()
        {
            director=null;
            claimed=true;
            candidateIndex=-1;
            roundId=-1;
            generation=-1;
        }

        public void ResetAfterNetworkDespawn()
        {
            // Client-side pooled copy cannot influence the next server generation.
            director=null;
            claimed=false;
            roundId=-1;
            generation=-1;
            candidateIndex=-1;
        }
    }
}
