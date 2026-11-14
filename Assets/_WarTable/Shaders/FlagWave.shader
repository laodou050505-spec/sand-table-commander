// 帅旗旗面：顶点动画随风飘动。网格本地坐标里 x = 离旗杆的距离（0..宽），y = 高度，旗面在 xy 平面上；
// 位移沿本地 z，离旗杆越远摆得越大，叠一层细碎抖动。法线按位移的导数解析求出，阴影投射用同样的位移。
Shader "WarTable/FlagWave"
{
    Properties
    {
        _BaseMap ("旗面图案", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1,1,1,1)
        _Width ("旗面宽（本地米）", Float) = 0.09
        _Amp ("摆幅（相对宽度）", Float) = 0.12
        _WaveLen ("波长（相对宽度）", Float) = 0.8
        _Speed ("波速", Float) = 2.2
        _Ambient ("环境光", Float) = 0.55
        _ShadowColor ("暗部颜色", Color) = (0.45,0.45,0.55,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half4 _ShadowColor;
            float _Width, _Amp, _WaveLen, _Speed, _Ambient;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        // 返回位移后的位置，并给出本地法线
        float3 Wave(float3 p, out float3 nrm)
        {
            float u = saturate(p.x / _Width);
            float k = 6.2831853 / (_WaveLen * _Width);
            float t = _Time.y * _Speed;
            float env = pow(u, 1.25);                         // 旗杆处固定
            float ph = p.x * k - t + p.y * k * 0.35;
            float a = _Amp * _Width;
            float z = a * env * sin(ph) + a * 0.18 * env * sin(ph * 2.7 + 1.3 + t * 0.6);
            // dz/dx、dz/dy（忽略包络的导数项里很小的部分）
            float dzdx = a * env * cos(ph) * k + a * 0.18 * env * cos(ph * 2.7 + 1.3 + t * 0.6) * k * 2.7
                       + a * 1.25 * pow(max(u, 1e-3), 0.25) / _Width * sin(ph);
            float dzdy = a * env * cos(ph) * k * 0.35;
            nrm = normalize(float3(-dzdx, -dzdy, 1));
            // 旗尾略下垂
            p.y -= a * 0.25 * u * u;
            p.z += z;
            return p;
        }
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
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 n; float3 p = Wave(v.positionOS.xyz, n);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(n);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                // 布料：两面都透一点光
                half wrap = saturate(dot(n, main.direction) * 0.5 + 0.5);
                half lit = wrap * lerp(0.35, 1.0, main.shadowAttenuation);
                half3 col = alb * lerp(_ShadowColor.rgb, 1.0.xxx, lit) * main.color + alb * SampleSH(n) * _Ambient;
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
            struct A { float4 positionOS : POSITION; };
            float4 vert(A v) : SV_POSITION
            {
                float3 n; float3 p = Wave(v.positionOS.xyz, n);
                float3 ws = TransformObjectToWorld(p);
                float3 nws = TransformObjectToWorldNormal(n);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
