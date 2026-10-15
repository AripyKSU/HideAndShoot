var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var net=Unity.Netcode.NetworkManager.Singleton;
var resolver=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.Networking.HostDeathResolver>();
HideAndShoot.Combat.HostPlayerLife host=null;
foreach(var life in UnityEngine.Object.FindObjectsByType<HideAndShoot.Combat.HostPlayerLife>(UnityEngine.FindObjectsSortMode.None))if(life.IsOwner)host=life;
var shot=new HideAndShoot.Networking.HostShotHistory.ApprovedShot(101,host.NetworkObjectId,1,0,net.ServerTime.Tick,net.ServerTime.Tick);
typeof(HideAndShoot.Networking.HostDeathResolver).GetMethod("HandleApprovedShot",flags).Invoke(resolver,new object[]{shot});
if(!host.IsDead.Value || host.RevealedUntil.Value!=0)throw new System.Exception("Death did not clear reveal");
return new { dead=host.IsDead.Value, revealCleared=host.RevealedUntil.Value==0, remoteKills=resolver.GetKills(101), time=net.ServerTime.Time };