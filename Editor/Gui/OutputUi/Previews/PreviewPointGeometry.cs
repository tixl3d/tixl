#nullable enable
using T3.Core.DataTypes;

namespace T3.Editor.Gui.OutputUi.Previews;

/// <summary>
/// A geometry with one loose point, e.g. the hovered row of a point table, for a preview op to highlight (see
/// <see cref="T3.Core.Rendering.PreviewOverlayState.Hovered"/>). A new instance only when the position changes.
/// </summary>
internal sealed class PreviewPointGeometry
{
    public Geometry? Get(bool hasPoint, Vector3 position)
    {
        if (!hasPoint)
            return null;

        if (_geometry != null && position == _position)
            return _geometry;

        _position = position;
        _geometry = new Geometry { Positions = [position] };
        _geometry.InvalidateTopologyCaches();
        return _geometry;
    }

    private Geometry? _geometry;
    private Vector3 _position;
}
