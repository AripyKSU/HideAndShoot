using System.Collections.Generic;
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
