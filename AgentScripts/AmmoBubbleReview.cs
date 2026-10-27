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

public static class AmmoBubbleReview
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    const string Output="AgentReports/AmmoBubble_2026-10-16";
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
    public static object Inspect()
    {
        var shader=Shader.Find("HideAndShoot/Items/PickupBubble");
        var actors=UnityEngine.Object.FindObjectsByType<AmmoPickupActor>(FindObjectsSortMode.None);
        var camera=GameObject.Find("AmmoGlow_ReviewPlayer").GetComponent<PlayerLocalMotor>().PlayerCamera;
        return new {shaderErrors=ShaderUtil.GetShaderMessages(shader).Select(m=>new {m.message,severity=m.severity.ToString()}).ToArray(),pickupCount=actors.Length,pointLights=actors.Sum(a=>a.GetComponentsInChildren<Light>(true).Length),materials=actors.SelectMany(a=>a.GetComponentsInChildren<Renderer>()).Select(r=>r.sharedMaterial.shader.name).Distinct().ToArray(),post=camera.GetUniversalAdditionalCameraData().renderPostProcessing,hdr=camera.allowHDR,bloom=UnityEngine.Object.FindFirstObjectByType<Volume>()?.sharedProfile?.name};
    }
    public static async System.Threading.Tasks.Task<object> Run()
    {
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        StartLocalHost();
        double deadline=EditorApplication.timeSinceStartup+15;
        while(SceneManager.GetActiveScene().name!="GameScene" || UnityEngine.Object.FindFirstObjectByType<AmmoSpawnDirector>()==null)
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("GameScene load timeout");
            await System.Threading.Tasks.Task.Yield();
        }
        Prepare();
        await System.Threading.Tasks.Task.Delay(250);
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/HideAndShoot/Materials/AmmoPickup_SubtleEmission.mat");
        if(material.shader.name!="Universal Render Pipeline/Lit" || material.GetColor("_BaseColor")!=Color.white || material.IsKeywordEnabled("_EMISSION"))
            throw new Exception("Original ammo appearance was not preserved");
        var actors=UnityEngine.Object.FindObjectsByType<AmmoPickupActor>(FindObjectsSortMode.None);
        foreach(var actor in actors)
        {
            var shell=actor.transform.Find("PickupGlowShell");
            if(shell==null || !shell.gameObject.activeSelf || shell.GetComponent<Collider>()!=null ||
               shell.GetComponent<Renderer>().sharedMaterial.shader.name!="HideAndShoot/Items/PickupBubble")
                throw new Exception("Invalid pickup bubble");
        }
        await SaveFrame("AmmoBubble_Gameplay.png");
        SetView("close");
        await SaveFrame("AmmoBubble_Close.png");
        var inspection=Inspect();
        var director=UnityEngine.Object.FindFirstObjectByType<AmmoSpawnDirector>();
        var pistol=GameObject.Find("AmmoGlow_ReviewPlayer").GetComponent<PistolCombatController>();
        var pickup=GameObject.Find("AmmoGlow_ReviewNear").GetComponent<AmmoPickupActor>();
        int instance=pickup.GetInstanceID();int before=pistol.ServerAmmo.Value;
        bool collected=director.TryCollect(pickup,pistol);
        bool shellHidden=!pickup.transform.Find("PickupGlowShell").gameObject.activeSelf;
        if(!collected || pistol.ServerAmmo.Value!=10 || !shellHidden)throw new Exception("Pickup despawn regression");
        typeof(AmmoSpawnDirector).GetMethod("SpawnOne",Private).Invoke(director,null);
        bool pooled=pickup.GetInstanceID()==instance && pickup.IsSpawned && pickup.transform.Find("PickupGlowShell").gameObject.activeSelf;
        if(!pooled || pickup.GetComponentsInChildren<Renderer>(true).Count(r=>r.name=="PickupGlowShell")!=1)
            throw new Exception("Bubble pool reuse regression");
        var result=new {ammoTexturePreserved=true,transparentShell=true,shellCollider=false,collection=collected,ammoBefore=before,ammoAfter=pistol.ServerAmmo.Value,shellHiddenOnDespawn=shellHidden,shellRestoredOnReuse=pooled,inspection,captures=new[]{Output+"/AmmoBubble_Gameplay.png",Output+"/AmmoBubble_Close.png"}};
        File.WriteAllText(Output+"/Verification.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        return result;
    }
    private static async System.Threading.Tasks.Task SaveFrame(string name)
    {
        await UnityEngine.Awaitable.EndOfFrameAsync();
        var image=ScreenCapture.CaptureScreenshotAsTexture();
        try{File.WriteAllBytes(Output+"/"+name,image.EncodeToPNG());}
        finally{UnityEngine.Object.Destroy(image);}
    }}


