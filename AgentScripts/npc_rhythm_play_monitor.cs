if(!UnityEditor.EditorApplication.isPlaying)throw new System.Exception("PlayMode required");
UnityEngine.Application.runInBackground=true;
var session=HideAndShoot.Networking.RelayLobbySession.Instance;var net=Unity.Netcode.NetworkManager.Singleton;
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
typeof(HideAndShoot.Networking.RelayLobbySession).GetField("localName",flags).SetValue(session,"NPC_Check");
net.NetworkConfig.ConnectionApproval=false;net.NetworkConfig.EnableSceneManagement=false;
net.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData("127.0.0.1",(ushort)7790,"127.0.0.1");
if(!net.StartHost())throw new System.Exception("Local host failed");
var round=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.World.HostRoundSetupCoordinator>();round.enabled=false;round.StopAllCoroutines();
var bootstrap=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.World.MatchAvatarBootstrap>();bootstrap.enabled=false;bootstrap.StopAllCoroutines();
var pool=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.NPC.NpcPoolCoordinator>();
if(!pool.BeginRound(1,2))throw new System.Exception("NPC pool failed");pool.ReleaseCountdownMovement(1);
var type=typeof(HideAndShoot.NPC.NpcWanderBrain);var stateField=type.GetField("state",flags);var motionField=type.GetField("ambientMotion",flags);
var states=new System.Collections.Generic.Dictionary<ulong,string>();
var destinations=new System.Collections.Generic.Dictionary<ulong,UnityEngine.Vector3>();
var pausePositions=new System.Collections.Generic.Dictionary<ulong,UnityEngine.Vector3>();
var pauseRotations=new System.Collections.Generic.Dictionary<ulong,UnityEngine.Quaternion>();
var seenMoving=new System.Collections.Generic.HashSet<ulong>();var seenLooking=new System.Collections.Generic.HashSet<ulong>();
var frozenPositions=new System.Collections.Generic.Dictionary<ulong,UnityEngine.Vector3>();
int midRoutePauses=0,resumedSamePath=0,facingTransitions=0,stage=0;float maxPauseDrift=0,freezeAt=0;
float started=UnityEngine.Time.time;int startFrame=UnityEngine.Time.frameCount;
string reportPath="AgentReports/NpcWanderRhythm/PlayMode.json";System.IO.Directory.CreateDirectory("AgentReports/NpcWanderRhythm");
System.IO.File.WriteAllText(reportPath,"{\"status\":\"running\"}");
UnityEditor.EditorApplication.CallbackFunction monitor=null;
monitor=()=>
{
 if(!UnityEditor.EditorApplication.isPlaying){UnityEditor.EditorApplication.update-=monitor;return;}
 try
 {
  var npcs=UnityEngine.Object.FindObjectsByType<HideAndShoot.NPC.NpcWanderBrain>(UnityEngine.FindObjectsSortMode.None);
  if(stage==0)
  {
   foreach(var npc in npcs)
   {
    if(!npc.IsSpawned || !npc.IsAlive)continue;
    ulong id=npc.NetworkObjectId;string state=stateField.GetValue(npc).ToString();
    var agent=npc.GetComponent<UnityEngine.AI.NavMeshAgent>();var motion=(HideAndShoot.NPC.NpcIdleMotion)motionField.GetValue(npc);
    string before=states.TryGetValue(id,out var old)?old:"";
    if(agent.velocity.sqrMagnitude>.1f)seenMoving.Add(id);
    if(state=="Pausing" && before!="Pausing" && motion.ResumeExistingPath)
    {
     midRoutePauses++;destinations[id]=agent.destination;pausePositions[id]=npc.transform.position;pauseRotations[id]=npc.transform.rotation;
     if(!agent.hasPath || !agent.isStopped)throw new System.Exception("Mid-route stop lost path or did not stop");
    }
    if(state=="Pausing" && destinations.ContainsKey(id))
    {
     maxPauseDrift=UnityEngine.Mathf.Max(maxPauseDrift,UnityEngine.Vector3.Distance(pausePositions[id],npc.transform.position));
     if(UnityEngine.Quaternion.Angle(pauseRotations[id],npc.transform.rotation)>10)seenLooking.Add(id);
    }
    if(state=="TurningToPath" && before!="TurningToPath")
    {facingTransitions++;if(!agent.isStopped || !npc.IsAlive)throw new System.Exception("Facing phase is not stationary/alive");}
    if(state=="Walking" && before=="TurningToPath" && destinations.TryGetValue(id,out var dest))
    {
     if(UnityEngine.Vector3.Distance(dest,agent.destination)>.05f)throw new System.Exception("Mid-route stop changed destination");
     resumedSamePath++;destinations.Remove(id);
    }
    states[id]=state;
   }
   if(UnityEngine.Time.time-started<18f)return;
   if(midRoutePauses==0 || resumedSamePath==0 || seenLooking.Count==0 || seenMoving.Count<2)throw new System.Exception("Ambient behaviors were not observed");
   if(maxPauseDrift>.15f)throw new System.Exception("NPC slid while pausing: "+maxPauseDrift);
   pool.FreezeMovement();foreach(var npc in npcs)if(npc.IsSpawned)frozenPositions[npc.NetworkObjectId]=npc.transform.position;
   freezeAt=UnityEngine.Time.time;stage=1;return;
  }
  if(UnityEngine.Time.time-freezeAt<.5f)return;
  foreach(var npc in npcs)
  {
   if(!npc.IsSpawned)continue;
   if(!npc.GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped || UnityEngine.Vector3.Distance(npc.transform.position,frozenPositions[npc.NetworkObjectId])>.05f)
    throw new System.Exception("Round freeze did not hold NPC still");
  }
  if(!pool.BeginRound(2,2))throw new System.Exception("Pooled round reset failed");
  int resetCount=0;
  foreach(var npc in UnityEngine.Object.FindObjectsByType<HideAndShoot.NPC.NpcWanderBrain>(UnityEngine.FindObjectsSortMode.None))
  {
   if(!npc.IsSpawned || npc.RoundId!=2)continue;
   var motion=(HideAndShoot.NPC.NpcIdleMotion)motionField.GetValue(npc);
   if(!float.IsPositiveInfinity(motion.NextActionAt) || !npc.GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped)throw new System.Exception("Stale timer/motion after pool reuse");
   resetCount++;
  }
  var report=new {status="passed",playSessions=1,seconds=UnityEngine.Time.time-started,frameDelta=UnityEngine.Time.frameCount-startFrame,npcsMoved=seenMoving.Count,midRoutePauses,resumedSamePath,npcsLookedAround=seenLooking.Count,facingTransitions,maxPauseDrift,freezeVerified=true,pooledNpcResets=resetCount,scope="Actual NPC pool/NavMesh on local host; no external client"};
  System.IO.File.WriteAllText(reportPath,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
  UnityEditor.EditorApplication.update-=monitor;
 }
 catch(System.Exception ex)
 {
  System.IO.File.WriteAllText(reportPath,Newtonsoft.Json.JsonConvert.SerializeObject(new{status="failed",error=ex.ToString(),midRoutePauses,resumedSamePath,looked=seenLooking.Count,maxPauseDrift},Newtonsoft.Json.Formatting.Indented));
  UnityEditor.EditorApplication.update-=monitor;
 }
};
UnityEditor.EditorApplication.update+=monitor;
return new {status="monitoring",seconds=18,actualNpcPool=pool.SpawnedCount,playSessions=1};