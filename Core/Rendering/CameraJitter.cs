using System.Numerics;
using T3.Core.Operator;

namespace T3.Core.Rendering;

/// <summary>
/// Applies the TXAA sub-pixel camera jitter, published as <see cref="RenderPassState.JitterOffset"/> (in pixels, set by
/// [TXAA] through [SetRenderPassState]), to a projection matrix.
/// </summary>
public static class CameraJitter
{
    /// <summary>
    /// Adds the jitter as a constant NDC translation (a tiny lens shift) rather than an eye
    /// offset — moving the eye would cause depth-dependent parallax the TAA resolve can't undo.
    /// </summary>
    public static void ApplyFromContext(EvaluationContext context, ref Matrix4x4 camToClipSpace)
    {
        var jitterInPixels = context.RenderPass.JitterOffset;
        if (jitterInPixels == Vector2.Zero)
            return;

        camToClipSpace.M31 += jitterInPixels.X * 2f / context.RequestedResolution.Width;
        camToClipSpace.M32 += jitterInPixels.Y * 2f / context.RequestedResolution.Height;
    }
}
