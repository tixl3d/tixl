#nullable enable
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// Backs <see cref="ICanvasProjection"/> with a pan/zoom <see cref="ScalableCanvas"/>, keeping the
/// canvas as the single source of the transform (and its fit/zoom/transition features) rather than
/// re-deriving it. A class so it can be cached and passed to widgets without boxing; wrap the
/// canvas once and reuse it every frame.
/// </summary>
internal sealed class ScalableCanvasProjection : ICanvasProjection
{
    public ScalableCanvasProjection(ScalableCanvas canvas)
    {
        _canvas = canvas;
    }

    public Vector2 CanvasToScreen(Vector2 posInCanvas) => _canvas.TransformPositionFloat(posInCanvas);
    public Vector2 ScreenToCanvas(Vector2 posOnScreen) => _canvas.InverseTransformPositionFloat(posOnScreen);

    private readonly ScalableCanvas _canvas;
}
