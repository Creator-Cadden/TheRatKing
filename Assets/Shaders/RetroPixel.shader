// Object-space retro look for The Rat King (URP).
// Keeps edges sharp (readable at any distance) while giving a PS1/retro feel via:
//  • vertex snapping (the classic "wobble")
//  • colour posterize (banded shading)
//  • point-sampled texture (chunky if a texture is used; harmless on flat colours)
// Still lit by the scene's main directional light + ambient, so it responds to
// your lighting pass. Assign this shader to a Material, then put that Material on
// your world/enemy/player meshes.
Shader "RatKing/RetroPixel"
{
    Properties
    {
        _BaseMap ("Base Texture (optional)", 2D) = "white" {}
        _BaseColor ("Base Colour", Color) = (1,1,1,1)
        [Tooltip] _SnapPixels ("Vertex Snap (higher = subtler wobble)", Range(20,400)) = 120
        _PosterizeLevels ("Posterize Levels (lower = chunkier shading)", Range(2,32)) = 8
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _SnapPixels;
                float  _PosterizeLevels;
                float  _AmbientBoost;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = posWS;

                float4 posHCS = TransformWorldToHClip(posWS);

                // Vertex snap: quantise the clip-space XY to a grid → PS1 wobble.
                float2 grid = _SnapPixels.xx;
                float2 ndc  = posHCS.xy / posHCS.w;
                ndc = floor(ndc * grid) / grid;
                posHCS.xy = ndc * posHCS.w;

                OUT.positionHCS = posHCS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                float3 N = normalize(IN.normalWS);

                // Main directional light + shadows.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                float  ndotl       = saturate(dot(N, mainLight.direction));
                float3 direct      = mainLight.color * (ndotl * mainLight.shadowAttenuation);

                // Additional lights — your per-room point / spot lights.
                #ifdef _ADDITIONAL_LIGHTS
                uint addCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < addCount; li++)
                {
                    Light L  = GetAdditionalLight(li, IN.positionWS);
                    float nl = saturate(dot(N, L.direction));
                    direct  += L.color * (nl * L.distanceAttenuation * L.shadowAttenuation);
                }
                #endif

                // Ambient from the scene's environment lighting.
                float3 ambient = SampleSH(N) * _AmbientBoost;

                half3 col = albedo.rgb * (direct + ambient);

                // Posterize the final colour for banded, retro shading.
                col = floor(col * _PosterizeLevels) / _PosterizeLevels;

                return half4(col, albedo.a);
            }
            ENDHLSL
        }

        // Lets these objects cast shadows in your lighting pass.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct SAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct SVaryings   { float4 positionHCS : SV_POSITION; };

            SVaryings shadowVert (SAttributes IN)
            {
                SVaryings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 hcs   = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                OUT.positionHCS = hcs;
                return OUT;
            }

            half4 shadowFrag (SVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
