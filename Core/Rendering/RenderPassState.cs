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
    /// The render target being drawn has a velocity buffer. Draws then keep the history they need to write motion
    /// (e.g. last frame's points), which they can skip otherwise.
    /// </summary>
    public bool VelocityRequested;
}
