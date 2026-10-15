using HideAndShoot.Player;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HideAndShoot.UI
{
    public sealed class PodiumCelebration : MonoBehaviour
    {
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        public AnimationClip SelectedClip {get;private set;}

        public void PlayRank(int rank,int variation)
        {
            var set=Resources.Load<PodiumAnimationSet>("PodiumAnimationSet");
            var animator=GetComponent<Animator>();
            if(set==null || set.clips==null || set.clips.Length<3 || animator==null)
            {Debug.LogError("[HideAndShoot/Podium] Three celebration clips are required.");return;}
            if(graph.IsValid())graph.Destroy();
            foreach(var grip in GetComponentsInChildren<PistolFingerGrip>(true))grip.enabled=false;
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name.Contains("Pistol") ||
                    renderer.GetComponentInParent<Transform>().name.Contains("Handgun"))
                    renderer.enabled=false;
            foreach(var transform in GetComponentsInChildren<Transform>(true))
                if(transform.name=="Pistol_WorldVisible" || transform.name=="RightHandSocket")
                    transform.gameObject.SetActive(false);
            animator.applyRootMotion=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController=null;
            SelectedClip=set.clips[(int)(((long)Mathf.Max(0,variation)+Mathf.Clamp(rank,0,2))%set.clips.Length)];
            graph=PlayableGraph.Create("PodiumCelebration_"+rank);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            playable=AnimationClipPlayable.Create(graph,SelectedClip);
            playable.SetTime(rank*.23f);
            var output=AnimationPlayableOutput.Create(graph,"FullBodyCelebration",animator);
            output.SetSourcePlayable(playable);
            graph.Play();graph.Evaluate(0);
        }

        public void SampleTime(float seconds)
        {
            if(!graph.IsValid())return;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable.SetTime(seconds);
            graph.Evaluate(0);
        }
        private void OnDestroy(){if(graph.IsValid())graph.Destroy();}
    }
}
