#nullable enable
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Entity mutations, graph glue, and shared labels/menus for output setups. These operations are shared
/// by the setup sidebar, the canvas views, and the flow view — they are model operations, not panel UI,
/// so they live here in a neutral class none of the views own.
/// </summary>
internal static class SetupActions
{
    /// <summary>
    /// Whether the two kinds form a routing connection at all — the drop matrix, direction-agnostic.
    /// Connectable pairs: surface↔output, slice↔output, source↔output, slice↔surface, source↔surface,
    /// slice↔patch, source↔patch.
    /// </summary>
    internal static bool CanConnect(SetupEntitySelection.EntityKind a, SetupEntitySelection.EntityKind b)
    {
        // Normalize so `a` is the content-flow upstream side.
        if (RoutingRank(a) > RoutingRank(b))
            (a, b) = (b, a);

        return b switch
                   {
                       SetupEntitySelection.EntityKind.Output => a is SetupEntitySelection.EntityKind.Surface
                                                                      or SetupEntitySelection.EntityKind.Slice
                                                                      or SetupEntitySelection.EntityKind.ContentSource,
                       SetupEntitySelection.EntityKind.Surface => a is SetupEntitySelection.EntityKind.Slice
                                                                       or SetupEntitySelection.EntityKind.ContentSource,
                       SetupEntitySelection.EntityKind.Patch => a is SetupEntitySelection.EntityKind.Slice
                                                                     or SetupEntitySelection.EntityKind.ContentSource,
                       _ => false,
                   };
    }

    // Position along the content flow (source → slice → surface → output); used to normalize drop direction.
    private static int RoutingRank(SetupEntitySelection.EntityKind kind)
    {
        return kind switch
                   {
                       SetupEntitySelection.EntityKind.ContentSource => 0,
                       SetupEntitySelection.EntityKind.Slice => 1,
                       SetupEntitySelection.EntityKind.Surface => 2,
                       SetupEntitySelection.EntityKind.Patch => 3,
                       SetupEntitySelection.EntityKind.Output => 3,
                       _ => -1,
                   };
    }

    /// <summary>
    /// Wraps a structural setup mutation in one undo step: snapshot the whole setup before, run the edit,
    /// and push a snapshot command + save only when something actually changed. Setup files are a few KB
    /// of plain DTOs, so whole-state snapshots are simpler and more robust than per-operation inverses —
    /// every cascade the edit performs is captured by construction.
    /// </summary>
    internal static void RunUndoable(string name, Setup setup, Action mutate)
    {
        var oldJson = setup.ToJsonString();
        mutate();
        var newJson = setup.ToJsonString();
        if (newJson == oldJson)
            return;

        // Already applied by mutate(), so Add rather than AddAndExecute.
        UndoRedoStack.Add(new SetupSnapshotCommand(name, setup.Id, oldJson, newJson));
        OutputSetupHandling.SaveActive();
    }

    /// <summary>
    /// Closes a continuous gesture (a drag) as one undo step: <paramref name="oldJson"/> is the setup's snapshot
    /// from the gesture's start; nothing is pushed when the setup came back unchanged.
    /// </summary>
    internal static void CommitGesture(Setup setup, string name, string oldJson)
    {
        var newJson = setup.ToJsonString();
        if (newJson == oldJson)
            return;

        UndoRedoStack.Add(new SetupSnapshotCommand(name, setup.Id, oldJson, newJson));
        OutputSetupHandling.SaveActive();
    }

    internal static void ApplyDrop(Setup setup, SetupEntitySelection.EntityKind dragKind, Guid dragId,
                                   SetupEntitySelection.EntityKind targetKind, Guid targetId)
    {
        RunUndoable("Connect", setup, () => ApplyDropInternal(setup, dragKind, dragId, targetKind, targetId));
    }

    private static void ApplyDropInternal(Setup setup, SetupEntitySelection.EntityKind dragKind, Guid dragId,
                                          SetupEntitySelection.EntityKind targetKind, Guid targetId)
    {
        // A drop means "connect these two" regardless of which one was picked up — dragging an output onto a
        // surface is the same link as dragging the surface onto the output. Normalize so the upstream side is
        // always the drag and the cases below only handle one direction each.
        if (RoutingRank(dragKind) > RoutingRank(targetKind))
        {
            (dragKind, targetKind) = (targetKind, dragKind);
            (dragId, targetId) = (targetId, dragId);
        }

        if (targetKind == SetupEntitySelection.EntityKind.Output && dragKind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.FindSurface(dragId);
            var output = setup.FindOutput(targetId);
            if (surface != null && output != null
                // A Layout child rides its parent's pin — a mapping of its own would detach it from the hierarchy.
                && !(surface.Kind == Surface.SurfaceKinds.Layout && surface.ParentId != Guid.Empty)
                && !surface.OutputMappings.Exists(m => m.OutputId == targetId))
            {
                surface.OutputMappings.Add(CreateDefaultMapping(output));
            }

            return;
        }

        // Dropping a source or slice straight onto an output shows it full-frame: the direct pipe, as a new
        // full-canvas patch (no surface, no corner pin). Dropped onto a patch, it re-feeds that patch.
        if (dragKind is SetupEntitySelection.EntityKind.Slice or SetupEntitySelection.EntityKind.ContentSource
            && targetKind is SetupEntitySelection.EntityKind.Output or SetupEntitySelection.EntityKind.Patch)
        {
            var sliceId = Guid.Empty;
            if (dragKind == SetupEntitySelection.EntityKind.Slice && setup.FindSlice(dragId) != null)
            {
                sliceId = dragId;
            }
            else if (dragKind == SetupEntitySelection.EntityKind.ContentSource)
            {
                var source = setup.FindSourceByChildId(dragId);
                if (source != null)
                    sliceId = EnsureSlice(setup, source).Id;
            }

            if (sliceId == Guid.Empty)
                return;

            if (targetKind == SetupEntitySelection.EntityKind.Patch)
            {
                var patch = setup.FindPatch(targetId, out _);
                if (patch != null)
                    patch.SliceId = sliceId;
            }
            else
            {
                var output = setup.FindOutput(targetId);
                if (output != null)
                    AddPatchInternal(output, sliceId);
            }

            return;
        }

        // Dropping a slice on a free surface shows it there. A *different* slice dropped on an occupied surface
        // lands as a sub-region cut to its own aspect (the poster-slot case) rather than replacing — but
        // re-dropping the slice the surface already shows is a no-op, not a spurious duplicate sub-region.
        if (dragKind == SetupEntitySelection.EntityKind.Slice)
        {
            var slice = setup.FindSlice(dragId);
            var surface = setup.FindSurface(targetId);
            if (slice != null && surface != null && surface.SliceId != slice.Id)
            {
                if (surface.SliceId == Guid.Empty)
                    surface.SliceId = slice.Id;
                else
                    AddRegionForSlice(setup, surface, slice);
            }
        }

        if (dragKind == SetupEntitySelection.EntityKind.ContentSource)
        {
            var source = setup.FindSourceByChildId(dragId);
            var surface = source == null ? null : setup.FindSurface(targetId);
            if (source != null && surface != null)
                surface.SliceId = EnsureSlice(setup, source).Id;
        }
    }

    internal static bool TryParseDrag(string data, out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;
        var separator = data.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(data.AsSpan(0, separator), out var kindInt)
            || !Guid.TryParse(data.AsSpan(separator + 1), out id))
            return false;

        kind = (SetupEntitySelection.EntityKind)kindInt;
        return true;
    }

    internal static void AddSlice(SetupEntitySelection selection, Setup setup, ContentSource source)
    {
        RunUndoable("Add slice", setup, () =>
                                        {
                                            // Left unnamed: the label is derived from the source, so it stays right when the op is later renamed.
                                            var slice = new Slice { SourceId = source.Id };
                                            setup.Slices.Add(slice);
                                            selection.Select(SetupEntitySelection.EntityKind.Slice, slice.Id);
                                        });
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

    /// <summary>Adds an unfed full-canvas patch to an output — the direct pipe waiting for content.</summary>
    internal static void AddPatch(SetupEntitySelection selection, Setup setup, OutputDefinition output)
    {
        RunUndoable("Add patch", setup, () =>
                                        {
                                            var patch = AddPatchInternal(output, Guid.Empty);
                                            selection.Select(SetupEntitySelection.EntityKind.Patch, patch.Id);
                                        });
    }

    private static OutputDefinition.Patch AddPatchInternal(OutputDefinition output, Guid sliceId)
    {
        // Left unnamed: the label is derived from its position (see PatchLabel).
        var patch = new OutputDefinition.Patch { SliceId = sliceId, Quad = output.FullCanvasQuad() };
        output.Patches.Add(patch);
        return patch;
    }

    /// <summary>
    /// Whether <paramref name="kind"/>/<paramref name="id"/> can take the primary selection as its input, and
    /// whether it already does. Clicking the in-gutter then binds or unbinds without any dragging: select a
    /// slice and the surfaces that could show it light up; select a surface and the outputs light up.
    /// </summary>
    internal static bool TryDescribeInputToggle(Setup setup, SetupEntitySelection.EntityKind kind, Guid id,
                                                SetupEntitySelection.EntityKind sourceKind, Guid sourceId, out bool isBound)
    {
        isBound = false;
        if (sourceKind == SetupEntitySelection.EntityKind.None)
            return false;

        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface == null)
                    return false;

                if (sourceKind == SetupEntitySelection.EntityKind.Slice)
                {
                    isBound = surface.SliceId == sourceId;
                    return setup.FindSlice(sourceId) != null;
                }

                if (sourceKind == SetupEntitySelection.EntityKind.ContentSource)
                {
                    var source = setup.FindSourceByChildId(sourceId);
                    if (source == null)
                        return false;

                    isBound = SetupRelations.IsSliceOf(setup, surface.SliceId, source.Id);
                    return true;
                }

                return false;
            }

            case SetupEntitySelection.EntityKind.Output:
            {
                var output = setup.FindOutput(id);
                if (output == null)
                    return false;

                // A slice or source binds through the direct pipe: a full-canvas patch showing it.
                if (sourceKind == SetupEntitySelection.EntityKind.Slice)
                {
                    isBound = SetupRelations.OutputShowsSlice(output, sourceId);
                    return setup.FindSlice(sourceId) != null;
                }

                if (sourceKind == SetupEntitySelection.EntityKind.ContentSource)
                {
                    var source = setup.FindSourceByChildId(sourceId);
                    if (source == null)
                        return false;

                    isBound = SetupRelations.OutputShowsSource(setup, output, source.Id);
                    return true;
                }

                if (sourceKind != SetupEntitySelection.EntityKind.Surface)
                    return false;

                var surface = setup.FindSurface(sourceId);
                if (surface == null)
                    return false;

                // A Layout child rides its parent's pin — it can't bind to an output itself.
                if (surface.Kind == Surface.SurfaceKinds.Layout && surface.ParentId != Guid.Empty)
                    return false;

                isBound = surface.OutputMappings.Exists(m => m.OutputId == id);
                return true;
            }

            case SetupEntitySelection.EntityKind.Patch:
            {
                var patch = setup.FindPatch(id, out _);
                if (patch == null)
                    return false;

                if (sourceKind == SetupEntitySelection.EntityKind.Slice)
                {
                    isBound = patch.SliceId == sourceId;
                    return setup.FindSlice(sourceId) != null;
                }

                if (sourceKind == SetupEntitySelection.EntityKind.ContentSource)
                {
                    var source = setup.FindSourceByChildId(sourceId);
                    if (source == null)
                        return false;

                    isBound = SetupRelations.IsSliceOf(setup, patch.SliceId, source.Id);
                    return true;
                }

                return false;
            }

            default:
                return false;
        }
    }

    internal static void ToggleInput(Setup setup, SetupEntitySelection.EntityKind kind, Guid id,
                                     SetupEntitySelection.EntityKind sourceKind, Guid sourceId)
    {
        RunUndoable("Change binding", setup, () => ToggleInputInternal(setup, kind, id, sourceKind, sourceId));
    }

    private static void ToggleInputInternal(Setup setup, SetupEntitySelection.EntityKind kind, Guid id,
                                            SetupEntitySelection.EntityKind sourceKind, Guid sourceId)
    {
        if (!TryDescribeInputToggle(setup, kind, id, sourceKind, sourceId, out var isBound))
            return;

        if (kind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.FindSurface(id);
            if (surface == null)
                return;

            if (isBound)
            {
                surface.SliceId = Guid.Empty;
            }
            else if (sourceKind == SetupEntitySelection.EntityKind.Slice)
            {
                surface.SliceId = sourceId;
            }
            else
            {
                var source = setup.FindSourceByChildId(sourceId);
                if (source == null)
                    return;

                surface.SliceId = EnsureSlice(setup, source).Id;
            }
        }
        else if (kind == SetupEntitySelection.EntityKind.Patch)
        {
            var patch = setup.FindPatch(id, out _);
            if (patch == null)
                return;

            if (isBound)
            {
                patch.SliceId = Guid.Empty;
            }
            else if (sourceKind == SetupEntitySelection.EntityKind.Slice)
            {
                patch.SliceId = sourceId;
            }
            else
            {
                var source = setup.FindSourceByChildId(sourceId);
                if (source == null)
                    return;

                patch.SliceId = EnsureSlice(setup, source).Id;
            }
        }
        else if (sourceKind is SetupEntitySelection.EntityKind.Slice or SetupEntitySelection.EntityKind.ContentSource)
        {
            // Toggling content on an output: unbound adds a full-canvas patch; bound drops every patch showing it.
            var output = setup.FindOutput(id);
            if (output == null)
                return;

            var sliceId = sourceId;
            var sourceOfChild = sourceKind == SetupEntitySelection.EntityKind.ContentSource ? setup.FindSourceByChildId(sourceId) : null;
            if (sourceKind == SetupEntitySelection.EntityKind.ContentSource)
            {
                if (sourceOfChild == null)
                    return;

                sliceId = EnsureSlice(setup, sourceOfChild).Id;
            }

            if (!isBound)
            {
                AddPatchInternal(output, sliceId);
            }
            else if (sourceOfChild != null)
            {
                output.Patches.RemoveAll(p => SetupRelations.IsSliceOf(setup, p.SliceId, sourceOfChild.Id));
            }
            else
            {
                output.Patches.RemoveAll(p => p.SliceId == sliceId);
            }
        }
        else
        {
            var surface = setup.FindSurface(sourceId);
            var output = setup.FindOutput(id);
            if (surface == null || output == null)
                return;

            if (isBound)
                surface.OutputMappings.RemoveAll(m => m.OutputId == id);
            else
                surface.OutputMappings.Add(CreateDefaultMapping(output));
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
                for (var i = 0; i < mapping.Quad.Length; i++)
                    mapping.Quad[i] += new Vector2(24, 24);
            }
        }

        setup.Surfaces.Add(copy);
        DuplicateChildrenOf(setup, surface.Id, copy.Id);

        selection.Select(SetupEntitySelection.EntityKind.Surface, copy.Id);
    }

    internal static void AddSurface(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        RunUndoable("Add surface", setup, () =>
                                          {
                                              var surface = new Surface { Name = $"Surface {setup.Surfaces.Count + 1}" };
                                              setup.Surfaces.Add(surface);
                                              selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
                                          });
    }

    internal static void AddProp(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        RunUndoable("Add prop", setup, () =>
                                       {
                                           var prop = new Prop();
                                           setup.Props.Add(prop);
                                           selection.Select(SetupEntitySelection.EntityKind.Prop, prop.Id);
                                       });
    }

    internal static void AddOutput(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        RunUndoable("Add output", setup, () =>
                                         {
                                             var output = new OutputDefinition
                                                              {
                                                                  Name = $"P{CountProjectorOutputs(setup) + 1}",
                                                                  Kind = OutputDefinition.Kinds.Projector,
                                                                  CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1200),
                                                              };
                                             setup.Outputs.Add(output);
                                             selection.Select(SetupEntitySelection.EntityKind.Output, output.Id);
                                         });
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

        RunUndoable("Add reference image", setup, () =>
                                                  {
                                                      var image = new ReferenceImage
                                                                      {
                                                                          Name = Path.GetFileNameWithoutExtension(asset.Address),
                                                                          FilePath = asset.Address,
                                                                          BoardPlacement = new CanvasPlacement { Position = boardPosition },
                                                                      };
                                                      setup.ReferenceImages.Add(image);
                                                      selection.Select(SetupEntitySelection.EntityKind.ReferenceImage, image.Id);
                                                  });
    }

    /// <summary>Traces an existing (untraced) surface on an image: a default quad in the photo's middle to drag onto the wall.</summary>
    internal static void TraceSurfaceOnImage(SetupEntitySelection selection, Setup setup, Surface surface, ReferenceImage image)
    {
        RunUndoable("Trace surface", setup, () =>
                                            {
                                                surface.Reference = new Surface.ReferenceBinding { ImageId = image.Id, Quad = DefaultReferenceQuad(image) };
                                                selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
                                            });
    }

    /// <summary>A new surface, traced on the image right away — the "there is a wall in this photo" gesture.</summary>
    internal static void TraceNewSurface(SetupEntitySelection selection, Setup setup, ReferenceImage image)
    {
        RunUndoable("Trace new surface", setup, () =>
                                                {
                                                    var surface = new Surface
                                                                      {
                                                                          Name = $"Surface {setup.Surfaces.Count + 1}",
                                                                          Reference = new Surface.ReferenceBinding { ImageId = image.Id, Quad = DefaultReferenceQuad(image) },
                                                                      };
                                                    setup.Surfaces.Add(surface);
                                                    selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
                                                });
    }

    private static Vector2[] DefaultReferenceQuad(ReferenceImage image)
    {
        float w = Math.Max(1, image.Width);
        float h = Math.Max(1, image.Height);
        float x0 = w * 0.25f, x1 = w * 0.75f, y0 = h * 0.25f, y1 = h * 0.75f;
        return [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)];
    }

    internal static void AddReferenceImage(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        RunUndoable("Add reference image", setup, () =>
                                                  {
                                                      var image = new ReferenceImage { Name = $"Image {setup.ReferenceImages.Count + 1}" };
                                                      setup.ReferenceImages.Add(image);
                                                      selection.Select(SetupEntitySelection.EntityKind.ReferenceImage, image.Id);
                                                  });
    }

    /// <summary>
    /// Drops a <c>SendToOutput</c> op into the focused composition, selects it, and frames the view on it. When a
    /// texture-outputting op is selected it lands to its right and is wired straight in, so the feed shows up in
    /// the setup at once (the CONTENT row appears next frame, once <see cref="ContentSourceSync"/> adopts it).
    /// </summary>
    internal static void AddContentSend(SetupEntitySelection selection)
    {
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null)
            return;

        if (!composition.Symbol.TryGetSymbolUi(out var compositionUi)
            || !SymbolUiRegistry.TryGetSymbolUi(SendToOutputSymbolId, out var sendSymbolUi))
            return;

        // A selected texture op becomes the feed: place the send op to its right and wire it up.
        var selected = projectView.NodeSelection.GetSelectedInstanceWithoutComposition();
        var sourceSlot = selected == null ? null : FindTextureOutput(selected);
        var selectedUi = selected?.GetChildUi();
        var pos = selectedUi != null
                      ? selectedUi.PosOnCanvas + new Vector2(selectedUi.Size.X + 40, 0)
                      : Vector2.Zero;

        var newChildUi = GraphOperations.AddSymbolChild(sendSymbolUi.Symbol, compositionUi, pos);

        if (sourceSlot != null && selectedUi != null)
        {
            var connection = new Symbol.Connection(selectedUi.Id, sourceSlot.Id, newChildUi.Id, SendToOutputTextureInputId);
            UndoRedoStack.AddAndExecute(new AddConnectionCommand(compositionUi.Symbol, connection, 0));
        }

        projectView.NodeSelection.TrySelectCompositionChild(composition, newChildUi.Id, add: false);
        projectView.FocusViewToSelection();
    }

    /// <summary>
    /// Deletes everything deletable in the selection. Each kind keeps its own cascade (a surface re-parents
    /// its children, an output drops the mappings onto it), so deleting a set is just deleting each in turn —
    /// which is why the targets are copied first: those cascades mutate the setup underneath us.
    /// </summary>
    internal static void DeleteSelection(SetupEntitySelection selection, Setup setup)
    {
        RunUndoable("Delete selection", setup, () =>
                                               {
                                                   _deleteBuffer.Clear();
                                                   _deleteBuffer.AddRange(selection.Targets);

                                                   foreach (var target in _deleteBuffer)
                                                       DeleteEntityInternal(setup, target.Kind, target.EntityId);

                                                   selection.Clear();
                                               });
    }

    /// <summary>Renames an entity by kind. A content source has no name of its own — renaming it renames its
    /// op (already undoable as a graph command), which flows back through the sync.</summary>
    internal static void RenameEntity(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, string newName)
    {
        if (kind == SetupEntitySelection.EntityKind.ContentSource)
        {
            RenameContentSourceOp(id, newName);
            return;
        }

        RunUndoable("Rename", setup, () => RenameEntityInternal(setup, kind, id, newName));
    }

    private static void RenameEntityInternal(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, string newName)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Output:
                var output = setup.FindOutput(id);
                if (output == null)
                    return;

                output.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.ReferenceImage:
                var image = setup.FindReferenceImage(id);
                if (image == null)
                    return;

                image.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.Surface:
                var surface = setup.FindSurface(id);
                if (surface == null)
                    return;

                surface.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.Slice:
                var slice = setup.FindSlice(id);
                if (slice == null)
                    return;

                slice.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.Patch:
                var patch = setup.FindPatch(id, out _);
                if (patch == null)
                    return;

                patch.Name = newName;
                break;
        }
    }

    /// <summary>A content source is a graph op (delete the op instead); everything else deletes here.</summary>
    internal static bool CanDeleteDirectly(SetupEntitySelection.EntityKind kind)
    {
        return kind is SetupEntitySelection.EntityKind.Surface
                    or SetupEntitySelection.EntityKind.Slice
                    or SetupEntitySelection.EntityKind.Output
                    or SetupEntitySelection.EntityKind.ReferenceImage
                    or SetupEntitySelection.EntityKind.Prop
                    or SetupEntitySelection.EntityKind.Patch;
    }

    internal static void DeleteEntity(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        RunUndoable("Delete", setup, () => DeleteEntityInternal(setup, kind, id));
    }

    private static void DeleteEntityInternal(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                DeleteSurfaceSubtree(setup, id);
                break;

            case SetupEntitySelection.EntityKind.Slice:
                DeleteSliceInternal(setup, id);
                break;

            case SetupEntitySelection.EntityKind.Output:
                if (OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
                    DeleteOutputInternal(setup, machineConfig, id);

                break;

            case SetupEntitySelection.EntityKind.ReferenceImage:
                // Surfaces traced on this image lose their binding — a dangling ImageId would mean nothing.
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.Reference?.ImageId == id)
                        surface.Reference = null;
                }

                setup.ReferenceImages.RemoveAll(r => r.Id == id);
                break;

            case SetupEntitySelection.EntityKind.Prop:
                setup.Props.RemoveAll(p => p.Id == id);
                break;

            case SetupEntitySelection.EntityKind.Patch:
                if (setup.FindPatch(id, out var owner) != null)
                    owner!.Patches.RemoveAll(p => p.Id == id);

                break;
        }
    }

    /// <summary>A content source is its op — duplicating it wouldn't carry the feed; everything else clones.</summary>
    internal static bool CanDuplicate(SetupEntitySelection.EntityKind kind)
    {
        return kind is SetupEntitySelection.EntityKind.Surface
                    or SetupEntitySelection.EntityKind.Slice
                    or SetupEntitySelection.EntityKind.Output
                    or SetupEntitySelection.EntityKind.ReferenceImage
                    or SetupEntitySelection.EntityKind.Prop
                    or SetupEntitySelection.EntityKind.Patch;
    }

    /// <summary>A prop has no name to rename; a content source renames its op.</summary>
    internal static bool CanRename(SetupEntitySelection.EntityKind kind)
    {
        return kind is not (SetupEntitySelection.EntityKind.Prop or SetupEntitySelection.EntityKind.None);
    }

    internal static void DuplicateEntity(SetupEntitySelection selection, Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        RunUndoable("Duplicate", setup, () => DuplicateEntityInternal(selection, setup, kind, id));
    }

    private static void DuplicateEntityInternal(SetupEntitySelection selection, Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                var surface = setup.FindSurface(id);
                if (surface != null)
                    DuplicateSurface(selection, setup, surface);

                return;

            case SetupEntitySelection.EntityKind.Slice:
            {
                var slice = setup.FindSlice(id);
                var copy = slice == null ? null : CloneViaJson(slice.WriteToJson, Slice.ReadFromJson);
                if (copy == null)
                    return;

                copy.Id = Guid.NewGuid();
                if (!string.IsNullOrEmpty(copy.Name))
                    copy.Name += " copy";

                setup.Slices.Add(copy);
                selection.Select(SetupEntitySelection.EntityKind.Slice, copy.Id);
                break;
            }

            case SetupEntitySelection.EntityKind.Output:
            {
                var output = setup.FindOutput(id);
                var copy = output == null ? null : CloneViaJson(output.WriteToJson, OutputDefinition.ReadFromJson);
                if (copy == null)
                    return;

                // Fresh id: mappings and the machine's display binding stay with the original.
                copy.Id = Guid.NewGuid();
                copy.Name += " copy";
                setup.Outputs.Add(copy);
                selection.Select(SetupEntitySelection.EntityKind.Output, copy.Id);
                break;
            }

            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                var copy = image == null ? null : CloneViaJson(image.WriteToJson, ReferenceImage.ReadFromJson);
                if (copy == null)
                    return;

                copy.Id = Guid.NewGuid();
                copy.Name += " copy";
                setup.ReferenceImages.Add(copy);
                selection.Select(SetupEntitySelection.EntityKind.ReferenceImage, copy.Id);
                break;
            }

            case SetupEntitySelection.EntityKind.Prop:
            {
                var prop = setup.FindProp(id);
                var copy = prop == null ? null : CloneViaJson(prop.WriteToJson, Prop.ReadFromJson);
                if (copy == null)
                    return;

                copy.Id = Guid.NewGuid();
                setup.Props.Add(copy);
                selection.Select(SetupEntitySelection.EntityKind.Prop, copy.Id);
                break;
            }

            case SetupEntitySelection.EntityKind.Patch:
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
                selection.Select(SetupEntitySelection.EntityKind.Patch, copy.Id);
                break;
            }
        }
    }

    /// <summary>Clones a setup entity through its own JSON round-trip, so new fields are picked up without
    /// touching the clone. The caller re-ids the copy.</summary>
    private static T? CloneViaJson<T>(Action<JsonTextWriter> write, Func<JToken, T> read) where T : class
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
        foreach (var surface in setup.Surfaces)
            surface.OutputMappings.RemoveAll(m => m.OutputId == outputId);

        machineConfig.Unbind(outputId);
        if (OutputManager.PresentedOutputId == outputId)
            OutputManager.PresentedOutputId = Guid.Empty;
    }

    /// <summary>
    /// How many of the selected entities this panel can actually delete. A content source is a graph op and a
    /// slice's source may be gone, so the menu counts what will really go rather than how many rows are lit.
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

    internal static string SendName(Instance instance)
    {
        var parent = instance.Parent;
        if (parent != null && parent.Symbol.Children.TryGetValue(instance.SymbolChildId, out var child))
            return child.ReadableName;

        return "content";
    }

    internal static Instance? FindSendInstance(Guid childId)
    {
        var sinks = OutputSinkRegistry.Sinks;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is Instance instance && instance.SymbolChildId == childId)
                return instance;
        }

        return null;
    }

    /// <summary>
    /// Renames a content source by renaming its op — the source has no name of its own, it mirrors the
    /// SendToOutput op (see <see cref="ContentSourceSync"/>). Needs a live instance to reach the graph; an
    /// op that isn't instantiated can't be renamed from here.
    /// </summary>
    internal static void RenameContentSourceOp(Guid childId, string newName)
    {
        var parent = FindSendInstance(childId)?.Parent;
        var parentSymbolUi = parent?.GetSymbolUi();
        if (parentSymbolUi == null || !parentSymbolUi.ChildUis.TryGetValue(childId, out var childUi))
            return;

        UndoRedoStack.AddAndExecute(new ChangeSymbolChildNameCommand(childUi, parentSymbolUi.Symbol) { NewName = newName });
    }

    // Select the content's SendToOutput op in the focused graph and frame it — the sidebar → graph half of
    // the sync (the graph → sidebar highlight is handled by the highlighted-content id).
    internal static void RevealContentOpInGraph(Guid childId)
    {
        var instance = FindSendInstance(childId);
        var parentSymbolUi = instance?.Parent?.GetSymbolUi();
        if (instance == null || parentSymbolUi == null || ProjectView.Focused == null)
            return;

        if (!parentSymbolUi.ChildUis.TryGetValue(instance.SymbolChildId, out var childUi))
            return;

        ProjectView.Focused.NodeSelection.SetSelection(childUi, instance);
        FitViewToSelectionHandling.FitViewToSelection();
    }

    /// <summary>Display name of a content send, for labelling its slice on the source canvas.</summary>
    internal static string? TryGetContentName(Guid contentChildId)
    {
        return FindSendInstance(contentChildId) is { } instance ? SendName(instance) : null;
    }

    /// <summary>A display name for any entity kind — pin labels, tooltips. Resolves against the
    /// active setup; falls back to the kind's name when the entity (or its name) is gone.</summary>
    internal static string NameForEntity(SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return kind.ToString();

        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                return FallbackIfEmpty(setup.FindSurface(id)?.Name, "Surface");

            case SetupEntitySelection.EntityKind.Output:
                return FallbackIfEmpty(setup.FindOutput(id)?.Name, "Output");

            case SetupEntitySelection.EntityKind.ReferenceImage:
                return FallbackIfEmpty(setup.FindReferenceImage(id)?.Name, "Reference Image");

            case SetupEntitySelection.EntityKind.Slice:
            {
                var slice = setup.FindSlice(id);
                return slice == null ? "Slice" : SliceLabel(setup, slice);
            }

            case SetupEntitySelection.EntityKind.ContentSource:
                return TryGetContentName(id) ?? "Content";

            case SetupEntitySelection.EntityKind.Prop:
                return FallbackIfEmpty(setup.FindProp(id)?.Kind, "Prop");

            case SetupEntitySelection.EntityKind.Patch:
            {
                var patch = setup.FindPatch(id, out var owner);
                return patch == null || owner == null ? "Patch" : PatchLabel(owner, patch);
            }

            default:
                return kind.ToString();
        }
    }

    /// <summary>
    /// Replaces the output's patches with a columns × rows grid of tiles covering the canvas — the split-matrix
    /// and TV-wall case. Every tile is fed by what the first patch showed, so a full-frame feed becomes N copies
    /// ready to be re-routed one by one.
    /// </summary>
    internal static void SplitOutput(SetupEntitySelection selection, Setup setup, OutputDefinition output, int columns, int rows)
    {
        RunUndoable($"Split {columns}×{rows}", setup, () =>
                                                       {
                                                           var feed = output.Patches.Count > 0 ? output.Patches[0].SliceId : Guid.Empty;
                                                           output.Patches.Clear();

                                                           float w = Math.Max(1, output.CanvasResolution.Width);
                                                           float h = Math.Max(1, output.CanvasResolution.Height);
                                                           var cell = new Vector2(w / columns, h / rows);
                                                           for (var row = 0; row < rows; row++)
                                                           {
                                                               for (var column = 0; column < columns; column++)
                                                               {
                                                                   var min = new Vector2(column * cell.X, row * cell.Y);
                                                                   var max = min + cell;
                                                                   output.Patches.Add(new OutputDefinition.Patch
                                                                                          {
                                                                                              SliceId = feed,
                                                                                              Quad = [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)],
                                                                                          });
                                                               }
                                                           }

                                                           selection.Select(SetupEntitySelection.EntityKind.Patch, output.Patches[0].Id);
                                                       });
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

        RunUndoable("Use on surface", setup, () =>
                                             {
                                                 var min = patch.Quad[0];
                                                 var max = patch.Quad[0];
                                                 foreach (var corner in patch.Quad)
                                                 {
                                                     min = Vector2.Min(min, corner);
                                                     max = Vector2.Max(max, corner);
                                                 }

                                                 // A metre of width, the height by the quad's aspect: the physical size is unknown until
                                                 // measured, and the content density follows from the pixels the patch already covered.
                                                 var widthPx = MathF.Max(max.X - min.X, 1);
                                                 var heightPx = MathF.Max(max.Y - min.Y, 1);
                                                 var surface = new Surface
                                                                   {
                                                                       Name = string.IsNullOrEmpty(patch.Name) ? $"Surface {setup.Surfaces.Count + 1}" : patch.Name,
                                                                       SizeInMeters = new Vector2(1, heightPx / widthPx),
                                                                       PixelsPerMeter = widthPx,
                                                                       SliceId = patch.SliceId,
                                                                       OutputMappings =
                                                                       [
                                                                           new Surface.OutputMapping { OutputId = output.Id, Quad = (Vector2[])patch.Quad.Clone() },
                                                                       ],
                                                                   };

                                                 setup.Surfaces.Add(surface);
                                                 output.Patches.RemoveAll(p => p.Id == patchId);
                                                 selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
                                             });
    }

    /// <summary>A patch's display name: the typed name, else "Patch N" by its position on the output.</summary>
    internal static string PatchLabel(OutputDefinition output, OutputDefinition.Patch patch)
    {
        if (!string.IsNullOrEmpty(patch.Name))
            return patch.Name;

        var ordinal = 1;
        foreach (var other in output.Patches)
        {
            if (other.Id == patch.Id)
                break;

            ordinal++;
        }

        return $"Patch {ordinal}";
    }

    private static string FallbackIfEmpty(string? name, string fallback)
    {
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    /// <summary>
    /// A slice's display name: a name the user typed if there is one, otherwise a default derived from its
    /// source. Unnamed sources give "Slice N"; a renamed source gives "{name}.N", so naming the op renames
    /// every one of its auto-named slices at once. N is the slice's position among its source's slices.
    /// </summary>
    internal static string SliceLabel(Setup setup, Slice slice)
    {
        if (!string.IsNullOrEmpty(slice.Name))
            return slice.Name;

        var ordinal = 1;
        foreach (var other in setup.Slices)
        {
            if (other.SourceId != slice.SourceId)
                continue;

            if (other.Id == slice.Id)
                break;

            ordinal++;
        }

        var source = setup.FindSource(slice.SourceId);
        return source is { IsRenamed: true } && !string.IsNullOrEmpty(source.Name)
                   ? $"{source.Name}.{ordinal}"
                   : $"Slice {ordinal}";
    }

    internal static string SurfaceShortLabel(Surface surface)
    {
        return Abbreviate(surface.Name);
    }

    // Compact gutter form: uppercase letters + digits ("Surface 1" → "S1", "WallFront" → "WF"), falling back
    // to the full name when there's nothing to abbreviate (all-lowercase).
    internal static string Abbreviate(string name)
    {
        Span<char> buffer = stackalloc char[6];
        var length = 0;
        foreach (var c in name)
        {
            if ((char.IsUpper(c) || char.IsDigit(c)) && length < buffer.Length)
                buffer[length++] = c;
        }

        return length >= 1 ? new string(buffer[..length]) : name;
    }

    private static Surface.OutputMapping CreateDefaultMapping(OutputDefinition output)
    {
        float w = Math.Max(1, output.CanvasResolution.Width);
        float h = Math.Max(1, output.CanvasResolution.Height);
        float x0 = w * 0.2f, x1 = w * 0.8f, y0 = h * 0.2f, y1 = h * 0.8f;
        return new Surface.OutputMapping
                   {
                       OutputId = output.Id,
                       Quad = [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)],
                   };
    }

    /// <summary>
    /// A source's first slice, creating a full-frame one if it has none — assigning content needs a slice to
    /// name, and "the whole image" is simply the identity rect.
    /// </summary>
    private static Slice EnsureSlice(Setup setup, ContentSource source)
    {
        var existing = setup.Slices.Find(s => s.SourceId == source.Id);
        if (existing != null)
            return existing;

        // Unnamed: its label is derived from the source (see SliceLabel), so renaming the op renames it too.
        var slice = new Slice { SourceId = source.Id };
        setup.Slices.Add(slice);
        return slice;
    }

    /// <summary>
    /// A sub-region shaped to a slice: sized so its real-world proportions match the slice's pixels, so the
    /// content lands undistorted, and centred in the parent.
    /// </summary>
    private static void AddRegionForSlice(Setup setup, Surface parent, Slice slice)
    {
        var parentSize = parent.SizeInMeters;
        var aspect = TryGetSliceAspect(setup, slice, out var value) ? value : 1f;

        var width = parentSize.X * 0.5f;
        var height = width / MathF.Max(aspect, 0.0001f);
        if (height > parentSize.Y * 0.8f)
        {
            height = parentSize.Y * 0.5f;
            width = height * aspect;
        }

        // Centred in the parent, expressed from the parent's anchor like every child position.
        var bottomLeft = new Vector2(parentSize.X * 0.5f - width * 0.5f, parentSize.Y * 0.5f - height * 0.5f)
                         - parent.AnchorInMeters;

        var region = new Surface
                         {
                             Name = string.IsNullOrEmpty(slice.Name) ? $"Region {SetupRelations.CountChildren(setup, parent.Id) + 1}" : slice.Name,
                             Kind = Surface.SurfaceKinds.Layout,
                             ParentId = parent.Id,
                             SizeInMeters = new Vector2(MathF.Max(width, SurfaceGeometry.MinSize),
                                                        MathF.Max(height, SurfaceGeometry.MinSize)),
                             LocalPosition = bottomLeft,
                             PixelsPerMeter = parent.PixelsPerMeter,
                             SliceId = slice.Id,
                         };

        setup.Surfaces.Add(region);
    }

    /// <summary>
    /// Re-meters a surface: everything stored in its metres — the size, the measuring lines, the regions and
    /// their descendants — scales by <paramref name="scale"/> about the anchor. The projection and the trace
    /// are untouched: the wall is where it was, only the numbers describing it change.
    /// </summary>
    internal static void ScaleSurfaceMetric(Setup setup, Surface surface, Vector2 scale)
    {
        surface.SizeInMeters *= scale;
        foreach (var annotation in surface.Annotations)
        {
            annotation.P1 *= scale;
            annotation.P2 *= scale;
        }

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != surface.Id)
                continue;

            child.LocalPosition *= scale;
            ScaleSurfaceMetric(setup, child, scale);
        }
    }

    /// <summary>Declares a surface's real size — a re-metering (see <see cref="ScaleSurfaceMetric"/>), so lines and regions keep their place on the wall.</summary>
    internal static void RemeterSurface(Setup setup, Surface surface, Vector2 newSize)
    {
        var target = new Vector2(MathF.Max(newSize.X, SurfaceGeometry.MinSize), MathF.Max(newSize.Y, SurfaceGeometry.MinSize));
        var old = surface.SizeInMeters;
        if (old.X <= 0.0001f || old.Y <= 0.0001f)
        {
            surface.SizeInMeters = target;
            return;
        }

        ScaleSurfaceMetric(setup, surface, target / old);
    }

    /// <summary>
    /// Reshapes the surface so its real-world proportions match the pixels of the slice it shows — the inverse
    /// of the slice's "Match target aspect", for when the wall is what should give. Keeps the width and solves
    /// the height, so it reads as a nudge rather than a jump.
    /// </summary>
    internal static void MatchSurfaceToSliceAspect(Setup setup, Surface surface)
    {
        var slice = setup.FindSlice(surface.SliceId);
        if (slice == null || !TryGetSliceAspect(setup, slice, out var aspect))
            return;

        var oldState = new ResizeSurfaceCommand.State(surface);
        var width = MathF.Max(surface.SizeInMeters.X, SurfaceGeometry.MinSize);
        SurfaceGeometry.ResizeAnchored(surface, new Vector2(width, width / MathF.Max(aspect, 0.0001f)));

        UndoRedoStack.Add(new ResizeSurfaceCommand(surface.Id, oldState, new ResizeSurfaceCommand.State(surface)));
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Aspect of a slice's pixels — its uv extent against the source's resolution.</summary>
    private static bool TryGetSliceAspect(Setup setup, Slice slice, out float aspect)
    {
        aspect = 1f;
        var source = setup.FindSource(slice.SourceId);
        if (source == null || !OutputManager.TryGetSourceContent(source.SymbolChildId, out _, out var content)
            || content is not { IsDisposed: false })
            return false;

        var width = content.Description.Width * MathF.Max(slice.UvRect.Z - slice.UvRect.X, 0.0001f);
        var height = content.Description.Height * MathF.Max(slice.UvRect.W - slice.UvRect.Y, 0.0001f);
        if (width <= 0 || height <= 0)
            return false;

        aspect = width / height;
        return true;
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

    private static Surface CloneSurface(Surface source)
    {
        var copy = new Surface
                       {
                           Name = source.Name,
                           Type = source.Type,
                           Kind = source.Kind,
                           ParentId = source.ParentId,
                           Render = source.Render,
                           SizeInMeters = source.SizeInMeters,
                           Anchor = source.Anchor,
                           LockAspect = source.LockAspect,
                           LocalPosition = source.LocalPosition,
                           PixelsPerMeter = source.PixelsPerMeter,
                           ShowGrid = source.ShowGrid,
                           GridSubdivisions = source.GridSubdivisions,
                       };

        foreach (var mapping in source.OutputMappings)
        {
            copy.OutputMappings.Add(new Surface.OutputMapping
                                        {
                                            OutputId = mapping.OutputId,
                                            Mode = mapping.Mode,
                                            Quad = (Vector2[])mapping.Quad.Clone(),
                                        });
        }

        if (source.Placement != null)
            copy.Placement = new Surface.StagePlacement { Pose = source.Placement.Pose };

        return copy;
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

        RunUndoable("Add region", setup, () =>
                                             {
                                                 var child = new Surface
                                                                 {
                                                                     Name = $"Region {SetupRelations.CountChildren(setup, parent.Id) + 1}",
                                                                     Kind = Surface.SurfaceKinds.Layout,
                                                                     ParentId = parent.Id,
                                                                     SizeInMeters = size,
                                                                     LocalPosition = bottomLeft,
                                                                     PixelsPerMeter = parent.PixelsPerMeter,
                                                                 };

                                                 setup.Surfaces.Add(child);
                                                 selection.Select(SetupEntitySelection.EntityKind.Surface, child.Id);
                                             });
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
        RunUndoable("Clear output inputs", setup, () =>
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

        RunUndoable("Clear content inputs", setup, () => surface.SliceId = Guid.Empty);
    }

    private static ISlot? FindTextureOutput(Instance instance)
    {
        foreach (var slot in instance.Outputs)
        {
            if (slot.ValueType == typeof(Texture2D))
                return slot;
        }

        return null;
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

    // Lib SendToOutput op and its texture input — the CONTENT "+" instantiates this and wires a selected feed in.
    private static readonly Guid SendToOutputSymbolId = new("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60");
    private static readonly Guid SendToOutputTextureInputId = new("8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942");
}
