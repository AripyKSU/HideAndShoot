from pathlib import Path
root=Path('C:/UnityProject/HideAndShoot')
helper=root/'Assets/HideAndShoot/Scripts/NPC/NpcIdleMotion.cs'
helper.write_text('''using UnityEngine;

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
''',encoding='utf-8')
p=root/'Assets/HideAndShoot/Scripts/NPC/NpcWanderBrain.cs';s=p.read_text(encoding='utf-8')
a=s.index('        private float nextWalkingAction=');b=s.index('\n        private NavMeshAgent agent;',a)
s=s[:a]+'        private readonly NpcIdleMotion ambientMotion=new NpcIdleMotion();\n'+s[b:]
s=s.replace('        private float pauseEnd;\n','')
s=s.replace('Time.time>=nextWalkingAction','Time.time>=ambientMotion.NextActionAt')
s=s.replace('BeginPause(RandomRange(look?lookingSeconds:hesitationSeconds),look,true);','BeginPause(NpcIdleMotion.SampleRange(look?lookingSeconds:hesitationSeconds),look,true);')
a=s.index('        private static float RandomRange(');b=s.index('        private void ResetProgressWatchdog()',a)
s=s[:a]+'''        private void ScheduleWalkingAction()=>ambientMotion.Schedule(Time.time,walkingInterval);

'''+s[b:]
a=s.index('            nextWalkingAction=float.PositiveInfinity;');b=s.index('            if(agent!=null)agent.updateRotation=true;',a)
s=s[:a]+'            ambientMotion.Reset(transform.rotation);\n'+s[b:]
s=s.replace('            resumePathAfterPause=preservePath;\n','')
a=s.index('            pauseEnd=Time.time+Mathf.Max(');b=s.index('\n        private void UpdateTurnToPath(',a)
s=s[:a]+'''            ambientMotion.BeginPause(Time.time,seconds,look,preservePath,transform.rotation,lookAngleDegrees,lookTurnSpeed);
        }

        private void UpdatePause(float now,float deltaTime)
        {
            if(ambientMotion.PauseFinished(now))
            {
                state=NpcState.TurningToPath;
                // A mid-walk pause retains its route; arrival/retry pauses choose a new one.
                if(!ambientMotion.ResumeExistingPath || !agent.hasPath || agent.pathStatus!=NavMeshPathStatus.PathComplete)
                    ChooseDestination(true);
                return;
            }
            transform.rotation=ambientMotion.StepLook(now,deltaTime,transform.rotation);
        }
'''+s[b:]
a=s.index('            Vector3 direction=agent.steeringTarget-transform.position;');b=s.index('            if(!facing)return;',a)
s=s[:a]+'''            transform.rotation=NpcIdleMotion.FacePath(transform.rotation,agent.steeringTarget-transform.position,
                agent.angularSpeed,deltaTime,out bool facing);
'''+s[b:]
p.write_text(s,encoding='utf-8')
print('Extracted testable time/rotation model; NavMesh route ownership remains in NpcWanderBrain.')