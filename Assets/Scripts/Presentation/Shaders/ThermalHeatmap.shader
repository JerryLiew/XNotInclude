Shader "XNotInclude/ThermalHeatmap"
{
    Properties
    {
        _ScalarTex ("Scalar Field (RFloat)", 2D) = "black" {}
        _Mode ("Mode (0=Temp, 1=Material, 2=Mass)", Float) = 0
        _MinTempK ("Min Temp (K)", Float) = 253.15
        _MaxTempK ("Max Temp (K)", Float) = 373.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_ScalarTex);
            SAMPLER(sampler_ScalarTex);

            float _Mode;
            float _MinTempK;
            float _MaxTempK;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half3 HeatRamp(float t)
            {
                t = saturate(t);
                half3 cold = half3(0.03, 0.08, 0.45);
                half3 cool = half3(0.00, 0.62, 0.88);
                half3 mid  = half3(0.14, 0.84, 0.34);
                half3 warm = half3(0.98, 0.84, 0.12);
                half3 hot  = half3(0.94, 0.12, 0.06);

                if (t < 0.25) return lerp(cold, cool, t * 4.0);
                if (t < 0.50) return lerp(cool, mid,  (t - 0.25) * 4.0);
                if (t < 0.75) return lerp(mid,  warm, (t - 0.50) * 4.0);
                return lerp(warm, hot, (t - 0.75) * 4.0);
            }

            half3 ElementRamp(float id)
            {
                float phase = id * 2.399963;
                return half3(
                    0.5 + 0.5 * sin(phase),
                    0.5 + 0.5 * sin(phase + 2.094395),
                    0.5 + 0.5 * sin(phase + 4.188790)
                );
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float scalar = SAMPLE_TEXTURE2D(_ScalarTex, sampler_ScalarTex, IN.uv).r;
                half3 vacuumColor = half3(0.05, 0.05, 0.08);

                // Mode 1: 材质概览
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    if (scalar < 0.5) return half4(vacuumColor, 1.0);
                    return half4(ElementRamp(floor(scalar + 0.5)), 1.0);
                }

                // Mode 2: 质量概览
                if (_Mode >= 1.5)
                {
                    if (scalar <= 0.0001) return half4(vacuumColor, 1.0);
                    return half4(HeatRamp(scalar), 1.0);
                }

                // Mode 0: 温度热力图（真空 = 0K，非真空 >= 1K）
                if (scalar < 0.5) return half4(vacuumColor, 1.0);
                float norm = (scalar - _MinTempK) / max(0.001, _MaxTempK - _MinTempK);
                return half4(HeatRamp(norm), 1.0);
            }
            ENDHLSL
        }
    }
}