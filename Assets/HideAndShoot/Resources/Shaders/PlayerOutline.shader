Shader "Hidden/HideAndShoot/PlayerOutline"
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
