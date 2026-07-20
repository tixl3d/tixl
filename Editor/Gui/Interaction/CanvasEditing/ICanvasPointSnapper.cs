#nullable enable
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// Optional snapping pass for a dragged <see cref="CanvasPointHandle"/>. Returns a
/// possibly-snapped canvas position (the input unchanged when nothing snaps) and may draw snap
/// guides. Real implementations (point→point, grid, axis-align) are expected to build on the
/// shared <see cref="Snapping.SnapResult"/> model, one axis at a time. None exist yet — the seam
/// lives here so composers can opt in later without reworking the primitive.
/// </summary>
internal interface ICanvasPointSnapper
{
    Vector2 TrySnap(Vector2 candidateInCanvas, ICanvasProjection projection);
}
