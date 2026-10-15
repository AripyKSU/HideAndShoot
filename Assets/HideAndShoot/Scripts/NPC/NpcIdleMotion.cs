using UnityEngine;

namespace HideAndShoot.NPC
{
    /// <summary>Time-based ambient motion, independent of networking and NavMesh simulation.</summary>
    public sealed class NpcIdleMotion
    {
        public float NextActionAt { get; private set; }=float.PositiveInfinity;
        public float PauseEnd { get; private set; }
        public bool ResumeExistingPath { get; private set; }
        private Quaternion pauseHeading,lookHeading;
        private Vector2 lookAngles;
        private float lookSpeed,nextLookAt;
        private int remainingLooks,lookDirection;

        public static float SampleRange(Vector2 range)
        {
            float min=Mathf.Max(.05f,Mathf.Min(range.x,range.y));
            return Random.Range(min,Mathf.Max(min,Mathf.Max(range.x,range.y)));
        }
        public void Schedule(float now,Vector2 interval)=>NextActionAt=now+SampleRange(interval);
        public void Reset(Quaternion heading)
        {
            NextActionAt=float.PositiveInfinity;
            ResumeExistingPath=false;
            remainingLooks=0;PauseEnd=nextLookAt=0;
            pauseHeading=lookHeading=heading;
        }
        public void BeginPause(float now,float seconds,bool look,bool preservePath,Quaternion heading,
            Vector2 angles,Vector2 turnSpeed)
        {
            ResumeExistingPath=preservePath;
            PauseEnd=now+Mathf.Max(.05f,seconds);
            pauseHeading=lookHeading=heading;
            lookAngles=angles;lookSpeed=SampleRange(turnSpeed);
            remainingLooks=look?Random.Range(1,3):0;
            lookDirection=Random.value<.5f?-1:1;
            nextLookAt=now+Random.Range(.08f,.2f);
        }
        public bool PauseFinished(float now)=>now>=PauseEnd;
        public Quaternion StepLook(float now,float deltaTime,Quaternion current)
        {
            if(remainingLooks>0 && now>=nextLookAt)
            {
                lookHeading=pauseHeading*Quaternion.Euler(0,lookDirection*SampleRange(lookAngles),0);
                lookDirection=-lookDirection;remainingLooks--;
                nextLookAt=now+Random.Range(.6f,1f);
            }
            return Quaternion.RotateTowards(current,lookHeading,lookSpeed*Mathf.Max(0,deltaTime));
        }
        public static Quaternion FacePath(Quaternion current,Vector3 direction,float turnSpeed,
            float deltaTime,out bool facing)
        {
            direction.y=0;
            if(direction.sqrMagnitude<.01f){facing=true;return current;}
            var heading=Quaternion.LookRotation(direction);
            var next=Quaternion.RotateTowards(current,heading,Mathf.Max(60f,turnSpeed)*Mathf.Max(0,deltaTime));
            facing=Quaternion.Angle(next,heading)<=5f;
            return next;
        }
    }
}
