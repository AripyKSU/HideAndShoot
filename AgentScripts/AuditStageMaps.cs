using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HideAndShoot;
using HideAndShoot.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class AuditStageMaps
{
 const string Output="AgentReports/StageMaps_2026-10-16/";
 static void Capture(string name,Vector3 position,Vector3 look,bool orthographic)
 {
  var camera=new GameObject("StageMapAuditCamera").AddComponent<Camera>();camera.enabled=false;camera.transform.position=position;camera.transform.LookAt(look);camera.orthographic=orthographic;camera.orthographicSize=46;camera.fieldOfView=75;camera.farClipPlane=250;camera.nearClipPlane=.05f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.14f,.19f);
  var target=new RenderTexture(1400,1050,24);var previous=RenderTexture.active;var image=new Texture2D(1400,1050,TextureFormat.RGB24,false);
  try{camera.targetTexture=target;camera.aspect=4f/3;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1400,1050),0,0);image.Apply();File.WriteAllBytes(Output+name+".png",image.EncodeToPNG());}
  finally{RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(target);Object.DestroyImmediate(image);Object.DestroyImmediate(camera.gameObject);}
 }
 public static object Run()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var previousActive=SceneManager.GetActiveScene();
  var scene=SceneManager.GetSceneByPath("Assets/HideAndShoot/Scenes/GameScene.unity");bool opened=!scene.IsValid() || !scene.isLoaded;
  if(opened)scene=EditorSceneManager.OpenScene("Assets/HideAndShoot/Scenes/GameScene.unity",OpenSceneMode.Additive);
  if(scene.isDirty)throw new Exception("Preserve dirty GameScene before audit");
  var otherRoots=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s=>s!=scene).SelectMany(s=>s.GetRootGameObjects()).Select(g=>new {go=g,active=g.activeSelf}).ToArray();
  foreach(var state in otherRoots)state.go.SetActive(false);
  SceneManager.SetActiveScene(scene);
  var maps=Object.FindFirstObjectByType<StageMapDirector>();if(maps==null || !maps.HasThreeMaps)throw new Exception("Missing maps");
  var registry=Object.FindFirstObjectByType<SpawnPointRegistry>();var errors=new List<string>();var rows=new List<object>();
  Directory.CreateDirectory(Output);
  try
  {
   for(int k=0;k<3;k++)
   {
    if(!maps.ApplyMap(k))errors.Add("Map activation failed "+k);
    Physics.SyncTransforms();
    var map=maps.Definitions[k];var spawns=map.Anchors.GetComponentsInChildren<WorldAnchor>(true).Where(a=>a.Kind==WorldAnchor.AnchorKind.PlayerSpawn).OrderBy(a=>a.Index).ToArray();var items=registry.GetAmmoCandidates();
    var blocked=new List<string>();var missing=new List<string>();var disconnected=new List<string>();
    var hits=new List<Vector3>();
    foreach(var a in spawns)
    {
     if(!NavMesh.SamplePosition(a.transform.position,out var hit,.8f,NavMesh.AllAreas) || Mathf.Abs(hit.position.y-a.transform.position.y)> .8f)missing.Add(a.name);else hits.Add(hit.position);
     if(Physics.CheckCapsule(a.transform.position+Vector3.up*.36f,a.transform.position+Vector3.up*1.55f,.31f,~0,QueryTriggerInteraction.Ignore))blocked.Add(a.name);
    }
    for(int i=0;i<hits.Count;i++)for(int j=i+1;j<hits.Count;j++){var path=new NavMeshPath();if(!NavMesh.CalculatePath(hits[i],hits[j],NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)disconnected.Add(i+"-"+j);}
    var badItems=new List<string>();foreach(var item in items)
    {
     if(!NavMesh.SamplePosition(item.position,out var hit,.7f,NavMesh.AllAreas) || Mathf.Abs(hit.position.y-item.position.y)>.7f)badItems.Add(item.name);
     else if(hits.Count>0){var path=new NavMeshPath();if(!NavMesh.CalculatePath(hits[0],hit.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)badItems.Add(item.name+" disconnected");}
    }
    var ramps=map.World.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.Contains("Ramp") || c.name.Contains("Crater_") || c.name.Contains("Landing_")).ToArray();var badRamps=new List<string>();
    foreach(var ramp in ramps)
    {
     var v=ramp.sharedMesh.vertices;var a=ramp.transform.TransformPoint((v[0]+v[1])/2);var b=ramp.transform.TransformPoint((v[2]+v[3])/2);var d=(b-a).normalized;
     if(!NavMesh.SamplePosition(a-d*.55f,out var start,1f,NavMesh.AllAreas) || !NavMesh.SamplePosition(b+d*.55f,out var end,1f,NavMesh.AllAreas)){badRamps.Add(ramp.name+" endpoint");continue;}
     var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)badRamps.Add(ramp.name+" disconnected");
     var mid=(a+b)/2;if(!NavMesh.SamplePosition(mid,out var middle,.6f,NavMesh.AllAreas) || Mathf.Abs(middle.position.y-mid.y)>.5f)badRamps.Add(ramp.name+" midpoint");
    }
    if(blocked.Count+missing.Count+disconnected.Count+badItems.Count+badRamps.Count>0)errors.Add("Map "+k+" geometry validation failed");
    if(maps.Definitions.Count(m=>m.World.activeSelf)!=1)errors.Add("Multiple active maps");
    if(registry.PlayerSpawnCount!=9 || registry.AmmoCandidateCount!=16)errors.Add("Anchor counts "+k);
    var shaders=map.World.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.shader.name).Distinct().ToArray();
    if(shaders.Any(s=>s.Contains("InternalError")))errors.Add("Missing shader "+k);
    for(int seed=0;seed<50;seed++){if(!registry.TryDrawPlayerSpawns(8,seed,out var pts,out _) || pts.Distinct().Count()!=8)errors.Add("Spawn draw failed "+k);}
    var row=new {index=k,map.Name,spawns=spawns.Length,items=items.Count,blocked,missing,disconnected,badItems,badRamps,ramps=ramps.Length,shaders,vertices=NavMesh.CalculateTriangulation().vertices.Length};rows.Add(row);
    Capture("Map_"+k+"_Overview",new Vector3(62,72,-70),new Vector3(0,1,0),true);
    if(k==1)Capture("Map_1_Street",new Vector3(0,1.8f,-18),new Vector3(0,3,0),false);
    if(k==2)Capture("Map_2_Ground",new Vector3(-11,1.8f,-17),new Vector3(10,4,26),false);
   }
   var rng=new System.Random(137);var draws=Enumerable.Range(0,6000).Select(_=>StageMapDirector.DrawMapIndex(rng)).ToArray();var totals=Enumerable.Range(0,3).Select(i=>draws.Count(d=>d==i)).ToArray();bool repeats=draws.Skip(1).Where((d,i)=>d==draws[i]).Any();
   if(totals.Any(n=>n<1700 || n>2300) || !repeats)errors.Add("Random draw smoke check failed");
   var report=new {maps=rows,randomDraws=totals,repeatedMapsAllowed=repeats,errors,scope="Edit mode collider/NavMesh/spawn validation and captures; no runtime network test in this report"};
   File.WriteAllText(Output+"EditAudit.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
  }
  finally{maps.ApplyMap(0);foreach(var state in otherRoots)if(state.go!=null)state.go.SetActive(state.active);if(opened)EditorSceneManager.CloseScene(scene,true);if(previousActive.IsValid() && previousActive.isLoaded)SceneManager.SetActiveScene(previousActive);}
 }
}
