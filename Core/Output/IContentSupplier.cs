#nullable enable
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Output;

/// <summary>
/// A graph node that supplies pixels to the output setup. It does no drawing: it registers itself with the
/// <see cref="ContentSupplierRegistry"/> so the host's output manager can pull the content and composite it.
/// Routing (which surface shows what) is setup data keyed to this op's SymbolChild, not state on the op.
/// </summary>
public interface IContentSupplier
{
    Vector4 GetColor(EvaluationContext context);
    Texture2D? GetContent(EvaluationContext context);

    /// <summary>When false the host stops invalidating this content, freezing it at its last frame.</summary>
    bool GetUpdateEnabled(EvaluationContext context);

    /// <summary>The op input behind <see cref="GetUpdateEnabled"/>, so a host edits it through its own undoable
    /// input command rather than writing the slot directly.</summary>
    IInputSlot UpdateInput { get; }

    /// <summary>
    /// The resolution this content is rendered at, or 0×0 to inherit the one the host asks for — the canvas of
    /// the output it ends up on. Inheriting is the default, so a chain of auto-sized render targets follows the
    /// projector or display it is routed to instead of carrying a size of its own.
    /// </summary>
    Int2 GetResolution(EvaluationContext context);

    /// <summary>The op input behind <see cref="GetResolution"/>; see <see cref="UpdateInput"/>.</summary>
    IInputSlot ResolutionInput { get; }

    /// <summary>Marks the content input graph dirty so a following <see cref="GetContent"/> re-evaluates
    /// time-dependent upstream ops (the manager pulls content manually, outside the normal output path).</summary>
    void InvalidateContent();
}
