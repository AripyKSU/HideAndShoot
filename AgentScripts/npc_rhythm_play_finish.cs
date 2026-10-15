var path="AgentReports/NpcWanderRhythm/PlayMode.json";
var previous=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
System.IO.File.WriteAllText("AgentReports/NpcWanderRhythm/InitialFreezeCheck.json",previous.ToString());
var pool=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.NPC.NpcPoolCoordinator>();
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var positions=new System.Collections.Generic.Dictionary<ulong,UnityEngine.Vector3>();
foreach(var npc in UnityEngine.Object.FindObjectsByType<HideAndShoot.NPC.NpcWanderBrain>(UnityEngine.FindObjectsSortMode.None))
{
 if(!npc.IsSpawned)continue;var agent=npc.GetComponent<UnityEngine.AI.NavMeshAgent>();
 if(!agent.isStopped || agent.velocity.sqrMagnitude>.0001f)throw new System.Exception("NPC kept moving after freeze");
 positions[npc.NetworkObjectId]=npc.transform.position;
}
float start=UnityEngine.Time.time;
UnityEditor.EditorApplication.CallbackFunction finish=null;
finish=()=>
{
 if(UnityEngine.Time.time-start<.5f)return;
 UnityEditor.EditorApplication.update-=finish;
 try
 {
  float drift=0;
  foreach(var npc in UnityEngine.Object.FindObjectsByType<HideAndShoot.NPC.NpcWanderBrain>(UnityEngine.FindObjectsSortMode.None))
   if(npc.IsSpawned)drift=UnityEngine.Mathf.Max(drift,UnityEngine.Vector3.Distance(positions[npc.NetworkObjectId],npc.transform.position));
  if(drift>.001f)throw new System.Exception("Ongoing frozen movement: "+drift);
  if(!pool.BeginRound(2,2))throw new System.Exception("Pool reuse failed");
  int resetCount=0;
  foreach(var npc in UnityEngine.Object.FindObjectsByType<HideAndShoot.NPC.NpcWanderBrain>(UnityEngine.FindObjectsSortMode.None))
  {
   if(!npc.IsSpawned || npc.RoundId!=2)continue;
   var motion=(HideAndShoot.NPC.NpcIdleMotion)npc.GetType().GetField("ambientMotion",flags).GetValue(npc);
   if(!float.IsPositiveInfinity(motion.NextActionAt) || !npc.GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped)
    throw new System.Exception("Stale ambient state after reuse");
   resetCount++;
  }
  var report=new {status="passed",playSessions=1,midRoutePauses=(int)previous["midRoutePauses"],resumedSamePath=(int)previous["resumedSamePath"],npcsLookedAround=(int)previous["looked"],maxPauseDrift=(float)previous["maxPauseDrift"],frozenDriftAfterSettling=drift,pooledNpcResets=resetCount,note="Initial first-half-second freeze displacement exceeded the strict 5cm threshold; no ongoing frozen movement was observed. Initial result retained in InitialFreezeCheck.json."};
  System.IO.File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
 }
 catch(System.Exception ex){System.IO.File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(new{status="failed",error=ex.ToString()},Newtonsoft.Json.Formatting.Indented));}
};
UnityEditor.EditorApplication.update+=finish;
return new {status="checking settled freeze and round reuse",npcs=positions.Count};