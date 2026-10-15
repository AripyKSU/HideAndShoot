from pathlib import Path
import shutil
root=Path('C:/UnityProject/HideAndShoot')
p=root/'Assets/HideAndShoot/Scripts/NPC/NpcWanderBrain.cs'
s=p.read_text(encoding='utf-8-sig')
b=root/'AgentBackups/20261015-NpcWanderRhythm/Assets/HideAndShoot/Scripts/NPC/NpcWanderBrain.cs'
if not b.exists(): b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,b)
def sub(a,b):
 global s
 assert a in s, a[:100]
 s=s.replace(a,b,1)
sub('private enum NpcState { Pooled, Walking, Pausing, Dying }','''private enum NpcState { Pooled, Walking, Pausing, TurningToPath, Dying }

        [Header("Ambient movement (host only)")]
        [SerializeField] private Vector2 walkingInterval=new Vector2(2.5f,7f);
        [SerializeField] private Vector2 hesitationSeconds=new Vector2(.35f,1.15f);
        [SerializeField] private Vector2 lookingSeconds=new Vector2(.9f,2.2f);
        [SerializeField,Range(0f,1f)] private float lookAroundChance=.65f;
        [SerializeField] private Vector2 lookAngleDegrees=new Vector2(35f,100f);
        [SerializeField] private Vector2 lookTurnSpeed=new Vector2(85f,145f);

        private float nextWalkingAction=float.PositiveInfinity;
        private bool resumePathAfterPause;
        private Quaternion pauseHeading,lookHeading;
        private float lookSpeed,nextLookAt;
        private int remainingLooks,lookDirection;''')
sub('        private float glanceRate;\n','')
sub('public bool IsAlive => state==NpcState.Walking || state==NpcState.Pausing;','public bool IsAlive => state==NpcState.Walking || state==NpcState.Pausing || state==NpcState.TurningToPath;')
sub('''            state=NpcState.Pooled;
            movementReleased=false;''','''            state=NpcState.Pooled;
            movementReleased=false;
            ClearAmbientMotion();''')
sub('''            agent.speed=2.2f;
            movementReleased=false;''','''            agent.speed=2.2f;
            ClearAmbientMotion();
            movementReleased=false;''')
sub('''            movementReleased=true;
            agent.isStopped=false;''','''            movementReleased=true;
            ClearAmbientMotion();
            ScheduleWalkingAction();
            agent.isStopped=false;''')
sub('''            movementReleased=false;
            if(agent!=null && agent.enabled)''','''            movementReleased=false;
            ClearAmbientMotion();
            if(agent!=null && agent.enabled)''')
sub('''            if(Time.time>=nextZoneCheck)''','''            if(agent==null || !agent.enabled || !agent.isOnNavMesh)return;
            if(Time.time>=nextZoneCheck)''')
a=s.index('            if(state==NpcState.Pausing)\n');z=s.index('\n            if(!agent.pathPending',a)
s=s[:a]+'''            if(state==NpcState.Pausing)
            {
                UpdatePause(Time.time,Time.deltaTime);
                return;
            }
            if(state==NpcState.TurningToPath)
            {
                UpdateTurnToPath(Time.deltaTime);
                return;
            }
'''+s[z:]
sub('''                OnDestinationReached();
            }
            else if(Time.time-lastProgressCheck>=3f)''','''                OnDestinationReached();
            }
            else if(!agent.pathPending && Time.time>=nextWalkingAction &&
                agent.hasPath && !agent.isOnOffMeshLink)
            {
                bool look=UnityEngine.Random.value<lookAroundChance;
                BeginPause(RandomRange(look?lookingSeconds:hesitationSeconds),look,true);
            }
            else if(Time.time-lastProgressCheck>=3f)''')
a=s.index('            if(UnityEngine.Random.value<.35f)\n');z=s.index('        private void ChooseDestination()',a)
s=s[:a]+'''            if(UnityEngine.Random.value<.35f)
                BeginPause(UnityEngine.Random.Range(.5f,2f),UnityEngine.Random.value<lookAroundChance,false);
            else ChooseDestination();
        }

        private static float RandomRange(Vector2 range)
        {
            float min=Mathf.Max(.05f,Mathf.Min(range.x,range.y));
            return UnityEngine.Random.Range(min,Mathf.Max(min,Mathf.Max(range.x,range.y)));
        }

        private void ScheduleWalkingAction()=>nextWalkingAction=Time.time+RandomRange(walkingInterval);

        private void ResetProgressWatchdog()
        {
            lastProgressCheck=Time.time;
            lastProgressPosition=transform.position;
        }

        private void ClearAmbientMotion()
        {
            nextWalkingAction=float.PositiveInfinity;
            resumePathAfterPause=false;
            remainingLooks=0;
            pauseEnd=nextLookAt=0;
            pauseHeading=lookHeading=transform.rotation;
            if(agent!=null)agent.updateRotation=true;
        }

        private void BeginPause(float seconds,bool look,bool preservePath)
        {
            state=NpcState.Pausing;
            resumePathAfterPause=preservePath;
            agent.isStopped=true;
            agent.velocity=Vector3.zero;
            agent.updateRotation=false;
            if(!preservePath)agent.ResetPath();
            pauseEnd=Time.time+Mathf.Max(.05f,seconds);
            pauseHeading=lookHeading=transform.rotation;
            lookSpeed=RandomRange(lookTurnSpeed);
            remainingLooks=look?UnityEngine.Random.Range(1,3):0;
            lookDirection=UnityEngine.Random.value<.5f?-1:1;
            nextLookAt=Time.time+UnityEngine.Random.Range(.08f,.2f);
        }

        private void UpdatePause(float now,float deltaTime)
        {
            if(now>=pauseEnd)
            {
                state=NpcState.TurningToPath;
                // A mid-walk pause retains its route; arrival/retry pauses choose a new one.
                if(!resumePathAfterPause || !agent.hasPath || agent.pathStatus!=NavMeshPathStatus.PathComplete)
                    ChooseDestination(true);
                return;
            }
            if(remainingLooks>0 && now>=nextLookAt)
            {
                lookHeading=pauseHeading*Quaternion.Euler(0,lookDirection*RandomRange(lookAngleDegrees),0);
                lookDirection=-lookDirection;
                remainingLooks--;
                nextLookAt=now+UnityEngine.Random.Range(.6f,1f);
            }
            transform.rotation=Quaternion.RotateTowards(transform.rotation,lookHeading,lookSpeed*deltaTime);
        }

        private void UpdateTurnToPath(float deltaTime)
        {
            if(agent.pathPending)return;
            if(!agent.hasPath || agent.pathStatus!=NavMeshPathStatus.PathComplete)
            {ChooseDestination(true);return;}
            Vector3 direction=agent.steeringTarget-transform.position;direction.y=0;
            bool facing=direction.sqrMagnitude<.01f;
            if(!facing)
            {
                var heading=Quaternion.LookRotation(direction);
                transform.rotation=Quaternion.RotateTowards(transform.rotation,heading,
                    Mathf.Max(60f,agent.angularSpeed)*deltaTime);
                facing=Quaternion.Angle(transform.rotation,heading)<=5f;
            }
            if(!facing)return;
            state=NpcState.Walking;
            agent.updateRotation=true;
            agent.isStopped=false;
            ScheduleWalkingAction();
            ResetProgressWatchdog();
        }

'''+s[z:]
sub('        private void ChooseDestination()','        private void ChooseDestination(bool keepStopped=false)')
sub('''                state=NpcState.Pausing;
                agent.isStopped=true;
                pauseEnd=Time.time+.8f;
                return;''','''                BeginPause(.8f,false,false);
                return;''')
sub('''            agent.isStopped=false;
            agent.SetDestination(dest);''','''            if(!agent.SetDestination(dest)){BeginPause(.8f,false,false);return;}
            agent.isStopped=keepStopped;
            agent.updateRotation=!keepStopped;
            state=keepStopped?NpcState.TurningToPath:NpcState.Walking;
            ResetProgressWatchdog();''')
sub('''            state=NpcState.Dying;
            ReplicatedDead.Value=true;''','''            state=NpcState.Dying;
            ClearAmbientMotion();
            ReplicatedDead.Value=true;''')
# ReturnToPool's second Pooled assignment is deliberately separate from ResetForRound.
sub('''            state=NpcState.Pooled;
            movementReleased=false;
            recentZones.Clear();''','''            state=NpcState.Pooled;
            movementReleased=false;
            ClearAmbientMotion();
            recentZones.Clear();''')
sub('''            // Called by NGO's client-side instance handler, after NetworkObject is gone.
            // No ResetForRound call here: only Host assigns gameplay generations.''','''            // Called by NGO's client-side instance handler, after NetworkObject is gone.
            // No ResetForRound call here: only Host assigns gameplay generations.
            ClearAmbientMotion();''')
p.write_text(s,encoding='utf-8')
print('NpcWanderBrain updated: in-route hesitation/look pauses, smooth yaw, face-route-before-resume, route retention, lifecycle reset.')