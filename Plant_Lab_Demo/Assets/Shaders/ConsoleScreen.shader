// The 3D console's screen: shows the observation unlit (like a real display, independent of the console's
// lighting), with the static overlay (vignette, glare, scanlines) on top. Two-sided, so the screen mesh's
// facing direction doesn't matter.
Shader "BotanicLab/ConsoleScreen"
{
    Properties
    {
        _MainTex ("Observation", 2D) = "black" {}
        _OverlayTex ("Overlay (rgb color, a amount)", 2D) = "black" {}
        _Brightness ("Brightness", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_OverlayTex); SAMPLER(sampler_OverlayTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _OverlayTex_ST;
            float _Brightness;
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
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Brightness;
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
