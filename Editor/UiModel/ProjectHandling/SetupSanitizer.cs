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

            foreach (var mapping in surface.OutputMappings)
            {
                if (QuadIsUsable(mapping.Quad))
                    continue;

                var output = setup.FindOutput(mapping.OutputId);
                Log.Warning($"Setup repair: surface '{surface.Name}' had a corrupted corner-pin on output "
                            + $"'{output?.Name ?? mapping.OutputId.ToString()}' — reset to a default centered quad.");
                mapping.Quad = DefaultQuad();
                changed = true;
            }
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (QuadIsUsable(patch.Quad))
                    continue;

                Log.Warning($"Setup repair: a patch on output '{output.Name}' had a corrupted quad — reset to the full canvas.");
                patch.Quad = OutputDefinition.FullCanvasQuad();
                changed = true;
            }
        }

        return changed;
    }

    /// <param name="quad">In the canvas' own 0..1 space, so the bounds below are canvas-sizes, not pixels.</param>
    private static bool QuadIsUsable(Vector2[] quad)
    {
        if (quad.Length < 4)
            return false;

        // Generous overhang: a projector quad legitimately extends past the canvas, but corners further out
        // than a few canvas sizes make the recovered meters↔canvas projection numerically useless — and every
        // edit through it amplifies the damage.
        var min = new Vector2(-3, -3);
        var max = new Vector2(4, 4);
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

    /// <summary>A centred fifth-inset rectangle in the canvas' 0..1 space.</summary>
    private static Vector2[] DefaultQuad()
    {
        return [new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.8f, 0.8f), new Vector2(0.2f, 0.8f)];
    }

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
