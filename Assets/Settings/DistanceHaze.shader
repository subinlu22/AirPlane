// 거리 안개 (Full Screen Pass 용) — 깊이로 카메라와의 거리를 구해 멀수록 안개 색으로 섞음
// Unity 기본 포그가 적용되지 않는 Cesium 지도에도 먹도록 화면 전체에 후처리로 적용
Shader "Hidden/DistanceHaze"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.80, 0.86, 0.93, 1)
        _Start ("Start Distance", Float) = 500
        _Density ("Density", Float) = 0.00045
        _MaxFog ("Max Fog", Range(0, 1)) = 0.92
        _SkyHaze ("Sky Horizon Haze", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "DistanceHaze"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _FogColor;
            float _Start;
            float _Density;
            float _MaxFog;
            float _SkyHaze;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool isSky = raw <= 0.000001;
            #else
                bool isSky = raw >= 0.999999;
            #endif
                float f;
                if (isSky)
                {
                    // 하늘: 지평선 근처만 살짝 뿌옇게 (지도 끝과 하늘 경계를 자연스럽게)
                    float3 dir = normalize(ComputeWorldSpacePosition(uv, 0.5, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos);
                    f = _SkyHaze * saturate(1.0 - dir.y * 6.0);
                }
                else
                {
                    float d = LinearEyeDepth(raw, _ZBufferParams);
                    f = 1.0 - exp(-_Density * max(0.0, d - _Start));
                }
                f = min(saturate(f), _MaxFog);
                col.rgb = lerp(col.rgb, _FogColor.rgb, f);
                return col;
            }
            ENDHLSL
        }
    }
}