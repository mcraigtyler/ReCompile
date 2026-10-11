// The desk under the play mat: a dark console surface with a faint grid, a soft glow under the mat,
// and a contact shadow around the mat's edge. Everything is computed in world space (table units),
// so it stays crisp at any camera distance or angle.
Shader "Compile/TableSurface"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.028, 0.027, 0.042, 1)
        _EdgeColor ("Far edge", Color) = (0.012, 0.012, 0.02, 1)
        _GridColor ("Grid", Color) = (0.62, 0.92, 1, 0.06)
        _MajorColor ("Major grid", Color) = (0.8, 0.55, 1, 0.1)
        _GlowColor ("Glow under mat", Color) = (0.42, 0.2, 0.62, 0.18)
        _GridSize ("Grid size (units)", Float) = 1
        _MajorEvery ("Major line every", Float) = 5
        _GlowRadius ("Glow radius", Float) = 21
        _FadeRadius ("Fade radius", Float) = 70
        _MatHalfSize ("Mat half size", Vector) = (6.99, 18.94, 0, 0)
        _ShadowWidth ("Mat shadow width", Float) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "TableSurface"
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                half4 _GridColor;
                half4 _MajorColor;
                half4 _GlowColor;
                float _GridSize;
                float _MajorEvery;
                float _GlowRadius;
                float _FadeRadius;
                float4 _MatHalfSize;
                float _ShadowWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                return output;
            }

            // Anti-aliased grid lines about `width` pixels wide; fades out where cells get smaller than a few pixels.
            float Grid(float2 p, float size, float width)
            {
                float2 coord = p / size;
                float2 derivative = fwidth(coord);
                float2 distanceToLine = abs(frac(coord - 0.5) - 0.5) / max(derivative, 1e-5);
                float lineMask = 1.0 - saturate(min(distanceToLine.x, distanceToLine.y) / width);
                float visibility = saturate(1.0 - max(derivative.x, derivative.y) * 6.0);
                return lineMask * visibility;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xy;
                float centerDistance = length(p);

                half3 color = lerp(_BaseColor.rgb, _EdgeColor.rgb, saturate(centerDistance / _FadeRadius));
                color += _GlowColor.rgb * _GlowColor.a * exp(-(centerDistance * centerDistance) / (_GlowRadius * _GlowRadius));

                // Fine noise so the surface reads as a material rather than flat color.
                color += (Hash(floor(p * 48.0)) - 0.5) * 0.008;

                color = lerp(color, _GridColor.rgb, Grid(p, _GridSize, 1.0) * _GridColor.a);
                color = lerp(color, _MajorColor.rgb, Grid(p, _GridSize * _MajorEvery, 1.4) * _MajorColor.a);

                // Contact shadow just outside the mat.
                float2 q = abs(p) - _MatHalfSize.xy;
                float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
                color *= lerp(0.45, 1.0, saturate(outside / _ShadowWidth));

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
