#nullable enable
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;

namespace T3.Editor.Gui.OutputUi.Previews;

/// <summary>
/// Projects world positions onto a rendered preview in the output window, with the camera and resolution the preview
/// was rendered with, the way transform gizmos do. Used to draw overlays that line up with the render.
/// </summary>
internal sealed class PreviewProjection
{
    /// <summary>Call while the preview renders.</summary>
    public void CaptureView(EvaluationContext context)
    {
        _objectToClip = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace;
        _resolution = context.RequestedResolution;
    }

    /// <summary>
    /// Projects into <paramref name="screenRect"/> instead of the output canvas, for a render drawn into a part of the
    /// window (see <see cref="PreviewSidebar"/>); null returns to the canvas.
    /// </summary>
    public void SetTargetRect(ImRect? screenRect)
    {
        _targetRect = screenRect;
    }

    /// <summary>Call before projecting in a draw pass.</summary>
    /// <returns>False if there is no output canvas or no captured view.</returns>
    public bool BeginDraw()
    {
        if (_resolution.Width <= 0 || _resolution.Height <= 0)
            return false;

        if (_targetRect is { } rect)
        {
            _topLeft = rect.Min;
            _rectScale = rect.GetSize() / new Vector2(_resolution.Width, _resolution.Height);
            _canvas = null;
            return true;
        }

        _canvas = ImageOutputCanvas.Current;
        if (_canvas == null)
            return false;

        _topLeft = _canvas.TransformPosition(Vector2.Zero);
        return true;
    }

    /** Positions behind the camera can't be drawn and are skipped. */
    public bool TryProject(Vector3 position, out Vector2 screenPosition)
    {
        var clip = Vector4.Transform(new Vector4(position, 1), _objectToClip);
        if (clip.W <= MinClipW || (_canvas == null && _targetRect == null))
        {
            screenPosition = default;
            return false;
        }

        var inViewport = new Vector2(_resolution.Width * (clip.X / clip.W * 0.5f + 0.5f),
                                     _resolution.Height * (0.5f - clip.Y / clip.W * 0.5f));
        screenPosition = _topLeft + (_canvas != null ? _canvas.TransformDirection(inViewport) : inViewport * _rectScale);
        return true;
    }

    private const float MinClipW = 1e-5f;

    private Matrix4x4 _objectToClip = Matrix4x4.Identity;
    private Int2 _resolution;
    private ImageOutputCanvas? _canvas;
    private Vector2 _topLeft;
    private ImRect? _targetRect;
    private Vector2 _rectScale = Vector2.One;
}
