using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using HideAndShoot.World;
using HideAndShoot.Networking;
using HideAndShoot.NPC;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

public static class PlayStageMaps
{
 const string Output="AgentReports/StageMaps_2026-10-16/";
 const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
 public static object StartHost()
 {
  if(!UnityEditor.EditorApplication.isPlaying)throw new Exception("Play mode required");
  var net=NetworkManager.Singleton;var session=RelayLobbySession.Instance;
  if(net==null || net.IsListening)throw new Exception("Fresh offline menu session required");
  Application.runInBackground=true;
  typeof(RelayLobbySession).GetField("localName",Private).SetValue(session,"Map QA Host");
  net.NetworkConfig.ConnectionApproval=false;net.NetworkConfig.EnableSceneManagement=false;
  net.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData("127.0.0.1",(ushort)7793,"127.0.0.1");
  if(!net.StartHost())throw new Exception("Local host failed");
  typeof(RelayLobbySession).GetMethod("RegisterRosterHandler",Private).Invoke(session,null);
  UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
  var round=Object.FindFirstObjectByType<HostRoundSetupCoordinator>();if(round!=null){round.enabled=false;round.StopAllCoroutines();}
  var bootstrap=Object.FindFirstObjectByType<MatchAvatarBootstrap>();if(bootstrap!=null)bootstrap.enabled=false;
  return new {host=net.IsHost,scope="Local NGO host; no external clients or Relay room"};
 }
 static object WalkRamps(StageMapDirector maps)
 {
  var failures=new List<string>();int tested=0;
  var ramps=maps.Definitions[maps.SelectedMapIndex].World.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.Contains("Ramp") || c.name.Contains("Crater_") || c.name.Contains("Landing_")).ToArray();
  foreach(var ramp in ramps)
  {
   var v=ramp.sharedMesh.vertices;var start=ramp.transform.TransformPoint((v[0]+v[1])/2);var end=ramp.transform.TransformPoint((v[2]+v[3])/2);
   foreach(bool reverse in new[]{false,true})
   {
    var a=reverse?end:start;var b=reverse?start:end;var flat=b-a;flat.y=0;flat.Normalize();
    var go=new GameObject("QA_RampWalker");go.transform.position=a-flat*.7f+Vector3.up*.08f;
    var controller=go.AddComponent<CharacterController>();controller.height=2;controller.center=Vector3.up;controller.radius=.33f;controller.slopeLimit=45;controller.stepOffset=.3f;controller.skinWidth=.03f;
    Physics.SyncTransforms();float travel=Vector3.Distance(new Vector3(a.x,0,a.z),new Vector3(b.x,0,b.z))+1.4f;
    int steps=Mathf.CeilToInt(travel/.06f);
    for(int i=0;i<steps;i++)controller.Move(flat*.06f+Vector3.down*.04f);
    var wanted=b+flat*.7f;var delta=go.transform.position-wanted;
    if(new Vector2(delta.x,delta.z).magnitude>.7f || Mathf.Abs(delta.y)>.65f)failures.Add(ramp.name+(reverse?" down":" up")+" stopped "+go.transform.position+" expected "+wanted);
    tested++;Object.DestroyImmediate(go);
   }
  }
  return new {tested,failures};
 }
 public static async Task<object> Run()
 {
  if(!UnityEditor.EditorApplication.isPlaying)throw new Exception("Play mode required");
  var runtimeErrors=new List<string>();Application.LogCallback log=(s,t,k)=>{if(k==LogType.Error || k==LogType.Exception)runtimeErrors.Add(s);};Application.logMessageReceived+=log;
  var maps=Object.FindFirstObjectByType<StageMapDirector>();var registry=Object.FindFirstObjectByType<SpawnPointRegistry>();var pool=Object.FindFirstObjectByType<NpcPoolCoordinator>();var ammo=Object.FindFirstObjectByType<AmmoSpawnDirector>();var vision=Object.FindFirstObjectByType<VisionSpawnDirector>();
  var coordinator=Object.FindFirstObjectByType<HostRoundSetupCoordinator>();coordinator.enabled=false;coordinator.StopAllCoroutines();
  Object.FindFirstObjectByType<MatchAvatarBootstrap>().enabled=false;
  var rows=new List<object>();var seen=new HashSet<int>();int round=maps.SelectionRound;bool immutable=true,rampsClear=true;
  try
  {
   for(int desired=0;desired<3;desired++)foreach(int participants in new[]{2,8})
   {
    pool.ReleaseWholeRound();ammo.EndRound();vision.EndRound();
    for(int attempt=0;attempt<100;attempt++){if(!maps.HostChooseForStage(++round))throw new Exception("Host draw failed");if(maps.SelectedMapIndex==desired)break;}
    if(maps.SelectedMapIndex!=desired)throw new Exception("Random sampling never selected map "+desired);
    seen.Add(maps.SelectedMapIndex);int chosen=maps.SelectedMapIndex;
    immutable &= !maps.HostChooseForStage(round) && maps.SelectedMapIndex==chosen;
    if(!registry.TryDrawPlayerSpawns(participants,round,out var positions,out _))throw new Exception("Spawn draw failed");
    var walk=participants==2?WalkRamps(maps):null;
    if(walk!=null && ((List<string>)walk.GetType().GetProperty("failures").GetValue(walk)).Count>0)rampsClear=false;
    if(!pool.BeginRound(round,participants))throw new Exception("NPC pool failed for map "+desired);
    int target=NpcPoolCoordinator.TargetNpcCount(participants);var npcs=Object.FindObjectsByType<NpcWanderBrain>(FindObjectsSortMode.None).Where(n=>n.IsSpawned).ToArray();var before=npcs.Select(n=>n.transform.position).ToArray();
    pool.ReleaseCountdownMovement(round);await Task.Delay(2200);
    int moved=npcs.Where((n,i)=>n!=null && Vector3.Distance(before[i],n.transform.position)>.08f).Count();
    int offMesh=npcs.Count(n=>n==null || !n.GetComponent<NavMeshAgent>().isOnNavMesh);
    rows.Add(new {map=desired,participants,npcs=pool.SpawnedCount,target,moved,offMesh,spawnPositions=positions.Select(p=>new[]{p.x,p.y,p.z}).ToArray(),walk});
    if(pool.SpawnedCount!=target || moved==0 || offMesh>0)throw new Exception("NPC runtime movement failed");
   }
   if(!ammo.BeginRound(round,8) || !vision.BeginRound(round))throw new Exception("Pickup startup failed");
   bool initiallyEmpty=ammo.ActiveCount==0 && vision.ActiveCount==0;
   await Task.Delay(11500);
   var actors=Object.FindObjectsByType<AmmoPickupActor>(FindObjectsSortMode.None).Where(a=>a.IsSpawned).Select(a=>a.transform.position).Concat(Object.FindObjectsByType<VisionPickupActor>(FindObjectsSortMode.None).Where(a=>a.IsSpawned).Select(a=>a.transform.position)).ToArray();
   int embedded=actors.Count(p=>!NavMesh.SamplePosition(p,out var h,1f,NavMesh.AllAreas) || p.y-h.position.y<.15f || p.y-h.position.y>1f);
   var report=new {passed=rampsClear && immutable && initiallyEmpty && ammo.ActiveCount>0 && vision.ActiveCount==1 && embedded==0 && runtimeErrors.Count==0,maps=rows,seen=seen.OrderBy(i=>i).ToArray(),sameStageCannotReroll=immutable,rampsClear,initiallyEmpty,ammo=ammo.ActiveCount,vision=vision.ActiveCount,embeddedPickups=embedded,runtimeErrors,scope="Local NGO host; 2/8 participant population settings, real CharacterController ramp walks and timed pickups. External client handshake is not exercised."};
   Directory.CreateDirectory(Output);File.WriteAllText(Output+"PlayAudit.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
  }
  finally{Application.logMessageReceived-=log;ammo.EndRound();vision.EndRound();pool.ReleaseWholeRound();}
 }
}
