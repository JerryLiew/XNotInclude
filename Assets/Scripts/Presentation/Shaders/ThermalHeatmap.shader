Shader "XNotInclude/ThermalHeatmap"
{
    Properties
    {
        _ScalarTex ("Scalar Field", 2D) = "black" {}
        _Mode ("Mode (0=Heatmap, 1=Material)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _ScalarTex;
            float4 _ScalarTex_ST;
            float _Mode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _ScalarTex);
                return o;
            }

            fixed3 HeatRamp(float t)
            {
                fixed3 cold = fixed3(0.03, 0.06, 0.28);
                fixed3 cool = fixed3(0.00, 0.58, 0.85);
                fixed3 mid = fixed3(0.12, 0.82, 0.36);
                fixed3 warm = fixed3(0.98, 0.83, 0.10);
                fixed3 hot = fixed3(0.94, 0.12, 0.05);

                if (t < 0.25) return lerp(cold, cool, saturate(t / 0.25));
                if (t < 0.5) return lerp(cool, mid, saturate((t - 0.25) / 0.25));
                if (t < 0.75) return lerp(mid, warm, saturate((t - 0.5) / 0.25));
                return lerp(warm, hot, saturate((t - 0.75) / 0.25));
            }

            fixed3 ElementRamp(float id)
            {
                float phase = id * 2.399963;
                return fixed3(
                    0.5 + 0.5 * sin(phase),
                    0.5 + 0.5 * sin(phase + 2.094395),
                    0.5 + 0.5 * sin(phase + 4.188790)
                );
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float scalar = tex2D(_ScalarTex, i.uv).r;
                fixed3 color = _Mode > 0.5
                    ? ElementRamp(floor(scalar * 16.0 + 0.5))
                    : HeatRamp(saturate(scalar));
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
