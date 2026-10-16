// 거리 안개 + 구름 (Full Screen Pass 용)
// - 땅: 깊이로 카메라와의 거리를 구해 멀수록 안개 색으로 섞음 (Unity 기본 포그가 안 먹는 Cesium 지도에도 적용)
// - 하늘: 지평선 쪽은 같은 안개 색으로 이어서 땅/하늘 경계 선이 안 보이게, 위쪽엔 흘러가는 옅은 구름
Shader "Hidden/DistanceHaze"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.80, 0.86, 0.93, 1)
        _Start ("Start Distance", Float) = 500
        _Density ("Density", Float) = 0.00045
        _HorizonBand ("Sky Horizon Band", Range(0.02, 0.6)) = 0.18
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadow ("Cloud Shadow Color", Color) = (0.72, 0.78, 0.86, 1)
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.42
        _CloudSoftness ("Cloud Softness", Range(0.01, 0.5)) = 0.22
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.85
        _CloudScale ("Cloud Scale", Float) = 1.6
        _CloudSpeed ("Cloud Speed", Float) = 0.01
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

            float4 _FogColor, _CloudColor, _CloudShadow;
            float _Start, _Density, _HorizonBand;
            float _CloudCover, _CloudSoftness, _CloudOpacity, _CloudScale, _CloudSpeed;

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

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
                if (isSky)
                {
                    float3 dir = normalize(ComputeWorldSpacePosition(uv, 0.5, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos);
                    float up = max(dir.y, 0.0);

                    // 구름: 하늘 돔에 노이즈를 투영하고 천천히 흘려보냄
                    float2 cuv = dir.xz / (up + 0.12) * _CloudScale + _Time.y * _CloudSpeed * float2(1.0, 0.4);
                    float n = Fbm(cuv);
                    float cloud = smoothstep(_CloudCover, _CloudCover + _CloudSoftness, n);
                    float shade = smoothstep(_CloudCover, _CloudCover + 0.45, Fbm(cuv + 0.35));   // 두꺼운 곳은 밝게, 가장자리는 살짝 그늘
                    float3 cloudCol = lerp(_CloudShadow.rgb, _CloudColor.rgb, shade);
                    cloud *= _CloudOpacity * smoothstep(0.0, 0.25, up);                         // 지평선 쪽 구름은 안개 속으로 사라짐
                    col.rgb = lerp(col.rgb, cloudCol, cloud);

                    // 지평선: 땅의 먼 안개와 같은 색으로 이어지게 (경계 선 제거)
                    float h = 1.0 - smoothstep(0.0, _HorizonBand, up);
                    col.rgb = lerp(col.rgb, _FogColor.rgb, h * h);
                }
                else
                {
                    float d = LinearEyeDepth(raw, _ZBufferParams);
                    float f = 1.0 - exp(-_Density * max(0.0, d - _Start));
                    col.rgb = lerp(col.rgb, _FogColor.rgb, saturate(f));
                }
                return col;
            }
            ENDHLSL
        }
    }
}