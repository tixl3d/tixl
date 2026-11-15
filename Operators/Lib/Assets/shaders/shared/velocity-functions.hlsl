// Screen-space motion for temporal effects (TXAA, motion blur, reprojection).
//
// [RenderTarget] with WithVelocityBuffer binds an RGBA16F target as SV_Target2 and clears it to 0:
//   rg  current uv - previous uv without the projection jitter of temporal AA, so a still surface reads 0
//   b   1 - this surface's depth when written, to detect that a draw without velocity covered it later
//   a   1 when written, 0 = no motion known (pixel falls back to camera-only reprojection)
// Draw ops blend all targets like the color target, so alpha stays 1: alpha blending then overwrites, and additive
// blending corrupts b, which the depth check rejects. Depth is stored as 1 - depth: scene depth crowds near 1, where
// half floats step by ~0.0005, but near 0 they are precise.
// An unbound velocity map also reads 0, so consumers behave as before when nothing writes velocity.

/** Relative difference of (1 - depth) above which the stored velocity belongs to a surface that was covered later. */
static const float VelocityDepthTolerance = 0.002;

float2 ClipToUv(float4 clipPosition)
{
    float2 ndc = clipPosition.xy / clipPosition.w;
    return float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

/** The sub-pixel jitter (and lens shift) a projection adds, in uv. It sits in the NDC translation terms. */
float2 JitterUv(float4x4 cameraToClipSpace)
{
    return float2(cameraToClipSpace._31, -cameraToClipSpace._32) * 0.5;
}

/**
 * For draw shaders: clip positions of the same vertex this frame and last frame, and both frames' projections.
 * Without previous transforms (an unbound cbuffer reads 0) it reports no motion known.
 */
float4 EncodeVelocity(float4 clipPosition, float4 previousClipPosition,
                      float4x4 cameraToClipSpace, float4x4 previousCameraToClipSpace)
{
    if (previousClipPosition.w <= 1e-6)
        return 0;

    // The jitter moves the projected position by -JitterUv; adding the change back leaves only real motion.
    float2 jitteredVelocity = ClipToUv(clipPosition) - ClipToUv(previousClipPosition);
    float2 velocity = jitteredVelocity + JitterUv(cameraToClipSpace) - JitterUv(previousCameraToClipSpace);
    return float4(velocity, 1 - clipPosition.z / clipPosition.w, 1);
}

/** Screen distance per frame, in uv, beyond which a point's motion counts as a jump (a respawned or reordered point). */
static const float MaxPointJumpUv = 0.25;

/**
 * For point draws: the previous clip position of a vertex, or none (w = 0, motion unknown) when the point's center
 * jumped further than real motion would, so recycled particles don't streak across the screen.
 */
float4 PreviousClipUnlessJumped(float4 previousClipPosition, float4 centerClipPosition, float4 previousCenterClipPosition)
{
    if (centerClipPosition.w <= 1e-6 || previousCenterClipPosition.w <= 1e-6)
        return 0;

    float2 jump = ClipToUv(centerClipPosition) - ClipToUv(previousCenterClipPosition);
    return dot(jump, jump) > MaxPointJumpUv * MaxPointJumpUv ? 0 : previousClipPosition;
}

/** True if the stored motion belongs to the surface now visible at this pixel (sceneDepth from the depth buffer). */
bool TryDecodeVelocity(float4 encoded, float sceneDepth, out float2 velocity)
{
    velocity = encoded.rg;
    float inverseSceneDepth = 1 - sceneDepth;
    return encoded.a > 0.5 && abs(encoded.b - inverseSceneDepth) <= VelocityDepthTolerance * inverseSceneDepth + 1e-7;
}
