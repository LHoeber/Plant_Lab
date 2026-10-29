// The 3D console's screen. Two modes, blended by _FeedAmount:
//   1 = camera feed: the observation, unlit (like a real display, independent of the console's lighting)
//   0 = between trials: black with animated static and a slowly rolling bright band, and the screen texts (_UITex)
// The static overlay (vignette, glare, scanlines) lies on top of both. Two-sided, so the mesh's facing doesn't matter.
Shader "BotanicLab/ConsoleScreen"
{
    Properties
    {
        _MainTex ("Observation", 2D) = "black" {}
        _OverlayTex ("Overlay (rgb color, a amount)", 2D) = "black" {}
        _UITex ("Screen texts (rgb, a)", 2D) = "black" {}
        _FeedAmount ("Feed (1) or texts (0)", Range(0, 1)) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Static ("Static noise amount", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_OverlayTex); SAMPLER(sampler_OverlayTex);
        TEXTURE2D(_UITex); SAMPLER(sampler_point_clamp);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _OverlayTex_ST;
            float4 _UITex_ST;
            float _FeedAmount;
            float _Brightness;
            float _Static;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings vert (Attributes i)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
            o.uv = i.uv;
            return o;
        }
        float Hash (float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (Varyings i) : SV_Target
            {
                half3 feed = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Brightness;

                //between trials: coarse animated static (changes 20 times per second), a rolling band, the texts
                float2 cell = floor(i.uv * float2(340, 260));
                float noise = Hash(cell + floor(_Time.y * 20.0) * 17.0);
                float band = smoothstep(0.0, 0.06, 0.06 - abs(frac(i.uv.y * 0.7 - _Time.y * 0.15) - 0.5));
                half4 ui = SAMPLE_TEXTURE2D(_UITex, sampler_point_clamp, i.uv);
                half3 screen = noise * _Static * 0.35 + band * 0.03;
                screen = lerp(screen, ui.rgb, ui.a);

                half3 c = lerp(screen, feed, _FeedAmount);
                half4 o = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, i.uv);
                return half4(lerp(c, o.rgb, o.a), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
