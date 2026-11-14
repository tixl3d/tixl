// Screen-space motion for temporal effects (TXAA, motion blur, reprojection).
//
// [RenderTarget] with WithVelocityBuffer binds an RGBA16F target as SV_Target2 and clears it to 0:
//   rg  current uv - previous uv (as the jittered matrices give it; consumers remove the jitter delta)
//   b   this surface's depth when written, to detect that a draw without velocity covered it later
//   a   this surface's depth one frame earlier, 0 = no motion known (pixel falls back to camera-only reprojection)
// An unbound velocity map also reads 0, so consumers behave as before when nothing writes velocity.

/** Depth difference above which the stored velocity belongs to a surface that has since been covered. */
static const float VelocityDepthTolerance = 0.0001;

float2 ClipToUv(float4 clipPosition)
{
    float2 ndc = clipPosition.xy / clipPosition.w;
    return float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

/** For draw shaders: clip positions of the same vertex this frame and last frame. */
float4 EncodeVelocity(float4 clipPosition, float4 previousClipPosition)
{
    float2 velocity = ClipToUv(clipPosition) - ClipToUv(previousClipPosition);
    float depth = clipPosition.z / clipPosition.w;
    float previousDepth = max(previousClipPosition.z / previousClipPosition.w, 1e-6);
    return float4(velocity, depth, previousDepth);
}

/** True if the stored motion belongs to the surface now visible at this pixel (sceneDepth from the depth buffer). */
bool TryDecodeVelocity(float4 encoded, float sceneDepth, out float2 velocity, out float previousDepth)
{
    velocity = encoded.rg;
    previousDepth = encoded.a;
    return encoded.a > 0 && abs(encoded.b - sceneDepth) < VelocityDepthTolerance;
}
