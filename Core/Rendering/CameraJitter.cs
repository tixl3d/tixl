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
    /// <returns>The offset added, for <see cref="RenderPassState.ProjectionJitter"/>.</returns>
    public static Vector2 ApplyFromContext(EvaluationContext context, ref Matrix4x4 camToClipSpace)
    {
        var jitterInPixels = context.RenderPass.JitterOffset;
        if (jitterInPixels == Vector2.Zero)
            return Vector2.Zero;

        var jitter = new Vector2(jitterInPixels.X * 2f / context.RequestedResolution.Width,
                                 jitterInPixels.Y * 2f / context.RequestedResolution.Height);
        camToClipSpace.M31 += jitter.X;
        camToClipSpace.M32 += jitter.Y;
        return jitter;
    }

    /// <summary>
    /// The context's projection without the jitter its camera added. Overlays drawn on the resolved image (gizmos) use
    /// it, since the resolve removes the jitter from the image.
    /// </summary>
    public static Matrix4x4 GetUnjitteredCameraToClipSpace(EvaluationContext context)
    {
        var camToClipSpace = context.CameraToClipSpace;
        var jitter = context.RenderPass.ProjectionJitter;
        camToClipSpace.M31 -= jitter.X;
        camToClipSpace.M32 -= jitter.Y;
        return camToClipSpace;
    }
}
