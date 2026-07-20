#nullable enable
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.CanvasEditing;

/// <summary>
/// The one thing a canvas-editing widget needs from its view: mapping a 2D editing-space point to
/// the screen and back. This is deliberately backend-neutral — a pan/zoom
/// <see cref="ScalableCanvas"/> (via <see cref="ScalableCanvasProjection"/>) satisfies it today,
/// and an orthographic or perspective camera view (point on a plane through view·proj, ray-plane
/// inverse) can satisfy it later without any widget change.
/// </summary>
internal interface ICanvasProjection
{
    /// <summary>Editing-space position → screen pixels.</summary>
    Vector2 CanvasToScreen(Vector2 posInCanvas);

    /// <summary>Screen pixels → editing-space position.</summary>
    Vector2 ScreenToCanvas(Vector2 posOnScreen);
}
