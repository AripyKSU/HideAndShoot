var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var net=Unity.Netcode.NetworkManager.Singleton;var session=HideAndShoot.Networking.RelayLobbySession.Instance;
var resolver=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.Networking.HostDeathResolver>();
var lives=UnityEngine.Object.FindObjectsByType<HideAndShoot.Combat.HostPlayerLife>(UnityEngine.FindObjectsSortMode.None);
HideAndShoot.Combat.HostPlayerLife host=null,target=null,victim=null;
foreach(var life in lives){if(life.IsOwner)host=life;else if(life.OwnerClientId==101)target=life;else if(life.OwnerClientId==102)victim=life;}
var npcs=UnityEngine.Object.FindObjectsByType<HideAndShoot.NPC.NpcWanderBrain>(UnityEngine.FindObjectsSortMode.None);
void Hit(ulong shooter,Unity.Netcode.NetworkObject actor,int generation)
{
 var shot=new HideAndShoot.Networking.HostShotHistory.ApprovedShot(shooter,actor.NetworkObjectId,1,generation,net.ServerTime.Tick,net.ServerTime.Tick);
 typeof(HideAndShoot.Networking.HostDeathResolver).GetMethod("HandleApprovedShot",flags).Invoke(resolver,new object[]{shot});
}
int frameDelta=UnityEngine.Time.frameCount-UnityEditor.SessionState.GetInt("HS.CombatCheck.StartFrame",0);
if(frameDelta<2)throw new System.Exception("Game is not advancing");
Hit(net.LocalClientId,npcs[0].NetworkObject,npcs[0].Generation);
if(host.RevealSecondsRemaining<9 || npcs[0].IsAlive)throw new System.Exception("Host NPC penalty failed");
double first=host.RevealedUntil.Value;
Hit(net.LocalClientId,npcs[1].NetworkObject,npcs[1].Generation);
if(host.RevealedUntil.Value<first || host.RevealSecondsRemaining>10.001)throw new System.Exception("Penalty did not refresh to ten seconds");
Hit(101,npcs[2].NetworkObject,npcs[2].Generation);
if(target.RevealSecondsRemaining<9)throw new System.Exception("Remote actor penalty failed");
Hit(net.LocalClientId,victim.NetworkObject,0);
if(!victim.IsDead.Value || resolver.GetKills(net.LocalClientId)!=1 || session.KillFeed.Count!=1)throw new System.Exception("Kill/feed failed");
Hit(net.LocalClientId,victim.NetworkObject,0);
if(resolver.GetKills(net.LocalClientId)!=1 || session.KillFeed.Count!=1)throw new System.Exception("Duplicate death counted");
session.PublishMatchState();
if(session.Standings.Count!=1 || session.Standings[0].Kills!=1)throw new System.Exception("Kills snapshot failed");
var parts=new System.Collections.Generic.List<HideAndShoot.Player.PlayerHighlightTarget.DrawPart>();
var camera=host.GetComponent<HideAndShoot.Player.PlayerLocalMotor>().PlayerCamera;
HideAndShoot.Player.PlayerHighlightTarget.Collect(camera,parts);
if(parts.Count==0 || HideAndShoot.Player.PlayerHighlightTarget.IsSpectator(camera))throw new System.Exception("Alive penalty visibility failed");
var hud=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.UI.GameHUDPresenter>();hud.SendMessage("Update");
var ui=hud.transform.Find("GameHUD_Canvas");
if(!ui.Find("NpcRevealWarning").gameObject.activeSelf)throw new System.Exception("Warning not visible");
if(ui.Find("RankedScore/Row0/Kills").GetComponent<TMPro.TextMeshProUGUI>().text!="1")throw new System.Exception("HUD kills not updated");
if(!ui.Find("KillFeed/Entry0").gameObject.activeSelf)throw new System.Exception("Kill feed not visible");
UnityEditor.SessionState.SetFloat("HS.CombatCheck.RevealDeadline",(float)target.RevealedUntil.Value);
var report=new { frameDelta, hostPenaltySeconds=host.RevealSecondsRemaining, targetPenaltySeconds=target.RevealSecondsRemaining, kills=resolver.GetKills(net.LocalClientId), feed=session.KillFeed[0].Killer+" -> "+session.KillFeed[0].Victim, duplicateSuppressed=true, highlightedParts=parts.Count, warning=true, scoreboard=true };
System.IO.File.WriteAllText("AgentReports/CombatPresentation/Play_Hits.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
return report;