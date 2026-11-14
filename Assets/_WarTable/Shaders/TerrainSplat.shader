// 《将军沙盘》地形：模型地台的植绒草皮 / 干草 / 土路 / 岩石 / 河床
//  - 顶点色是混合权重：r 干草  g 土路  b 岩石  a 河床沙石（都为 0 时是青草）
//  - 贴图用物体空间 xz 平铺（沙盘整体移动 / 缩放时纹理跟着走）；岩石用三向投影，陡坡不拉伸
//  - 没有贴图时用颜色 + 程序噪声兜底
Shader "WarTable/TerrainSplat"
{
    Properties
    {
        _GrassTex ("青草", 2D) = "white" {}
        _DryTex ("干草", 2D) = "white" {}
        _DirtTex ("土路", 2D) = "white" {}
        _RockTex ("岩石", 2D) = "white" {}
        _SandTex ("河床", 2D) = "white" {}
        _GrassColor ("青草色", Color) = (0.42,0.52,0.18,1)
        _GrassColor2 ("青草色 2（噪声混入）", Color) = (0.62,0.62,0.22,1)
        _DryColor ("干草色", Color) = (0.78,0.66,0.32,1)
        _DirtColor ("土路色", Color) = (0.50,0.38,0.25,1)
        _RockColor ("岩石色", Color) = (0.55,0.54,0.52,1)
        _SandColor ("河床色", Color) = (0.62,0.56,0.44,1)
        _Tile ("贴图平铺（每米重复次数）", Float) = 6
        _RockTile ("岩石平铺", Float) = 9
        _NoiseScale ("色块噪声尺度", Float) = 18
        _NoiseAmount ("色块噪声强度", Range(0,1)) = 0.55
        _ShadowColor ("暗部色", Color) = (0.40,0.43,0.52,1)
        _ShadeSoft ("明暗过渡", Range(0.05,1)) = 0.6
        _Ambient ("环境光", Range(0,2)) = 0.5
        _BlendSharp ("混合边缘锐度", Range(1,16)) = 5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
        TEXTURE2D(_DryTex); TEXTURE2D(_DirtTex); TEXTURE2D(_RockTex); TEXTURE2D(_SandTex);
        CBUFFER_START(UnityPerMaterial)
            half4 _GrassColor, _GrassColor2, _DryColor, _DirtColor, _RockColor, _SandColor, _ShadowColor;
            float _Tile, _RockTile, _NoiseScale, _NoiseAmount, _ShadeSoft, _Ambient, _BlendSharp;
        CBUFFER_END

        float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float vnoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
        }
        float fbm(float2 p) { return vnoise(p) * 0.55 + vnoise(p * 2.13 + 7.1) * 0.3 + vnoise(p * 4.7 + 3.3) * 0.15; }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2; float3 normalOS : TEXCOORD3; half4 w : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            V vert(A v)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz; o.normalOS = v.normalOS;
                o.w = v.color;
                return o;
            }

            half3 tri(TEXTURE2D_PARAM(t, s), float3 p, float3 n, float k)
            {
                float3 b = pow(abs(n), 4); b /= (b.x + b.y + b.z);
                return SAMPLE_TEXTURE2D(t, s, p.zy * k).rgb * b.x + SAMPLE_TEXTURE2D(t, s, p.xz * k).rgb * b.y + SAMPLE_TEXTURE2D(t, s, p.xy * k).rgb * b.z;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.positionOS.xz * _Tile;
                float n1 = fbm(i.positionOS.xz * _NoiseScale), n2 = fbm(i.positionOS.xz * _NoiseScale * 3.7 + 11);
                half3 grass = lerp(_GrassColor.rgb, _GrassColor2.rgb, saturate((n1 - 0.35) * 2.2) * _NoiseAmount) * (0.85 + 0.3 * n2);
                grass *= SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uv).rgb;
                half3 dry = _DryColor.rgb * (0.85 + 0.3 * n2) * SAMPLE_TEXTURE2D(_DryTex, sampler_GrassTex, uv * 1.13).rgb;
                half3 dirt = _DirtColor.rgb * (0.88 + 0.25 * n2) * SAMPLE_TEXTURE2D(_DirtTex, sampler_GrassTex, uv * 0.9).rgb;
                half3 rock = _RockColor.rgb * (0.8 + 0.4 * n1) * tri(TEXTURE2D_ARGS(_RockTex, sampler_GrassTex), i.positionOS, normalize(i.normalOS), _RockTile);
                half3 sand = _SandColor.rgb * (0.85 + 0.3 * n2) * SAMPLE_TEXTURE2D(_SandTex, sampler_GrassTex, uv * 1.4).rgb;

                // 权重按噪声抖一抖再锐化：过渡处像真的撒了草粉，不是一条软渐变
                half4 w = i.w;
                half jit = (n2 - 0.5) * 0.5;
                w = saturate(w + w * jit * 1.5);
                w = pow(w, 1.0 / max(1, _BlendSharp * 0.25));
                half g = saturate(1 - (w.r + w.g + w.b + w.a));
                half sum = g + w.r + w.g + w.b + w.a + 1e-4;
                half3 alb = (grass * g + dry * w.r + dirt * w.g + rock * w.b + sand * w.a) / sum;

                float3 nrm = normalize(i.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrap = saturate(dot(nrm, main.direction) * 0.5 + 0.5);
                half lit = smoothstep(0.5 - _ShadeSoft * 0.5, 0.5 + _ShadeSoft * 0.5, wrap) * lerp(0.25, 1.0, main.shadowAttenuation);
                half3 col = alb * lerp(_ShadowColor.rgb, 1.0.xxx, lit) * main.color;
                col += alb * SampleSH(nrm) * _Ambient;
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A v)
            {
                V o; float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(ws, TransformObjectToWorldNormal(v.normalOS), _LightDirection));
                return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o = (V)0; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
