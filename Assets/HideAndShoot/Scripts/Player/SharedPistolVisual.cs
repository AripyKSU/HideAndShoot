using UnityEngine;

namespace HideAndShoot.Player
{
    public static class SharedPistolVisual
    {
        public static Transform EnsureWorldPistol(Transform root)
        {
            var animator=root!=null?root.GetComponentInChildren<Animator>(true):null;
            if(animator==null || animator.avatar==null || !animator.avatar.isHuman)return null;
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            if(hand==null)return null;
            if(animator.GetComponent<PistolFingerGrip>()==null)animator.gameObject.AddComponent<PistolFingerGrip>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name=="Pistol_ProvisionalCandidate")renderer.enabled=false;
            var mount=hand.Find("Pistol_WorldVisible");
            if(mount==null)
            {
                mount=new GameObject("Pistol_WorldVisible").transform;
                mount.SetParent(hand,false);
            }
            ConfigureGun(mount,"WorldHandgun");
            SetLayerRecursive(mount.gameObject,LayerMask.NameToLayer("WorldActor"));
            return mount;
        }

        public static Transform EnsureOwnerGun(Camera camera,Transform mount,Transform hand)
        {
            if(camera==null || hand==null)return null;
            var animator=hand.GetComponentInParent<Animator>();
            if(animator!=null && animator.GetComponent<PistolFingerGrip>()==null)
                animator.gameObject.AddComponent<PistolFingerGrip>();
            if(mount==null)mount=new GameObject("PistolViewModel_TO_BUILD").transform;
            mount.SetParent(hand,false);
            mount.localScale=Vector3.one;
            ConfigureGun(mount,"FPS_QuaterniusPistol");
            SetLayerRecursive(mount.gameObject,LayerMask.NameToLayer("FPSViewModel"));
            return mount;
        }

        public static void PoseGrip(Transform mount,Animator animator,Quaternion rotation)
        {
            if(mount==null || animator==null || animator.avatar==null || !animator.avatar.isHuman)return;
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var middle=animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if(hand==null)return;
            var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if(middle!=null && index!=null && little!=null)
            {
                // Index finger belongs above the pinky on an upright pistol grip.
                // Retargeted Kyle wrists otherwise present a flat, palm-up hand.
                var axis=(middle.position-hand.position).normalized;
                var across=Vector3.ProjectOnPlane(index.position-little.position,axis);
                var up=Vector3.ProjectOnPlane(rotation*Vector3.up,axis);
                if(across.sqrMagnitude>.000001f && up.sqrMagnitude>.000001f)
                    hand.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(across,up,axis),axis)*hand.rotation;
            }
            // The pivot is the palm, between wrist and knuckles, rather than the FBX origin.
            mount.position=middle!=null?Vector3.Lerp(hand.position,middle.position,.65f):hand.position;
            mount.rotation=rotation;
        }

        private static void ConfigureGun(Transform mount,string modelName)
        {
            var gun=mount.Find(modelName);
            if(gun==null)
            {
                // Reuse the authored owner FBX if it is already present.
                foreach(Transform child in mount)
                    if(child.GetComponentInChildren<MeshFilter>(true)!=null){gun=child;break;}
            }
            if(gun==null)
            {
                var source=Resources.Load<GameObject>("PistolViewModel");
                if(source==null){Debug.LogError("[HideAndShoot/Gun] Missing PistolViewModel");return;}
                gun=Object.Instantiate(source,mount,false).transform;
            }
            gun.name=modelName;
            // Quaternius mesh: barrel toward -X, height +Z. Convert once to +Z / +Y.
            gun.localPosition=Vector3.zero;
            gun.localRotation=Quaternion.Euler(-90f,90f,0f);
            gun.localScale=Vector3.one;
            NormalizeVisual(gun,mount,.26f);
            if(TryLocalBounds(gun,mount,out var bounds))
            {
                var grip=bounds.center+new Vector3(0,-bounds.size.y*.28f,-bounds.size.z*.28f);
                gun.localPosition-=grip;
                TryLocalBounds(gun,mount,out bounds);
                var muzzle=mount.Find("Muzzle");
                if(muzzle==null)
                {
                    muzzle=new GameObject("Muzzle").transform;
                    muzzle.SetParent(mount,false);
                }
                muzzle.localPosition=new Vector3(bounds.center.x,bounds.max.y-bounds.size.y*.18f,bounds.max.z);
                muzzle.localRotation=Quaternion.identity;
            }
            var material=Resources.Load<Material>("PistolViewModelMaterial");
            foreach(var renderer in gun.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled=true;
                if(material==null)continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)materials[i]=material;
                renderer.sharedMaterials=materials;
            }
        }

        public static void NormalizeVisual(Transform visual,Transform reference,float length)
        {
            if(!TryLocalBounds(visual,reference,out var bounds))return;
            float dimension=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            if(dimension>.000001f)visual.localScale*=length/dimension;
        }

        public static bool TryLocalBounds(Transform visual,Transform reference,out Bounds bounds)
        {
            bounds=default;
            bool found=false;
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var local=renderer.localBounds;
                // Multiply local transforms directly; world-space subtraction loses
                // precision for imported centimetre meshes far from the world origin.
                var matrix=Matrix4x4.identity;
                var current=renderer.transform;
                while(current!=null && current!=reference)
                {
                    matrix=Matrix4x4.TRS(current.localPosition,current.localRotation,current.localScale)*matrix;
                    current=current.parent;
                }
                if(current!=reference)matrix=reference.worldToLocalMatrix*renderer.localToWorldMatrix;
                for(int x=-1;x<=1;x+=2)
                for(int y=-1;y<=1;y+=2)
                for(int z=-1;z<=1;z+=2)
                {
                    var corner=local.center+Vector3.Scale(local.extents,new Vector3(x,y,z));
                    var point=matrix.MultiplyPoint3x4(corner);
                    if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        private static void SetLayerRecursive(GameObject root,int layer)
        {
            if(layer<0)return;
            root.layer=layer;
            foreach(Transform child in root.transform)SetLayerRecursive(child.gameObject,layer);
        }
    }
}
