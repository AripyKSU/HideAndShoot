using TMPro;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace HideAndShoot.EditorTools
{
    /// <summary>
    /// One-time, nondestructive setup for the approved TWO-scene layout.
    /// Runs after the first asset import; never overwrites existing authored scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class InitialSceneBuilder
    {
        private const string Root = "Assets/HideAndShoot";
        private const string SceneDir = Root + "/Scenes";
        private const string MenuPath = SceneDir + "/MenuLobbyScene.unity";
        private const string GamePath = SceneDir + "/GameScene.unity";
        private const string Kenney = Root + "/ThirdParty/Kenney/";

        private static bool busy;
        private static TMP_FontAsset uiFont;

        static InitialSceneBuilder()
        {
            EditorApplication.update += TryBuildOnFirstImport;
        }

        private static void TryBuildOnFirstImport()
        {
            if (busy || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (File.Exists(MenuPath) && File.Exists(GamePath))
            {
                EditorApplication.update -= TryBuildOnFirstImport;
                EnsureBuildSettings();
                return;
            }

            // Wait until FBX import finishes, rather than generating placeholders
            // that would later need replacing.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                Kenney + "CityKit_Commercial/Models/building-a.fbx") == null)
                return;

            EditorApplication.update -= TryBuildOnFirstImport;
            busy = true;
            try { BuildMissingScenes(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[HideAndShoot] Initial scene creation failed. Use menu Tools/HideAndShoot/Rebuild Missing Scenes once imports are done.");
            }
            finally { busy = false; }
        }

        [MenuItem("Tools/HideAndShoot/Rebuild Missing Scenes")]
        public static void BuildMissingScenes()
        {
            Directory.CreateDirectory(SceneDir);
            var previouslyActive = SceneManager.GetActiveScene();

            if (!File.Exists(GamePath))
            {
                var game = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(game);
                game.name = "GameScene";
                BuildGameScene();
                EditorSceneManager.SaveScene(game, GamePath);
                EditorSceneManager.CloseScene(game, true);
            }

            if (!File.Exists(MenuPath))
            {
                var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(menu);
                menu.name = "MenuLobbyScene";
                BuildMenuScene();
                EditorSceneManager.SaveScene(menu, MenuPath);
                EditorSceneManager.CloseScene(menu, true);
            }

            if (previouslyActive.IsValid() && previouslyActive.isLoaded)
                SceneManager.SetActiveScene(previouslyActive);
            EnsureBuildSettings();
            AssetDatabase.Refresh();
            Debug.Log("[HideAndShoot] MenuLobbyScene + GameScene ready in Build Settings. Existing scenes were not overwritten.");
        }

        private static void EnsureBuildSettings()
        {
            var desired = new[]
            {
                new EditorBuildSettingsScene(MenuPath, true),
                new EditorBuildSettingsScene(GamePath, true)
            };
            var current = EditorBuildSettings.scenes;
            if (current.Length != 2 || current[0].path != MenuPath ||
                current[1].path != GamePath || !current[0].enabled || !current[1].enabled)
                EditorBuildSettings.scenes = desired;
        }

        /// <summary>
        /// One-time editor authoring pass for the existing GameScene. These are
        /// fixed, precomputed inward-facing yaw angles, never calculated at runtime.
        /// </summary>
        [MenuItem("Tools/HideAndShoot/Set Inward Spawn Facing")]
        public static void SetInwardSpawnFacing()
        {
            Scene original=SceneManager.GetActiveScene();
            Scene scene=SceneManager.GetSceneByPath(GamePath);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(GamePath,OpenSceneMode.Additive);
            try
            {
                var anchorRoot=scene.GetRootGameObjects()
                    .FirstOrDefault(root=>root.name=="GameplayAnchors");
                if(anchorRoot==null)
                    throw new InvalidOperationException("GameplayAnchors missing from GameScene");

                bool changed=ApplySpawnFacing(anchorRoot);
                if(changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Debug.Log("[HideAndShoot/Spawn] Nine inward-facing spawn rotations saved in GameScene (changed="+changed+").");
            }
            finally
            {
                if(opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene,true);
                if(original.IsValid() && original.isLoaded)
                    SceneManager.SetActiveScene(original);
            }
        }

        /// <summary>Author nine fixed spawn rotations into a scene in edit mode.</summary>
        internal static bool ApplySpawnFacing(GameObject anchorRoot)
        {
            var anchors=anchorRoot.GetComponentsInChildren<WorldAnchor>(true)
                .Where(anchor=>anchor.Kind==WorldAnchor.AnchorKind.PlayerSpawn).ToArray();
            if(anchors.Length!=9 || anchors.Select(anchor=>anchor.Index).Distinct().Count()!=9 ||
               anchors.Any(anchor=>anchor.Index<0 || anchor.Index>8))
                throw new InvalidOperationException("Expected nine uniquely indexed player spawn anchors");

            // NW, N, NE / W, center, E / SW, S, SE.
            float[] yaw={135f,180f,225f,90f,0f,270f,45f,0f,315f};
            bool changed=false;
            foreach(var anchor in anchors)
            {
                var rotation=Quaternion.Euler(0f,yaw[anchor.Index],0f);
                if(Quaternion.Angle(anchor.transform.localRotation,rotation)<0.01f)continue;
                anchor.transform.localRotation=rotation;
                changed=true;
            }
            return changed;
        }

        private static void BuildGameScene()
        {
            CreateLighting(new Vector3(50, -40, 0));
            var root = new GameObject("CityWorld_72x72_PrototypeLayout");
            CreateTown(root.transform, true);

            var anchorRoot = new GameObject("GameplayAnchors");
            var spawns = new[]
            {
                new Vector3(-24, 0.20f, 24), new Vector3(0, 0.20f, 24), new Vector3(24, 0.20f, 24),
                new Vector3(-24, 0.20f, 0), new Vector3(0, 0.20f, 0), new Vector3(24, 0.20f, 0),
                new Vector3(-24, 0.20f, -24), new Vector3(0, 0.20f, -24), new Vector3(24, 0.20f, -24)
            };
            string[] ids = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE" };
            float[] facingYaws = { 135f, 180f, 225f, 90f, 0f, 270f, 45f, 0f, 315f };
            for (int i = 0; i < spawns.Length; i++)
                MakeAnchor(anchorRoot.transform, "PSpawn_" + ids[i], spawns[i],
                    WorldAnchor.AnchorKind.PlayerSpawn, i, facingYaws[i]);

            // Candidate locations only. Host spawns zero at the start of each active round;
            // one attempt every five seconds, active cap = round-start player count.
            var pickups = new[]
            {
                new Vector2(-28,-7), new Vector2(-18,-10), new Vector2(-8,-28), new Vector2(0,-18),
                new Vector2(8,-27), new Vector2(24,-5), new Vector2(6,29), new Vector2(-7,30),
                new Vector2(-28,7), new Vector2(-18,8), new Vector2(-8,18), new Vector2(4,13),
                new Vector2(26,10), new Vector2(8,-12), new Vector2(-9,-15), new Vector2(29,27)
            };
            for (int i = 0; i < pickups.Length; i++)
                MakeAnchor(anchorRoot.transform, "AmmoCandidate_" + (i+1).ToString("00"),
                    new Vector3(pickups[i].x, 0.22f, pickups[i].y),
                    WorldAnchor.AnchorKind.AmmoCandidate, i);

            var cameraObj = new GameObject("SceneOverviewCamera", typeof(Camera), typeof(AudioListener));
            cameraObj.transform.position = new Vector3(0, 69, -67);
            cameraObj.transform.LookAt(new Vector3(0, 0, 0));
            cameraObj.GetComponent<Camera>().fieldOfView = 55f;
            cameraObj.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
            cameraObj.tag = "MainCamera";

            // No hard-coded GameHUDCanvas: GameHUDPresenter owns the HUD at runtime.
            TryAddNavMeshSurface(root);
        }

        private static void BuildMenuScene()
        {
            CreateLighting(new Vector3(45, -35, 10));
            var decor = new GameObject("MenuAtmosphere_KenneyCity");
            CreateBox(decor.transform, "MenuFloor", new Vector3(0,-0.25f,0),
                new Vector3(54,0.5f,44),new Color(0.39f,0.46f,0.49f), true);

            CreateBuilding(decor.transform, "MenuCommercial", "CityKit_Commercial",
                "building-a.fbx", new Vector3(-14,0,3), 9f);
            CreateBuilding(decor.transform, "MenuSuburban", "CityKit_Suburban",
                "building-type-b.fbx", new Vector3(14,0,0), 9f);
            CreateBuilding(decor.transform, "MenuCityShops", "CityKit_Commercial",
                "building-d.fbx", new Vector3(0,0,15), 10f);
            CreateKenneyObject(decor.transform, "MenuTreeLeft", "NatureKit", "tree_default.fbx",
                new Vector3(-5,0,-5), 5f);
            CreateKenneyObject(decor.transform, "MenuTreeRight", "NatureKit", "tree_cone.fbx",
                new Vector3(7,0,-7), 4f);

            var camera = new GameObject("MenuBackgroundCamera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 17f, -34f);
            camera.transform.LookAt(new Vector3(0, 5, 4));
            camera.GetComponent<Camera>().fieldOfView = 55f;

            CreateMenuCanvas();
        }

        private static void CreateTown(Transform parent, bool withRooftops)
        {
            CreateBox(parent,"MapGround",new Vector3(0,-.30f,0),
                new Vector3(72,.60f,72),new Color(.42f,.53f,.47f),true);

            // A simple readable four-quadrant town, NOT dense 96m version.
            CreateBox(parent,"Road_NS",new Vector3(0,.025f,0),
                new Vector3(6,.05f,72),new Color(.27f,.32f,.35f),false);
            CreateBox(parent,"Road_EW",new Vector3(0,.028f,0),
                new Vector3(72,.05f,6),new Color(.27f,.32f,.35f),false);
            CreateBox(parent,"CentralPark",new Vector3(0,.055f,0),
                new Vector3(18,.11f,18),new Color(.35f,.58f,.35f),false);

            // Eight Kenney commercial/suburban buildings; two additional low
            // roof-accessible structures below = about ten structures total.
            var buildings = new (string, string, string, float, float, float)[]
            {
                ("NW_ShopA","CityKit_Commercial","building-a.fbx",-15,27,8),
                ("NW_ShopB","CityKit_Commercial","building-b.fbx",-29,15,7),
                ("NE_StoreA","CityKit_Commercial","building-c.fbx",15,27,8),
                ("NE_StoreB","CityKit_Commercial","building-d.fbx",29,15,7),
                ("SW_HouseA","CityKit_Suburban","building-type-a.fbx",-15,-27,8),
                ("SW_HouseB","CityKit_Suburban","building-type-b.fbx",-29,-15,7),
                ("SE_WarehouseA","CityKit_Suburban","building-type-c.fbx",15,-27,8),
                ("SE_WarehouseB","CityKit_Suburban","building-type-d.fbx",29,-15,7)
            };
            foreach (var b in buildings)
                CreateBuilding(parent,b.Item1,b.Item2,b.Item3,
                    new Vector3(b.Item4,0,b.Item5),b.Item6);

            if (withRooftops)
            {
                MakeRooftop(parent, "NorthWestRoof_A", new Vector3(-15, 0, 15), false);
                MakeRooftop(parent, "SouthEastRoof_B", new Vector3(16, 0,-15), true);
            }

            var treePoints = new [] { new Vector3(-7,0,-6),new Vector3(-6,0,5),
                new Vector3(6,0,6),new Vector3(6,0,-5),
                new Vector3(-32,0,-32),new Vector3(32,0,31),
                new Vector3(-32,0,30),new Vector3(32,0,-31)};
            for (int i = 0; i < treePoints.Length; i++)
                CreateKenneyObject(parent, "KenneyTree_" + i, "NatureKit",
                    i % 2 == 0 ? "tree_default.fbx" : "tree_cone.fbx",treePoints[i],3.5f);

            // Add restrained street greenery while preserving 2-8 player sightlines.
            var addedTrees = new[]
            {
                new Vector3(-31,0,5), new Vector3(-24,0,30),
                new Vector3(-8,0,31), new Vector3(8,0,32),
                new Vector3(32,0,7), new Vector3(31,0,-8),
                new Vector3(8,0,-32), new Vector3(-8,0,-31),
                new Vector3(-31,0,-5), new Vector3(-24,0,-30),
                new Vector3(24,0,31), new Vector3(24,0,-31)
            };
            for (int i = 0; i < addedTrees.Length; i++)
                CreateKenneyObject(parent, "StreetTree_" + (i+1).ToString("00"), "NatureKit",
                    i % 2 == 0 ? "tree_default.fbx" : "tree_cone.fbx", addedTrees[i], 3.15f);

            var bushes = new[]
            {
                new Vector3(-7,0,8),new Vector3(7,0,8),
                new Vector3(-7,0,-8),new Vector3(7,0,-8),
                new Vector3(30,0,0),new Vector3(-30,0,0)
            };
            for (int i = 0; i < bushes.Length; i++)
                CreateKenneyObject(parent, "ParkShrub_" + (i+1).ToString("00"), "NatureKit",
                    "plant_bushSmall.fbx", bushes[i], 1.25f);

            // Boundaries: an NPC and ground-based spectator cannot leave the map.
            CreateBox(parent,"BoundaryNorth",new Vector3(0,1.2f,36),
                new Vector3(72,2.4f,.35f),new Color(.32f,.36f,.34f),true);
            CreateBox(parent,"BoundarySouth",new Vector3(0,1.2f,-36),
                new Vector3(72,2.4f,.35f),new Color(.32f,.36f,.34f),true);
            CreateBox(parent,"BoundaryWest",new Vector3(-36,1.2f,0),
                new Vector3(.35f,2.4f,72),new Color(.32f,.36f,.34f),true);
            CreateBox(parent,"BoundaryEast",new Vector3(36,1.2f,0),
                new Vector3(.35f,2.4f,72),new Color(.32f,.36f,.34f),true);
        }

        private static void MakeRooftop(Transform root,string name,Vector3 pos,bool reverse)
        {
            var site = new GameObject(name);
            site.transform.SetParent(root,false);
            site.transform.localPosition = pos;
            CreateBox(site.transform,"GroundFloorBuilding",new Vector3(0,1.55f,0),
                new Vector3(7,3.10f,7),new Color(.55f,.52f,.45f),true);
            CreateBox(site.transform,"WalkableRoof_Y3.2",new Vector3(0,3.35f,0),
                new Vector3(8,.25f,8),new Color(.32f,.40f,.42f),true);
            // Keep access on the clear side: NW roof from south, SE roof from north.
            // A roughly 24.6 degree ramp meets the 3.475m roof top with no large lip.
            var ramp = CreateBox(site.transform,"WalkableRamp",
                new Vector3(0f, 1.70f, reverse ? 7.5f : -7.5f),
                new Vector3(2.5f, .28f, 7.75f),
                new Color(.55f,.47f,.40f),true);
            ramp.transform.localRotation = Quaternion.Euler(reverse ? 24.6f : -24.6f, 0, 0);
        }

        private static void CreateBuilding(Transform parent,string name,string pack,string file,Vector3 at,float width)
        {
            CreateKenneyObject(parent,name,pack,file,at,width);
        }

        private static void CreateKenneyObject(Transform parent,string name,string pack,string file,Vector3 at,float targetWidth)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent,false);
            group.transform.localPosition = at;

            string path=Kenney+pack+"/Models/"+file;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning("[HideAndShoot] Model not imported: "+path);
                return;
            }
            var instance = PrefabUtility.InstantiatePrefab(model,group.transform) as GameObject;
            if (instance == null) return;
            instance.name = file.Replace(".fbx","");
            instance.transform.localPosition=Vector3.zero;
            instance.transform.localRotation=Quaternion.identity;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length==0) return;

            Bounds b=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++) b.Encapsulate(renderers[i].bounds);
            float span=Mathf.Max(b.size.x,b.size.z,.01f);
            float scale=Mathf.Clamp(targetWidth/span,.05f,100f);
            instance.transform.localScale *= scale;
            b=instance.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>()) b.Encapsulate(renderer.bounds);
            instance.transform.position += Vector3.up * (-b.min.y+0.08f);
            b = instance.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>()) b.Encapsulate(renderer.bounds);

            // Only buildings need a bounding collision; trees are decorative
            // and do not break the NPC Navigation mesh.
            if (!name.Contains("Tree"))
            {
                var collision = group.AddComponent<BoxCollider>();
                collision.center = group.transform.InverseTransformPoint(b.center);
                collision.size = b.size;
            }
        }

        private static void MakeAnchor(Transform parent,string name,Vector3 localPos,
            WorldAnchor.AnchorKind kind,int index,float yaw=0f)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent,false);
            anchor.transform.localPosition=localPos;
            anchor.transform.localRotation=Quaternion.Euler(0f,yaw,0f);
            var component=anchor.AddComponent<WorldAnchor>();
            component.Kind=kind;
            component.Index=index;
        }

        private static GameObject CreateBox(Transform parent,string name,Vector3 localPos,
            Vector3 size,Color color,bool keepCollider)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name=name;
            cube.transform.SetParent(parent,false);
            cube.transform.localPosition=localPos;
            cube.transform.localScale=size;
            cube.isStatic=true;
            if (!keepCollider)
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            var renderer=cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=CreateColorMaterial(color,name);
            return cube;
        }

        private static Material CreateColorMaterial(Color color,string id)
        {
            string safe = id.Replace(' ','_');
            string folder=Root+"/Materials/Generated";
            Directory.CreateDirectory(folder);
            string path=folder+"/"+safe+"_"+ColorUtility.ToHtmlStringRGB(color)+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null)shader=Shader.Find("Standard");
            var material=new Material(shader){name=safe};
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",color);
            if(material.HasProperty("_Color"))material.SetColor("_Color",color);
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",0.03f);
            if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.25f);
            AssetDatabase.CreateAsset(material,path);
            return material;
        }

        private static void CreateLighting(Vector3 euler)
        {
            var sun=new GameObject("Sun_Directional",typeof(Light));
            sun.transform.rotation=Quaternion.Euler(euler);
            var light=sun.GetComponent<Light>();
            light.type=LightType.Directional;
            light.intensity=1.25f;
            light.color=new Color(1f,.96f,.86f);
            RenderSettings.ambientLight=new Color(.62f,.66f,.7f);
        }

        private static void TryAddNavMeshSurface(GameObject world)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies()
                .Select(a=>a.GetType("Unity.AI.Navigation.NavMeshSurface",false))
                .FirstOrDefault(t=>t!=null);
            if (type == null)
            {
                Debug.LogWarning("[HideAndShoot] AI Navigation assembly unavailable; add NavMeshSurface when package finishes resolving.");
                return;
            }
            world.AddComponent(type);
            Debug.Log("[HideAndShoot] NavMeshSurface attached. Bake and check ramps/rooftops before AI integration.");
        }

        private static TMP_FontAsset GetUIFont()
        {
            if (uiFont == null)
                uiFont = HideAndShoot.UI.TacticalUI.Font;
            return uiFont;
        }

        private static GameObject UIObject(string name,Transform parent,Vector2 anchor,Vector2 size,Vector2 pos)
        {
            var go=new GameObject(name,typeof(RectTransform));
            go.transform.SetParent(parent,false);
            var rt=(RectTransform)go.transform;
            rt.anchorMin=anchor;rt.anchorMax=anchor;
            rt.sizeDelta=size;rt.anchoredPosition=pos;
            return go;
        }

        private static TextMeshProUGUI UIText(string name,Transform parent,string str,int fontSize,Vector2 anchor,Vector2 size,Vector2 pos,
            TextAnchor align=TextAnchor.MiddleLeft)
        {
            var go=UIObject(name,parent,anchor,size,pos);
            var text=go.AddComponent<TextMeshProUGUI>();
            text.font=GetUIFont();text.fontSize=fontSize;
            text.alignment=HideAndShoot.UI.TacticalUI.Align(align);text.color=Color.white;
            text.text=str;text.enableWordWrapping=true;
            return text;
        }

        private static Image UIPanel(string name,Transform parent,Vector2 anchor,Vector2 size,Vector2 pos,
            Color background)
        {
            var go=UIObject(name,parent,anchor,size,pos);
            var image=go.AddComponent<Image>();
            image.color=background;return image;
        }

        private static Button UIButton(string name,Transform parent,string label,Vector2 pos,int width=325)
        {
            var image=UIPanel(name,parent,new Vector2(.5f,.5f),new Vector2(width,56),pos,
                new Color(.17f,.23f,.26f,.96f));
            var button=image.gameObject.AddComponent<Button>();
            var text=UIText("Text",image.transform,label,23,
                new Vector2(.5f,.5f),new Vector2(width-10,46),Vector2.zero,TextAnchor.MiddleCenter);
            text.raycastTarget=false;
            return button;
        }

        private static TMP_InputField UIInput(string name,Transform parent,string placeholder,Vector2 pos)
        {
            var panel=UIPanel(name,parent,new Vector2(.5f,.5f),new Vector2(332,55),pos,
                new Color(.88f,.9f,.91f,.95f));
            var input=panel.gameObject.AddComponent<TMP_InputField>();
            input.lineType=TMP_InputField.LineType.SingleLine;
            var viewport=UIObject("TMPViewport",panel.transform,new Vector2(.5f,.5f),
                new Vector2(300,50),Vector2.zero).GetComponent<RectTransform>();
            viewport.gameObject.AddComponent<RectMask2D>();
            var text=UIText("ValueText",viewport,"",23,
                new Vector2(.5f,.5f),new Vector2(300,50),Vector2.zero);
            text.color=new Color(.07f,.12f,.15f,1f);
            var ph=UIText("Placeholder",viewport,placeholder,22,
                new Vector2(.5f,.5f),new Vector2(300,50),Vector2.zero);
            ph.color=new Color(.50f,.54f,.56f,1f);
            input.textViewport=viewport;input.textComponent=text;input.placeholder=ph;
            return input;
        }

        private static Canvas CreateCanvas(string name)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),
                typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=go.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);
            scaler.matchWidthOrHeight=.5f;
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>()==null)
            {
                var e=new GameObject("EventSystem",typeof(EventSystem));
                var inputType=AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a=>a.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule",false))
                    .FirstOrDefault(t=>t!=null);
                if (inputType!=null) e.AddComponent(inputType);
                else e.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        private static void CreateMenuCanvas()
        {
            var canvas=CreateCanvas("MenuLobbyCanvas");
            var left=UIPanel("RulesPanel",canvas.transform,
                new Vector2(0,.5f),new Vector2(470,590),new Vector2(265,0),
                new Color(.06f,.10f,.13f,.86f));
            UIText("RulesTitle",left.transform,"조작 방법",32,
                new Vector2(.5f,1),new Vector2(414,65),new Vector2(0,-60),TextAnchor.MiddleCenter);
            var rules = UIText("RulesBody",left.transform,
                "WASD   이동\nShift   달리기\n오른쪽 클릭   조준\n왼쪽 클릭   사격\n\n" +
                "NPC 사이에 숨은 상대를 찾아\n마지막까지 살아남으세요.\n\n3승을 먼저 거두면 우승!",
                23,new Vector2(.5f,.5f),new Vector2(415,420),new Vector2(0,-3),TextAnchor.MiddleLeft);
            rules.lineSpacing = 1.35f;

            UIText("GameTitle",canvas.transform,"HIDE AND SHOOT",74,
                new Vector2(.5f,1),new Vector2(890,120),new Vector2(0,-90),TextAnchor.MiddleCenter);

            var right=UIPanel("MenuControlsPanel",canvas.transform,
                new Vector2(1,.5f),new Vector2(455,590),new Vector2(-255,0),
                new Color(.06f,.10f,.13f,.88f));
            UIText("NicknameLabel",right.transform,"닉네임",27,
                new Vector2(.5f,.5f),new Vector2(330,48),new Vector2(0,195),TextAnchor.MiddleLeft);
            UIInput("NicknameInput",right.transform,"닉네임 입력",new Vector2(0,142));
            var divider = UIPanel("NicknameDivider",right.transform,
                new Vector2(.5f,.5f),new Vector2(332,2),new Vector2(0,98),
                new Color(.60f,.67f,.68f,1f));
            divider.raycastTarget = false;
            UIButton("CreateRoomButton",right.transform,"방 만들기",new Vector2(0,45));
            UIText("RoomCodeLabel",right.transform,"방 코드로 참가",26,
                new Vector2(.5f,.5f),new Vector2(330,50),new Vector2(0,-35),TextAnchor.MiddleLeft);
            UIInput("RoomCodeInput",right.transform,"방 코드",new Vector2(0,-89));
            UIButton("JoinRoomButton",right.transform,"참가",new Vector2(0,-175));

            var overlay=UIPanel("RoomOverlay",right.transform,
                new Vector2(.5f,.5f),new Vector2(450,588),new Vector2(0,0),
                new Color(.08f,.13f,.15f,1f));
            UIText("PeopleAndCode",overlay.transform,"현재 인원 1 / 8    방 코드: ------",23,
                new Vector2(.5f,.5f),new Vector2(395,60),new Vector2(-15,200));
            UIButton("CopyCodeButton",overlay.transform,"복사",new Vector2(140,142),100);
            UIText("ConnectedPlayers",overlay.transform,
                "참가자 목록 / Ping\n\n방장                  -- ms\n\n",
                23,new Vector2(.5f,.5f),new Vector2(395,260),new Vector2(0,-15));
            var play=UIButton("HostPlayButton",overlay.transform,"PLAY",new Vector2(0,-222),330);
            play.interactable=false;
            overlay.gameObject.SetActive(false);
            UIText("MenuStatusText",right.transform,"",17,
                new Vector2(.5f,0),new Vector2(430,40),new Vector2(0,18),TextAnchor.MiddleCenter);
        }

        private static void CreateGameHud()
        {
            var canvas=CreateCanvas("GameHUDCanvas");
            UIText("RoomRoundInfo",canvas.transform,"CODE: ------  |  ROUND 1",23,
                new Vector2(0,1),new Vector2(430,55),new Vector2(255,-45));
            UIText("AliveCount",canvas.transform,"2명 생존 중!",33,
                new Vector2(.5f,1),new Vector2(450,65),new Vector2(0,-50),TextAnchor.MiddleCenter);
            UIText("RedoVoteProgress",canvas.transform,"재시작 투표 0/2",24,
                new Vector2(1,1),new Vector2(370,60),new Vector2(-220,-45),TextAnchor.MiddleCenter);
            UIText("Scoreboard",canvas.transform,"참가자 / 점수\n(라운드 서버 상태 연결 예정)",20,
                new Vector2(0,1),new Vector2(400,210),new Vector2(235,-170));
            UIText("AmmoCount",canvas.transform,"탄약 5 / 10",32,
                new Vector2(1,0),new Vector2(300,65),new Vector2(-195,65),TextAnchor.MiddleRight);
            var crosshair=UIText("ADSCrosshair",canvas.transform,"+",43,
                new Vector2(.5f,.5f),new Vector2(70,70),Vector2.zero,TextAnchor.MiddleCenter);
            crosshair.gameObject.SetActive(false); // only while fully aimed
            var dead=UIText("SpectatorBanner",canvas.transform,"사망! 관전 중",33,
                new Vector2(.5f,1),new Vector2(480,68),new Vector2(0,-120),TextAnchor.MiddleCenter);
            dead.gameObject.SetActive(false);
            var winner=UIText("WinnerBanner",canvas.transform,"WIN!",64,
                new Vector2(.5f,.5f),new Vector2(840,115),new Vector2(0,150),TextAnchor.MiddleCenter);
            winner.gameObject.SetActive(false);
        }
    }
}
