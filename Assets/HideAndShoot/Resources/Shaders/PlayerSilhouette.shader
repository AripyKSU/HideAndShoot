Shader "Hidden/HideAndShoot/PlayerSilhouette"
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
