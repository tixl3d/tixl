#nullable enable
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Keeps the pixels on a wall where they are while its rectangle is edited. A surface or region shows one
/// slice stretched over its whole rectangle, so a plain crop of the rectangle would re-fit the content; these
/// helpers co-edit the slice's UV so the window shrinks over unmoved pixels (crop), or slides the UV under a
/// fixed window (pan). Rect bounds are Y-up (the surface and parent spaces), UVs Y-down (the texture), so the
/// top edge of the rect meets the UV's top row.
/// </summary>
internal static class CropHandling
{
    /// <summary>
    /// The slice a content edit will write, and its UV at the press. A slice that also feeds other surfaces or
    /// patches is cloned for it first, so a local crop can't move pixels elsewhere; the shared slice stays reachable
    /// on the content card. False when the surface shows nothing — the rect edit then runs alone.
    /// </summary>
    public static bool TryBegin(Setup setup, Surface surface, out Guid sliceId, out Vector4 uvStart)
    {
        sliceId = Guid.Empty;
        uvStart = default;
        var slice = setup.FindSlice(surface.SliceId);
        if (slice == null)
            return false;

        if (SetupRelations.CountConsumersOfSlice(setup, slice.Id) > 1)
            slice = SetupActions.CloneSliceForSurface(setup, surface, slice);

        sliceId = slice.Id;
        uvStart = slice.UvRect;
        return true;
    }

    /// <summary>Re-derives the UV from the pre-drag rect and UV, so the cropped window shows exactly the pixels it covered.</summary>
    public static void ApplyCrop(Setup setup, Guid sliceId, Vector4 uvStart, Vector2 oldMin, Vector2 oldMax, Vector2 newMin, Vector2 newMax)
    {
        var slice = setup.FindSlice(sliceId);
        if (slice == null)
            return;

        var oldSize = oldMax - oldMin;
        if (oldSize.X <= 0.00001f || oldSize.Y <= 0.00001f)
            return;

        var uvWidth = uvStart.Z - uvStart.X;
        var uvHeight = uvStart.W - uvStart.Y;

        // Fractions of the old rect: X from the left, Y from the top (Y-up rect, Y-down UV).
        var left = (newMin.X - oldMin.X) / oldSize.X;
        var right = (newMax.X - oldMin.X) / oldSize.X;
        var top = (oldMax.Y - newMax.Y) / oldSize.Y;
        var bottom = (oldMax.Y - newMin.Y) / oldSize.Y;

        slice.UvRect = new Vector4(uvStart.X + left * uvWidth,
                                   uvStart.Y + top * uvHeight,
                                   uvStart.X + right * uvWidth,
                                   uvStart.Y + bottom * uvHeight);
    }

    /// <summary>
    /// Slides the UV under a fixed window by a drag <paramref name="delta"/> in the rect's units: dragging the
    /// content right shows more of the source's left. The window never leaves the source.
    /// </summary>
    public static void ApplyPan(Setup setup, Guid sliceId, Vector4 uvStart, Vector2 delta, Vector2 rectSize)
    {
        var slice = setup.FindSlice(sliceId);
        if (slice == null || rectSize.X <= 0.00001f || rectSize.Y <= 0.00001f)
            return;

        var uvWidth = uvStart.Z - uvStart.X;
        var uvHeight = uvStart.W - uvStart.Y;
        var shiftX = -delta.X / rectSize.X * uvWidth;
        var shiftY = delta.Y / rectSize.Y * uvHeight;

        shiftX = Math.Clamp(shiftX, -uvStart.X, 1f - uvStart.Z);
        shiftY = Math.Clamp(shiftY, -uvStart.Y, 1f - uvStart.W);

        slice.UvRect = new Vector4(uvStart.X + shiftX, uvStart.Y + shiftY, uvStart.Z + shiftX, uvStart.W + shiftY);
    }
}
