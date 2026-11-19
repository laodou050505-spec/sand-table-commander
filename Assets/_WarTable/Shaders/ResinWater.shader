// 《将军沙盘》河水：模型师用的透明树脂水效果
//  - 半透明，顶点色 a = 水深（0 岸边 → 1 河心）：浅处透出河床，深处偏青蓝
//  - 程序噪声做细小涟漪法线，慢慢顺流漂；太阳高光 + 菲涅尔天光
//  - 岸边一圈浅色泡沫线（按深度）
Shader "WarTable/ResinWater"
{
    Properties
    {
        _DeepColor ("深处", Color) = (0.10,0.30,0.38,0.88)
        _ShallowColor ("浅处", Color) = (0.38,0.62,0.62,0.35)
        _SkyColor ("天光", Color) = (0.78,0.86,0.92,1)
        _FoamColor ("岸边泡沫", Color) = (0.92,0.94,0.90,1)
        _Flow ("流向（物体空间 xz）与速度", Vector) = (0,1,0.012,0)
        _RippleScale ("涟漪尺度", Float) = 160
        _RippleStrength ("涟漪强度", Range(0,1)) = 0.12
        _Gloss ("高光锐度", Range(8,512)) = 90
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _SkyColor, _FoamColor; float4 _Flow; float _RippleScale, _RippleStrength, _Gloss;
            CBUFFER_END

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 positionOS : TEXCOORD1; half4 color : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.positionOS = v.positionOS.xyz; o.color = v.color;
                return o;
            }

            float height(float2 p)
            {
                float2 flow = _Flow.xy * _Flow.z * _Time.y;
                return vnoise(p * _RippleScale + flow * _RippleScale) * 0.6 + vnoise(p * _RippleScale * 2.3 - flow * _RippleScale * 1.7 + 5) * 0.4;
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.positionOS.xz;
                float e = 0.5 / _RippleScale;
                float h0 = height(p), hx = height(p + float2(e, 0)), hz = height(p + float2(0, e));
                float3 nOS = normalize(float3(-(hx - h0) * _RippleStrength * 4, 1, -(hz - h0) * _RippleStrength * 4));
                float3 n = normalize(TransformObjectToWorldNormal(nOS));
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half depth = saturate(i.color.a);
                half4 body = lerp(_ShallowColor, _DeepColor, smoothstep(0.0, 0.9, depth));
                half fres = pow(1 - saturate(dot(n, v)), 4);
                half3 col = body.rgb * (0.55 + 0.45 * main.color * main.shadowAttenuation) + _SkyColor.rgb * fres * 0.5;
                float3 hv = normalize(main.direction + v);
                col += main.color * pow(saturate(dot(n, hv)), _Gloss) * 0.6 * main.shadowAttenuation;
                half foam = smoothstep(0.18, 0.02, depth) * (0.55 + 0.45 * vnoise(p * _RippleScale * 0.7));
                col = lerp(col, _FoamColor.rgb, foam * 0.6);
                return half4(col, saturate(body.a + fres * 0.3 + foam * 0.4));
            }
            ENDHLSL
        }
    }
}
