Shader "MuseDesk/Spatial/CeilingScanGrid"
{
    // One flat quad on the ceiling. Procedural grid, radial reveal from the centre, thin footprint ring.
    // Unlit, alpha blended (not additive), no depth write, ZTest LEqual, single-pass instanced stereo.
    // Quad UV (0..1) maps to _Size metres, so all distances below are in metres.
    Properties
    {
        _BaseColor ("Line Colour", Color) = (0.184, 0.639, 0.604, 0.6)
        _RevealRadius ("Reveal Radius (m)", Float) = 0.0
        _Size ("Quad Size (m)", Float) = 1.6
        _Cell ("Grid Cell (m)", Float) = 0.1
        _LineWidth ("Line Width (m)", Float) = 0.004
        _FalloffRadius ("Fade Out Radius (m)", Float) = 0.8
        _RingRadius ("Footprint Ring Radius (m)", Float) = 0.18
        _RingWidth ("Footprint Ring Width (m)", Float) = 0.012
        _Fade ("Fade", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CeilingScan"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _RevealRadius;
                float _Size;
                float _Cell;
                float _LineWidth;
                float _FalloffRadius;
                float _RingRadius;
                float _RingWidth;
                half _Fade;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 p = (IN.uv - 0.5) * _Size;          // metres from the centre
                float d = length(p);
                float aa = max(fwidth(d), 0.0005);

                // Grid: distance to the nearest line, in metres.
                float2 g = abs(frac(p / _Cell + 0.5) - 0.5) * _Cell;
                float lineDist = min(g.x, g.y);
                float half_w = _LineWidth * 0.5;
                float lines = 1.0 - smoothstep(half_w - aa, half_w + aa, lineDist);

                // Wipe: lines exist inside the expanding radius; a soft bright front rides its edge.
                float reveal = 1.0 - smoothstep(_RevealRadius - 0.05, _RevealRadius, d);
                float front = exp(-pow((d - _RevealRadius) / 0.035, 2.0)) * step(0.001, _RevealRadius);

                // Lines fade with distance from the centre.
                float fall = 1.0 - smoothstep(0.0, max(_FalloffRadius, 0.01), d);
                fall *= fall;

                // Footprint ring, shown once the wipe has passed it.
                float ringHalf = _RingWidth * 0.5;
                float ring = (1.0 - smoothstep(ringHalf - aa, ringHalf + aa, abs(d - _RingRadius)))
                           * step(_RingRadius, _RevealRadius);

                float a = lines * fall * reveal * 0.55
                        + lines * front * 0.45
                        + ring * 0.95;
                a = saturate(a) * _BaseColor.a * _Fade;

                return half4(_BaseColor.rgb, a);
            }
            ENDHLSL
        }
    }
}
