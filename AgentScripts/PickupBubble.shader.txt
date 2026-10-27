Shader "HideAndShoot/Items/PickupBubble"
{
    Properties
    {
        _Tint("Shell Tint", Color)=(0.04,0.3,0.8,1)
        [HDR] _EdgeColor("Edge Light", Color)=(0.12,1.5,4,1)
        _EdgeOpacity("Edge Opacity", Range(0,1))=0.55
        _CenterOpacity("Center Opacity", Range(0,0.1))=0.008
        _RimPower("Edge Focus", Range(1,6))=3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Bubble"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint, _EdgeColor;
                half _EdgeOpacity, _CenterOpacity, _RimPower;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS:POSITION;
                float3 normalOS:NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                half fog:TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output=(Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=p.positionCS;
                output.positionWS=p.positionWS;
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.fog=ComputeFogFactor(p.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal=normalize(input.normalWS);
                half3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim=pow(1-saturate(dot(normal,view)),_RimPower);
                half glint=pow(saturate(dot(normal,normalize(half3(-.4,.7,-.5)))),48);
                half pulse=1+.06*sin(_Time.y*1.8);
                half alpha=saturate(_CenterOpacity+rim*_EdgeOpacity+glint*.22);
                half3 color=lerp(_Tint.rgb,_EdgeColor.rgb,rim)*pulse+glint*half3(1.5,2.4,3);
                // The center stays almost clear; the ammo texture is never recolored or refracted.
                return half4(MixFog(color,input.fog),alpha);
            }
            ENDHLSL
        }
    }
}
