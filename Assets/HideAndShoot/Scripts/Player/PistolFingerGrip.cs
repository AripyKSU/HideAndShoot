using System.Collections.Generic;
using UnityEngine;

namespace HideAndShoot.Player
{
    // Kyle's retargeted locomotion clips leave the fingers open. Curl only the
    // right-hand muscles after animation, while keeping arm and body motion intact.
    [DefaultExecutionOrder(-50)]
    public sealed class PistolFingerGrip : MonoBehaviour
    {
        private Animator animator;
        private HumanPoseHandler handler;
        private HumanPose pose;
        private readonly List<int> muscles=new List<int>();

        public void ApplyGrip()
        {
            if(animator==null)animator=GetComponent<Animator>();
            if(animator==null || !animator.isHuman || animator.avatar==null)return;
            if(animator.GetBool("Dead"))return;
            if(handler==null)
            {
                handler=new HumanPoseHandler(animator.avatar,animator.transform);
                var names=HumanTrait.MuscleName;
                for(int i=0;i<names.Length;i++)
                    if(names[i].StartsWith("Right ") && names[i].Contains("Stretched"))muscles.Add(i);
            }
            handler.GetHumanPose(ref pose);
            foreach(int muscle in muscles)
            {
                string name=HumanTrait.MuscleName[muscle];
                pose.muscles[muscle]=name.Contains("Index")?-.25f:name.Contains("Thumb")?-.35f:-.65f;
            }
            handler.SetHumanPose(ref pose);
        }

        private void LateUpdate()=>ApplyGrip();
        private void OnDestroy()
        {
            handler?.Dispose();
            handler=null;
        }
    }
}
