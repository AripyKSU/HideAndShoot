using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using HideAndShoot;
using HideAndShoot.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class FixIndustrialRoadNavMesh
{
 const string ScenePath="Assets/HideAndShoot/Scenes/GameScene.unity";
 const string Report="AgentReports/IndustrialRoadNavMesh_2026-10-16/";
 static Bounds RoadBounds(Transform t)
 {
  var rr=t.GetComponentsInChildren<Renderer>(true);
  var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);return b;
 }
 static Transform[] Roads(GameObject root)=>root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("road-",StringComparison.Ordinal)).ToArray();
 static Vector3 Top(Transform t){var b=RoadBounds(t);return new Vector3(b.center.x,b.max.y,b.center.z);}
 static Vector3 Sample(Vector3 p,float range=.4f)
 {
  if(!NavMesh.SamplePosition(p,out var h,range,NavMesh.AllAreas))throw new Exception("Missing NavMesh at "+p);
  return h.position;
 }
 static void RequirePath(Vector3 a,Vector3 b,string label,List<string> errors)
 {
  var path=new NavMeshPath();
  if(!NavMesh.CalculatePath(a,b,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)errors.Add(label);
 }
 public static object Verify()
 {
  var scene=SceneManager.GetSceneByPath(ScenePath);
  var root=scene.GetRootGameObjects().Single(g=>g.name=="StageMap_01_IndustrialRing");
  if(!root.activeInHierarchy)throw new Exception("Industrial map must be active for audit");
  Physics.SyncTransforms();
  var roads=Roads(root);var errors=new List<string>();int tested=0,blocked=0;
  var seed=Sample(new Vector3(-24,.2f,24),.8f);
  foreach(var t in roads)
  {
   var b=RoadBounds(t);var center=Top(t);
   foreach(var offset in new[]{Vector3.zero,Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
   {
    var p=center+offset;
    // Keep intentional cargo/building footprints excluded.
    if(Physics.CheckCapsule(p+Vector3.up*.65f,p+Vector3.up*1.5f,.51f,~0,QueryTriggerInteraction.Ignore)){blocked++;continue;}
    tested++;
    if(!NavMesh.SamplePosition(p,out var h,.25f,NavMesh.AllAreas)||h.position.y<p.y-.02f){errors.Add(t.name+" road surface "+p);continue;}
    RequirePath(seed,h.position,t.name+" disconnected "+p,errors);
   }
  }
  int ramps=0;
  foreach(var c in root.GetComponentsInChildren<MeshCollider>().Where(c=>c.name.Contains("Ramp")))
  {
   var v=c.sharedMesh.vertices;
   var a=c.transform.TransformPoint((v[0]+v[1])*.5f);var b=c.transform.TransformPoint((v[2]+v[3])*.5f);
   var flat=b-a;flat.y=0;flat.Normalize();
   var start=Sample(a-flat*.7f,.9f);var end=Sample(b+flat*.7f,.9f);
   RequirePath(start,end,c.name+" up",errors);RequirePath(end,start,c.name+" down",errors);ramps++;
  }
  var anchors=root.GetComponentsInChildren<WorldAnchor>();
  int anchorChecks=0;
  foreach(var a in anchors)
  {
   var p=Sample(a.transform.position,.8f);RequirePath(seed,p,a.name+" disconnected",errors);anchorChecks++;
  }
  int roofLeaks=0;
  foreach(var m in root.GetComponentsInChildren<NavMeshModifier>().Where(m=>m.overrideArea&&m.area==1))
  {
   var c=m.GetComponent<BoxCollider>();if(c==null||c.bounds.size.y<1)continue;
   var b=c.bounds;var p=new Vector3(b.center.x,b.max.y,b.center.z);
   if(NavMesh.SamplePosition(p,out var h,.2f,NavMesh.AllAreas)&&Mathf.Abs(h.position.y-p.y)<.2f){roofLeaks++;errors.Add(m.name+" unwanted rooftop");}
  }
  var report=new {passed=errors.Count==0,roads=roads.Length,groundRoads=roads.Count(t=>Top(t).y<1),elevatedRoads=roads.Count(t=>Top(t).y>3),roadSurfaceChecks=tested,intentionalObstacleSamples=blocked,ramps,bidirectionalRampPaths=ramps*2,anchorChecks,roofLeaks,errors};
  Directory.CreateDirectory(Report);File.WriteAllText(Report+"EditAudit.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
  return report;
 }
 public static object Run()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var scene=SceneManager.GetSceneByPath(ScenePath);
  if(!scene.IsValid()||!scene.isLoaded)throw new Exception("Expected connected GameScene");
  var root=scene.GetRootGameObjects().Single(g=>g.name=="StageMap_01_IndustrialRing");
  var surface=root.GetComponent<NavMeshSurface>();
  if(surface.useGeometry!=NavMeshCollectGeometry.PhysicsColliders)throw new Exception("Unexpected geometry setting");
  var old=surface.navMeshData;var assetPath=AssetDatabase.GetAssetPath(old);
  if(string.IsNullOrEmpty(assetPath))throw new Exception("Expected persisted NavMesh");
  string backup="AgentBackups/IndustrialRoadNavMesh_2026-10-16/"+DateTime.Now.ToString("HHmmss")+"/";
  Directory.CreateDirectory(backup);
  if(!EditorSceneManager.SaveScene(scene,backup+"GameScene.unsaved-before-road-fix.unity",true))throw new Exception("Snapshot failed");
  File.Copy(ScenePath,backup+"GameScene.on-disk-before-road-fix.unity");
  File.Copy(assetPath,backup+"NavMesh.before.asset");
  int added=0;Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Include industrial roads in NavMesh");
  foreach(var t in Roads(root))
  {
   var b=RoadBounds(t);var c=t.GetComponent<BoxCollider>();
   if(c==null){c=Undo.AddComponent<BoxCollider>(t.gameObject);added++;}
   else Undo.RecordObject(c,"Road collider");
   c.center=t.InverseTransformPoint(b.center);
   var scale=t.lossyScale;c.size=new Vector3(b.size.x/Mathf.Abs(scale.x),b.size.y/Mathf.Abs(scale.y),b.size.z/Mathf.Abs(scale.z));
   c.enabled=true;c.isTrigger=false;
  }
  // Resolve the thin road decks without sinking the mesh under their visible surface.
  surface.overrideVoxelSize=true;surface.voxelSize=.05f;
  Physics.SyncTransforms();surface.BuildNavMesh();
  var built=surface.navMeshData;
  if(built==null||built==old)throw new Exception("NavMesh build failed");
  surface.RemoveData();EditorUtility.CopySerialized(built,old);old.name=Path.GetFileNameWithoutExtension(assetPath);surface.navMeshData=old;surface.AddData();Object.DestroyImmediate(built);
  EditorUtility.SetDirty(old);AssetDatabase.SaveAssetIfDirty(old);
  EditorSceneManager.MarkSceneDirty(scene);
  if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new Exception("Scene save failed");
  Undo.CollapseUndoOperations(undo);
  var audit=Verify();
  return new {added,backup,navMesh=assetPath,audit};
 }
 public static async Task<object> PlayVerify()
 {
  if(!EditorApplication.isPlaying)throw new Exception("Play mode required");
  var scene=SceneManager.GetSceneByPath(ScenePath);var director=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StageMapDirector>(true)).Single();
  director.ApplyMap(1);Application.runInBackground=true;
  int startFrame=Time.frameCount;var errors=new List<string>();int completed=0;
  var starts=new[]{new Vector3(-12,.2f,19),new Vector3(-18,4.2f,30),new Vector3(0,.2f,21)};
  var ends=new[]{new Vector3(12,.2f,19),new Vector3(18,4.2f,30),new Vector3(0,4.2f,31)};
  var agents=new List<NavMeshAgent>();
  try
  {
   for(int i=0;i<starts.Length;i++)
   {
    var go=new GameObject("QA_IndustrialRoadWalker_"+i);go.transform.position=Sample(starts[i],.8f);
    var a=go.AddComponent<NavMeshAgent>();a.radius=.5f;a.height=2;a.speed=12;a.acceleration=60;a.angularSpeed=720;a.stoppingDistance=.15f;
    if(!a.isOnNavMesh||!a.SetDestination(Sample(ends[i],.8f)))errors.Add("Agent setup "+i);agents.Add(a);
   }
   float until=Time.realtimeSinceStartup+15;var done=new bool[agents.Count];
   while(Time.realtimeSinceStartup<until&&completed<agents.Count)
   {
    await Task.Delay(100);
    for(int i=0;i<agents.Count;i++)
    {
     var a=agents[i];if(done[i])continue;
     if(!a.isOnNavMesh){errors.Add("Off mesh "+i);done[i]=true;continue;}
     if(!a.pathPending&&a.pathStatus==NavMeshPathStatus.PathComplete&&Vector3.Distance(a.transform.position,Sample(ends[i],.8f))<.4f){done[i]=true;completed++;}
    }
   }
   if(completed!=agents.Count)errors.Add("Only "+completed+"/"+agents.Count+" routes completed");
   int frames=Time.frameCount-startFrame;if(frames<2)errors.Add("Game did not advance");
   var report=new {passed=errors.Count==0,completed,routes=agents.Count,frames,errors,scope="Actual NavMeshAgent movement on ground road, elevated road and connecting ramp"};
   File.WriteAllText(Report+"PlayAudit.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
  }
  finally{foreach(var a in agents)if(a!=null)Object.Destroy(a.gameObject);}
 }
}


