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
/// Lays walls out side by side on one content source, a wallpaper running around the room: each wall shows a
/// slice of the strip, in the order the walls stand and at the height they stand at, so content drawn across the
/// strip (a line of type scrolling along) runs on continuously around the corners. The strip renders at the pixel
/// density the walls are projected at, so it costs no more pixels than the walls receive.
/// </summary>
internal static class WallpaperLayout
{
    /// <summary>
    /// The walls a menu opened on this entity would lay out: a floor plan's raised walls, else the selected
    /// stage surfaces when the entity is part of the selection, else the entity itself when it is a surface.
    /// </summary>
    public static void CollectWalls(Setup setup, SetupEntitySelection selection, SetupEntityKinds kind, Guid id, List<Surface> walls)
    {
        walls.Clear();
        if (kind == SetupEntityKinds.FloorPlan)
        {
            var plan = setup.FindFloorPlan(id);
            if (plan == null)
                return;

            for (var segment = 0; segment < plan.SegmentCount; segment++)
            {
                var wall = setup.FindSurface(plan.WallOf(segment));
                if (wall != null)
                    walls.Add(wall);
            }

            return;
        }

        if (selection.IsSelected(kind, id))
        {
            for (var i = 0; i < selection.Targets.Count; i++)
            {
                var target = selection.Targets[i];
                if (target.Kind != SetupEntityKinds.Surface)
                    continue;

                var surface = setup.FindSurface(target.EntityId);
                if (surface != null && IsWallCandidate(setup, surface) && !walls.Contains(surface))
                    walls.Add(surface);
            }

            return;
        }

        if (kind == SetupEntityKinds.Surface && setup.FindSurface(id) is { } single && IsWallCandidate(setup, single))
            walls.Add(single);
    }

    /// <summary>
    /// The walls already showing a slice of this source: what a re-cut of an existing wallpaper lays out again,
    /// without the walls having to be selected along with it.
    /// </summary>
    public static void CollectWallsShowing(Setup setup, ContentSource source, List<Surface> walls)
    {
        walls.Clear();
        foreach (var surface in setup.Surfaces)
        {
            if (setup.FindSlice(surface.SliceId) is { } slice && slice.SourceId == source.Id && IsWallCandidate(setup, surface))
                walls.Add(surface);
        }
    }

    /// <summary>
    /// The one content source selected along with the walls, which a layout re-cuts instead of adding a new send.
    /// Only when the menu's entity is part of that selection: a plan right-clicked on its own adds a new send.
    /// </summary>
    public static ContentSource? FindSelectedSource(Setup setup, SetupEntitySelection selection, SetupEntityKinds kind, Guid id)
    {
        if (!selection.IsSelected(kind, id))
            return null;

        ContentSource? found = null;
        for (var i = 0; i < selection.Targets.Count; i++)
        {
            var target = selection.Targets[i];
            if (target.Kind != SetupEntityKinds.ContentSource)
                continue;

            var source = setup.FindSourceByChildId(target.EntityId);
            if (source == null)
                continue;

            if (found != null)
                return null;

            found = source;
        }

        return found;
    }

    /// <summary>
    /// Cuts the strip into one slice per wall and shows each on its wall, as one undo step together with the
    /// graph edits. Without <paramref name="source"/> a new SendToOutput op is added for it; with one, that
    /// source is re-cut and the slices its walls already show are kept, so their routing survives a re-run.
    /// </summary>
    public static void Create(SetupEntitySelection selection, Setup setup, IReadOnlyList<Surface> walls, ContentSource? source)
    {
        if (walls.Count == 0)
            return;

        var ordered = new List<Surface>(walls.Count);
        OrderAroundRoom(setup, walls, ordered, out var hasGaps);
        if (hasGaps)
            Log.Warning("Wallpaper layout: the walls don't form one continuous run, so the strip jumps where walls are missing.");

        var pixelsPerMeter = FindPixelsPerMeter(setup, ordered);

        // The strip spans every wall's height range, so a line of content stays at one height in the room.
        var stripBottom = float.MaxValue;
        var stripTop = float.MinValue;
        var stripWidth = 0f;
        foreach (var wall in ordered)
        {
            var bottom = BottomElevationOf(setup, wall);
            stripBottom = MathF.Min(stripBottom, bottom);
            stripTop = MathF.Max(stripTop, bottom + wall.SizeInMeters.Y);
            stripWidth += wall.SizeInMeters.X;
        }

        var stripHeight = stripTop - stripBottom;
        var maxPixelsPerMeter = OutputDefinition.MaxResolution / MathF.Max(stripWidth, stripHeight);
        if (pixelsPerMeter > maxPixelsPerMeter)
        {
            Log.Warning($"Wallpaper layout: {stripWidth:0.##} m of wall at {pixelsPerMeter:0} px/m exceeds the largest texture "
                        + $"({OutputDefinition.MaxResolution} px); rendering at {MathF.Floor(maxPixelsPerMeter):0} px/m instead.");
            pixelsPerMeter = MathF.Floor(maxPixelsPerMeter);
        }

        var resolution = new Int2(Math.Max(1, (int)MathF.Round(stripWidth * pixelsPerMeter)),
                                  Math.Max(1, (int)MathF.Round(stripHeight * pixelsPerMeter)));

        var oldJson = setup.ToJsonString();
        var macro = new MacroCommand("Create wallpaper layout");
        Guid childId;
        if (source != null)
        {
            childId = source.SymbolChildId;
        }
        else if (!ContentSourceSync.TryAddSendOp(macro, WallpaperOpName, out childId))
        {
            Log.Warning("Wallpaper layout: no composition is open to add the SendToOutput op to.");
            return;
        }

        if (!ContentSourceSync.TrySetSendResolution(macro, childId, resolution))
            Log.Warning($"Wallpaper layout: can't set the send op's resolution; set it to {resolution.Width}×{resolution.Height} by hand.");

        if (source == null)
        {
            // Registered right away rather than left to the sync's adoption next frame: the slices need a source to name.
            source = new ContentSource { SymbolChildId = childId, Name = WallpaperOpName, IsRenamed = true };
            setup.ContentSources.Add(source);
        }

        // Pixel edges are rounded once along the strip, so neighbouring slices share their edge exactly.
        var metersAlong = 0f;
        foreach (var wall in ordered)
        {
            var left = MathF.Round(metersAlong * pixelsPerMeter);
            metersAlong += wall.SizeInMeters.X;
            var right = MathF.Round(metersAlong * pixelsPerMeter);

            var bottom = BottomElevationOf(setup, wall);
            var top = MathF.Round((stripTop - bottom - wall.SizeInMeters.Y) * pixelsPerMeter);
            var below = MathF.Round((stripTop - bottom) * pixelsPerMeter);

            var slice = FindReusableSlice(setup, wall, source) ?? CreateSlice(setup, source);
            if (string.IsNullOrEmpty(slice.Name))
                slice.Name = wall.Name;

            slice.Rotation = 0;
            slice.SetUvRect(new Vector4(left / resolution.Width, top / resolution.Height,
                                        right / resolution.Width, below / resolution.Height));
            wall.SliceId = slice.Id;
        }

        selection.Select(SetupEntityKinds.ContentSource, childId);

        var newJson = setup.ToJsonString();
        macro.AddExecutedCommandForUndo(new SetupSnapshotCommand("Create wallpaper layout", setup.Id, oldJson, newJson));
        UndoRedoStack.Add(macro);
        OutputSetupHandling.SaveActive();

        Log.Info($"Wallpaper layout: {ordered.Count} walls on a {resolution.Width}×{resolution.Height} strip at {pixelsPerMeter:0} px/m.");
    }

    /// <summary>
    /// Orders walls the way they follow each other from inside the room, left to right: plan by plan around each
    /// one, starting after a missing wall so a closed room's run isn't cut at its first corner. Walls on no plan
    /// follow, ordered as they lie on the Board.
    /// </summary>
    internal static void OrderAroundRoom(Setup setup, IReadOnlyList<Surface> walls, List<Surface> ordered, out bool hasGaps)
    {
        hasGaps = false;
        var planWalls = new List<Surface>();
        foreach (var plan in setup.FloorPlans)
        {
            // A run starts at a chosen wall whose predecessor isn't chosen. Every wall of a closed room chosen
            // makes one loop without such a start, which then begins at the first segment.
            var segmentCount = plan.SegmentCount;
            var chosenCount = 0;
            var runCount = 0;
            var first = 0;
            for (var segment = segmentCount - 1; segment >= 0; segment--)
            {
                if (!IsChosenWall(setup, plan, segment, walls))
                    continue;

                chosenCount++;
                var previous = segment == 0 && plan.IsClosed ? segmentCount - 1 : segment - 1;
                if (previous >= 0 && IsChosenWall(setup, plan, previous, walls))
                    continue;

                runCount++;
                first = segment;
            }

            if (chosenCount == 0)
                continue;

            hasGaps |= runCount > 1;

            planWalls.Clear();
            for (var step = 0; step < segmentCount; step++)
            {
                var segment = plan.IsClosed ? (first + step) % segmentCount : step;
                if (IsChosenWall(setup, plan, segment, walls) && setup.FindSurface(plan.WallOf(segment)) is { } wall && !ordered.Contains(wall))
                    planWalls.Add(wall);
            }

            // A wall facing left of its segment has its right end at the segment's start, so from inside the room
            // the next segment's wall stands to its left.
            var facesLeft = !plan.IsClosed || plan.SignedAreaTwice() >= 0;
            if (facesLeft)
                planWalls.Reverse();

            ordered.AddRange(planWalls);
        }

        var rest = new List<Surface>();
        foreach (var wall in walls)
        {
            if (!ordered.Contains(wall))
                rest.Add(wall);
        }

        rest.Sort((a, b) => LeftEdgeOnBoard(a).CompareTo(LeftEdgeOnBoard(b)));
        ordered.AddRange(rest);
    }

    private static bool IsChosenWall(Setup setup, FloorPlan plan, int segment, IReadOnlyList<Surface> walls)
    {
        var wall = setup.FindSurface(plan.WallOf(segment));
        if (wall == null)
            return false;

        for (var i = 0; i < walls.Count; i++)
        {
            if (walls[i] == wall)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The sharpest density any wall is projected at: pixels on its output canvas per metre of wall. Walls that
    /// land on no canvas yet fall back to their own content resolution.
    /// </summary>
    private static float FindPixelsPerMeter(Setup setup, IReadOnlyList<Surface> walls)
    {
        var mapped = 0f;
        var own = 0f;
        var quad = new Vector2[4];
        foreach (var wall in walls)
        {
            own = MathF.Max(own, wall.PixelsPerMeter);
            if (!SetupRelations.TryFindCanvasQuad(setup, wall, quad, out var outputId) || setup.FindOutput(outputId) is not { } output)
                continue;

            for (var corner = 0; corner < 4; corner++)
            {
                quad[corner] = output.ToPixels(quad[corner]);
            }

            var widthInPixels = (Vector2.Distance(quad[0], quad[1]) + Vector2.Distance(quad[3], quad[2])) * 0.5f;
            var heightInPixels = (Vector2.Distance(quad[0], quad[3]) + Vector2.Distance(quad[1], quad[2])) * 0.5f;
            mapped = MathF.Max(mapped, MathF.Max(widthInPixels / MathF.Max(wall.SizeInMeters.X, SurfaceGeometry.MinSize),
                                                 heightInPixels / MathF.Max(wall.SizeInMeters.Y, SurfaceGeometry.MinSize)));
        }

        return mapped > 0 ? mapped : MathF.Max(own, 1);
    }

    /// <summary>Where the wall's bottom edge stands above the ground: its plan elevation, else its stage pose.</summary>
    private static float BottomElevationOf(Setup setup, Surface wall)
    {
        if (setup.FindFloorPlanOf(wall.Id, out _) != null)
            return FloorPlanSync.ElevationOf(setup, wall.Id);

        return wall.Placement != null ? wall.Placement.Pose.Position.Y - wall.AnchorInMeters.Y : 0;
    }

    /// <summary>The slice of this source the wall already shows, so a re-run re-cuts it in place.</summary>
    private static Slice? FindReusableSlice(Setup setup, Surface wall, ContentSource source)
    {
        var current = setup.FindSlice(wall.SliceId);
        if (current == null || current.SourceId != source.Id)
            return null;

        // A slice shared with another surface or patch would drag that one along.
        return SetupRelations.CountConsumersOfSlice(setup, current.Id) <= 1 ? current : null;
    }

    private static Slice CreateSlice(Setup setup, ContentSource source)
    {
        var slice = new Slice { SourceId = source.Id };
        setup.Slices.Add(slice);
        return slice;
    }

    private static float LeftEdgeOnBoard(Surface surface)
    {
        return (surface.BoardPlacement?.Position.X ?? 0) - surface.AnchorInMeters.X;
    }

    /** A stage surface other than a room's floor, which has no place in a strip running around it. */
    private static bool IsWallCandidate(Setup setup, Surface surface)
    {
        if (surface.Kind != Surface.Kinds.Physical || surface.ParentId != Guid.Empty)
            return false;

        foreach (var plan in setup.FloorPlans)
        {
            if (plan.FloorSurfaceId == surface.Id)
                return false;
        }

        return true;
    }

    private const string WallpaperOpName = "Wallpaper";
}
