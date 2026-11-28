// Bug wings (BugSwarm): semi-transparent, two-sided, flapping in the vertex shader.
// Every vertex drawn with this material is rotated around the bug's forward axis through the model's origin (the wing
// hinge): right side up by the flap angle, left side mirrored. The angle is
//   center + amplitude * sin(2 pi * frequency * _LabTime + phase)
// _LabTime is set by the swarm from the trial's tick time (not Unity's _Time), so the wings are in exactly the same
// position at the same tick in every run. Axes, flap values and phase come per bug from BugSwarm (property block);
// the material values are only defaults.
Shader "BotanicLab/BugWing"
{
    Properties
    {
        _BaseColor ("Color (alpha = opacity)", Color) = (0.85, 0.88, 0.92, 0.35)
        _BaseMap ("Texture (alpha = opacity)", 2D) = "white" {}
        _Ambient ("Ambient light", Range(0, 2)) = 1
        _Shine ("Shine (view-dependent sheen)", Range(0, 1)) = 0.25
        _FlapFrequency ("Flap frequency (Hz)", Float) = 40
        _FlapAmplitude ("Flap amplitude (deg)", Float) = 50
        _FlapCenter ("Flap center (deg)", Float) = 10
        _FlapPhase ("Flap phase (rad)", Float) = 0
        _FlapForward ("Forward axis (object space)", Vector) = (0, 0, 1, 0)
        _FlapSide ("Side axis (object space)", Vector) = (1, 0, 0, 0)
        _FlapUp ("Up axis (object space)", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float _LabTime;//global, set by BugSwarm

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Ambient;
                half _Shine;
                float _FlapFrequency, _FlapAmplitude, _FlapCenter, _FlapPhase;
                float4 _FlapForward, _FlapSide, _FlapUp;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            // rotates v in the (side, up) plane by angle a for the right side, -a for the left side
            float3 Flap(float3 v, float a)
            {
                float f = dot(v, _FlapForward.xyz), s = dot(v, _FlapSide.xyz), u = dot(v, _FlapUp.xyz);
                float c = cos(a), sn = sin(a);
                return f * _FlapForward.xyz + (s * c - u * sn) * _FlapSide.xyz + (s * sn + u * c) * _FlapUp.xyz;
            }

            Varyings vert(Attributes i)
            {
                float angle = radians(_FlapCenter + _FlapAmplitude * sin(6.2831853 * _FlapFrequency * _LabTime + _FlapPhase));
                float side = dot(i.positionOS.xyz, _FlapSide.xyz) >= 0 ? 1.0 : -1.0;
                float a = angle * side;
                float3 pos = Flap(i.positionOS.xyz, a);
                float3 nrm = Flap(i.normalOS, a);

                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                float3 v = normalize(i.viewWS);
                Light light = GetMainLight();
                half diffuse = abs(dot(n, light.direction)) * 0.6 + 0.4;//thin membrane: lit from both sides
                half3 col = tex.rgb * (light.color * diffuse + SampleSH(n) * _Ambient);
                half sheen = pow(1 - saturate(abs(dot(n, v))), 3) * _Shine;//brighter at grazing angles
                return half4(col + sheen, saturate(tex.a + sheen * 0.5));
            }
            ENDHLSL
        }
    }
}
