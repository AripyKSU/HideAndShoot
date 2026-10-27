using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using HideAndShoot.World;
using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.Player;

public static class DetectionItemReview
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    const string Output="AgentReports/DetectionItem_2026-10-16";
    public static object StartLocalHost()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Play mode required");
        var session=RelayLobbySession.Instance;
        var net=NetworkManager.Singleton;
        if(net==null || net.IsListening)throw new Exception("Fresh offline session required");
        Application.runInBackground=true;
        typeof(RelayLobbySession).GetField("localName",Private).SetValue(session,"탄약 확인");
        net.NetworkConfig.ConnectionApproval=false;
        net.NetworkConfig.EnableSceneManagement=false;
        net.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData("127.0.0.1",(ushort)7789,"127.0.0.1");
        if(!net.StartHost())throw new Exception("Local host failed");
        typeof(RelayLobbySession).GetMethod("RegisterRosterHandler",Private).Invoke(session,null);
        SceneManager.LoadScene("GameScene");
        return new {localHost=true,externalConnection=false};
    }
    public static object Prepare()
    {
        var net=NetworkManager.Singleton;
        var session=RelayLobbySession.Instance;
        var round=UnityEngine.Object.FindFirstObjectByType<HostRoundSetupCoordinator>();
        round.enabled=false;round.StopAllCoroutines();
        var bootstrap=UnityEngine.Object.FindFirstObjectByType<MatchAvatarBootstrap>();
        bootstrap.enabled=false;bootstrap.StopAllCoroutines();
        typeof(HostRoundSetupCoordinator).GetField("network",Private).SetValue(round,net);
        typeof(HostRoundSetupCoordinator).GetField("phase",Private).SetValue(round,HostRoundSetupCoordinator.HostPhase.Active);
        typeof(HostRoundSetupCoordinator).GetField("roundId",Private).SetValue(round,1);
        typeof(HostRoundSetupCoordinator).GetField("participantsAtStart",Private).SetValue(round,2);
        var participants=(System.Collections.Generic.HashSet<ulong>)typeof(HostRoundSetupCoordinator).GetField("roundParticipants",Private).GetValue(round);
        participants.Add(net.LocalClientId);participants.Add(101);
        var names=(System.Collections.Generic.Dictionary<ulong,string>)typeof(RelayLobbySession).GetField("names",Private).GetValue(session);
        names[net.LocalClientId]="탄약 확인";names[101]="검증 대상";
        var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
        var player=UnityEngine.Object.Instantiate(catalog.PlayerAvatar,new Vector3(0,.12f,-22f),Quaternion.identity);
        player.name="AmmoGlow_ReviewPlayer";
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(net.LocalClientId,true);
        player.GetComponent<HostPlayerLife>().HostResetForRound(1);
        player.GetComponent<PistolCombatController>().HostSetRound(1,true);
        var motor=player.GetComponent<PlayerLocalMotor>();motor.SetMovementLocked(true);
        var camera=motor.PlayerCamera;
        camera.transform.localRotation=Quaternion.identity;player.transform.Find("FPSRig_OWNER_ONLY").localRotation=Quaternion.Euler(14f,0,0);
        foreach(var other in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if(other!=camera)other.enabled=false;
        var ammo=UnityEngine.Object.FindFirstObjectByType<AmmoSpawnDirector>();
        if(!ammo.BeginRound(1,2))throw new Exception("Ammo director did not start");
        typeof(AmmoSpawnDirector).GetMethod("SpawnOne",Private).Invoke(ammo,null);
        typeof(AmmoSpawnDirector).GetMethod("SpawnOne",Private).Invoke(ammo,null);
        ammo.enabled=false;
        var pickups=UnityEngine.Object.FindObjectsByType<AmmoPickupActor>(FindObjectsSortMode.None).OrderBy(a=>a.NetworkObjectId).ToArray();
        for(int i=0;i<pickups.Length;i++)
        {
            pickups[i].name=i==0?"AmmoGlow_ReviewNear":"AmmoGlow_ReviewFar";
            Vector3 spot=i==0?new Vector3(0,.55f,-18):new Vector3(1.7f,.55f,-12);
            if(UnityEngine.AI.NavMesh.SamplePosition(spot,out var hit,3f,UnityEngine.AI.NavMesh.AllAreas))spot=hit.position+Vector3.up*.55f;
            pickups[i].transform.SetPositionAndRotation(spot,Quaternion.Euler(0,140f,0));
            pickups[i].enabled=false;
        }
        session.PublishMatchState();
        Directory.CreateDirectory(Output);
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        return new {playing=Application.isPlaying,host=net.IsHost,pickups=pickups.Select(p=>new {p.name,position=p.transform.position.ToString(),spawned=p.IsSpawned}).ToArray(),camera=camera.transform.position.ToString(),bloom=UnityEngine.Object.FindFirstObjectByType<PickupGlowPresentation>()!=null};
    }
    public static object SetView(string view)
    {
        var player=GameObject.Find("AmmoGlow_ReviewPlayer");
        var camera=player.GetComponent<PlayerLocalMotor>().PlayerCamera;
        var controller=player.GetComponent<CharacterController>();controller.enabled=false;
        player.transform.position=view=="close"?new Vector3(0,.12f,-20.1f):new Vector3(0,.12f,-22f);
        controller.enabled=true;camera.transform.localRotation=Quaternion.identity;player.transform.Find("FPSRig_OWNER_ONLY").localRotation=Quaternion.Euler(view=="close"?29f:14f,0,0);
        var hud=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.UI.GameHUDPresenter>();hud.SendMessage("Update");
        return new {view,camera=camera.transform.position.ToString(),post=camera.GetUniversalAdditionalCameraData().renderPostProcessing};
    }

    static readonly System.Collections.Generic.List<string> checks=new System.Collections.Generic.List<string>();
    static void Check(bool valid,string message)
    {
        if(!valid)throw new Exception(message);
        checks.Add(message);
    }
    static void MovePlayer(GameObject player,Vector3 position)
    {
        var controller=player.GetComponent<CharacterController>();controller.enabled=false;
        player.transform.position=position;controller.enabled=true;Physics.SyncTransforms();
    }
    static System.Collections.Generic.List<PlayerHighlightTarget.DrawPart> Parts(Camera camera)
    {
        var parts=new System.Collections.Generic.List<PlayerHighlightTarget.DrawPart>();
        PlayerHighlightTarget.Collect(camera,parts);return parts;
    }
    static bool HasTarget(System.Collections.Generic.List<PlayerHighlightTarget.DrawPart> parts,GameObject target)
        =>parts.Any(p=>p.Renderer.transform.IsChildOf(target.transform));
    public static object ValidateAssets()
    {
        var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
        Check(catalog.VisionPickup!=null && catalog.VisionPickup.GetComponent<VisionPickupActor>()!=null,"Detection network prefab registered");
        Check(catalog.PlayerAvatar.GetComponent<PlayerDetectionVision>()!=null,"Player buff component authored");
        var shell=catalog.VisionPickup.transform.Find("PickupGlowShell");
        Check(shell!=null && shell.GetComponent<Collider>()==null,"Purple shell is presentation only");
        Check(shell.GetComponent<Renderer>().sharedMaterial.GetFloat("_CenterOpacity")<.02f,"Sunglasses remain visible inside the shell");
        Check(!ShaderUtil.ShaderHasError(shell.GetComponent<Renderer>().sharedMaterial.shader),"Bubble shader has no errors");
        var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/HideAndShoot/Materials/AmmoPickup_SubtleEmission.mat");
        Check(original.shader.name=="Universal Render Pipeline/Lit" && original.GetColor("_BaseColor")==Color.white,"Original ammo appearance retained");
        return new {checks=checks.ToArray()};
    }
    static async System.Threading.Tasks.Task SaveFrame(string name)
    {
        await UnityEngine.Awaitable.EndOfFrameAsync();
        var image=ScreenCapture.CaptureScreenshotAsTexture();
        try{File.WriteAllBytes(Output+"/"+name,image.EncodeToPNG());}
        finally{UnityEngine.Object.Destroy(image);}
    }
    static Color32[] RenderWorld(Camera camera)
    {
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        var target=new RenderTexture(640,360,24);target.Create();
        var texture=new Texture2D(640,360,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,640,360),0,0);texture.Apply();return texture.GetPixels32();
        }
        finally
        {
            camera.targetTexture=oldTarget;RenderTexture.active=oldActive;
            target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(texture);
        }
    }
    static int CheckWall(Camera camera,GameObject hidden)
    {
        var bounds=new Bounds(hidden.transform.position+Vector3.up*.9f,new Vector3(1f,1.9f,.7f));
        var points=new System.Collections.Generic.List<Vector3>();
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            points.Add(camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))));
        int minX=Mathf.Clamp(Mathf.FloorToInt(points.Min(p=>p.x)*640)-3,0,639);
        int maxX=Mathf.Clamp(Mathf.CeilToInt(points.Max(p=>p.x)*640)+3,0,639);
        int minY=Mathf.Clamp(Mathf.FloorToInt(points.Min(p=>p.y)*360)-3,0,359);
        int maxY=Mathf.Clamp(Mathf.CeilToInt(points.Max(p=>p.y)*360)+3,0,359);
        var highlight=hidden.GetComponent<PlayerHighlightTarget>();
        highlight.enabled=false;var without=RenderWorld(camera);
        highlight.enabled=true;var with=RenderWorld(camera);
        int changed=0;
        for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
        {
            int i=y*640+x;
            if(Math.Abs(with[i].r-without[i].r)>8 || Math.Abs(with[i].g-without[i].g)>8 || Math.Abs(with[i].b-without[i].b)>8)changed++;
        }
        Check(changed==0,"Wall completely occludes detection outline (pixel comparison)");return changed;
    }
    public static async System.Threading.Tasks.Task<object> Run()
    {
        Directory.CreateDirectory(Output);checks.Clear();ValidateAssets();
        EditorApplication.ExecuteMenuItem("Window/General/Game");StartLocalHost();
        double deadline=EditorApplication.timeSinceStartup+15;
        while(SceneManager.GetActiveScene().name!="GameScene" || UnityEngine.Object.FindFirstObjectByType<VisionSpawnDirector>()==null)
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("GameScene load timeout");
            await System.Threading.Tasks.Task.Yield();
        }
        Prepare();
        var net=NetworkManager.Singleton;var session=RelayLobbySession.Instance;
        var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
        var owner=GameObject.Find("AmmoGlow_ReviewPlayer");
        var life=owner.GetComponent<HostPlayerLife>();var pistol=owner.GetComponent<PistolCombatController>();
        var vision=owner.GetComponent<PlayerDetectionVision>();var camera=owner.GetComponent<PlayerLocalMotor>().PlayerCamera;
        var round=UnityEngine.Object.FindFirstObjectByType<HostRoundSetupCoordinator>();
        var participants=(System.Collections.Generic.HashSet<ulong>)typeof(HostRoundSetupCoordinator).GetField("roundParticipants",Private).GetValue(round);
        var names=(System.Collections.Generic.Dictionary<ulong,string>)typeof(RelayLobbySession).GetField("names",Private).GetValue(session);
        var remote=new GameObject[2];
        for(int i=0;i<2;i++)
        {
            ulong id=(ulong)(101+i);participants.Add(id);names[id]=i==0?"검증 대상":"벽 뒤 대상";
            remote[i]=UnityEngine.Object.Instantiate(catalog.PlayerAvatar,new Vector3(i==0?-1.7f:1.7f,.12f,-16),Quaternion.Euler(0,180,0));
            remote[i].name=i==0?"Detection_VisibleTarget":"Detection_OccludedTarget";
            remote[i].GetComponent<NetworkObject>().SpawnWithOwnership(id,true);
            remote[i].GetComponent<HostPlayerLife>().HostResetForRound(1);
            remote[i].GetComponent<PistolCombatController>().HostSetRound(1,true);
        }
        typeof(HostRoundSetupCoordinator).GetField("participantsAtStart",Private).SetValue(round,3);
        session.PublishMatchState();
        foreach(var actor in UnityEngine.Object.FindObjectsByType<AmmoPickupActor>(FindObjectsSortMode.None))actor.transform.position+=Vector3.left*25;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Detection_QA_Occluder";
        wall.transform.position=new Vector3(1.25f,1f,-19);wall.transform.localScale=new Vector3(1.5f,2.4f,.35f);
        var wallMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));wallMaterial.color=new Color(.26f,.29f,.32f);wall.GetComponent<Renderer>().material=wallMaterial;
        await System.Threading.Tasks.Task.Delay(200);
        Check(Parts(camera).Count==0,"No detection before a pickup");
        var remoteVision=remote[0].GetComponent<PlayerDetectionVision>();
        Check(remoteVision.HostActivate(1),"Server can grant the remote-owned buff");
        Check(Parts(camera).Count==0,"Another player's buff does not highlight targets for this owner");
        Check(remoteVision.DetectionEndsAt.CanClientRead(101) && !remoteVision.DetectionEndsAt.CanClientRead(102),"Timer read permission is restricted to its owner and server");
        remoteVision.HostClear();
        var director=UnityEngine.Object.FindFirstObjectByType<VisionSpawnDirector>();
        Check(director.BeginRound(1),"Detection spawner starts with the round");
        double spawnStart=Time.realtimeSinceStartupAsDouble;
        Check(director.ActiveCount==0 && director.SecondsUntilSpawn>9.8,"First item waits ten seconds");
        while(director.ActiveCount==0)
        {
            if(Time.realtimeSinceStartupAsDouble-spawnStart>13)throw new Exception("First item did not spawn");
            await System.Threading.Tasks.Task.Delay(50);
        }
        double firstSpawnElapsed=Time.realtimeSinceStartupAsDouble-spawnStart;
        Check(firstSpawnElapsed>=9.9,"First item actually spawned after ten seconds");
        var pickup=UnityEngine.Object.FindFirstObjectByType<VisionPickupActor>();pickup.enabled=false;
        var ammo=UnityEngine.Object.FindFirstObjectByType<AmmoSpawnDirector>();
        Check(!ammo.IsCandidateOccupied(pickup.CandidateIndex),"Sunglasses and ammo do not share a spawn candidate");
        bool extra=(bool)typeof(VisionSpawnDirector).GetMethod("SpawnOne",Private).Invoke(director,null);
        Check(!extra && director.ActiveCount==1,"World spawn cap is one");
        pickup.transform.SetPositionAndRotation(new Vector3(0,.62f,-19.8f),Quaternion.Euler(0,150,0));
        Physics.SyncTransforms();
        await SaveFrame("Sunglasses_World.png");
        Check(!director.TryCollect(pickup,pistol),"A player outside pickup range cannot collect");
        pickup.transform.position=owner.transform.position+new Vector3(0,.5f,1f);Physics.SyncTransforms();
        int pickupInstance=pickup.GetInstanceID();int generation=pickup.Generation;
        Check(director.TryCollect(pickup,pistol),"Touch grants detection to a living participant");
        double collectedAt=Time.realtimeSinceStartupAsDouble;
        Check(!director.TryCollect(pickup,remote[0].GetComponent<PistolCombatController>()),"A second claimant cannot consume the same item");
        Check(vision.SecondsRemaining>6.8 && director.ActiveCount==0 && director.SecondsUntilSpawn>9.8,"Pickup grants seven seconds and schedules replacement after ten seconds");
        var parts=Parts(camera);
        Check(HasTarget(parts,remote[0]) && HasTarget(parts,remote[1]) && !HasTarget(parts,owner),"Detection selects every other living player and excludes self");
        var otherCamera=new GameObject("Detection_QA_OtherCamera").AddComponent<Camera>();otherCamera.enabled=false;
        Check(Parts(otherCamera).Count==0,"Only the owner's gameplay camera receives detection");UnityEngine.Object.Destroy(otherCamera.gameObject);
        int wallChangedPixels=CheckWall(camera,remote[1]);
        life.HostRevealForNpcHit(1);remote[0].GetComponent<HostPlayerLife>().HostRevealForNpcHit(1);
        parts=Parts(camera);
        Check(parts.Select(p=>p.Renderer).Distinct().Count()==parts.Count,"Overlapping reveal reasons draw each renderer once");
        var hud=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.UI.GameHUDPresenter>();hud.SendMessage("Update");
        var notice=hud.transform.Find("GameHUD_Canvas/DetectionVisionNotice");var warning=hud.transform.Find("GameHUD_Canvas/NpcRevealWarning");
        Check(notice.gameObject.activeSelf && warning.gameObject.activeSelf,"Detection notice and misfire warning can both be visible");
        Check(notice.GetComponent<RectTransform>().anchoredPosition.y>warning.GetComponent<RectTransform>().anchoredPosition.y+44,"Detection notice is above the misfire warning without overlap");
        await SaveFrame("Detection_Active.png");
        await System.Threading.Tasks.Task.Delay(1100);hud.SendMessage("Update");
        string countdown=notice.Find("Text").GetComponent<TMPro.TextMeshProUGUI>().text;
        Check(countdown.StartsWith("6초") || countdown.StartsWith("5초"),"Notice counts down using server time");
        while(vision.SecondsRemaining>0)
        {
            if(Time.realtimeSinceStartupAsDouble-collectedAt>9)throw new Exception("Buff failed to expire");
            await System.Threading.Tasks.Task.Delay(40);
        }
        hud.SendMessage("Update");
        Check(!notice.gameObject.activeSelf,"Detection notice disappears when the buff expires");
        parts=Parts(camera);
        Check(HasTarget(parts,remote[0]) && !HasTarget(parts,remote[1]),"Expiry preserves an independent misfire reveal and clears detection-only targets");
        await SaveFrame("Detection_Expired.png");
        Check(director.ActiveCount==0,"Replacement is not spawned before ten seconds");
        while(director.ActiveCount==0)
        {
            if(Time.realtimeSinceStartupAsDouble-collectedAt>13)throw new Exception("Replacement did not spawn");
            await System.Threading.Tasks.Task.Delay(50);
        }
        var replacement=UnityEngine.Object.FindFirstObjectByType<VisionPickupActor>();
        Check(replacement.GetInstanceID()==pickupInstance && replacement.Generation>generation && replacement.transform.Find("PickupGlowShell").gameObject.activeSelf,"Replacement reuses the pooled item and restores its shell");
        Check(!replacement.TryClaim(1,generation),"Stale pooled generations cannot claim the replacement");
        Check(vision.HostActivate(1),"Buff can be granted again");
        Check(life.HostKill(1,net.ServerTime.Tick),"Death accepted for cleanup check");
        Check(vision.SecondsRemaining==0 && vision.DetectionEndsAt.Value==0,"Death clears the detection state");
        life.HostResetForRound(1);pistol.HostSetRound(1,true);vision.HostActivate(1);pistol.HostSetRound(2,false);
        Check(vision.DetectionEndsAt.Value==0,"Round changes clear detection");
        director.EndRound();Check(director.ActiveCount==0,"Round end despawns the world item");
        var result=new {scope="Connected Editor local host with two simulated remote-owned players; no external client process",checks=checks.ToArray(),firstSpawnSeconds=firstSpawnElapsed,replacementSeconds=Time.realtimeSinceStartupAsDouble-collectedAt,wallChangedPixels,captures=new[]{Output+"/Sunglasses_World.png",Output+"/Detection_Active.png",Output+"/Detection_Expired.png"}};
        File.WriteAllText(Output+"/Verification.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }

}
