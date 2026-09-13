#nullable enable
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// A surface's rectangle at a gesture's press — its size, position, anchor and every corner-pin quad, all by
/// value. A live edge drag re-bases from it each frame: cropping rewrites the surface's bounds, so editing the
/// live rectangle incrementally would feed back on itself. The rectified view freezes its basis to it as well.
/// </summary>
internal sealed class SurfaceRectSnapshot
{
    public SurfaceRectSnapshot(Surface surface)
    {
        Size = surface.SizeInMeters;

        // A Layout child's rectangle is size + where it sits in its parent, so both travel together.
        LocalPosition = surface.LocalPosition;

        // A crop re-derives the anchor so the origin stays put, so it has to be re-based with the rectangle.
        Anchor = surface.Anchor;

        _quads = new (Guid, Vector2[])[surface.OutputMappings.Count];
        for (var i = 0; i < surface.OutputMappings.Count; i++)
        {
            var mapping = surface.OutputMappings[i];
            _quads[i] = (mapping.OutputId, (Vector2[])mapping.Quad.Clone());
        }
    }

    public readonly Vector2 Size;
    public readonly Vector2 LocalPosition;
    public readonly Vector2 Anchor;

    public bool TryGetQuad(Guid outputId, out Vector2[] quad)
    {
        for (var i = 0; i < _quads.Length; i++)
        {
            if (_quads[i].OutputId == outputId)
            {
                quad = _quads[i].Quad;
                return true;
            }
        }

        quad = [];
        return false;
    }

    /// <summary>Puts the snapshot back onto the surface. Called per drag frame, so no allocations.</summary>
    public void Restore(Surface surface)
    {
        surface.SizeInMeters = Size;
        surface.LocalPosition = LocalPosition;
        surface.Anchor = Anchor;

        for (var i = 0; i < _quads.Length; i++)
        {
            var (outputId, quad) = _quads[i];
            if (quad.Length < 4)
                continue;

            var mappings = surface.OutputMappings;
            for (var m = 0; m < mappings.Count; m++)
            {
                if (mappings[m].OutputId != outputId || mappings[m].Quad.Length < 4)
                    continue;

                Array.Copy(quad, mappings[m].Quad, 4);
                break;
            }
        }
    }

    private readonly (Guid OutputId, Vector2[] Quad)[] _quads;
}
