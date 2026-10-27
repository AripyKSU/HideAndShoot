using System;
using System.IO;
using System.IO.Compression;
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
using Object = UnityEngine.Object;

public static class BuildStageMaps
{
    const string Root="Assets/HideAndShoot/";
    const string Kenney=Root+"ThirdParty/Kenney/";
    const string Generated=Root+"Maps/Generated/";
    const string ScenePath=Root+"Scenes/GameScene.unity";
    const string Report="AgentReports/StageMaps_2026-10-16/";
    static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
    static Scene scene;
    static Transform parent;
    static int count;

    public static object Import()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
        int added=0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(var pack in new[]{new[]{"kenney_city-kit-industrial_2.0.zip","CityKit_Industrial"},new[]{"kenney_space-kit.zip","SpaceKit"}})
            {
                string dest=Kenney+pack[1]+"/";
                Directory.CreateDirectory(dest+"Models");Directory.CreateDirectory(dest+"Textures");
                using(var zip=new ZipArchive(File.OpenRead("C:/Users/PC/Downloads/"+pack[0]),ZipArchiveMode.Read))
                foreach(var entry in zip.Entries)
                {
                    string target=null;
                    if(entry.FullName.StartsWith("Models/FBX format/") && entry.Name.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase))target=dest+"Models/"+entry.Name;
                    if(entry.Name.EndsWith(".png",StringComparison.OrdinalIgnoreCase) && entry.FullName.IndexOf("Textures/",StringComparison.OrdinalIgnoreCase)>=0 && !entry.FullName.StartsWith("Isometric/") && !entry.FullName.StartsWith("Models/GLB") && !entry.FullName.StartsWith("Models/OBJ"))target=dest+"Textures/"+entry.Name;
                    if(entry.FullName=="License.txt")target=dest+"License.txt";
                    if(target==null || File.Exists(target))continue;
                    using(var from=entry.Open())using(var to=File.Create(target))from.CopyTo(to);
                    added++;
                }
            }
        }
        finally {AssetDatabase.StopAssetEditing();}
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return new {added,industrial=AssetDatabase.FindAssets("t:Model",new[]{Kenney+"CityKit_Industrial"}).Length,space=AssetDatabase.FindAssets("t:Model",new[]{Kenney+"SpaceKit"}).Length,textures=AssetDatabase.FindAssets("t:Texture",new[]{Kenney+"SpaceKit"}).Select(AssetDatabase.GUIDToAssetPath).ToArray()};
    }

    static Material Mat(string name,Color color,string texture=null)
    {
        if(materials.TryGetValue(name,out var cached))return cached;
        string path=Generated+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.12f);
        if(texture!=null)m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        EditorUtility.SetDirty(m);materials[name]=m;return m;
    }
    static Material Palette(string pack)=>Mat("Palette_"+pack,Color.white,Kenney+pack+"/Textures/colormap.png");
    static Material Concrete=>Mat("Concrete",new Color(.47f,.49f,.47f));
    static Material Asphalt=>Mat("Asphalt",new Color(.18f,.21f,.24f));
    static Material Sand=>Mat("MarsSand",new Color(.82f,.43f,.28f));
    static Material Cliff=>Mat("MarsCliff",new Color(.69f,.32f,.20f));
    static Material Stripe=>Mat("SafetyYellow",new Color(.96f,.65f,.14f));
    static Material White=>Mat("BaseWhite",new Color(.81f,.85f,.86f));
    static GameObject Group(string name,Transform holder=null)
    {
        var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(holder??parent,false);return go;
    }
    static void Obstacle(GameObject go)
    {
        var mod=go.GetComponent<NavMeshModifier>()??go.AddComponent<NavMeshModifier>();mod.overrideArea=true;mod.area=1;
    }
    static GameObject Box(string name,Vector3 center,Vector3 size,Material mat,bool solid=true,bool obstacle=false)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(parent,false);go.transform.position=center;go.transform.localScale=size;go.isStatic=true;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());
        if(obstacle)Obstacle(go);return go;
    }
    static GameObject Floor(string name,float x0,float x1,float z0,float z1,float top,Material mat,float thick=1f)=>Box(name,new Vector3((x0+x1)/2,top-thick/2,(z0+z1)/2),new Vector3(x1-x0,thick,z1-z0),mat);
    static Bounds BoundsOf(GameObject obj)
    {
        var rr=obj.GetComponentsInChildren<Renderer>(true);if(rr.Length==0)throw new Exception("No mesh: "+obj.name);
        var b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);return b;
    }
    static GameObject Model(string pack,string file,Vector3 at,Vector3 size,float yaw=0,bool solid=true)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Kenney+pack+"/Models/"+file+".fbx");if(source==null)throw new Exception("Missing model "+pack+"/"+file);
        var group=Group(file+"_"+(count++));
        var model=(GameObject)PrefabUtility.InstantiatePrefab(source,group.transform);model.transform.localRotation=Quaternion.Euler(0,yaw,0);
        var b=BoundsOf(model);
        // Scale in world axes on the wrapper after the model has been oriented.
        group.transform.localScale=new Vector3(size.x/Mathf.Max(.001f,b.size.x),size.y>0?size.y/Mathf.Max(.001f,b.size.y):size.x/Mathf.Max(.001f,b.size.x),size.z/Mathf.Max(.001f,b.size.z));
        b=BoundsOf(model);group.transform.position+=at-new Vector3(b.center.x,b.min.y,b.center.z);
        foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
        foreach(var rr in model.GetComponentsInChildren<Renderer>(true))
        {
            rr.sharedMaterials=rr.sharedMaterials.Select(m=>pack=="SpaceKit" ? Mat("Space_"+(m!=null?m.name:"White"),m!=null && m.HasProperty("_Color")?m.GetColor("_Color"):Color.white) : Palette(pack)).ToArray();
            rr.gameObject.isStatic=true;
        }
        bool road=pack=="CityKit_Roads" && file.StartsWith("road-",StringComparison.Ordinal);
        // Collider baking must follow the visible road deck, above the supporting ground.
        if(solid || road){b=BoundsOf(model);var c=group.AddComponent<BoxCollider>();c.center=group.transform.InverseTransformPoint(b.center);c.size=new Vector3(b.size.x/group.transform.lossyScale.x,b.size.y/group.transform.lossyScale.y,b.size.z/group.transform.lossyScale.z);if(solid)Obstacle(group);}
        return group;
    }
    static GameObject Ramp(string name,Vector3 start,Vector3 end,float width,Material material)
    {
        var go=Group(name);var across=Vector3.Cross((end-start).normalized,Vector3.up).normalized*width/2;
        var top=new[]{start-across,start+across,end-across,end+across};
        var vv=top.Concat(top.Select(v=>v-Vector3.up*.25f)).ToArray();
        var mesh=new Mesh {name=name};mesh.vertices=vv;mesh.triangles=new[]{0,1,2,1,3,2,4,6,5,5,6,7,0,4,2,2,4,6,1,3,5,3,7,5,0,1,4,1,5,4,2,6,3,3,6,7};mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Generated+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
        var delta=end-start;float length=delta.magnitude;var forward=delta.normalized;
        for(int sign=-1;sign<=1;sign+=2)
        {
            var rail=Box(name+"_Edge",(start+end)/2+across.normalized*(width/2+.12f)*sign+Vector3.up*.24f,new Vector3(.18f,.45f,length),Stripe,true,true);
            rail.transform.rotation=Quaternion.LookRotation(forward,Vector3.up);
        }
        return go;
    }
    static void Boundary(bool mars)
    {
        float y=mars?3.2f:5.5f;var mat=mars?Cliff:Concrete;
        Box("Boundary_N",new Vector3(0,y,35.3f),new Vector3(72,3,1.4f),mat,true,true);Box("Boundary_S",new Vector3(0,y,-35.3f),new Vector3(72,3,1.4f),mat,true,true);
        Box("Boundary_E",new Vector3(35.3f,y,0),new Vector3(1.4f,3,72),mat,true,true);Box("Boundary_W",new Vector3(-35.3f,y,0),new Vector3(1.4f,3,72),mat,true,true);
        // Invisible full-height boundary prevents walking below raised perimeter walls.
        foreach(var a in new[]{new Vector3(0,1,35.6f),new Vector3(0,1,-35.6f),new Vector3(35.6f,1,0),new Vector3(-35.6f,1,0)})
        {var b=Box("BoundaryCollision",a,Mathf.Abs(a.x)>35?new Vector3(.8f,14,72):new Vector3(72,14,.8f),mat,true,true);b.GetComponent<Renderer>().enabled=false;}
    }
    static void Anchors(GameObject world,bool mars)
    {
        var root=Group("MapAnchors",world.transform);
        float[,] spawn=mars?new float[,]{{-28,25,135},{-3,16,180},{28,17,225},{-28,-4,90},{-14,-8,180},{28,-8,270},{-26,-26,45},{0,-16,0},{29,-26,315}}:new float[,]{{-24,24,135},{-8,19,180},{24,24,225},{-24,6,90},{-13,-6,180},{24,-6,270},{-24,-24,45},{0,-18,0},{24,-24,315}};
        for(int i=0;i<9;i++){var go=Group("PSpawn_"+(i+1).ToString("00"),root.transform);go.transform.position=new Vector3(spawn[i,0],.2f,spawn[i,1]);go.transform.rotation=Quaternion.Euler(0,spawn[i,2],0);var a=go.AddComponent<WorldAnchor>();a.Kind=WorldAnchor.AnchorKind.PlayerSpawn;a.Index=i;}
        Vector3[] items=mars?new[]{new Vector3(-28,0,17),new Vector3(-21,0,0),new Vector3(-22,0,-16),new Vector3(-13,0,9),new Vector3(8,0,14),new Vector3(30,0,7),new Vector3(20,0,-13),new Vector3(16,0,-28),new Vector3(-21,0,-25),new Vector3(-5,0,-15),new Vector3(-6,-1.5f,0),new Vector3(6,-1.5f,0),new Vector3(-8,3,27),new Vector3(24,3,27),new Vector3(-5,2,-24),new Vector3(4,2,-24)}:new[]{new Vector3(-19,0,8),new Vector3(-19,0,-12),new Vector3(19,0,12),new Vector3(19,0,-10),new Vector3(-8,0,17),new Vector3(8,0,-19),new Vector3(-13,0,7),new Vector3(13,0,-7),new Vector3(-25,0,25),new Vector3(25,0,25),new Vector3(-25,0,-25),new Vector3(25,0,-25),new Vector3(-15,4,30),new Vector3(15,4,30),new Vector3(-15,4,-30),new Vector3(15,4,-30)};
        for(int i=0;i<items.Length;i++){var go=Group("AmmoCandidate_"+(i+1).ToString("00"),root.transform);go.transform.position=items[i]+Vector3.up*.22f;var a=go.AddComponent<WorldAnchor>();a.Kind=WorldAnchor.AnchorKind.AmmoCandidate;a.Index=i;}
    }
    static GameObject City()
    {
        var world=Group("StageMap_01_IndustrialRing");world.transform.SetParent(null);parent=world.transform;
        Floor("CityGround",-36,36,-36,36,0,Concrete);
        // Raised perimeter with four notches for the continuous ramps.
        foreach(int sign in new[]{-1,1})
        {
            float z0=sign>0?27:-33,z1=sign>0?33:-27;
            Floor("HighRoad_NorthSouth",-33,-2,z0,z1,4,Asphalt,4);Floor("HighRoad_NorthSouth",2,33,z0,z1,4,Asphalt,4);
            Floor("HighRoad_OuterLane",-2,2,sign>0?30:-33,sign>0?33:-30,4,Asphalt,4);
            float x0=sign>0?27:-33,x1=sign>0?33:-27;
            Floor("HighRoad_EastWest",x0,x1,-27,-2,4,Asphalt,4);Floor("HighRoad_EastWest",x0,x1,2,27,4,Asphalt,4);
            Floor("HighRoad_OuterLane",sign>0?30:-33,sign>0?33:-30,-2,2,4,Asphalt,4);
        }
        for(int side=0;side<4;side++)
        {
            float sign=side<2?1:-1;bool horizontal=side%2==0;
            for(int i=-2;i<=2;i++)Model("CityKit_Roads","road-straight",horizontal?new Vector3(i*6,.015f,sign*19):new Vector3(sign*19,.015f,i*6),horizontal?new Vector3(6,.10f,8):new Vector3(8,.10f,6),horizontal?0:90,false);
            for(int i=-4;i<=4;i++)if(i!=0)Model("CityKit_Roads","road-straight",horizontal?new Vector3(i*6,4.015f,sign*30):new Vector3(sign*30,4.015f,i*6),new Vector3(6,.08f,6),horizontal?0:90,false);
            for(int i=-3;i<=3;i++)
            {
                Box("LaneMark",horizontal?new Vector3(i*4,.13f,sign*19):new Vector3(sign*19,.13f,i*4),horizontal?new Vector3(1.8f,.015f,.10f):new Vector3(.10f,.015f,1.8f),White,false);
                if(i!=0)Box("HighLaneMark",horizontal?new Vector3(i*8,4.13f,sign*30):new Vector3(sign*30,4.13f,i*8),horizontal?new Vector3(2,.015f,.10f):new Vector3(.10f,.015f,2),White,false);
            }
        }
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            Model("CityKit_Roads","road-bend-square",new Vector3(x*19,.015f,z*19),new Vector3(8,.10f,8),x>0?(z>0?0:90):(z>0?270:180),false);
            Model("CityKit_Roads","road-bend-square",new Vector3(x*30,4.015f,z*30),new Vector3(6,.08f,6),x>0?(z>0?0:90):(z>0?270:180),false);
        }
        // Main landmark is a four-module industrial complex with one solid footprint.
        Floor("CentralFoundation",-10,10,-10,10,.18f,Concrete);
        int n=0;foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Model("CityKit_Industrial",new[]{"building-l","building-p","building-t","building-h"}[n++],new Vector3(x*4.9f,.18f,z*4.9f),new Vector3(9.7f,8.8f,9.7f),x>0?180:0,false);
        var central=Box("CentralComplexCollision",new Vector3(0,4.8f,0),new Vector3(20,9.6f,20),Concrete,true,true);central.GetComponent<Renderer>().enabled=false;
        Model("CityKit_Industrial","chimney-large",new Vector3(4,8.9f,3),new Vector3(2.5f,4,2.5f),0,false);
        Ramp("CityRamp_N",new Vector3(0,0,22),new Vector3(0,4,30),4,Concrete);Ramp("CityRamp_S",new Vector3(0,0,-22),new Vector3(0,4,-30),4,Concrete);Ramp("CityRamp_E",new Vector3(22,0,0),new Vector3(30,4,0),4,Concrete);Ramp("CityRamp_W",new Vector3(-22,0,0),new Vector3(-30,4,0),4,Concrete);
        Floor("LowPlatform_NW",-15,-10,15,18,1.2f,Concrete,1.2f);Ramp("CityLowRamp_NW",new Vector3(-13.5f,0,9),new Vector3(-13.5f,1.2f,15),3,Concrete);
        Floor("LowPlatform_SE",10,15,-18,-15,1.2f,Concrete,1.2f);Ramp("CityLowRamp_SE",new Vector3(13.5f,0,-9),new Vector3(13.5f,1.2f,-15),3,Concrete);
        float[,] cover={{-28,20,7,2},{-7,21,5,2},{21,20,7,2},{-27,4,2,5},{-14,-4,2,5},{25,-9,2,5},{-28,-23,7,2},{3,-22,4,2},{21,-23,7,2},{-21,9,4,2},{17,-13,4,2},{-23,27,7,1},{16,27,7,1},{-23,-28,7,1},{16,-28,7,1},{-28,12,1,6},{27,-18,1,6}};
        for(int i=0;i<cover.GetLength(0);i++){float x=cover[i,0],z=cover[i,1],dx=cover[i,2],dz=cover[i,3],y=i>=11?4:0;Model("CityKit_Industrial",i%3==0?"shipping-container-a":"shipping-container-b",new Vector3(x+dx/2,y,z+dz/2),new Vector3(dx,2.6f,dz),dx<dz?90:0);}
        foreach(int sign in new[]{-1,1})
        {
            for(int i=-2;i<=2;i++)if(i!=0){Model("CityKit_Roads","construction-barrier",new Vector3(i*10,4,sign*33),new Vector3(3,1.1f,.45f),0);Model("CityKit_Roads","construction-barrier",new Vector3(sign*33,4,i*10),new Vector3(.45f,1.1f,3),90);}
            foreach(int z in new[]{-11,11})Model("CityKit_Roads","light-curved",new Vector3(sign*24.8f,0,z),new Vector3(1.2f,5,1.2f),sign>0?90:270,false);
        }
        Model("CityKit_Industrial","detail-tank-large",new Vector3(-7,0,12.5f),new Vector3(3,2.6f,2.5f));
        Model("CityKit_Roads","dumpster",new Vector3(10.8f,0,-4),new Vector3(1.2f,1.2f,2));
        Boundary(false);Anchors(world,false);return world;
    }
    static GameObject Mars()
    {
        var world=Group("StageMap_02_MarsOutpost");world.transform.SetParent(null);parent=world.transform;
        Floor("MarsGround_W",-36,-10,-36,36,0,Sand,3);Floor("MarsGround_E",10,36,-36,36,0,Sand,3);
        Floor("MarsGround_NW",-10,-2,10,36,0,Sand,3);Floor("MarsGround_NE",2,10,10,36,0,Sand,3);Floor("MarsGround_N",-2,2,16,36,0,Sand,3);
        Floor("MarsGround_SW",-10,-2,-36,-8,0,Sand,3);Floor("MarsGround_SE",2,10,-36,-8,0,Sand,3);Floor("MarsGround_S",-2,2,-36,-14,0,Sand,3);
        Floor("ExcavationFloor",-10,10,-8,10,-1.5f,Cliff,2);Ramp("MarsCrater_N",new Vector3(0,0,16),new Vector3(0,-1.5f,10),4,Sand);Ramp("MarsCrater_S",new Vector3(0,0,-14),new Vector3(0,-1.5f,-8),4,Sand);
        Floor("NorthernMesa",-16,32,22,34,3,Cliff,3);
        Ramp("MarsBaseRamp_W",new Vector3(-10,0,10),new Vector3(-10,3,22),4,Sand);Ramp("MarsBaseRamp_E",new Vector3(24,0,10),new Vector3(24,3,22),4,Sand);
        Floor("LandingPad",-10,6,-29,-19,2,Concrete,2);Floor("LandingPadSurface",-9.8f,5.8f,-28.8f,-19.2f,2.02f,White,.04f);
        Ramp("MarsLanding_W",new Vector3(-18,0,-24),new Vector3(-10,2,-24),4,Concrete);Ramp("MarsLanding_E",new Vector3(14,0,-24),new Vector3(6,2,-24),4,Concrete);
        Model("SpaceKit","hangar_largeA",new Vector3(-22,0,9),new Vector3(14,6,12),90);
        Model("SpaceKit","hangar_smallB",new Vector3(20,0,0),new Vector3(14,5,12),270);
        Model("SpaceKit","satelliteDish_large",new Vector3(22.5f,0,-24),new Vector3(7,6,6));
        // Stack the supplied rocket modules at their authored deck elevations.
        string[] rocket={"rocket_baseA","rocket_fuelA","rocket_sidesA","rocket_fuelB","rocket_topA"};float[] rh={1.2f,3.2f,2.5f,2.4f,2.3f};float ry=3;
        for(int i=0;i<rocket.Length;i++){Model("SpaceKit",rocket[i],new Vector3(13,ry,27),new Vector3(i==0?7:3.5f,rh[i],i==0?7:3.5f),0,i==0);ry+=rh[i];}
        var rocketCollider=Box("RocketCoreCollision",new Vector3(13,8,27),new Vector3(3.7f,10,3.7f),White,true,true);rocketCollider.GetComponent<Renderer>().enabled=false;
        Model("SpaceKit","craft_cargoA",new Vector3(-2,2.04f,-24),new Vector3(5.5f,3,4.5f),0);
        float[,] covers={{-31,21,6,2},{-6,18,6,2},{26,19,4,2},{-31,-2,6,2},{-16,-6,4,2},{26,-6,4,2},{-28,-24,5,2},{-7,-12,4,2},{25,-24,5,2},{-5,4,4,3},{3,-5,4,3},{-31,-16,5,3},{6,20,5,2},{-8,22,5,2},{18,22,3,2}};
        for(int i=0;i<covers.GetLength(0);i++){float x=covers[i,0],z=covers[i,1],dx=covers[i,2],dz=covers[i,3];float y=i==9 || i==10?-1.5f:i>=13?3:0;string f=i%3==0?"rock_largeA":i%3==1?"machine_generatorLarge":"rock_largeB";Model("SpaceKit",f,new Vector3(x+dx/2,y,z+dz/2),new Vector3(dx,2.7f,dz),i*37);}
        // Small volcanic details stay outside spawn pockets and all ramp landings.
        foreach(var v in new[]{new Vector3(-33,0,12),new Vector3(-31,0,-31),new Vector3(31,0,-31),new Vector3(33,0,0),new Vector3(5,3,33),new Vector3(-13,3,31),new Vector3(-8,-1.5f,-5),new Vector3(8,-1.5f,7)})Model("SpaceKit","rocks_smallA",v,new Vector3(2,1.2f,1.8f),23,false);
        for(int i=0;i<5;i++)Model("SpaceKit","monorail_trackStraight",new Vector3(-27+i*6,6.8f,30),new Vector3(6,.6f,1.2f),90,false);
        foreach(int x in new[]{-27,-15,-3})Model("SpaceKit","monorail_trackSupport",new Vector3(x,x<-16?0:3,30),new Vector3(1.2f,x<-16?6.8f:3.8f,1.2f),0,x<-16);
        Model("SpaceKit","rover",new Vector3(-20,0,-30),new Vector3(3.5f,2,3),40);
        Boundary(true);Anchors(world,true);return world;
    }
    static void Bake(GameObject root)
    {
        var surface=root.GetComponent<NavMeshSurface>()??root.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Children;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.agentTypeID=0;surface.overrideVoxelSize=true;surface.voxelSize=root.name=="StageMap_01_IndustrialRing"?.05f:.10f;surface.overrideTileSize=true;surface.tileSize=128;
        Physics.SyncTransforms();surface.BuildNavMesh();if(surface.navMeshData==null)throw new Exception("No NavMesh for "+root.name);
        string path=Generated+root.name+"_NavMesh.asset";
        var data=surface.navMeshData;var previous=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
        if(previous!=null){EditorUtility.CopySerialized(data,previous);surface.RemoveData();surface.navMeshData=previous;surface.AddData();}else AssetDatabase.CreateAsset(data,path);
    }
    public static object FixEastExit()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
        scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        bool wasDirty=scene.isDirty;
        if(wasDirty)
        {
            Directory.CreateDirectory("AgentBackups/StageMaps_2026-10-16");
            if(!EditorSceneManager.SaveScene(scene,"AgentBackups/StageMaps_2026-10-16/GameScene.unsaved-before-exit-fix.unity",true))throw new Exception("Could not preserve unsaved scene snapshot");
        }
        var director=scene.GetRootGameObjects().Select(g=>g.GetComponent<StageMapDirector>()).FirstOrDefault(d=>d!=null);
        if(director==null)throw new Exception("Map director missing");
        director.ApplyMap(2);var mars=director.Definitions[2].World;
        var rock=mars.GetComponentsInChildren<BoxCollider>(true).Single(c=>Mathf.Abs(c.bounds.center.x-21.5f)<.1f && Mathf.Abs(c.bounds.center.z-23)<.1f && c.name.StartsWith("rock_largeB"));
        var t=rock.transform;t.localScale=new Vector3(t.localScale.x*.6f,t.localScale.y,t.localScale.z);Physics.SyncTransforms();t.position+=Vector3.right*(19.5f-rock.bounds.center.x);
        Bake(mars);director.ApplyMap(0);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new Exception("Scene save failed");
        return new {exitCleared=true,preexistingChangesPreserved=wasDirty,snapshot=wasDirty?"AgentBackups/StageMaps_2026-10-16/GameScene.unsaved-before-exit-fix.unity":null};
    }
    public static object Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
        var active=SceneManager.GetActiveScene();scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.isDirty)throw new Exception("GameScene has unsaved edits; refusing to overwrite");
        Directory.CreateDirectory("AgentBackups/StageMaps_2026-10-16");
        string backup="AgentBackups/StageMaps_2026-10-16/GameScene.before.unity";if(!File.Exists(backup))File.Copy(ScenePath,backup);
        Directory.CreateDirectory(Generated);Directory.CreateDirectory(Report);AssetDatabase.Refresh();SceneManager.SetActiveScene(scene);
        var original=scene.GetRootGameObjects().Single(g=>g.name=="CityWorld_72x72_PrototypeLayout");
        var registry=scene.GetRootGameObjects().Single(g=>g.name=="GameplayAnchors").GetComponent<SpawnPointRegistry>();
        var services=scene.GetRootGameObjects().Single(g=>g.name=="GameplayServices");
        foreach(var old in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("StageMap_0")).ToArray())Object.DestroyImmediate(old);
        parent=null;original.SetActive(false);var city=City();city.SetActive(false);parent=null;var mars=Mars();mars.SetActive(false);
        city.SetActive(true);Bake(city);city.SetActive(false);mars.SetActive(true);Bake(mars);mars.SetActive(false);
        var director=services.GetComponent<StageMapDirector>()??services.AddComponent<StageMapDirector>();
        director.Configure(new[]{new StageMapDefinition{Name="기존 도시",World=original,Anchors=registry.transform},new StageMapDefinition{Name="도시 순환도로",World=city,Anchors=city.transform.Find("MapAnchors")},new StageMapDefinition{Name="화성 전초기지",World=mars,Anchors=mars.transform.Find("MapAnchors"),Ambient=new Color(.83f,.67f,.55f)}},registry);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");
        var result=new {maps=director.Definitions.Select(m=>new {m.Name,models=m.World.GetComponentsInChildren<MeshRenderer>(true).Length,spawns=m.Anchors.GetComponentsInChildren<WorldAnchor>(true).Count(a=>a.Kind==WorldAnchor.AnchorKind.PlayerSpawn)}).ToArray(),scene=ScenePath,existingWorldPreserved=true};
        File.WriteAllText(Report+"Authoring.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        if(opened)EditorSceneManager.CloseScene(scene,true);if(active.IsValid() && active.isLoaded)SceneManager.SetActiveScene(active);
        return result;
    }
}


