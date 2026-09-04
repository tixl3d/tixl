#nullable enable
using System.Numerics;
using T3.Core.Logging;
using T3.Core.Output;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// Repairs setup data that would break the editor's projection math. Setups are user-editable JSON on
/// disk, so any loader must assume hostile input — hand edits, other tools, or leftovers from old bugs.
/// Values that make the recovered projection numerically useless (non-finite, absurdly out of range,
/// degenerate quads) are force-reset to safe defaults, each with a warning naming what was fixed; merely
/// unusual values (overhanging quads, out-of-rect pivots from crops) are left alone.
/// </summary>
internal static class SetupSanitizer
{
    /// <summary>Returns true when something had to be repaired (the caller should persist the setup).</summary>
    public static bool Sanitize(Setup setup)
    {
        var changed = false;

        foreach (var surface in setup.Surfaces)
        {
            if (!IsFinite(surface.SizeInMeters) || surface.SizeInMeters.X <= 0 || surface.SizeInMeters.Y <= 0)
            {
                Log.Warning($"Setup repair: surface '{surface.Name}' had an invalid size ({surface.SizeInMeters.X}, {surface.SizeInMeters.Y}) — reset to 1×1 m.");
                surface.SizeInMeters = new Vector2(1, 1);
                changed = true;
            }

            if (!IsFinite(surface.LocalPosition))
            {
                Log.Warning($"Setup repair: surface '{surface.Name}' had an invalid position — reset to its parent's anchor.");
                surface.LocalPosition = Vector2.Zero;
                changed = true;
            }

            if (!IsFinite(surface.Anchor))
            {
                Log.Warning($"Setup repair: surface '{surface.Name}' had an invalid anchor — reset to the bottom-centre.");
                surface.Anchor = Surface.DefaultAnchor;
                changed = true;
            }

            // Only Regions (Layout kind) may be children. A Physical surface nested under another surface is
            // contradictory — it claims its own plane while riding a parent's — so it is detached back to a
            // root, keeping its mappings and placement intact.
            if (surface.Kind != Surface.SurfaceKinds.Layout && surface.ParentId != Guid.Empty)
            {
                Log.Warning($"Setup repair: surface '{surface.Name}' was nested under another surface — detached to a root (only regions nest).");
                surface.ParentId = Guid.Empty;
                changed = true;
            }

            // A Layout child rides its carrier's corner pin — a mapping of its own silently turns it back
            // into an independently editable surface (hierarchy corruption, e.g. from older builds that
            // allowed mapping a region directly).
            if (surface.Kind == Surface.SurfaceKinds.Layout && surface.ParentId != Guid.Empty
                && surface.OutputMappings.Count > 0)
            {
                Log.Warning($"Setup repair: sub-region '{surface.Name}' carried {surface.OutputMappings.Count} corner-pin mapping(s) of its own — removed (regions ride their parent's mapping).");
                surface.OutputMappings.Clear();
                changed = true;
            }

            foreach (var mapping in surface.OutputMappings)
            {
                var output = setup.FindOutput(mapping.OutputId);
                var width = Math.Max(1, output?.CanvasResolution.Width ?? 1920);
                var height = Math.Max(1, output?.CanvasResolution.Height ?? 1080);
                if (QuadIsUsable(mapping.Quad, width, height))
                    continue;

                Log.Warning($"Setup repair: surface '{surface.Name}' had a corrupted corner-pin on output "
                            + $"'{output?.Name ?? mapping.OutputId.ToString()}' — reset to a default centered quad.");
                mapping.Quad = DefaultQuad(width, height);
                changed = true;
            }
        }

        foreach (var output in setup.Outputs)
        {
            var width = Math.Max(1, output.CanvasResolution.Width);
            var height = Math.Max(1, output.CanvasResolution.Height);
            foreach (var patch in output.Patches)
            {
                if (QuadIsUsable(patch.Quad, width, height))
                    continue;

                Log.Warning($"Setup repair: a patch on output '{output.Name}' had a corrupted quad — reset to the full canvas.");
                patch.Quad = output.FullCanvasQuad();
                changed = true;
            }
        }

        return changed;
    }

    private static bool QuadIsUsable(Vector2[] quad, float width, float height)
    {
        if (quad.Length < 4)
            return false;

        // Generous overhang: a projector quad legitimately extends past the canvas, but corners further out
        // than a few canvas sizes make the recovered meters↔pixels projection numerically useless — and every
        // edit through it amplifies the damage.
        var min = new Vector2(-3 * width, -3 * height);
        var max = new Vector2(4 * width, 4 * height);
        for (var i = 0; i < 4; i++)
        {
            if (!IsFinite(quad[i]))
                return false;

            if (quad[i].X < min.X || quad[i].X > max.X || quad[i].Y < min.Y || quad[i].Y > max.Y)
                return false;
        }

        // Collinear/degenerate corners defeat the projection recovery entirely.
        Span<Vector2> unitRect = [Vector2.Zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)];
        return Homography.TryComputeQuadToQuad(unitRect, quad, out _);
    }

    private static Vector2[] DefaultQuad(float width, float height)
    {
        float x0 = width * 0.2f, x1 = width * 0.8f, y0 = height * 0.2f, y1 = height * 0.8f;
        return [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)];
    }

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
