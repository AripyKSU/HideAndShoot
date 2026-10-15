var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var net=Unity.Netcode.NetworkManager.Singleton;var session=HideAndShoot.Networking.RelayLobbySession.Instance;
HideAndShoot.Combat.HostPlayerLife host=null,target=null;
var lives=UnityEngine.Object.FindObjectsByType<HideAndShoot.Combat.HostPlayerLife>(UnityEngine.FindObjectsSortMode.None);
foreach(var life in lives){if(life.IsOwner)host=life;else if(life.OwnerClientId==101)target=life;}
if(target.RevealSecondsRemaining!=0)throw new System.Exception("Ten-second penalty did not expire");
var camera=(UnityEngine.Camera)typeof(HideAndShoot.Player.LocalSpectatorRig).GetField("spectatorCamera",flags).GetValue(host.GetComponent<HideAndShoot.Player.LocalSpectatorRig>());
var parts=new System.Collections.Generic.List<HideAndShoot.Player.PlayerHighlightTarget.DrawPart>();
HideAndShoot.Player.PlayerHighlightTarget.Collect(camera,parts);
if(parts.Count==0)throw new System.Exception("Observer outline lost after penalty expiry");
var wall=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
wall.name="QA_NameOccluder";wall.transform.position=new UnityEngine.Vector3(0,1001.2f,3);wall.transform.localScale=new UnityEngine.Vector3(3,4,.5f);
UnityEngine.Physics.SyncTransforms();camera.Render();
var label=target.transform.Find("SpectatorNickname");
if(label==null || label.gameObject.activeSelf)throw new System.Exception("Runtime name leaked behind wall");
UnityEngine.Object.Destroy(wall);
var round=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.World.HostRoundSetupCoordinator>();
typeof(HideAndShoot.World.HostRoundSetupCoordinator).GetField("phase",flags).SetValue(round,HideAndShoot.World.HostRoundSetupCoordinator.HostPhase.Countdown);
typeof(HideAndShoot.World.HostRoundSetupCoordinator).GetField("roundId",flags).SetValue(round,2);
foreach(var life in lives)
{life.HostResetForRound(2);life.GetComponent<HideAndShoot.Combat.PistolCombatController>().HostSetRound(2,false);}
var resolver=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.Networking.HostDeathResolver>();resolver.NotifyNewRound(2);
session.PublishMatchState();
var ownerCamera=host.GetComponent<HideAndShoot.Player.PlayerLocalMotor>().PlayerCamera;
HideAndShoot.Player.PlayerHighlightTarget.Collect(ownerCamera,parts);
if(parts.Count!=0 || host.RevealedUntil.Value!=0 || target.RevealedUntil.Value!=0)throw new System.Exception("Round highlight reset failed");
if(resolver.GetKills(net.LocalClientId)!=1 || resolver.GetKills(101)!=1)throw new System.Exception("Round reset lost match kills");
var hud=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.UI.GameHUDPresenter>();hud.SendMessage("Update");
var ui=hud.transform.Find("GameHUD_Canvas");
if(ui.Find("NpcRevealWarning").gameObject.activeSelf || ui.Find("KillFeed/Entry0").gameObject.activeSelf)throw new System.Exception("Old round UI survived reset");
var report=new { playSessions=1, penaltyExpired=true, spectatorSurvivesPenaltyExpiry=true, nameOccludedByWall=true, roundResetClearsReveal=true, roundResetHidesOldFeed=true, matchKillsRetained=true, scope="Local NGO host with actual prefabs and NPCs; synthetic remote owners and host-approved hit inputs; no Relay client test" };
System.IO.File.WriteAllText("AgentReports/CombatPresentation/Play_Final.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
return report;