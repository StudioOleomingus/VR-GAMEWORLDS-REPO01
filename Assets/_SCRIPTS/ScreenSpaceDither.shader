Shader "Hidden/Oleo/ScreenSpaceDither"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "ScreenSpaceDither"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #pragma multi_compile_local_fragment _PATTERN_BAYER _PATTERN_IGN _PATTERN_BLUENOISE
            #pragma multi_compile_local_fragment _ _USE_DEPTH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_BlueNoiseTex);
            float4 _BlueNoiseTex_TexelSize;

            float _Strength;
            float _Levels;
            float _PixelScale;
            float _LumaLow;
            float _LumaHigh;
            float _MaskDither;
            float _Desaturate;
            float _DepthInfluence;
            float _DepthNear;
            float _DepthFar;
            float _DepthInvert;
            float _SkyAmount;
            float _FrameIndex;   // already wrapped to [0, 64) on the CPU
            float _Animate;      // 0 = static, 1 = temporal offset

            // ---------------------------------------------------------------
            // Threshold generators.
            //
            // All of these are evaluated from an INTEGER pixel coordinate and
            // return a value in (0,1) whose mean is exactly 0.5. Nothing here
            // is ever sampled with a filter, so the pattern cannot be smeared,
            // resampled or mip-selected — which is where "banding" in a dither
            // texture normally comes from.
            // ---------------------------------------------------------------

            // Recursive Bayer. Bayer2 is periodic in 2, so feeding it small
            // coordinates keeps everything well inside fp16/fp32 precision.
            float Bayer2(float2 a)
            {
                a = floor(a);
                return frac(a.x * 0.5 + a.y * a.y * 0.75);
            }
            #define Bayer4(a) (Bayer2(0.5 * (a)) * 0.25 + Bayer2(a))
            #define Bayer8(a) (Bayer4(0.5 * (a)) * 0.25 + Bayer2(a))

            // Jimenez's interleaved gradient noise. No tile at all, so no tile
            // seam — but it carries a faint diagonal low-frequency structure.
            float InterleavedGradientNoise(float2 p)
            {
                return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
            }

            float DitherThreshold(float2 pixel)
            {
                #if defined(_PATTERN_BLUENOISE)
                    // Integer load, wrapped by the texture's own dimensions.
                    uint2 dim = (uint2)_BlueNoiseTex_TexelSize.zw;
                    uint2 c   = (uint2)pixel % max(dim, uint2(1, 1));
                    float v   = LOAD_TEXTURE2D(_BlueNoiseTex, c).r;
                    // An 8-bit texture stores i/255, whose mean is not 0.5.
                    // Remap to (i + 0.5)/256 to remove the DC bias — this bias
                    // is a common cause of a visible lightness step between
                    // dithered and undithered regions.
                    v = (v * 255.0 + 0.5) / 256.0;
                    v = frac(v + _Animate * frac(_FrameIndex * 0.61803398875));
                    return v;
                #elif defined(_PATTERN_IGN)
                    return InterleavedGradientNoise(pixel + _Animate * _FrameIndex * 5.588238);
                #else
                    // Bayer8 returns i/64, i in [0,63]. Offset by half a step so
                    // the 64 thresholds straddle 0.5 symmetrically.
                    return Bayer8(pixel) + (1.0 / 128.0);
                #endif
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv   = input.texcoord;
                float3 src  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                // Integer render-target pixel. SV_Position is pixel-centred at
                // +0.5, so floor() gives the exact texel index. Deriving this
                // from uv * _ScreenParams instead would drift under render
                // scale and dynamic resolution and reintroduce moire.
                float2 pixel = floor(input.positionCS.xy / max(1.0, floor(_PixelScale)));

                float t = DitherThreshold(pixel);

                // ---- luminance mask -------------------------------------
                // sqrt() ≈ gamma 2.0, so the two sliders behave perceptually
                // rather than bunching up in the darks.
                float luma     = sqrt(saturate(Luminance(src)));
                float darkMask = 1.0 - smoothstep(_LumaLow, _LumaHigh, luma);

                // ---- depth mask ------------------------------------------
                #if defined(_USE_DEPTH)
                    float raw = SampleSceneDepth(uv);
                    float eye = LinearEyeDepth(raw, _ZBufferParams);

                    #if UNITY_REVERSED_Z
                        float sky = step(raw, 1e-6);
                    #else
                        float sky = step(1.0 - 1e-6, raw);
                    #endif

                    float d = saturate((eye - _DepthNear) / max(1e-4, _DepthFar - _DepthNear));
                    d = d * d * (3.0 - 2.0 * d);             // smoothstep, no hard edge
                    d = lerp(d, 1.0 - d, _DepthInvert);
                    d = lerp(d, _SkyAmount, sky);            // skybox has no meaningful depth

                    float depthMask = lerp(1.0, d, _DepthInfluence);
                #else
                    float depthMask = 1.0;
                #endif

                float mask = darkMask * depthMask;

                // Self-dither the FADE itself with a decorrelated noise. The
                // boundary between dithered and clean regions is the one place
                // a contour will still appear; perturbing the mask by less than
                // one step breaks that contour into stipple instead.
                float m = InterleavedGradientNoise(pixel + 17.0);
                mask = saturate(mask + (m - 0.5) * _MaskDither);
                mask *= _Strength;

                // ---- quantisation ----------------------------------------
                float3 v = saturate(src);
                v = lerp(v, Luminance(v).xxx, _Desaturate);

                float  levels = max(2.0, floor(_Levels));
                float  steps  = levels - 1.0;

                // Ordered dither proper: the threshold is added to the value
                // BEFORE the floor, and its amplitude is exactly one
                // quantisation step. Any other amplitude produces either
                // residual banding (too small) or mush (too large).
                float3 q = floor(v * steps + t) / steps;

                return float4(lerp(src, q, mask), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
