using System.Numerics;
using T3.Core.Operator;

namespace T3.Core.Rendering;

/// <summary>
/// Applies the TXAA sub-pixel camera jitter published via the <c>_TXAA_JitterOffset</c> context
/// variable (in pixels, e.g. from [Vec2JitterOffset]) to a projection matrix.
/// </summary>
public static class CameraJitter
{
    /// <summary>
    /// Adds the jitter as a constant NDC translation (a tiny lens shift) rather than an eye
    /// offset — moving the eye would cause depth-dependent parallax the TAA resolve can't undo.
    /// </summary>
    public static void ApplyFromContext(EvaluationContext context, ref Matrix4x4 camToClipSpace)
    {
        if (!context.ObjectVariables.TryGetValue(VariableName, out var jitterValue))
            return;

        var jitterInPixels = jitterValue switch
                                 {
                                     Vector2 v2 => v2,
                                     Vector3 v3 => new Vector2(v3.X, v3.Y),
                                     _          => Vector2.Zero
                                 };
        camToClipSpace.M31 += jitterInPixels.X * 2f / context.RequestedResolution.Width;
        camToClipSpace.M32 += jitterInPixels.Y * 2f / context.RequestedResolution.Height;
    }

    private const string VariableName = "_TXAA_JitterOffset";
}
