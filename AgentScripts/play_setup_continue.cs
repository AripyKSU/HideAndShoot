var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance; var session=HideAndShoot.Networking.RelayLobbySession.Instance; var net=Unity.Netcode.NetworkManager.Singleton;
var round=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.World.HostRoundSetupCoordinator>();round.enabled=false;round.StopAllCoroutines();
var bootstrap=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.World.MatchAvatarBootstrap>();bootstrap.enabled=false;bootstrap.StopAllCoroutines();
var roundType=typeof(HideAndShoot.World.HostRoundSetupCoordinator);
roundType.GetField("network",flags).SetValue(round,net);
roundType.GetField("phase",flags).SetValue(round,HideAndShoot.World.HostRoundSetupCoordinator.HostPhase.Active);
roundType.GetField("roundId",flags).SetValue(round,1);
roundType.GetField("participantsAtStart",flags).SetValue(round,3);
var participants=(System.Collections.Generic.HashSet<ulong>)roundType.GetField("roundParticipants",flags).GetValue(round);
participants.Add(net.LocalClientId);participants.Add(101);participants.Add(102);
var names=(System.Collections.Generic.Dictionary<ulong,string>)typeof(HideAndShoot.Networking.RelayLobbySession).GetField("names",flags).GetValue(session);
names[101]="관전_대상";names[102]="검증_Victim";
var floor=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);floor.name="QA_LocalFloor";
floor.transform.position=new UnityEngine.Vector3(0,999.5f,3);floor.transform.localScale=new UnityEngine.Vector3(20,1,20);
var catalog=UnityEngine.Resources.Load<HideAndShoot.World.NetworkPrefabCatalog>("NetworkPrefabCatalog");
var history=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.Networking.HostShotHistory>();
var resolver=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.Networking.HostDeathResolver>();resolver.NotifyNewRound(1);
for(int i=0;i<3;i++)
{
 var player=UnityEngine.Object.Instantiate(catalog.PlayerAvatar,new UnityEngine.Vector3(i==2?2:0,1000,i==0?0:6),UnityEngine.Quaternion.identity);
 player.name=i==0?"QA_Host":i==1?"QA_Target":"QA_Victim";
 var actor=player.GetComponent<Unity.Netcode.NetworkObject>();
 if(i==0)actor.SpawnAsPlayerObject(net.LocalClientId,true);else actor.SpawnWithOwnership((ulong)(100+i),true);
 player.GetComponent<HideAndShoot.Combat.HostPlayerLife>().HostResetForRound(1);
 player.GetComponent<HideAndShoot.Combat.PistolCombatController>().HostSetRound(1,true);
 history.RegisterActor(actor,1,0);
}
var pool=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.NPC.NpcPoolCoordinator>();
if(!pool.BeginRound(1,2))throw new System.Exception("NPC setup failed");
session.PublishMatchState();
UnityEditor.SessionState.SetInt("HS.CombatCheck.StartFrame",UnityEngine.Time.frameCount);
return new { host=net.IsHost, syntheticRemoteActors=2, round=round.RoundId, phase=round.Phase.ToString(), frame=UnityEngine.Time.frameCount, scope="One local host session; inject host-approved hits; no external client" };