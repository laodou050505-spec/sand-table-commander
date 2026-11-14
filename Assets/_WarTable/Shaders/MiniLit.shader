// 《将军沙盘》彩绘微缩模型着色器（URP）
//  - P2 贴图本身带手绘明暗，这里只加柔和的整体受光：半兰伯特 + 柔和过渡，暗部偏冷灰，不死黑
//  - 哑光清漆的弱高光（_Gloss）、边缘光、顶点色（合并网格用）、自发光
//  - _TeamColor：底座色圈等需要队伍色的部分用顶点色 a 通道做遮罩
Shader "WarTable/MiniLit"
{
    Properties
    {
        _BaseMap ("贴图", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("使用顶点色", Float) = 0
        _ShadowColor ("暗部色", Color) = (0.42,0.44,0.52,1)
        _ShadeSoft ("明暗过渡", Range(0.05,1)) = 0.55
        _Gloss ("清漆高光", Range(0,1)) = 0.12
        _GlossSize ("高光锐度", Range(4,128)) = 24
        _Cutoff ("透明裁切（0 = 不裁）", Range(0,1)) = 0
        _Sheen ("绒布光泽", Range(0,1)) = 0
        _SheenColor ("绒布光泽色", Color) = (1,0.92,0.97,1)
        _RimColor ("边缘光", Color) = (1,0.95,0.86,1)
        _RimPower ("边缘光宽度", Range(0.5,8)) = 3
        _RimStrength ("边缘光强度", Range(0,2)) = 0.15
        _Ambient ("环境光", Range(0,2)) = 0.45
        [HDR] _EmissionColor ("自发光", Color) = (0,0,0,1)
        _EmissionMap ("自发光遮罩", 2D) = "white" {}
        _Flash ("受击闪白", Range(0,1)) = 0
        _WorldTint ("世界色调（门后世界整体染色，如黄昏城堡）", Color) = (1,1,1,1)
        _Alpha ("透明度(配合 Fade)", Range(0,1)) = 1
        _HazeColor ("景深雾颜色", Color) = (1,0.93,0.9,1)
        _HazeStart ("景深雾起点(米)", Float) = 4
        _HazeEnd ("景深雾终点(米)", Float) = 16
        _HazeAmount ("景深雾强度", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除", Float) = 2
        [IntRange] _StencilRef ("Stencil 值", Range(0,255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil 比较", Float) = 8
        [HideInInspector] _StencilReadMask ("Stencil ReadMask（门后世界 = 7：门洞 64|n 和玩具化覆盖 128|n 都通过）", Float) = 255
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        half4 _ToyGrade;   // 全局，不进材质 CBUFFER
        half _ToyContrast; // 全局贴图对比度（BoardLook）
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowColor, _SheenColor, _RimColor, _EmissionColor, _HazeColor, _WorldTint;
            float _HazeStart, _HazeEnd, _HazeAmount;
            half _Cutoff, _UseVertexColor, _ShadeSoft, _Gloss, _GlossSize, _Sheen, _RimPower, _RimStrength, _Ambient, _Flash, _Alpha;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToyForward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Stencil { Ref [_StencilRef] ReadMask [_StencilReadMask] Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3; half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                half4 vc = v.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                vc.rgb = SRGBToLinear(vc.rgb);      // 顶点色按 sRGB 写入，线性空间里要先转换，否则粉彩发白
                #endif
                o.color = lerp(half4(1,1,1,1), vc, _UseVertexColor);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
                // 贴图对比度（全局，BoardLook 设置）：彩绘模型的明暗笔触更清楚
                half tc = _ToyContrast > 0 ? _ToyContrast : 1;
                tex.rgb = saturate((tex.rgb - 0.42) * tc + 0.42);
                if (_Cutoff > 0) clip(tex.a - _Cutoff);
                float3 n = normalize(i.normalWS) * (facing > 0 ? 1 : -1);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrap = saturate(dot(n, main.direction) * 0.5 + 0.5);
                half lit = smoothstep(0.5 - _ShadeSoft * 0.5, 0.5 + _ShadeSoft * 0.5, wrap) * lerp(0.2, 1.0, main.shadowAttenuation);
                half3 tint = lerp(_ShadowColor.rgb, 1.0.xxx, lit);
                half3 col = tex.rgb * tint * main.color;

                // 塑料高光：柔和的 Blinn-Phong
                float3 h = normalize(main.direction + v);
                half spec = pow(saturate(dot(n, h)), _GlossSize) * _Gloss * (0.4 + 0.6 * main.shadowAttenuation);
                col += main.color * spec;

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    half d = saturate(dot(n, l.direction) * 0.5 + 0.5);
                    col += tex.rgb * l.color * d * l.distanceAttenuation;
                }
                #endif

                half4 g = _ToyGrade.w > 0 ? _ToyGrade : half4(1, 1, 1, 1);   // 全局画面调色（ToyLook 组件设置）：x 边缘光 y 环境光 z 饱和度 w 亮度
                col += tex.rgb * SampleSH(n) * _Ambient * g.y;

                half fres = 1.0 - saturate(dot(n, v));
                // 绒布：掠射角泛起柔光
                col += _SheenColor.rgb * pow(fres, 3.0) * _Sheen * 0.25;
                // 边缘光
                col += _RimColor.rgb * pow(fres, _RimPower) * _RimStrength * g.x;
                half luma = dot(col, half3(0.3, 0.59, 0.11));
                col = max(0, lerp(luma.xxx, col, g.z)) * g.w;
                // 自发光
                col *= _WorldTint.rgb;                // 世界染色只压受光部分，自发光（窗灯、彩灯）不受影响
                col += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                col = lerp(col, 1.0.xxx, _Flash);
                // 景深雾：门后世界越远越朦胧（雪景球般的空气感）
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                col = lerp(col, _HazeColor.rgb, saturate((dist - _HazeStart) / max(_HazeEnd - _HazeStart, 0.01)) * _HazeAmount);
                col = MixFog(col, i.fog);
                return half4(col, tex.a * _Alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(ws, nw, _LightDirection));
                return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            Stencil { Ref [_StencilRef] ReadMask [_StencilReadMask] Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
