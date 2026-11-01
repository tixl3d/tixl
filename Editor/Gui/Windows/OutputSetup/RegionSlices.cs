#nullable enable
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Cuts one picture the size of a surface into a slice per region, each from where the region sits on the surface,
/// so an image crossing from one region to the next lines up: a stack of old TV sets showing one picture. The
/// spatial counterpart of <see cref="WallpaperLayout"/>, which lines its walls up in a strip instead.
/// </summary>
internal static class RegionSlices
{
    /// <summary>The surface's direct regions. Nested ones stay out: they sit inside a region that already shows that area.</summary>
    public static void CollectRegions(Setup setup, Surface parent, List<Surface> regions)
    {
        regions.Clear();
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId == parent.Id)
                regions.Add(surface);
        }
    }

    /// <summary>The one content source the regions already show, which an update re-cuts without being selected.</summary>
    public static ContentSource? FindShownSource(Setup setup, IReadOnlyList<Surface> regions)
    {
        ContentSource? found = null;
        foreach (var region in regions)
        {
            if (setup.FindSlice(region.SliceId) is not { } slice)
                continue;

            var source = setup.FindSource(slice.SourceId);
            if (found != null && source != found)
                return null;

            found = source;
        }

        return found;
    }

    /// <summary>
    /// Shows a slice on every region, as one undo step together with the graph edits. Without
    /// <paramref name="source"/> a new SendToOutput op is added; with one, that source is re-cut and the slices
    /// its regions already show are kept, so their routing survives a re-run. Each slice takes its region's name,
    /// and the slices follow the regions' order.
    /// </summary>
    public static void Create(SetupEntitySelection selection, Setup setup, Surface parent, IReadOnlyList<Surface> regions, ContentSource? source)
    {
        if (regions.Count == 0)
            return;

        var pixelsPerMeter = WallpaperLayout.FindPixelsPerMeter(setup, regions);
        var size = parent.SizeInMeters;
        var maxPixelsPerMeter = OutputDefinition.MaxResolution / MathF.Max(MathF.Max(size.X, size.Y), SurfaceGeometry.MinSize);
        if (pixelsPerMeter > maxPixelsPerMeter)
        {
            Log.Warning($"Slices from regions: {size.X:0.##}×{size.Y:0.##} m at {pixelsPerMeter:0} px/m exceeds the largest texture "
                        + $"({OutputDefinition.MaxResolution} px); rendering at {MathF.Floor(maxPixelsPerMeter):0} px/m instead.");
            pixelsPerMeter = MathF.Floor(maxPixelsPerMeter);
        }

        var resolution = new Int2(Math.Max(1, (int)MathF.Round(size.X * pixelsPerMeter)),
                                  Math.Max(1, (int)MathF.Round(size.Y * pixelsPerMeter)));

        var oldJson = setup.ToJsonString();
        var macro = new MacroCommand("Create slices from regions");
        Guid childId;
        if (source != null)
        {
            childId = source.SymbolChildId;
        }
        else if (!ContentSourceSync.TryAddSendOp(macro, parent.Name, out childId))
        {
            Log.Warning("Slices from regions: no composition is open to add the SendToOutput op to.");
            return;
        }

        if (!ContentSourceSync.TrySetSendResolution(macro, childId, resolution))
            Log.Warning($"Slices from regions: can't set the send op's resolution; set it to {resolution.Width}×{resolution.Height} by hand.");

        if (source == null)
        {
            // Registered right away rather than left to the sync's adoption next frame: the slices need a source to name.
            source = new ContentSource { SymbolChildId = childId, Name = parent.Name, IsRenamed = true };
            setup.ContentSources.Add(source);
        }

        SurfaceGeometry.LocalBounds(parent, out var parentMin, out var parentMax);
        foreach (var region in regions)
        {
            // Pixel edges are rounded on the source, so regions that touch share their edge exactly. A region
            // sits in metres from the parent's anchor with Y up; a slice's rect is a share of its source with Y down.
            var regionMin = region.LocalPosition;
            var regionMax = regionMin + region.SizeInMeters;
            var left = MathF.Round((regionMin.X - parentMin.X) * pixelsPerMeter);
            var right = MathF.Round((regionMax.X - parentMin.X) * pixelsPerMeter);
            var top = MathF.Round((parentMax.Y - regionMax.Y) * pixelsPerMeter);
            var bottom = MathF.Round((parentMax.Y - regionMin.Y) * pixelsPerMeter);

            var slice = WallpaperLayout.FindReusableSlice(setup, region, source) ?? WallpaperLayout.CreateSlice(setup, source);
            slice.Name = region.Name;
            slice.Rotation = 0;
            slice.SetUvRect(new Vector4(left / resolution.Width, top / resolution.Height,
                                        right / resolution.Width, bottom / resolution.Height));
            region.SliceId = slice.Id;
        }

        SortSlicesLikeRegions(setup, regions);
        selection.Select(SetupEntityKinds.ContentSource, childId);

        macro.AddExecutedCommandForUndo(new SetupSnapshotCommand("Create slices from regions", setup.Id, oldJson, setup.ToJsonString()));
        UndoRedoStack.Add(macro);
        OutputSetupHandling.SaveActive();

        Log.Info($"Slices from regions: {regions.Count} slices on a {resolution.Width}×{resolution.Height} source at {pixelsPerMeter:0} px/m.");
    }

    /** The regions' slices trade places among the list positions they already hold, so every other slice stays put. */
    private static void SortSlicesLikeRegions(Setup setup, IReadOnlyList<Surface> regions)
    {
        var positions = new List<int>(regions.Count);
        foreach (var region in regions)
        {
            var position = setup.Slices.FindIndex(slice => slice.Id == region.SliceId);
            if (position >= 0 && !positions.Contains(position))
                positions.Add(position);
        }

        var ordered = new List<Slice>(positions.Count);
        foreach (var position in positions)
            ordered.Add(setup.Slices[position]);

        positions.Sort();
        for (var i = 0; i < positions.Count; i++)
            setup.Slices[positions[i]] = ordered[i];
    }
}
