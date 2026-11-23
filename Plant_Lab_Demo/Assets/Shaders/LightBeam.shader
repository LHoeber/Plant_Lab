// Fake visible light beam (RectLampLight): additive, two-sided, no depth writes. Brightest where the beam's surface
// faces the camera (its outline fades out, so it looks like a volume, not a box), fading out along its length
// (uv.y: 0 at the lamp, 1 at the end).
Shader "BotanicLab/LightBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.95, 0.85, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.15
        _EdgePower ("Edge softness (higher = softer outline)", Range(0.1, 8)) = 2
        _LengthPower ("Length fade (higher = fades sooner)", Range(0.1, 8)) = 1.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _EdgePower;
                half _LengthPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half facing = pow(saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS)))), _EdgePower);
                half along = pow(saturate(1.0 - i.uv.y), _LengthPower);
                return half4(_Color.rgb * (facing * along * _Intensity), 0);
            }
            ENDHLSL
        }
    }
}
