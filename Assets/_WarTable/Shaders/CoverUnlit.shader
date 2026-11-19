// 封面 v6 的平面件：墨笔刷底、亮笔刷（悬停时从左往右刷出）、朱砂墨迹、行军地图残片、顶光光柱、压暗房间的罩子。
// 贴图 rgb × 颜色，透明度 = 贴图 a × 颜色 a；_Reveal 按 uv.x 从左往右显出（边缘柔和 _RevealSoft）；_EdgeFade 让光柱这类网格在掠射角淡出。
// 混合方式由属性给：普通半透明 SrcAlpha/OneMinusSrcAlpha，光柱叠加 One/One；压暗罩的透明度通道用 One/OneMinusSrcAlpha，
// 这样头显透视画面也会被压暗（PICO 按帧缓冲的透明度合成真实画面）。
Shader "WarTable/CoverUnlit"
{
    Properties
    {
        _BaseMap ("贴图", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1,1,1,1)
        _Reveal ("显出 0-1（从左往右）", Range(0,1)) = 1
        _RevealSoft ("显出边缘柔和", Range(0.001,0.5)) = 0.08
        _EdgeFade ("掠射角淡出", Range(0,4)) = 0
        _VFade ("纵向淡出（光柱：下端淡）", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendA ("SrcA", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendA ("DstA", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ZWrite Off
            ZTest [_ZTest]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor; half _Reveal, _RevealSoft, _EdgeFade, _VFade;
                half _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _Cull, _ZTest;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(i.uv, _BaseMap)) * _BaseColor;
                // 显出边：_Reveal = 0 时完全不显（左端不留残边），= 1 时整张显出
                half edge = _Reveal * (1 + _RevealSoft);
                c.a *= 1 - smoothstep(edge - _RevealSoft, edge, i.uv.x);
                if (_EdgeFade > 0)
                {
                    half ndv = abs(dot(normalize(i.normalWS), GetWorldSpaceNormalizeViewDir(i.positionWS)));
                    c.a *= pow(ndv, _EdgeFade);
                }
                c.a *= lerp(1, i.uv.y, _VFade);
                return c;
            }
            ENDHLSL
        }
    }
}
