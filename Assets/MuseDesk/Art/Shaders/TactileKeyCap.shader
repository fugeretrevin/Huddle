Shader "MuseDesk/TactileKeyCap"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.957, 0.937, 0.886, 1)
        _Color ("Color (SDK Sync)", Color) = (0.957, 0.937, 0.886, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.42
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _StateMix ("State Tint Mix", Range(0, 1)) = 0
        _StateTint ("State Tint", Color) = (1, 1, 1, 1)
        _DisabledAmount ("Disabled Desaturate", Range(0, 1)) = 0
        _UseSdkColor ("Use _Color Property", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color;
                half4 _EmissionColor;
                half4 _StateTint;
                half  _Smoothness;
                half  _StateMix;
                half  _DisabledAmount;
                half  _UseSdkColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = NormalizeNormalPerVertex(normInputs.normalWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half3 N = normalize(IN.normalWS);
                half3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                half3 baseCol = _UseSdkColor > 0.5h ? _Color.rgb : _BaseColor.rgb;
                baseCol = lerp(baseCol, _StateTint.rgb, saturate(_StateMix));

                // Clean desaturation & cool dimming for Disabled state
                half lum = dot(baseCol, half3(0.299h, 0.587h, 0.114h));
                half3 disabledCol = lerp(half3(lum, lum, lum), half3(0.32h, 0.35h, 0.40h), 0.55h);
                baseCol = lerp(baseCol, disabledCol, saturate(_DisabledAmount));

                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(N, mainLight.direction));
                half wrapDiffuse = saturate((dot(N, mainLight.direction) + 0.35h) / 1.35h);

                half3 H = normalize(mainLight.direction + V);
                half specPower = lerp(8.0h, 64.0h, _Smoothness);
                half spec = pow(saturate(dot(N, H)), specPower) * _Smoothness * 0.28h * NdotL;

                half3 ambient = SampleSH(N) * 0.65h + half3(0.18h, 0.18h, 0.20h);
                half3 lit = baseCol * (ambient + mainLight.color * wrapDiffuse * 0.85h)
                          + mainLight.color * spec
                          + _EmissionColor.rgb;

                // Subtle soft rim for crisp silhouette readability in Quest passthrough
                half fresnel = pow(1.0h - saturate(dot(N, V)), 3.0h);
                lit += baseCol * fresnel * 0.10h;

                return half4(lit, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Simple Lit"
}

