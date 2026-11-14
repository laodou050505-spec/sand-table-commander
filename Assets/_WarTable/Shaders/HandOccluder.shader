// 手部遮挡（11-04 用户：手不要被沙盘挡住，手应该在沙盘上）：头显透视画面里虚拟物体总盖在真手上面。
// 在手的位置画一副看不见的手形网格：不写颜色、只写深度，并把帧缓冲的透明度写成 0——
// 比它远的沙盘、棋子都被挡掉，这几块像素透明，PICO 合成时露出底下透视画面里的真手。
// 先画（Queue = Geometry-10），这样后面的不透明物体被深度测试挡住；半透明件（光环、提示）也一样被挡。
Shader "WarTable/HandOccluder"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Occlude"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Off
            ColorMask A
            Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag(V i) : SV_Target { return half4(0, 0, 0, 0); }
            ENDHLSL
        }
    }
}
