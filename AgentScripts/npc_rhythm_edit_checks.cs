if(UnityEditor.EditorApplication.isPlaying)throw new System.Exception("EditMode only");
int passed=0;void Check(bool ok,string name){if(!ok)throw new System.Exception(name);passed++;}
var random=UnityEngine.Random.state;UnityEngine.GameObject root=null;
try
{
 UnityEngine.Random.InitState(7531);
 var motion=new HideAndShoot.NPC.NpcIdleMotion();
 var identity=UnityEngine.Quaternion.identity;
 Check(float.IsPositiveInfinity(motion.NextActionAt),"no action before movement release");
 motion.Schedule(10,new UnityEngine.Vector2(2.5f,7));
 Check(motion.NextActionAt>=12.5f && motion.NextActionAt<=17,"walking deadline range");
 motion.BeginPause(10,2,true,true,identity,new UnityEngine.Vector2(35,100),new UnityEngine.Vector2(85,145));
 Check(!motion.PauseFinished(11.99f) && motion.PauseFinished(12),"pause duration boundary");
 Check(motion.ResumeExistingPath,"mid-route resume policy");
 Check(UnityEngine.Quaternion.Angle(identity,motion.StepLook(10.01f,.01f,identity))<.01f,"look begins after hesitation");
 var turned=motion.StepLook(10.3f,.1f,identity);float yaw=UnityEngine.Quaternion.Angle(identity,turned);
 Check(yaw>=8.49f && yaw<=14.51f,"smooth rotation obeys speed cap");
 Check(UnityEngine.Quaternion.Angle(turned,motion.StepLook(10.4f,0,turned))<.01f,"zero delta does not rotate");
 motion.BeginPause(20,1,false,true,turned,new UnityEngine.Vector2(35,100),new UnityEngine.Vector2(85,145));
 Check(UnityEngine.Quaternion.Angle(turned,motion.StepLook(20.5f,.5f,turned))<.01f,"plain hesitation stays facing same way");
 motion.BeginPause(30,1,true,false,identity,new UnityEngine.Vector2(35,100),new UnityEngine.Vector2(85,145));
 Check(!motion.ResumeExistingPath,"arrival requests new destination");
 motion.Reset(turned);
 Check(float.IsPositiveInfinity(motion.NextActionAt) && !motion.ResumeExistingPath && motion.PauseEnd==0,"freeze/pool reset clears timers and route policy");
 Check(UnityEngine.Quaternion.Angle(turned,motion.StepLook(100,.1f,turned))<.01f,"no stale glance after reset");
 var facingBack=UnityEngine.Quaternion.Euler(0,180,0);
 var next=HideAndShoot.NPC.NpcIdleMotion.FacePath(facingBack,UnityEngine.Vector3.forward,180,.1f,out bool facing);
 Check(!facing && UnityEngine.Quaternion.Angle(facingBack,next)<=18.01f,"do not resume during large turn");
 for(int i=0;i<30 && !facing;i++)next=HideAndShoot.NPC.NpcIdleMotion.FacePath(next,UnityEngine.Vector3.forward,180,.1f,out facing);
 Check(facing && UnityEngine.Vector3.Angle(next*UnityEngine.Vector3.forward,UnityEngine.Vector3.forward)<=5.1f,"resume only facing route");
 next=HideAndShoot.NPC.NpcIdleMotion.FacePath(identity,new UnityEngine.Vector3(0,20,5),180,.1f,out facing);
 Check(facing && UnityEngine.Quaternion.Angle(next,identity)<.01f,"path facing does not pitch body");
 next=HideAndShoot.NPC.NpcIdleMotion.FacePath(turned,UnityEngine.Vector3.zero,180,.1f,out facing);
 Check(facing && UnityEngine.Quaternion.Angle(next,turned)<.01f,"zero-distance steering target");
 for(int i=0;i<30;i++)
 {
  float sample=HideAndShoot.NPC.NpcIdleMotion.SampleRange(new UnityEngine.Vector2(7,2.5f));
  if(sample<2.5f || sample>7)throw new System.Exception("Reversed inspector range");
 }
 Check(true,"reversed range remains valid");
 Check(HideAndShoot.NPC.NpcIdleMotion.SampleRange(new UnityEngine.Vector2(-3,-1))>=.05f,"invalid range cannot create zero-time loop");
 root=new UnityEngine.GameObject("NpcStateCheck");var brain=root.AddComponent<HideAndShoot.NPC.NpcWanderBrain>();
 var field=typeof(HideAndShoot.NPC.NpcWanderBrain).GetField("state",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
 field.SetValue(brain,System.Enum.Parse(field.FieldType,"TurningToPath"));Check(brain.IsAlive,"turning NPC remains hittable");
 field.SetValue(brain,System.Enum.Parse(field.FieldType,"Dying"));Check(!brain.IsAlive,"dying NPC remains dead");
 var report=new {mode="EditMode",passed,failed=0,smoothTurnDegrees=yaw};
 System.IO.Directory.CreateDirectory("AgentReports/NpcWanderRhythm");System.IO.File.WriteAllText("AgentReports/NpcWanderRhythm/EditMode.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
 return report;
}
finally{UnityEngine.Random.state=random;if(root!=null)UnityEngine.Object.DestroyImmediate(root);}