Shader "MuseDesk/Spatial/PortalCone"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.184, 0.78, 0.76, 0.42)
        _Intensity ("Intensity", Range(0.0, 3.0)) = 1.0
        _ScanSpeed ("Scan Speed", Float) = 1.25
        _ScanDirection ("Scan Direction", Float) = -1.0
        _RingMode ("Ring Mode", Float) = 0.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
        [Toggle(_LITE_MODE)] _LiteMode ("Lite Mode (Low ALU)", Float) = 0
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
            Name "PortalAdditive"
            Blend SrcAlpha One
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _LITE_MODE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                half _ScanSpeed;
                half _ScanDirection;
                half _RingMode;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half v = IN.uv.y;
                half alpha;

                if (_RingMode > 0.5h)
                {
                    half radialFade = smoothstep(0.0h, 0.28h, v) * smoothstep(1.0h, 0.30h, v);
                    #if defined(_LITE_MODE)
                        alpha = radialFade * 0.90h;
                    #else
                        half shimmer = 0.84h + 0.16h * sin(IN.uv.x * 25.1327h + _Time.y * _ScanSpeed * 2.0h);
                        alpha = radialFade * shimmer;
                    #endif
                }
                else
                {
                    half endFade = smoothstep(0.0h, 0.14h, v) * smoothstep(1.0h, 0.18h, v);
                    #if defined(_LITE_MODE)
                        alpha = endFade * 0.78h;
                    #else
                        float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                        float3 normWS = normalize(IN.normalWS);
                        half edgeSoft = pow(saturate(abs(dot(normWS, viewDir))), 1.25h);
                        half bands = 0.72h + 0.28h * sin(v * 20.0h + _Time.y * _ScanSpeed * _ScanDirection * 6.28318h);
                        alpha = endFade * edgeSoft * bands;
                    #endif
                }

                half outAlpha = saturate(_BaseColor.a * alpha * _Intensity);
                half3 outRgb = _BaseColor.rgb * (_Intensity * 1.15h);
                return half4(outRgb, outAlpha);
            }
            ENDHLSL
        }
    }
}

