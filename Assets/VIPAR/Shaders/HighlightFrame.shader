// A rectangle drawn around a detected object: bright corner brackets, a faint connecting
// outline and a light tint. Drawn with shader math so lines stay crisp at any box size.
Shader "VIPAR/Highlight Frame"
{
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.9, 1, 1)
        _Size ("Size in Meters", Vector) = (1, 1, 0, 0)
        _Thickness ("Line Thickness in Meters", Float) = 0.004
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.1
        _OutlineAlpha ("Outline Alpha", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "HighlightFrame"
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Size;
                float _Thickness;
                float _FillAlpha;
                float _OutlineAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Distance in meters from this pixel to the nearest vertical and horizontal edges.
                float2 size = max(_Size.xy, 0.0001);
                float2 edge = (0.5 - abs(input.uv - 0.5)) * size;
                float nearestEdge = min(edge.x, edge.y);
                float antialias = max(fwidth(nearestEdge), 0.00001);

                float thin = 1.0 - smoothstep(_Thickness * 0.4, _Thickness * 0.4 + antialias, nearestEdge);
                float thick = 1.0 - smoothstep(_Thickness, _Thickness + antialias, nearestEdge);

                float bracketLength = min(min(size.x, size.y) * 0.25, 0.25);
                float inCorner = step(edge.x, bracketLength) * step(edge.y, bracketLength);

                float alpha = _FillAlpha;
                alpha = max(alpha, thin * _OutlineAlpha);
                alpha = max(alpha, thick * inCorner);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
