namespace T3.Core.Rendering;

/// <summary>
/// What the pass currently being rendered asks of the ops evaluated inside it. Ops that render a pass (e.g. [RenderTarget])
/// set the members that concern them on <see cref="T3.Core.Operator.EvaluationContext.RenderPass"/> before evaluating
/// their subtree and restore the previous state afterwards, so nested passes don't leak into their siblings.
/// Graphs read it with [GetRenderPassState].
/// </summary>
/// <remarks>
/// Engine state belongs here as typed members rather than in string-named context variables.
/// </remarks>
public struct RenderPassState
{
    /// <summary>
    /// Index of this evaluation among repeats of the same frame by motion blur ([RenderWithMotionBlur],
    /// [RepeatWithMotionBlur]). 0 is the frame itself.
    /// </summary>
    public int RepeatPass;

    /// <summary>Face being rendered by [RenderToCubeMap], 0–5; 0 outside a cube map.</summary>
    public int CubeFace;

    /// <summary>
    /// This evaluation repeats the current frame (a later motion blur sample or cube face). Ops that keep state over
    /// time (dampers, springs, simulations, audio reactions) must not advance it again.
    /// </summary>
    public readonly bool IsRepeatOfFrame => RepeatPass > 0 || CubeFace > 0;

    /// <summary>
    /// This evaluation is a subframe of [RepeatFrame]: the frame is rendered as several samples in time order, ending at
    /// the frame time, and state advances by <see cref="SubframeStepSec"/> in each instead of a whole frame.
    /// </summary>
    public bool IsSubframe;

    /// <summary>Time since the previous subframe (for the first, since the previous frame), in seconds.</summary>
    public double SubframeStepSec;

    /// <summary>
    /// How far ops that integrate over time (damping, springs) advance in this evaluation, in seconds: a subframe's step,
    /// nothing for further cube faces and other repeats, otherwise the whole frame.
    /// </summary>
    public readonly double GetStepDurationSec(double frameDurationSec)
    {
        if (CubeFace > 0)
            return 0;

        if (IsSubframe)
            return SubframeStepSec;

        return RepeatPass > 0 ? 0 : frameDurationSec;
    }

    /// <summary>
    /// Context variable names that older graphs use for <see cref="RepeatPass"/> and <see cref="CubeFace"/>. [Loop] and
    /// [RenderToCubeMap] still write them so graphs reading them with [GetIntVar] keep working.
    /// </summary>
    public const string LegacyRepeatPassVariable = "__MotionBlurPass";

    public const string LegacyCubeFaceVariable = "CubeFace";

    /// <summary>
    /// The render target being drawn has a velocity buffer. Draws then keep the history they need to write motion
    /// (e.g. last frame's points), which they can skip otherwise.
    /// </summary>
    public bool VelocityRequested;
}
