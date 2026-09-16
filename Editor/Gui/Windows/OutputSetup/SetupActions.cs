#nullable enable
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Creating, deleting, renaming and duplicating setup entities — the per-kind mutations, each one undo step
/// (see <see cref="SetupUndo"/>). Shared by the outliner, the canvas views and the parameter cards, so they live
/// in a neutral class none of the views own. Routing is <see cref="SetupRouting"/>, graph glue
/// <see cref="ContentSourceSync"/>, names <see cref="SetupLabels"/>, metres <see cref="SurfaceMetrics"/>.
/// </summary>
internal static class SetupActions
{
    internal static void AddSlice(SetupEntitySelection selection, Setup setup, ContentSource source)
    {
        SetupUndo.RunUndoable("Add slice", setup, () =>
                                                  {
                                                      // Left unnamed: the label is derived from the source, so it stays right when the op is later renamed.
                                                      var slice = new Slice { SourceId = source.Id };
                                                      setup.Slices.Add(slice);
                                                      selection.Select(SetupEntityKinds.Slice, slice.Id);
                                                  });
    }

    /// <summary>
    /// Adds a patch as a visible tile — a centred quarter of the canvas — rather than the full canvas. A sole
    /// full-canvas patch is the output's implicit one and is folded away in the views
    /// (<see cref="SetupRelations.TryGetImplicitPatch"/>), so a patch added by hand has to be something you can
    /// see and drag.
    /// </summary>
    internal static void AddPatch(SetupEntitySelection selection, Setup setup, OutputDefinition output)
    {
        SetupUndo.RunUndoable("Add patch", setup, () =>
                                                  {
                                                      var patch = AddPatchInternal(output, Guid.Empty);
                                                      var min = new Vector2(0.25f, 0.25f);
                                                      var max = new Vector2(0.75f, 0.75f);
                                                      patch.Quad = [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
                                                      selection.Select(SetupEntityKinds.Patch, patch.Id);
                                                  });
    }

    /// <summary>A full-canvas patch fed by a slice, appended outside any undo step — the caller's.</summary>
    internal static OutputDefinition.Patch AddPatchInternal(OutputDefinition output, Guid sliceId)
    {
        // Left unnamed: the label is derived from its position (see SetupLabels.PatchLabel).
        var patch = new OutputDefinition.Patch { SliceId = sliceId, Quad = OutputDefinition.FullCanvasQuad() };
        output.Patches.Add(patch);
        return patch;
    }

    internal static void AddSurface(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        SetupUndo.RunUndoable("Add surface", setup, () =>
                                                    {
                                                        var surface = new Surface { Name = $"Surface {setup.Surfaces.Count + 1}" };
                                                        setup.Surfaces.Add(surface);
                                                        selection.Select(SetupEntityKinds.Surface, surface.Id);
                                                    });
    }

    internal static void AddProp(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        SetupUndo.RunUndoable("Add prop", setup, () =>
                                                 {
                                                     var prop = new Prop();
                                                     setup.Props.Add(prop);
                                                     selection.Select(SetupEntityKinds.Prop, prop.Id);
                                                 });
    }

    /// <summary>A closed rectangular floor plan of <paramref name="size"/> metres, with or without its floor surface.</summary>
    internal static void AddFloorPlan(SetupEntitySelection selection, Vector2 size, bool withFloor)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        SetupUndo.RunUndoable("Add floor plan", setup, () =>
                                                       {
                                                           var plan = FloorPlanSync.CreateRectangle(setup, size, withFloor);
                                                           selection.Select(SetupEntityKinds.FloorPlan, plan.Id);
                                                       });
    }

    /// <summary>Starts a floor plan from a surface: as its first wall, or as the floor of a room its size.</summary>
    internal static void StartFloorPlanFromSurface(SetupEntitySelection selection, Setup setup, Surface surface, bool asFloor)
    {
        SetupUndo.RunUndoable(asFloor ? "Use surface as floor" : "Start floor plan from wall", setup, () =>
                                                                                                 {
                                                                                                     var plan = asFloor
                                                                                                                    ? FloorPlanSync.StartFromFloor(setup, surface)
                                                                                                                    : FloorPlanSync.StartFromWall(setup, surface);
                                                                                                     selection.Select(SetupEntityKinds.FloorPlan, plan.Id);

                                                                                                     // A run started from a wall wants its next wall right away.
                                                                                                     if (!asFloor)
                                                                                                         SetupOutputView.PendingPlanDrawId = plan.Id;
                                                                                                 });
    }

    /// <summary>Whether the kind has an order of its own to move within: outputs, surfaces among their siblings, slices under their source, patches on their output.</summary>
    internal static bool CanReorder(SetupEntityKinds kind)
    {
        return kind is SetupEntityKinds.Output or SetupEntityKinds.Surface or SetupEntityKinds.Slice or SetupEntityKinds.Patch;
    }

    /// <summary>Moves an entity one place up (-1) or down (+1) among its siblings in the list that orders it. No undo step of its own: the caller's gesture holds the snapshot.</summary>
    internal static void MoveAmongSiblings(Setup setup, SetupEntityKinds kind, Guid id, int direction)
    {
        switch (kind)
        {
            case SetupEntityKinds.Patch:
                if (setup.FindPatch(id, out var owner) != null && owner != null)
                    SwapWithNeighbour(owner.Patches, owner.Patches.FindIndex(p => p.Id == id), direction, _ => true);

                break;

            case SetupEntityKinds.Output:
                // The Default output is hidden, so it is never a neighbour to swap with.
                SwapWithNeighbour(setup.Outputs, setup.Outputs.FindIndex(o => o.Id == id), direction, o => o.Kind != OutputDefinition.Kinds.Default);
                break;

            case SetupEntityKinds.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface != null)
                    SwapWithNeighbour(setup.Surfaces, setup.Surfaces.IndexOf(surface), direction, s => s.ParentId == surface.ParentId);

                break;
            }

            case SetupEntityKinds.Slice:
            {
                var slice = setup.FindSlice(id);
                if (slice != null)
                    SwapWithNeighbour(setup.Slices, setup.Slices.IndexOf(slice), direction, s => s.SourceId == slice.SourceId);

                break;
            }
        }
    }

    /** Swaps the item at index with the nearest list neighbour in the given direction that is a sibling. */
    private static void SwapWithNeighbour<T>(List<T> list, int index, int direction, Func<T, bool> isSibling)
    {
        if (index < 0)
            return;

        for (var other = index + direction; other >= 0 && other < list.Count; other += direction)
        {
            if (!isSibling(list[other]))
                continue;

            (list[index], list[other]) = (list[other], list[index]);
            return;
        }
    }

    internal static void AddOutput(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        SetupUndo.RunUndoable("Add output", setup, () =>
                                                   {
                                                       var output = new OutputDefinition
                                                                        {
                                                                            Name = $"P{CountProjectorOutputs(setup) + 1}",
                                                                            Kind = OutputDefinition.Kinds.Projector,
                                                                            CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1200),
                                                                        };
                                                       setup.Outputs.Add(output);
                                                       selection.Select(SetupEntityKinds.Output, output.Id);
                                                   });
    }

    /// <summary>Maps a surface onto an output with a centred corner-pin quad at the surface's aspect — appended outside any undo step, the caller's.</summary>
    internal static void AddMapping(Surface surface, OutputDefinition output, Guid outputId)
    {
        var canvasW = Math.Max(1, output.ResolvedResolution.Width);
        var canvasH = Math.Max(1, output.ResolvedResolution.Height);

        var aspect = surface.SizeInMeters.Y > 0.0001f ? surface.SizeInMeters.X / surface.SizeInMeters.Y : 1f;
        var maxW = canvasW * 0.6f;
        var maxH = canvasH * 0.6f;
        var w = maxW;
        var h = w / aspect;
        if (h > maxH)
        {
            h = maxH;
            w = h * aspect;
        }

        var cx = canvasW * 0.5f;
        var cy = canvasH * 0.5f;

        // Laid out in pixels for the aspect, stored as fractions of the canvas like every mapping quad.
        var canvas = new Vector2(canvasW, canvasH);
        var quad = new[]
                       {
                           new Vector2(cx - w * 0.5f, cy - h * 0.5f) / canvas, // top-left
                           new Vector2(cx + w * 0.5f, cy - h * 0.5f) / canvas, // top-right
                           new Vector2(cx + w * 0.5f, cy + h * 0.5f) / canvas, // bottom-right
                           new Vector2(cx - w * 0.5f, cy + h * 0.5f) / canvas, // bottom-left
                       };

        surface.OutputMappings.Add(new Surface.OutputMapping { OutputId = outputId, Quad = quad });
    }

    /// <summary>The image asset type's extensions in the picker's comma-separated form, built once.</summary>
    internal static string ImageFileFilter
    {
        get
        {
            if (_imageFileFilter != null)
                return _imageFileFilter;

            var extensions = new List<string>();
            foreach (var id in AssetHandling.Images.ExtensionIds)
            {
                if (FileExtensionRegistry.TryGetExtensionForId(id, out var extension))
                    extensions.Add(extension.TrimStart('.'));
            }

            _imageFileFilter = string.Join(',', extensions);
            return _imageFileFilter;
        }
    }

    /// <summary>
    /// Adds a reference image for an asset — an image dropped from the Asset Library or the OS (the latter
    /// imported into the project's <c>Assets/images/reference</c> first, unless it is already an asset).
    /// Placed at <paramref name="boardPosition"/> on the Board and selected.
    /// </summary>
    internal static void AddReferenceImageFromFile(SetupEntitySelection selection, Setup setup, string addressOrPath, Vector2 boardPosition)
    {
        if (!AssetRegistry.TryGetAsset(addressOrPath, out var asset))
        {
            var package = ProjectView.Focused?.OpenedProject.Package;
            if (package == null)
                return;

            var destination = Path.Combine(package.AssetsFolder, ReferenceImageFolder);
            if (!FileImport.TryImportDroppedFile(addressOrPath, package, destination, out asset))
            {
                Log.Warning($"Can't import {addressOrPath} as a reference image.");
                return;
            }
        }

        if (asset.AssetType != AssetHandling.Images)
        {
            Log.Warning($"{asset.Address} is not an image.");
            return;
        }

        SetupUndo.RunUndoable("Add reference image", setup, () =>
                                                            {
                                                                var image = new ReferenceImage
                                                                                {
                                                                                    Name = Path.GetFileNameWithoutExtension(asset.Address),
                                                                                    FilePath = asset.Address,
                                                                                    BoardPlacement = new BoardPlacement { Position = boardPosition },
                                                                                };
                                                                setup.ReferenceImages.Add(image);
                                                                selection.Select(SetupEntityKinds.ReferenceImage, image.Id);
                                                            });
    }

    internal static void AddReferenceImage(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        SetupUndo.RunUndoable("Add reference image", setup, () =>
                                                            {
                                                                var image = new ReferenceImage { Name = $"Image {setup.ReferenceImages.Count + 1}" };
                                                                setup.ReferenceImages.Add(image);
                                                                selection.Select(SetupEntityKinds.ReferenceImage, image.Id);
                                                            });
    }

    /// <summary>Traces an existing (untraced) surface on an image: a default quad in the photo's middle to drag onto the wall.</summary>
    internal static void TraceSurfaceOnImage(SetupEntitySelection selection, Setup setup, Surface surface, ReferenceImage image)
    {
        SetupUndo.RunUndoable("Trace surface", setup, () =>
                                                      {
                                                          surface.Trace = new Surface.TraceBinding { ImageId = image.Id, Quad = DefaultReferenceQuad(image) };
                                                          selection.Select(SetupEntityKinds.Surface, surface.Id);
                                                      });
    }

    /// <summary>
    /// A new surface, traced on the image right away — the "there is a wall in this photo" gesture. It is seeded
    /// to be plausible rather than default: sized like its trace (metres estimated from the walls already traced
    /// on this photo, or a wall's height when it is the first), and placed on the floor right of the last card.
    /// </summary>
    internal static void TraceNewSurface(SetupEntitySelection selection, Setup setup, ReferenceImage image)
    {
        SetupUndo.RunUndoable("Trace new surface", setup, () =>
                                                          {
                                                              var quad = DefaultReferenceQuad(image);
                                                              var surface = new Surface
                                                                                {
                                                                                    Name = $"Surface {setup.Surfaces.Count + 1}",
                                                                                    Trace = new Surface.TraceBinding { ImageId = image.Id, Quad = quad },
                                                                                    SizeInMeters = SurfaceMetrics.EstimateTracedSize(setup, image, quad),
                                                                                };
                                                              surface.BoardPlacement = new BoardPlacement
                                                                                           {
                                                                                               Position = new Vector2(SurfaceMetrics.NextFreeBoardX(setup), 0) + surface.AnchorInMeters,
                                                                                           };
                                                              setup.Surfaces.Add(surface);
                                                              selection.Select(SetupEntityKinds.Surface, surface.Id);
                                                          });
    }

    /// <summary>
    /// Adds a Layout child — a rectangle living inside its parent, riding the parent's corner pin rather than
    /// carrying one of its own. Its position is stored in meters from the parent's anchor, so it stays welded
    /// to the meter raster when the parent is cropped or stretched.
    /// </summary>
    internal static void AddSubRegion(SetupEntitySelection selection, Setup setup, Surface parent)
    {
        var parentSize = parent.SizeInMeters;
        var size = new Vector2(MathF.Max(parentSize.X * 0.3f, SurfaceGeometry.MinSize),
                               MathF.Max(parentSize.Y * 0.3f, SurfaceGeometry.MinSize));

        // Land inside the parent rather than at its anchor: cropping an edge past the anchor legitimately
        // pushes it outside the rectangle, and a child sitting on it would then start outside the parent —
        // where extrapolating through a keystoned projection sends it a very long way off.
        var bottomLeft = new Vector2(parentSize.X * 0.1f, parentSize.Y * 0.1f) - parent.AnchorInMeters;

        SetupUndo.RunUndoable("Add region", setup, () =>
                                                   {
                                                       var child = new Surface
                                                                       {
                                                                           Name = $"Region {SetupRelations.CountChildren(setup, parent.Id) + 1}",
                                                                           Kind = Surface.Kinds.Layout,
                                                                           ParentId = parent.Id,
                                                                           SizeInMeters = size,
                                                                           LocalPosition = bottomLeft,
                                                                           PixelsPerMeter = parent.PixelsPerMeter,
                                                                       };

                                                       setup.Surfaces.Add(child);
                                                       selection.Select(SetupEntityKinds.Surface, child.Id);
                                                   });
    }

    /// <summary>Adds a reference point at a surface-space position, named by its ordinal ("P3").</summary>
    internal static void AddReferencePoint(Setup setup, Surface surface, Vector2 position)
    {
        SetupUndo.RunUndoable("Add reference point", setup, () =>
                                                            {
                                                                surface.Annotations.Add(new Annotation
                                                                                            {
                                                                                                Kind = Annotation.Kinds.Point,
                                                                                                Name = $"P{SurfaceMetrics.CountPoints(surface) + 1}",
                                                                                                P1 = position,
                                                                                                P2 = position,
                                                                                            });
                                                            });
    }

    /// <summary>
    /// Replaces the output's patches with a columns × rows grid of tiles covering the canvas — the split-matrix
    /// and TV-wall case. Every tile is fed by what the first patch showed, so a full-frame feed becomes N copies
    /// ready to be re-routed one by one.
    /// </summary>
    internal static void SplitOutput(SetupEntitySelection selection, Setup setup, OutputDefinition output, int columns, int rows)
    {
        SetupUndo.RunUndoable($"Split {columns}×{rows}", setup, () =>
                                                                 {
                                                                     // The tiles take over the old patch's picture: its feed, and which way up it is.
                                                                     var feed = output.Patches.Count > 0 ? output.Patches[0].SliceId : Guid.Empty;
                                                                     var turns = output.Patches.Count > 0 ? output.Patches[0].QuarterTurns : 0;
                                                                     output.Patches.Clear();

                                                                     // Boundaries come from one expression per grid line, so tile n's right edge and
                                                                     // tile n+1's left edge are the *same float* — bit-identical shared edges are what
                                                                     // makes the rasterizer's top-left fill rule tile them with no seam and no overlap.
                                                                     // Deriving max as min + cell would round differently and could cost a pixel row.
                                                                     for (var row = 0; row < rows; row++)
                                                                     {
                                                                         for (var column = 0; column < columns; column++)
                                                                         {
                                                                             var min = new Vector2(column / (float)columns, row / (float)rows);
                                                                             var max = new Vector2((column + 1) / (float)columns, (row + 1) / (float)rows);
                                                                             output.Patches.Add(new OutputDefinition.Patch
                                                                                                    {
                                                                                                        SliceId = feed,
                                                                                                        QuarterTurns = turns,
                                                                                                        Quad = [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)],
                                                                                                    });
                                                                         }
                                                                     }

                                                                     selection.Select(SetupEntityKinds.Patch, output.Patches[0].Id);
                                                                 });
    }

    /// <summary>Whether this surface is a region that overrides its parent's pin on at least one output.</summary>
    internal static bool HasOwnPin(Surface surface)
    {
        return surface.Kind == Surface.Kinds.Layout && surface.ParentId != Guid.Empty && surface.OutputMappings.Count > 0;
    }

    /// <summary>Drops a region's own pins, so it rides its parent's again on every output.</summary>
    internal static void ClearOwnPin(Setup setup, Guid surfaceId)
    {
        var surface = setup.FindSurface(surfaceId);
        if (surface == null || !HasOwnPin(surface))
            return;

        SetupUndo.RunUndoable("Follow parent's pin", setup, () => surface.OutputMappings.Clear());
    }

    /// <summary>
    /// "Use on Surface": materializes a surface for a patch when a surface-only feature is reached for (real
    /// size, raster, straightening). The quad transfers verbatim onto the surface's mapping — same numbers,
    /// nothing moves on the wall — and the patch goes, since a route's quad has one home at a time.
    /// </summary>
    internal static void PromotePatchToSurface(SetupEntitySelection selection, Setup setup, Guid patchId)
    {
        var patch = setup.FindPatch(patchId, out var output);
        if (patch == null || output == null || patch.Quad.Length < 4)
            return;

        SetupUndo.RunUndoable("Use on surface", setup, () =>
                                                       {
                                                           var min = patch.Quad[0];
                                                           var max = patch.Quad[0];
                                                           foreach (var corner in patch.Quad)
                                                           {
                                                               min = Vector2.Min(min, corner);
                                                               max = Vector2.Max(max, corner);
                                                           }

                                                           // A metre of width, the height by the quad's aspect: the physical size is unknown until
                                                           // measured, and the content density follows from the pixels the patch already covered —
                                                           // its share of the canvas, taken at the canvas' current size.
                                                           var covered = (max - min) * output.CanvasSize;

                                                           // A turned patch is a surface lying on its side: its own width runs along the picture,
                                                           // which after an odd number of turns is the canvas' vertical. The mapping gets the
                                                           // turned corner order, so the surface's top-left is where the picture's was.
                                                           var sideways = (OutputDefinition.Patch.NormalizeTurns(patch.QuarterTurns) & 1) == 1;
                                                           var widthPx = MathF.Max(sideways ? covered.Y : covered.X, 1);
                                                           var heightPx = MathF.Max(sideways ? covered.X : covered.Y, 1);
                                                           var mappedQuad = new Vector2[4];
                                                           patch.CopyTurnedCorners(mappedQuad);
                                                           var surface = new Surface
                                                                             {
                                                                                 Name = string.IsNullOrEmpty(patch.Name) ? $"Surface {setup.Surfaces.Count + 1}" : patch.Name,
                                                                                 SizeInMeters = new Vector2(1, heightPx / widthPx),
                                                                                 PixelsPerMeter = widthPx,
                                                                                 SliceId = patch.SliceId,
                                                                                 OutputMappings =
                                                                                 [
                                                                                     new Surface.OutputMapping { OutputId = output.Id, Quad = mappedQuad },
                                                                                 ],
                                                                             };

                                                           setup.Surfaces.Add(surface);
                                                           output.Patches.RemoveAll(p => p.Id == patchId);
                                                           selection.Select(SetupEntityKinds.Surface, surface.Id);
                                                       });
    }

    /// <summary>Turns the whole patch a quarter clockwise around its centre, shape and picture together, as one undo step.</summary>
    internal static void RotatePatchClockwise(Setup setup, OutputDefinition output, OutputDefinition.Patch patch)
    {
        SetupUndo.RunUndoable("Rotate patch", setup, () => TurnPatch(output, patch, 1));
    }

    /// <summary>Turns only the picture a quarter clockwise while the patch keeps its place and shape — for content
    /// fed straight to a display mounted on its side, where the patch must go on filling the canvas.</summary>
    internal static void RotatePatchContentClockwise(Setup setup, OutputDefinition.Patch patch)
    {
        SetPatchTurns(setup, patch, patch.QuarterTurns + 1);
    }

    /// <summary>
    /// Turns a patch by quarter turns clockwise around its centre: the quad turns on the canvas, so a wide patch
    /// becomes a tall one, and the picture turns with it. Worked in canvas pixels, so the shape keeps its
    /// proportions on a canvas that isn't square. The corners are re-indexed afterwards so they stay TL, TR, BR,
    /// BL on the canvas, which every edit, snap and size field relies on. Mutates in place; the caller owns the
    /// undo step.
    /// </summary>
    internal static void TurnPatch(OutputDefinition output, OutputDefinition.Patch patch, int quarterTurns)
    {
        var turns = OutputDefinition.Patch.NormalizeTurns(quarterTurns);
        if (turns == 0)
            return;

        patch.QuarterTurns = OutputDefinition.Patch.NormalizeTurns(patch.QuarterTurns + turns);
        var canvas = output.CanvasSize;

        // A fitted patch is always centred, so re-fitting with the new turns is the whole rotation.
        if (patch.TryFitQuad(canvas) || patch.Quad.Length < 4)
            return;

        Span<Vector2> pixels = stackalloc Vector2[4];
        Span<Vector2> turned = stackalloc Vector2[4];
        var centre = Vector2.Zero;
        for (var c = 0; c < 4; c++)
        {
            pixels[c] = patch.Quad[c] * canvas;
            centre += pixels[c];
        }

        centre /= 4;
        for (var t = 0; t < turns; t++)
        {
            for (var c = 0; c < 4; c++)
            {
                // Clockwise on a Y-down canvas; the corner that was top-left is now top-right, one index further round.
                var d = pixels[c] - centre;
                turned[(c + 1) % 4] = centre + new Vector2(-d.Y, d.X);
            }

            turned.CopyTo(pixels);
        }

        for (var c = 0; c < 4; c++)
            patch.Quad[c] = pixels[c] / canvas;
    }

    /// <summary>
    /// Gives the surface a private copy of a slice it shares with other consumers, so a local crop leaves the
    /// others untouched. Called from inside a canvas gesture — the gesture's snapshot makes it undoable.
    /// </summary>
    internal static Slice CloneSliceForSurface(Setup setup, Surface surface, Slice shared)
    {
        var copy = CloneViaJson(shared.WriteToJson, Slice.ReadFromJson) ?? new Slice { SourceId = shared.SourceId, UvRect = shared.UvRect };
        copy.Id = Guid.NewGuid();
        setup.Slices.Add(copy);
        surface.SliceId = copy.Id;
        return copy;
    }

    /// <summary>Whether anything feeds the output: a surface mapped onto it, or a patch on its canvas.</summary>
    internal static bool OutputHasInputs(Setup setup, OutputDefinition output)
    {
        if (output.Patches.Count > 0)
            return true;

        foreach (var surface in setup.Surfaces)
        {
            if (SetupRelations.IsMappedTo(surface, output.Id))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Disconnects everything from an output: every surface's mapping onto it and all of its patches go. The
    /// surfaces, their slices and the output itself stay — only the routes into this canvas are cut.
    /// </summary>
    internal static void ClearOutputInputs(Setup setup, OutputDefinition output)
    {
        SetupUndo.RunUndoable("Clear output inputs", setup, () =>
                                                            {
                                                                foreach (var surface in setup.Surfaces)
                                                                    surface.OutputMappings.RemoveAll(m => m.OutputId == output.Id);

                                                                output.Patches.Clear();
                                                            });
    }

    /// <summary>
    /// Drops this surface from every send that targets it, so it stops receiving content. The surface itself
    /// and its calibration are untouched — this only edits the sends' target lists (op-side, like the drag).
    /// </summary>
    internal static void ClearContentInputs(Guid surfaceId)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = setup.FindSurface(surfaceId);
        if (surface == null || surface.SliceId == Guid.Empty)
            return;

        SetupUndo.RunUndoable("Clear content inputs", setup, () => surface.SliceId = Guid.Empty);
    }

    // ---- Rename ------------------------------------------------------------------------------------

    /// <summary>A prop has no name to rename; a content source renames its op; a plug renames its stream (displays
    /// are named by the OS — see <see cref="CanRenamePlug"/>).</summary>
    internal static bool CanRename(SetupEntityKinds kind) => SetupEntityKindInfo.Of(kind).CanRename;

    internal static bool CanRenamePlug(Guid plugId) => !Plugs.TryGetDisplayIndex(plugId, out _);

    /// <summary>Renames an entity by kind. A content source has no name of its own — renaming it renames its
    /// op (already undoable as a graph command), which flows back through the sync.</summary>
    internal static void RenameEntity(Setup setup, SetupEntityKinds kind, Guid id, string newName)
    {
        if (kind == SetupEntityKinds.ContentSource)
        {
            ContentSourceSync.RenameContentSourceOp(id, newName);
            return;
        }

        if (kind == SetupEntityKinds.Plug)
        {
            if (OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
                Plugs.RenameStream(machineConfig, id, newName);

            return;
        }

        SetupUndo.RunUndoable("Rename", setup, () => SetupEntities.TrySetName(setup, kind, id, newName));
    }

    // ---- Delete ------------------------------------------------------------------------------------

    /// <summary>A content source is a graph op (delete the op instead); everything else deletes here.</summary>
    internal static bool CanDeleteDirectly(SetupEntityKinds kind) => SetupEntityKindInfo.Of(kind).CanDelete;

    /// <summary>
    /// How many of the selected entities can actually be deleted here. A content source is a graph op and a
    /// slice's source may be gone, so the menu counts what will really go rather than how many items are lit.
    /// </summary>
    internal static int CountDeletable(SetupEntitySelection selection)
    {
        var count = 0;
        for (var i = 0; i < selection.Targets.Count; i++)
        {
            if (CanDeleteDirectly(selection.Targets[i].Kind))
                count++;
        }

        return count;
    }

    internal static void DeleteEntity(Setup setup, SetupEntityKinds kind, Guid id)
    {
        SetupUndo.RunUndoable("Delete", setup, () => DeleteEntityInternal(setup, kind, id));
    }

    /// <summary>
    /// Deletes everything deletable in the selection. Each kind keeps its own cascade (a surface re-parents
    /// its children, an output drops the mappings onto it), so deleting a set is just deleting each in turn —
    /// which is why the targets are copied first: those cascades mutate the setup underneath us.
    /// </summary>
    internal static void DeleteSelection(SetupEntitySelection selection, Setup setup)
    {
        SetupUndo.RunUndoable("Delete selection", setup, () =>
                                                         {
                                                             _deleteBuffer.Clear();
                                                             _deleteBuffer.AddRange(selection.Targets);

                                                             foreach (var target in _deleteBuffer)
                                                                 DeleteEntityInternal(setup, target.Kind, target.EntityId);

                                                             selection.Clear();
                                                         });
    }

    // ---- Duplicate ---------------------------------------------------------------------------------

    /// <summary>A content source is its op — duplicating it wouldn't carry the feed; everything else clones.</summary>
    internal static bool CanDuplicate(SetupEntityKinds kind) => SetupEntityKindInfo.Of(kind).CanDuplicate;

    internal static void DuplicateEntity(SetupEntitySelection selection, Setup setup, SetupEntityKinds kind, Guid id)
    {
        SetupUndo.RunUndoable("Duplicate", setup, () => DuplicateEntityInternal(selection, setup, kind, id));
    }

    /// <summary>Clones a setup entity through its own JSON round-trip, so new fields are picked up without
    /// touching the clone. The caller re-ids the copy.</summary>
    internal static T? CloneViaJson<T>(Action<JsonTextWriter> write, Func<JToken, T> read) where T : class
    {
        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var writer = new JsonTextWriter(stringWriter))
        {
            write(writer);
            writer.Flush();
        }

        return read(JObject.Parse(sb.ToString()));
    }

    /// <summary>Sets how many quarter turns the patch's picture makes, as one undo step.</summary>
    private static void SetPatchTurns(Setup setup, OutputDefinition.Patch patch, int turns)
    {
        var normalized = OutputDefinition.Patch.NormalizeTurns(turns);
        if (normalized == patch.QuarterTurns)
            return;

        SetupUndo.RunUndoable("Rotate patch", setup, () => patch.QuarterTurns = normalized);
    }

    private static Vector2[] DefaultReferenceQuad(ReferenceImage image)
    {
        float w = Math.Max(1, image.Width);
        float h = Math.Max(1, image.Height);
        float x0 = w * 0.25f, x1 = w * 0.75f, y0 = h * 0.25f, y1 = h * 0.75f;
        return [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)];
    }

    /// <summary>The delete cascades, one per kind, side by side.</summary>
    private static void DeleteEntityInternal(Setup setup, SetupEntityKinds kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntityKinds.Surface:
                DeleteSurfaceSubtree(setup, id);
                break;

            case SetupEntityKinds.Slice:
                DeleteSliceInternal(setup, id);
                break;

            case SetupEntityKinds.Output:
                if (OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
                    DeleteOutputInternal(setup, machineConfig, id);

                break;

            case SetupEntityKinds.ReferenceImage:
                // Surfaces traced on this image lose their binding — a dangling ImageId would mean nothing.
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.Trace?.ImageId == id)
                        surface.Trace = null;
                }

                setup.ReferenceImages.RemoveAll(r => r.Id == id);
                break;

            case SetupEntityKinds.Prop:
                setup.Props.RemoveAll(p => p.Id == id);
                break;

            case SetupEntityKinds.FloorPlan:
                // Its surfaces stay where the plan last put them; only the derivation ends.
                setup.FloorPlans.RemoveAll(p => p.Id == id);
                break;

            case SetupEntityKinds.Patch:
                if (setup.FindPatch(id, out var owner) != null)
                    owner!.Patches.RemoveAll(p => p.Id == id);

                break;
        }
    }

    /// <summary>Deleting a slice clears it from anything showing it — the reference would mean nothing.</summary>
    private static void DeleteSliceInternal(Setup setup, Guid sliceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                surface.SliceId = Guid.Empty;
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    patch.SliceId = Guid.Empty; // the patch keeps its place on the canvas, just unfed
            }
        }

        setup.Slices.RemoveAll(s => s.Id == sliceId);
    }

    // Deleting a surface takes its sub-region subtree with it (they're cuts of the parent, meaningless on
    // their own). Children aren't guaranteed to follow their parent in list order, so sweep until no new
    // descendant is found.
    private static void DeleteSurfaceSubtree(Setup setup, Guid rootId)
    {
        var ids = new HashSet<Guid> { rootId };
        bool grew;
        do
        {
            grew = false;
            foreach (var surface in setup.Surfaces)
            {
                if (ids.Contains(surface.ParentId) && ids.Add(surface.Id))
                    grew = true;
            }
        }
        while (grew);

        FloorPlanSync.ReleaseSurfaces(setup, ids);
        for (var i = setup.Surfaces.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(setup.Surfaces[i].Id))
                setup.Surfaces.RemoveAt(i);
        }
    }

    // Deleting an output cascades: drop every surface's mapping onto it, unbind the display, and stop
    // presenting it. Surfaces left without a mapping simply have no output — not lost. (The display binding
    // lives in the per-machine config outside the setup file, so an undo restores the output unbound.)
    private static void DeleteOutputInternal(Setup setup, MachineConfig machineConfig, Guid outputId)
    {
        setup.Outputs.RemoveAll(o => o.Id == outputId);
        OutputPresentation.ReleaseOutput(outputId);
        foreach (var surface in setup.Surfaces)
            surface.OutputMappings.RemoveAll(m => m.OutputId == outputId);

        machineConfig.Unbind(outputId);
        if (OutputPresentation.PresentedOutputId == outputId)
            OutputPresentation.PresentedOutputId = Guid.Empty;
    }

    /// <summary>The duplicate cascades, one per kind, side by side.</summary>
    /// <summary>The copy without its own undo step, for a gesture that duplicates and moves in one; selects the copy.</summary>
    internal static void DuplicateEntityInternal(SetupEntitySelection selection, Setup setup, SetupEntityKinds kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntityKinds.Surface:
                var surface = setup.FindSurface(id);
                if (surface != null)
                    DuplicateSurface(selection, setup, surface);

                return;

            case SetupEntityKinds.Slice:
            {
                var slice = setup.FindSlice(id);
                var copy = slice == null ? null : CloneViaJson(slice.WriteToJson, Slice.ReadFromJson);
                if (copy == null)
                    return;

                copy.Id = Guid.NewGuid();
                if (!string.IsNullOrEmpty(copy.Name))
                    copy.Name += " copy";

                setup.Slices.Add(copy);
                selection.Select(SetupEntityKinds.Slice, copy.Id);
                break;
            }

            case SetupEntityKinds.Output:
            {
                var output = setup.FindOutput(id);
                var copy = output == null ? null : CloneViaJson(output.WriteToJson, OutputDefinition.ReadFromJson);
                if (copy == null)
                    return;

                // Fresh id: mappings and the machine's display binding stay with the original.
                copy.Id = Guid.NewGuid();
                copy.Name += " copy";
                setup.Outputs.Add(copy);
                selection.Select(SetupEntityKinds.Output, copy.Id);
                break;
            }

            case SetupEntityKinds.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                var copy = image == null ? null : CloneViaJson(image.WriteToJson, ReferenceImage.ReadFromJson);
                if (copy == null)
                    return;

                copy.Id = Guid.NewGuid();
                copy.Name += " copy";
                setup.ReferenceImages.Add(copy);
                selection.Select(SetupEntityKinds.ReferenceImage, copy.Id);
                break;
            }

            case SetupEntityKinds.Prop:
            {
                var prop = setup.FindProp(id);
                var copy = prop == null ? null : CloneViaJson(prop.WriteToJson, Prop.ReadFromJson);
                if (copy == null)
                    return;

                copy.Id = Guid.NewGuid();
                setup.Props.Add(copy);
                selection.Select(SetupEntityKinds.Prop, copy.Id);
                break;
            }

            case SetupEntityKinds.FloorPlan:
            {
                var plan = setup.FindFloorPlan(id);
                var copy = plan == null ? null : CloneViaJson(plan.WriteToJson, FloorPlan.ReadFromJson);
                if (copy == null || plan == null)
                    return;

                // The footprint copies, its surfaces don't: a wall stands on one segment only.
                copy.Id = Guid.NewGuid();
                copy.Name += " copy";
                copy.FloorSurfaceId = Guid.Empty;
                for (var i = 0; i < copy.WallSurfaceIds.Count; i++)
                    copy.WallSurfaceIds[i] = Guid.Empty;

                if (copy.BoardPlacement != null && plan.TryGetBounds(out var planMin, out var planMax))
                    copy.BoardPlacement.Position += new Vector2(planMax.X - planMin.X + 0.5f, 0);

                setup.FloorPlans.Add(copy);
                selection.Select(SetupEntityKinds.FloorPlan, copy.Id);
                break;
            }

            case SetupEntityKinds.Patch:
            {
                var patch = setup.FindPatch(id, out var owner);
                var copy = patch == null ? null : CloneViaJson(patch.WriteToJson, OutputDefinition.Patch.ReadFromJson);
                if (copy == null || owner == null)
                    return;

                // Same place on the canvas: the copy is meant to be re-fed or moved, not hidden under the original.
                copy.Id = Guid.NewGuid();
                if (!string.IsNullOrEmpty(copy.Name))
                    copy.Name += " copy";

                owner.Patches.Add(copy);
                selection.Select(SetupEntityKinds.Patch, copy.Id);
                break;
            }
        }
    }

    /// <summary>
    /// Copies a surface — with its sub-regions — offset a little so it doesn't hide under the original. The
    /// copy gets fresh GUIDs, so content sends still point at the original; the duplicate starts unbound.
    /// </summary>
    private static void DuplicateSurface(SetupEntitySelection selection, Setup setup, Surface surface)
    {
        var copy = CloneSurface(surface);
        var isChild = surface.ParentId != Guid.Empty;
        copy.Name = isChild ? $"Region {SetupRelations.CountChildren(setup, surface.ParentId) + 1}" : surface.Name + " copy";

        if (isChild)
        {
            copy.LocalPosition = surface.LocalPosition + new Vector2(surface.SizeInMeters.X * 0.15f,
                                                                    -surface.SizeInMeters.Y * 0.15f);
        }
        else
        {
            // A root carries its own pins, so nudge those instead.
            foreach (var mapping in copy.OutputMappings)
            {
                // A nudge of the canvas rather than a pixel count, so it reads the same at any resolution.
                for (var i = 0; i < mapping.Quad.Length; i++)
                    mapping.Quad[i] += new Vector2(0.0125f, 0.0125f);
            }
        }

        setup.Surfaces.Add(copy);
        DuplicateChildrenOf(setup, surface.Id, copy.Id);

        selection.Select(SetupEntityKinds.Surface, copy.Id);
    }

    private static void DuplicateChildrenOf(Setup setup, Guid sourceParentId, Guid newParentId)
    {
        // Snapshot first: the loop appends to the same list it walks.
        var originals = setup.Surfaces.FindAll(s => s.ParentId == sourceParentId);
        foreach (var original in originals)
        {
            var copy = CloneSurface(original);
            copy.ParentId = newParentId;
            setup.Surfaces.Add(copy);
            DuplicateChildrenOf(setup, original.Id, copy.Id);
        }
    }

    /// <summary>
    /// A full copy through the JSON round-trip (so measurements, the trace and the Board card come along, and
    /// so do fields added later), with fresh identities: the surface's own, and its annotations' — the aims
    /// keyed on them are re-keyed to match. The slice is deliberately not carried: the copy starts unfed.
    /// </summary>
    private static Surface CloneSurface(Surface source)
    {
        var copy = CloneViaJson(source.WriteToJson, Surface.ReadFromJson) ?? new Surface();
        copy.Id = Guid.NewGuid();
        copy.SliceId = Guid.Empty;

        foreach (var annotation in copy.Annotations)
        {
            var newId = Guid.NewGuid();
            foreach (var mapping in copy.OutputMappings)
            {
                if (mapping.PointAims.Remove(annotation.Id, out var aim))
                    mapping.PointAims[newId] = aim;
            }

            annotation.Id = newId;
        }

        return copy;
    }

    private static int CountProjectorOutputs(Setup setup)
    {
        var count = 0;
        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Projector)
                count++;
        }

        return count;
    }

    private static readonly List<SelectionTarget> _deleteBuffer = [];

    private static string? _imageFileFilter;

    /// <summary>Where dropped photos and plans land inside the project's assets folder.</summary>
    private const string ReferenceImageFolder = "images/reference";
}
