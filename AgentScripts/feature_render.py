exec(open('C:/UnityProject/HideAndShoot/AgentScripts/feature_core.py',encoding='utf-8').read().split("write('Assets/")[0])
write('Assets/HideAndShoot/Scripts/Player/PlayerHighlightTarget.cs', '''using System.Collections.Generic;
using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.UI;
using HideAndShoot.World;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace HideAndShoot.Player
{
    /// <summary>Local presentation only. Server-owned reveal state stays on HostPlayerLife.</summary>
    public sealed class PlayerHighlightTarget : MonoBehaviour
    {
        public readonly struct DrawPart
        {
            public readonly Renderer Renderer;
            public readonly int Submeshes;
            public DrawPart(Renderer renderer,int submeshes){Renderer=renderer;Submeshes=submeshes;}
        }
        private static readonly List<PlayerHighlightTarget> targets=new List<PlayerHighlightTarget>(8);
        private static readonly RaycastHit[] rayHits=new RaycastHit[32];
        private readonly List<DrawPart> parts=new List<DrawPart>();
        private HostPlayerLife life;
        private PistolCombatController pistol;
        private Transform head;
        private TextMeshPro nameLabel;
        private Material nameMaterial;
#if UNITY_EDITOR
        // Used only by deterministic edit-mode render checks; never serialized into a build.
        public static Camera PreviewCamera;
        public bool PreviewTarget;
#endif
        private void Awake()
        {
            life=GetComponent<HostPlayerLife>();pistol=GetComponent<PistolCombatController>();
            var body=transform.Find("WorldVisual");
            if(body==null)return;
            var animator=body.GetComponentInChildren<Animator>(true);
            if(animator!=null && animator.isHuman)head=animator.GetBoneTransform(HumanBodyBones.Head);
            foreach(var renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:
                    renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh!=null)parts.Add(new DrawPart(renderer,mesh.subMeshCount));
            }
        }
        private void OnEnable()
        {
            if(targets.Count==0)RenderPipelineManager.beginCameraRendering+=PrepareNames;
            targets.Add(this);
        }
        private void OnDisable()
        {
            targets.Remove(this);
            if(targets.Count==0)RenderPipelineManager.beginCameraRendering-=PrepareNames;
            if(nameLabel!=null)nameLabel.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            if(nameLabel!=null)Destroy(nameLabel.gameObject);
            if(nameMaterial!=null)Destroy(nameMaterial);
        }
        public static bool IsSpectator(Camera camera)
        {
#if UNITY_EDITOR
            if(camera!=null && camera==PreviewCamera)return true;
#endif
            var session=RelayLobbySession.Instance;
            var net=NetworkManager.Singleton;
            if(camera==null || camera.cameraType!=CameraType.Game || net==null || !net.IsClient ||
                session==null || !session.IsMatchInProgress ||
                (session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Active &&
                 session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Result))return false;
            foreach(var target in targets)
                if(target.life!=null && target.life.IsSpawned && target.life.IsOwner)
                    return target.life.IsDead.Value;
            return true; // a late joiner has a spectator camera without a player avatar
        }
        public static void Collect(Camera camera,List<DrawPart> destination)
        {
            destination.Clear();
            bool spectator=IsSpectator(camera);
            var session=RelayLobbySession.Instance;
            foreach(var target in targets)
            {
                bool draw=false;
#if UNITY_EDITOR
                draw=camera==PreviewCamera && target.PreviewTarget;
#endif
                if(!draw)
                {
                    if(session==null || (session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Active &&
                        session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Result))continue;
                    if(target.life==null || !target.life.IsSpawned || target.life.IsDead.Value || target.life.IsOwner ||
                        target.pistol==null || target.pistol.RoundId.Value!=session.MatchRoundId)continue;
                    draw=spectator || target.life.RevealSecondsRemaining>0d;
                }
                if(!draw)continue;
                foreach(var part in target.parts)
                    if(part.Renderer!=null && part.Renderer.enabled && part.Renderer.gameObject.activeInHierarchy &&
                        (camera.cullingMask&(1<<part.Renderer.gameObject.layer))!=0)
                        destination.Add(part);
            }
        }
        private static void PrepareNames(ScriptableRenderContext context,Camera camera)
        {
            bool spectator=IsSpectator(camera);
            foreach(var target in targets)target.UpdateName(camera,spectator);
        }
        private void UpdateName(Camera camera,bool spectator)
        {
            bool visible=spectator && life!=null && life.IsSpawned && !life.IsDead.Value && !life.IsOwner &&
                pistol!=null && RelayLobbySession.Instance!=null && pistol.RoundId.Value==RelayLobbySession.Instance.MatchRoundId;
            Vector3 headPosition=head!=null?head.position:transform.position+Vector3.up*1.72f;
            Vector3 viewport=camera.WorldToViewportPoint(headPosition);
            visible=visible && viewport.z>camera.nearClipPlane && viewport.x>=0 && viewport.x<=1 && viewport.y>=0 && viewport.y<=1;
            if(visible)
            {
                Vector3 ray=headPosition-camera.transform.position;
                int count=Physics.RaycastNonAlloc(camera.transform.position,ray.normalized,rayHits,ray.magnitude,~(1<<2),QueryTriggerInteraction.Ignore);
                if(count==rayHits.Length)visible=false;
                for(int i=0;i<count && visible;i++)
                    if(rayHits[i].collider!=null && !rayHits[i].collider.transform.IsChildOf(transform))visible=false;
            }
            if(!visible){if(nameLabel!=null)nameLabel.gameObject.SetActive(false);return;}
            if(nameLabel==null)
            {
                var go=new GameObject("SpectatorNickname",typeof(TextMeshPro));
                go.transform.SetParent(transform,false);
                nameLabel=go.GetComponent<TextMeshPro>();nameLabel.font=TacticalUI.Font;
                nameLabel.alignment=TextAlignmentOptions.Center;nameLabel.richText=false;
                nameLabel.fontSize=2.4f;nameLabel.enableAutoSizing=true;nameLabel.fontSizeMin=1.2f;nameLabel.fontSizeMax=2.4f;
                nameLabel.rectTransform.sizeDelta=new Vector2(3.6f,.45f);
                nameLabel.textWrappingMode=TextWrappingModes.NoWrap;nameLabel.overflowMode=TextOverflowModes.Truncate;
                nameLabel.color=TacticalUI.White;
                nameMaterial=new Material(nameLabel.fontSharedMaterial);
                nameMaterial.SetFloat("unity_GUIZTestMode",(float)CompareFunction.LessEqual);
                nameLabel.fontSharedMaterial=nameMaterial;
                nameLabel.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            }
            nameLabel.gameObject.SetActive(true);
            nameLabel.text=RelayLobbySession.Instance.GetRankedNickname(life.OwnerClientId);
            nameLabel.transform.position=headPosition+Vector3.up*.34f;
            nameLabel.transform.rotation=camera.transform.rotation;
        }
    }
}
''')
write('Assets/HideAndShoot/Scripts/Player/PlayerOutlineRendererFeature.cs', '''using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HideAndShoot.Player
{
    /// <summary>Full silhouette plus linear depth, followed by fused dilation/depth-tested alpha overlay.</summary>
    public sealed class PlayerOutlineRendererFeature : ScriptableRendererFeature
    {
        public Shader maskShader,outlineShader;
        [Range(1,3)]public int outlinePixels=2;
        public Color outlineColor=new Color(.94f,.96f,.97f,.95f);
        private Material maskMaterial,outlineMaterial;
        private OutlinePass pass;
        public override void Create()
        {
            CoreUtils.Destroy(maskMaterial);CoreUtils.Destroy(outlineMaterial);
            if(maskShader==null || outlineShader==null)return;
            maskMaterial=CoreUtils.CreateEngineMaterial(maskShader);
            outlineMaterial=CoreUtils.CreateEngineMaterial(outlineShader);
            pass=new OutlinePass(maskMaterial,outlineMaterial){renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing};
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            if(pass==null || renderingData.cameraData.cameraType!=CameraType.Game)return;
            pass.Gather(renderingData.cameraData.camera);
            if(pass.Count==0)return;
            pass.Radius=outlinePixels;pass.Color=outlineColor;
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing)
        {CoreUtils.Destroy(maskMaterial);CoreUtils.Destroy(outlineMaterial);}

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private readonly Material maskMaterial,outlineMaterial;
            private readonly List<PlayerHighlightTarget.DrawPart> parts=new List<PlayerHighlightTarget.DrawPart>(32);
            public int Count=>parts.Count;
            public int Radius;
            public Color Color;
            public OutlinePass(Material mask,Material outline)
            {maskMaterial=mask;outlineMaterial=outline;ConfigureInput(ScriptableRenderPassInput.Depth);}
            public void Gather(Camera camera)=>PlayerHighlightTarget.Collect(camera,parts);
            private class MaskData
            {
                public Material Material;
                public PlayerHighlightTarget.DrawPart[] Parts;
            }
            private class CompositeData
            {
                public Material Material;
                public TextureHandle Mask,SceneDepth;
                public Vector4 Size;
                public Color Color;
                public int Radius;
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                if(parts.Count==0)return;
                var resources=frameData.Get<UniversalResourceData>();
                var camera=frameData.Get<UniversalCameraData>();
                if(!resources.cameraDepthTexture.IsValid())return;
                int width=camera.cameraTargetDescriptor.width,height=camera.cameraTargetDescriptor.height;
                var mask=graph.CreateTexture(new TextureDesc(width,height)
                {
                    name="Player silhouette and linear depth",format=GraphicsFormat.R32_SFloat,
                    clearBuffer=true,clearColor=UnityEngine.Color.clear,filterMode=FilterMode.Point
                });
                var depth=graph.CreateTexture(new TextureDesc(width,height)
                {name="Player silhouette depth attachment",format=GraphicsFormat.D32_SFloat,clearBuffer=true});
                using(var builder=graph.AddRasterRenderPass<MaskData>("Player silhouette",out var data))
                {
                    data.Material=maskMaterial;data.Parts=parts.ToArray();
                    builder.SetRenderAttachment(mask,0,AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depth,AccessFlags.Write);
                    builder.SetRenderFunc(static (MaskData d,RasterGraphContext context)=>
                    {
                        foreach(var part in d.Parts)
                            if(part.Renderer!=null)
                                for(int sub=0;sub<part.Submeshes;sub++)context.cmd.DrawRenderer(part.Renderer,d.Material,sub,0);
                    });
                }
                using(var builder=graph.AddRasterRenderPass<CompositeData>("Depth-tested player outline",out var data))
                {
                    data.Material=outlineMaterial;data.Mask=mask;data.SceneDepth=resources.cameraDepthTexture;
                    data.Size=new Vector4(width,height,1f/width,1f/height);data.Radius=Mathf.Clamp(Radius,1,3);data.Color=Color;
                    builder.UseTexture(mask,AccessFlags.Read);builder.UseTexture(data.SceneDepth,AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (CompositeData d,RasterGraphContext context)=>
                    {
                        context.cmd.SetGlobalTexture("_HSSilhouette",d.Mask);
                        context.cmd.SetGlobalTexture("_HSSceneDepth",d.SceneDepth);
                        context.cmd.SetGlobalVector("_HSOutlineSize",d.Size);
                        context.cmd.SetGlobalVector("_HSOutlineColor",d.Color);
                        context.cmd.SetGlobalInt("_HSOutlineRadius",d.Radius);
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.Material,0,MeshTopology.Triangles,3);
                    });
                }
            }
        }
    }
}
''')
write('Assets/HideAndShoot/Resources/Shaders/PlayerSilhouette.shader', '''Shader "Hidden/HideAndShoot/PlayerSilhouette"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float depth:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(world);
                output.depth=-TransformWorldToView(world).z;
                return output;
            }
            float Frag(Varyings input):SV_Target {return input.depth;}
            ENDHLSL
        }
    }
}
''')
write('Assets/HideAndShoot/Resources/Shaders/PlayerOutline.shader', '''Shader "Hidden/HideAndShoot/PlayerOutline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_HSSilhouette);
            TEXTURE2D(_HSSceneDepth);
            float4 _HSOutlineSize,_HSOutlineColor;
            int _HSOutlineRadius;
            struct Varyings {float4 positionCS:SV_POSITION;};
            Varyings Vert(uint vertex:SV_VertexID)
            {Varyings output;output.positionCS=GetFullScreenTriangleVertexPosition(vertex);return output;}
            float SceneDepth(int2 p)
            {return LinearEyeDepth(LOAD_TEXTURE2D(_HSSceneDepth,p).r,_ZBufferParams);}
            half4 Frag(Varyings input):SV_Target
            {
                int2 p=int2(input.positionCS.xy);
                if(LOAD_TEXTURE2D(_HSSilhouette,p).r>0)return 0;
                float nearest=1e10,depth=0;
                int2 source=p;
                int radius=_HSOutlineRadius;
                [loop]for(int y=-radius;y<=radius;y++)
                [loop]for(int x=-radius;x<=radius;x++)
                {
                    float distanceSq=x*x+y*y;
                    if(distanceSq>(radius+.25)*(radius+.25) || distanceSq>=nearest)continue;
                    int2 q=p+int2(x,y);
                    if(any(q<0) || any(q>=int2(_HSOutlineSize.xy)))continue;
                    float candidate=LOAD_TEXTURE2D(_HSSilhouette,q).r;
                    if(candidate<=0)continue;
                    nearest=distanceSq;depth=candidate;source=q;
                }
                if(depth<=0)return 0;
                // Check both the source body pixel and the expanded destination pixel.
                const float tolerance=.002;
                if(depth>SceneDepth(source)+tolerance || depth>SceneDepth(p)+tolerance)return 0;
                return _HSOutlineColor;
            }
            ENDHLSL
        }
    }
}
''')
p='Assets/HideAndShoot/Scripts/Combat/HostPlayerLife.cs';s=read(p)
s=replace(s,'public override void OnNetworkSpawn()\n        {','public override void OnNetworkSpawn()\n        {\n            if(GetComponent<PlayerHighlightTarget>()==null)gameObject.AddComponent<PlayerHighlightTarget>();')
write(p,s)
print('Rendered highlight written: fused 1-3px dilation, body/destination depth tests, observer-only depth-tested names.')