using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HideAndShoot.World;
using HideAndShoot.Player;
using HideAndShoot.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class AuthorDetectionItem
{
    static Material Lit(string name,Color color,float smoothness)
    {
        string path="Assets/HideAndShoot/Materials/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",.08f);mat.SetFloat("_Smoothness",smoothness);
        EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
    }
    public static object Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
        const string path="Assets/HideAndShoot/Prefabs/VisionPickup.prefab";
        var frame=Lit("DetectionSunglasses_Frame",new Color(.035f,.025f,.05f),.3f);
        var lens=Lit("DetectionSunglasses_Lens",new Color(.36f,.075f,.65f),.6f);
        lens.SetColor("_EmissionColor",new Color(.08f,.012f,.18f));lens.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(lens);AssetDatabase.SaveAssetIfDirty(lens);
        var bubble=AssetDatabase.LoadAssetAtPath<Material>("Assets/HideAndShoot/Materials/DetectionSunglasses_Bubble.mat");
        if(bubble==null){bubble=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/HideAndShoot/Materials/AmmoPickup_Bubble.mat"));AssetDatabase.CreateAsset(bubble,"Assets/HideAndShoot/Materials/DetectionSunglasses_Bubble.mat");}
        bubble.SetColor("_Tint",new Color(.25f,.035f,.6f));bubble.SetColor("_EdgeColor",new Color(2f,.25f,4f));
        EditorUtility.SetDirty(bubble);AssetDatabase.SaveAssetIfDirty(bubble);
        var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HideAndShoot/ThirdParty/DetectionSunglasses/Sunglasses.fbx");
        if(model==null)throw new Exception("Selected CC0 FBX was not imported");
        int triangles=0;
        var preview=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("VisionPickup");SceneManager.MoveGameObjectToScene(root,preview);
        try
        {
            root.layer=LayerMask.NameToLayer("AmmoPickup");
            root.AddComponent<NetworkObject>();root.AddComponent<NetworkTransform>();
            var trigger=root.AddComponent<SphereCollider>();trigger.isTrigger=true;trigger.radius=.46f;trigger.center=new Vector3(0,.3f,0);
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var actor=root.AddComponent<VisionPickupActor>();
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(model,preview);visual.name="SunglassesVisual";
            visual.transform.SetParent(root.transform,false);visual.transform.localPosition=Vector3.zero;
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)materials[i]=materials[i]!=null && materials[i].name.ToLowerInvariant().Contains("black")?frame:lens;
                renderer.sharedMaterials=materials;
                var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh!=null)triangles+=mesh.triangles.Length/3;
            }
            actor.PrepareVisual();
            var shell=GameObject.CreatePrimitive(PrimitiveType.Sphere);shell.name="PickupGlowShell";
            shell.transform.SetParent(root.transform,false);shell.transform.localPosition=Vector3.zero;shell.transform.localScale=Vector3.one*.70f;shell.layer=root.layer;
            UnityEngine.Object.DestroyImmediate(shell.GetComponent<Collider>());
            var shellRenderer=shell.GetComponent<MeshRenderer>();shellRenderer.sharedMaterial=bubble;
            shellRenderer.shadowCastingMode=ShadowCastingMode.Off;shellRenderer.receiveShadows=false;
            shellRenderer.lightProbeUsage=LightProbeUsage.Off;shellRenderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(preview);}
        var pickup=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var networkObject=pickup.GetComponent<NetworkObject>();
        typeof(NetworkObject).GetMethod("OnValidate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(networkObject,null);
        EditorUtility.SetDirty(networkObject);AssetDatabase.SaveAssetIfDirty(pickup);
        string playerPath="Assets/HideAndShoot/Prefabs/PlayerAvatar.prefab";
        var player=PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            if(player.GetComponent<PlayerDetectionVision>()==null)player.AddComponent<PlayerDetectionVision>();
            PrefabUtility.SaveAsPrefabAsset(player,playerPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(player);}
        var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");catalog.VisionPickup=pickup;
        EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
        var originallyActive=SceneManager.GetActiveScene();
        const string scenePath="Assets/HideAndShoot/Scenes/GameScene.unity";
        var scene=SceneManager.GetSceneByPath(scenePath);bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        bool wasDirty=scene.isDirty;bool saved=false;
        try
        {
            var services=scene.GetRootGameObjects().Single(g=>g.name=="GameplayServices");
            if(services.GetComponent<VisionSpawnDirector>()==null)Undo.AddComponent<VisionSpawnDirector>(services);
            var hud=services.GetComponent<GameHUDPresenter>();var canvas=hud.transform.Find("GameHUD_Canvas");
            var panel=canvas.Find("DetectionVisionNotice");
            if(panel==null)
            {
                var warning=canvas.Find("NpcRevealWarning");
                var copy=UnityEngine.Object.Instantiate(warning.gameObject,canvas);copy.name="DetectionVisionNotice";
                Undo.RegisterCreatedObjectUndo(copy,"Add detection item notice");panel=copy.transform;
                var rect=copy.GetComponent<RectTransform>();rect.anchoredPosition+=Vector2.up*52f;
            }
            var label=panel.Find("Text").GetComponent<TextMeshProUGUI>();
            Undo.RecordObject(label,"Style detection notice");label.color=new Color(.79f,.58f,1f);
            label.text="7초 동안 다른 플레이어의 위치가 보입니다.";
            label.enableAutoSizing=true;label.fontSizeMin=13;label.fontSizeMax=18;label.textWrappingMode=TextWrappingModes.NoWrap;
            panel.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!wasDirty){EditorSceneManager.SaveScene(scene);saved=true;}
        }
        finally
        {
            if(opened && saved)EditorSceneManager.CloseScene(scene,true);
            if(originallyActive.IsValid() && originallyActive.isLoaded)SceneManager.SetActiveScene(originallyActive);
        }
        var field=typeof(NetworkObject).GetField("GlobalObjectIdHash",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        var hashes=new[]{catalog.PlayerAvatar,catalog.NPCActor,catalog.AmmoPickup,catalog.VisionPickup}.Select(go=>(uint)field.GetValue(go.GetComponent<NetworkObject>())).ToArray();
        if(hashes.Any(h=>h==0) || hashes.Distinct().Count()!=hashes.Length)throw new Exception("Network prefab hashes must be nonzero and unique");
        Directory.CreateDirectory("AgentReports/DetectionItem_2026-10-16");
        var result=new {triangles,license="CC0 1.0",model="glasses12.001",prefab=path,networkHashes=hashes,ownerOnlyTimer=true,sceneSaved=saved,preexistingDirtyScenePreserved=wasDirty};
        File.WriteAllText("AgentReports/DetectionItem_2026-10-16/Authoring.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        return result;
    }
}