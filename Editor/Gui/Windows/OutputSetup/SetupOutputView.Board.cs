#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The Board: the 2D unfolded overview every entity lives on — metres, Y up, floor at y = 0, a metric grid
/// behind. Surfaces are cards at true metre size standing on the floor (regions nested inside), content
/// and outputs are pixel cards at a presentation scale with their slices/patches as sub-rects, reference
/// images are pixel cards too, props are figures at true scale. Cards select (click, fence) and drag as a
/// group; pixel cards scale from a corner — presentation only, nothing physical changes. Selecting keeps
/// the Board on screen; double-clicking a card enters its space.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>Whether the Board is the current view — selection changes then keep showing it rather than
    /// switching to the selected entity's canvas.</summary>
    public bool ShowsBoard => _editMode == EditMode.Board;

    /// <summary>The reference image whose space was entered from the Board (double-click); Empty while none is.
    /// Cleared by every other entry point, so leaving it is a matter of showing anything else.</summary>
    public Guid OpenedReferenceImageId { get; private set; }

    /// <summary>Debug-protocol hook: the header tab by name.</summary>
    public bool TrySetEditMode(string name)
    {
        if (!Enum.TryParse<EditMode>(name, true, out var mode))
            return false;

        _editMode = mode;
        return true;
    }

    /// <summary>
    /// The Board with no output focused — what the window shows while nothing else claims it. A shown surface
    /// traced on a photo can still take the Straight tab: it straightens on that photo, in place.
    /// </summary>
    public void DrawBoardStandalone(SetupEntitySelection? selection, Guid shownSurfaceId = default)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        _shownSurfaceId = shownSurfaceId;
        OpenedReferenceImageId = Guid.Empty;
        var tracedImage = _editMode == EditMode.Straight ? TracedImageOf(setup, shownSurfaceId) : null;
        if (tracedImage == null)
            _editMode = EditMode.Board;

        DrawHeader(setup, null, Guid.Empty);

        var canvasTop = ImGui.GetCursorScreenPos();
        _boardCanvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        SeedBoardPlacements(setup);
        if (tracedImage != null)
            EnterSpace(setup, SetupEntitySelection.EntityKind.ReferenceImage, tracedImage.Id, true);
        else
            EnterSpace(setup, _spaceKind, _spaceId, false); // whatever was open fades back into its card

        DrawBoardLayer(setup, machineConfig, selection);
        if (_spaceBlend > 0.001f && _spaceKind == SetupEntitySelection.EntityKind.ReferenceImage)
            DrawReferenceSpaceForShown(setup, selection, straighten: tracedImage != null);

        ResolvePicking(setup, selection);
        dl.PopClipRect();
    }

    /// <summary>Header for the canvases without tabs (the source canvas): the way back to the Board, then the title.</summary>
    private void DrawBoardReturnHeader(string title)
    {
        if (CustomComponents.StateButton("Board", CustomComponents.ButtonStates.Default))
        {
            _editMode = EditMode.Board;
            OpenedReferenceImageId = Guid.Empty;
        }

        ImGui.SameLine(0, 8 * T3Ui.UiScaleFactor);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.TextMuted);
    }

    /// <summary>
    /// The Board behind every space: grid, cards and their interactions. While a space is in, the cards it
    /// draws itself are skipped and the rest fade by the blend — at full blend they are gone and inert.
    /// </summary>
    private void DrawBoardLayer(Setup setup, MachineConfig machineConfig, SetupEntitySelection? selection)
    {
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        var screenMin = _boardCanvas.WindowPos;
        var screenMax = screenMin + _boardCanvas.WindowSize;
        var onBoard = _spaceBlend <= 0.001f;
        _boardLayerFade = 1f - _spaceBlend;

        FitBoardIfNeeded(setup);

        var pixelsPerMeter = MathF.Abs(_boardCanvas.Scale.X);
        MetricGridRaster.Draw(dl, _boardProjection, screenMin, screenMax, pixelsPerMeter, _boardDragKind != SetupEntitySelection.EntityKind.None ? 1f : 0.6f);

        // A live edge crop changes a size the metadata shows — rebuilt per frame only while one runs.
        if (_boardMetaVersion != OutputSetupHandling.StructureVersion || _resizeOldState != null)
            RefreshBoardMeta(setup, machineConfig);

        // Fully inside a space nothing of the Board is left to draw or to click.
        if (_boardLayerFade <= 0.001f)
        {
            _boardFenceCandidates.Clear();
            return;
        }

        // A press that never became a drag must not linger. A plain press on an already selected card kept
        // the set (for a group drag), so it is the release that selects that card alone.
        if (_boardGrabScreen != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (_boardGrabOnSelected)
                selection?.Select(_boardGrabKind, _boardGrabId);

            _boardGrabScreen = null;
            _boardGrabOnSelected = false;
        }

        _boardFenceCandidates.Clear();
        _boardContext ??= new EvaluationContext();

        // Draw order is stacking order: reference images at the back, then content, surfaces, outputs, props.
        foreach (var image in setup.ReferenceImages)
        {
            if (IsDrawnBySpace(setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id)
                || !TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, out var min, out var max))
                continue;

            var srv = TryGetReferenceSrv(image);
            DrawBoardCard(setup, selection, dl, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, min, max,
                          image.Name, BoardMeta(image.Id), srv, true);
            DrawBoardTraces(setup, selection, dl, image, min, max);
        }

        foreach (var source in setup.ContentSources)
        {
            if (IsDrawnBySpace(setup, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId)
                || !TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId, out var min, out var max))
                continue;

            var srv = OutputManager.TryGetSourceContent(source.SymbolChildId, out _, out var content) && content is { IsDisposed: false }
                          ? SrvManager.GetSrvForTexture(content)
                          : null;
            DrawBoardCard(setup, selection, dl, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId, min, max,
                          source.Name, BoardMeta(source.Id), srv, true);

            // Slices are cuts of the card: sub-rects in the texture's own (Y-down) uv, mapped into the card. The
            // primary one is edited in place — edges crop, corners scale, the label moves — through the source
            // space's editor pointed at the card.
            var size = max - min;
            var textureSize = BoardPixelSize(setup, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId);
            var editableSliceId = _boardLayerFade >= 0.999f && selection != null
                                  && selection.TryResolve(setup, out var primaryKind, out var primaryId)
                                  && primaryKind == SetupEntitySelection.EntityKind.Slice
                                      ? primaryId
                                      : Guid.Empty;
            foreach (var slice in setup.Slices)
            {
                if (slice.SourceId != source.Id)
                    continue;

                if (slice.Id == editableSliceId)
                {
                    _projection.Origin = new Vector2(min.X, max.Y);
                    _projection.PixelsPerMeter = textureSize.X / MathF.Max(size.X, 0.0001f);
                    EditSlice(setup, dl, slice, slice.UvRect, Vector2.Zero, textureSize, Guid.Empty, dimOutside: false);

                    // A slice gesture is the slice's, not the card's — the card must not come along.
                    if (_sliceLabelDragging || _sliceDragOldRect != null)
                    {
                        _boardGrabScreen = null;
                        _boardGrabOnSelected = false;
                    }

                    continue;
                }

                var uv = slice.UvRect;
                var sliceMin = new Vector2(min.X + uv.X * size.X, min.Y + (1 - uv.W) * size.Y);
                var sliceMax = new Vector2(min.X + uv.Z * size.X, min.Y + (1 - uv.Y) * size.Y);
                DrawBoardSubRect(setup, selection, dl, SetupEntitySelection.EntityKind.Slice, slice.Id, sliceMin, sliceMax,
                                 SetupActions.SliceLabel(setup, slice));
            }
        }

        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId != Guid.Empty || IsDrawnBySpace(setup, SetupEntitySelection.EntityKind.Surface, surface.Id))
                continue;

            if (!TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.Surface, surface.Id, out var min, out var max))
                continue;

            // A traced surface wears the straightened crop of its photo — what the wall looks like.
            TryGetTracedFragment(setup, surface, out var fragment, out var uvMin, out var uvMax);
            DrawBoardCard(setup, selection, dl, SetupEntitySelection.EntityKind.Surface, surface.Id, min, max,
                          surface.Name, BoardMeta(surface.Id), fragment, true, uvMin, uvMax);
            DrawBoardRegions(setup, selection, dl, surface, min + surface.AnchorInMeters);
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Default || IsDrawnBySpace(setup, SetupEntitySelection.EntityKind.Output, output.Id))
                continue;

            if (!TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.Output, output.Id, out var min, out var max))
                continue;

            var composite = OutputManager.RenderOutput(output.Id);
            var srv = composite is { IsDisposed: false } ? SrvManager.GetSrvForTexture(composite) : null;
            DrawBoardCard(setup, selection, dl, SetupEntitySelection.EntityKind.Output, output.Id, min, max,
                          output.Name, BoardMeta(output.Id), srv, true);

            // Patches are cuts of the canvas: their px quads mapped into the card (px run Y-down from the top).
            var ppm = PixelsPerMeterOf(output.BoardPlacement);
            foreach (var patch in output.Patches)
            {
                if (patch.Quad.Length < 4)
                    continue;

                for (var c = 0; c < 4; c++)
                    _boardQuad[c] = _boardProjection.CanvasToScreen(new Vector2(min.X + patch.Quad[c].X / ppm, max.Y - patch.Quad[c].Y / ppm));

                var isSelected = selection?.IsSelected(SetupEntitySelection.EntityKind.Patch, patch.Id) ?? false;
                var pulse = isSelected ? 0f : FrameStats.GetPulse(patch.Id);
                var color = (isSelected ? UiColors.StatusActivated : PulseColor(UiColors.ForegroundFull.Fade(0.5f), pulse)).Fade(_boardLayerFade);
                dl.AddQuad(_boardQuad[0], _boardQuad[1], _boardQuad[2], _boardQuad[3], color, (isSelected ? 2f : 1f) * scale);
                DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Patch, _boardQuad, patch.Id, SetupActions.PatchLabel(output, patch), isSelected, 0.9f * _boardLayerFade, pulse);
            }
        }

        foreach (var prop in setup.Props)
            DrawBoardProp(setup, selection, dl, prop);

        // The Board's own gestures belong to the Board; a fading layer is only looked at.
        if (!onBoard)
            return;

        HandleBoardDrag(setup, selection);
        HandleBoardFence(selection);
        HandleBoardKeys(setup, selection);
        HandleBoardDrop(setup, selection, dl, screenMin, screenMax);
    }

    /// <summary>
    /// The whole Board is a drop zone for images: an asset from the Asset Library, or a file from the OS
    /// (imported into the project first). Each becomes a reference image card where it was dropped.
    /// </summary>
    private void HandleBoardDrop(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, Vector2 screenMin, Vector2 screenMax)
    {
        if (selection == null)
            return;

        var area = new ImRect(screenMin, screenMax);
        var assetResult = DragAndDropHandling.TryHandleDropOnRect(DragAndDropHandling.DragTypes.FileAsset, area, out var address);
        var fileResult = DragAndDropHandling.TryHandleDropOnRect(DragAndDropHandling.DragTypes.ExternalFile, area, out var files);

        if (assetResult == DragAndDropHandling.DragInteractionResult.Hovering || fileResult == DragAndDropHandling.DragInteractionResult.Hovering)
        {
            var scale = T3Ui.UiScaleFactor;
            var mouse = ImGui.GetMousePos() + new Vector2(16, 16) * scale;
            const string label = "Add as reference image";
            var labelSize = ImGui.CalcTextSize(label);
            dl.AddRectFilled(mouse - new Vector2(6, 4) * scale, mouse + labelSize + new Vector2(6, 4) * scale, UiColors.BackgroundFull.Fade(0.8f), 4 * scale);
            dl.AddText(mouse, UiColors.ForegroundFull, label);
            return;
        }

        var dropPosition = _boardProjection.ScreenToCanvas(ImGui.GetMousePos());
        if (assetResult == DragAndDropHandling.DragInteractionResult.Dropped && !string.IsNullOrEmpty(address))
        {
            SetupActions.AddReferenceImageFromFile(selection, setup, address, dropPosition);
            return;
        }

        if (fileResult != DragAndDropHandling.DragInteractionResult.Dropped || string.IsNullOrEmpty(files))
            return;

        foreach (var path in files.Split('|'))
        {
            SetupActions.AddReferenceImageFromFile(selection, setup, path, dropPosition);
            dropPosition += new Vector2(0.2f, -0.2f);
        }
    }

    /// <summary>A card: fill or thumbnail, outline by state, name chip with muted metadata, and its pick/grab area.</summary>
    private void DrawBoardCard(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl,
                               SetupEntitySelection.EntityKind kind, Guid id, Vector2 min, Vector2 max,
                               string name, string? meta, SharpDX.Direct3D11.ShaderResourceView? srv, bool scalable,
                               Vector2 uvMin = default, Vector2? uvMax = null)
    {
        var scale = T3Ui.UiScaleFactor;
        var fade = _boardLayerFade;
        var sMin = _boardProjection.CanvasToScreen(new Vector2(min.X, max.Y));
        var sMax = _boardProjection.CanvasToScreen(new Vector2(max.X, min.Y));

        // A fading card is only looked at: no hover, no pick, no grab.
        var interactive = fade >= 0.999f;
        var isSelected = selection?.IsSelected(kind, id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.GetPulse(id);
        var hovered = interactive && ImGui.IsMouseHoveringRect(sMin, sMax) && ImGui.IsWindowHovered();
        if (hovered)
            FrameStats.PulseItemWithId(id);

        if (srv is { IsDisposed: false })
            dl.AddImage(srv.NativePointer, sMin, sMax, uvMin, uvMax ?? Vector2.One, UiColors.ForegroundFull.Fade(fade));
        else
            dl.AddRectFilled(sMin, sMax, UiColors.BackgroundPopup.Fade(0.85f * fade));

        // A surface's content over its photo backdrop, at the preview opacity — the same look as on the traced quad.
        var preview = UserSettings.Config.OutputSetupContentPreview;
        if (kind == SetupEntitySelection.EntityKind.Surface && preview > 0.01f
            && OutputManager.TryGetSurfaceSlice(id, out _, out var surfaceContent, out var contentUv) && surfaceContent is { IsDisposed: false })
        {
            var contentSrv = SrvManager.GetSrvForTexture(surfaceContent);
            if (contentSrv is { IsDisposed: false })
                dl.AddImage(contentSrv.NativePointer, sMin, sMax, new Vector2(contentUv.X, contentUv.Y), new Vector2(contentUv.Z, contentUv.W),
                            UiColors.ForegroundFull.Fade(preview * fade));
        }

        if (isSelected)
            dl.AddRectFilled(sMin, sMax, UiColors.StatusActivated.Fade(0.12f * fade));
        else if (pulse > 0.001f)
            dl.AddRectFilled(sMin, sMax, UiColors.StatusActivated.Fade(pulse * 0.2f * fade));

        var outline = (isSelected ? UiColors.StatusActivated : PulseColor(UiColors.ForegroundFull.Fade(hovered ? 0.7f : 0.4f), pulse)).Fade(fade);
        dl.AddRect(sMin, sMax, outline, 0, ImDrawFlags.None, (isSelected ? 2f : 1f) * scale);

        // Name chip at the card's top-left; metadata muted beside it.
        var pad = 4 * scale;
        var labelPos = sMin + new Vector2(pad, pad);
        var nameSize = ImGui.CalcTextSize(name);
        var chipMax = labelPos + nameSize + new Vector2(2 * pad, 2 * pad);
        CornerPinHandles.DrawLabelChip(dl, (labelPos, chipMax), name,
                                       (isSelected ? UiColors.ForegroundFull : UiColors.Text.Fade(0.9f)).Fade(fade),
                                       (isSelected ? UiColors.StatusActivated : UiColors.BackgroundFull.Fade(0.7f)).Fade(fade));
        if (meta != null)
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(chipMax.X + pad, labelPos.Y + pad), UiColors.TextMuted.Fade(fade), meta);

        if (!interactive)
            return;

        // The whole card is its pick and grab area (the picker cycles stacked cards on repeated clicks), and
        // what the fence catches.
        _picker.AddTarget(kind, id, sMin, sMax, isBackground: true);
        _boardFenceCandidates.Add((kind, id, new ImRect(sMin, sMax)));
        GrabBoardCard(kind, id, hovered, isSelected);

        // Double-click enters the card's space: the canvas that edits it.
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            selection?.Select(kind, id);
            if (kind == SetupEntitySelection.EntityKind.ReferenceImage)
                OpenedReferenceImageId = id;

            _editMode = kind switch
                            {
                                SetupEntitySelection.EntityKind.Surface => EditMode.Straight,
                                SetupEntitySelection.EntityKind.ContentSource => EditMode.Content,
                                SetupEntitySelection.EntityKind.Output => EditMode.Output,
                                _ => _editMode,
                            };
        }

        if (kind == SetupEntitySelection.EntityKind.Surface && isSelected && setup.FindSurface(id) is { } surface)
            DrawBoardSurfaceEdges(setup, surface, min, max);

        // The scale handle at the top-right corner. On a pixel card (square) it is presentation only — the
        // card's px-per-metre, never resolution, routing or projection. On a surface (circle) it is physical:
        // the wall grows or shrinks with its aspect kept, everything on it scaling along.
        if (scalable && isSelected)
        {
            var isSurface = kind == SetupEntitySelection.EntityKind.Surface;
            var corner = new Vector2(max.X, max.Y);
            var style = CanvasPointHandle.Style.Default(UiColors.ForegroundFull, isSurface ? CanvasPointHandle.Shape.Circle : CanvasPointHandle.Shape.Square, true);
            ImGui.PushID(id.GetHashCode());
            var phase = CanvasPointHandle.Draw(ref corner, _boardProjection, style);
            ImGui.PopID();
            if (phase == CanvasPointHandle.DragPhase.Started)
            {
                _boardGestureOldJson = setup.ToJsonString();
                _boardScaleStartWidth = max.X - min.X;
                _boardScaleApplied = Vector2.One;
            }

            if (phase == CanvasPointHandle.DragPhase.Dragging)
            {
                if (isSurface && setup.FindSurface(id) is { } scaledSurface)
                {
                    var factor = MathF.Max(corner.X - min.X, SurfaceGeometry.MinSize) / MathF.Max(_boardScaleStartWidth, SurfaceGeometry.MinSize);
                    ApplyBoardScale(setup, scaledSurface, new Vector2(factor, factor), fixedCorner: 3);
                }
                else if (TryGetPlacement(setup, kind, id, out var placement))
                {
                    var pixelWidth = BoardPixelSize(setup, kind, id).X;
                    var newWidth = MathF.Max(corner.X - min.X, 0.05f);
                    placement.PixelsPerMeter = Math.Clamp(pixelWidth / newWidth, 10f, 100000f);
                }
            }

            if (phase == CanvasPointHandle.DragPhase.Completed)
                CommitBoardGesture(setup, isSurface ? "Scale surface" : "Scale card");

            if (ImGui.IsItemHovered())
            {
                if (isSurface)
                    CustomComponents.TooltipForLastItem("Scale", "Changes the surface's real size, aspect kept; regions and lines scale with it.");
                else
                    CustomComponents.TooltipForLastItem("Presentation scale", "Changes how big the card is on the Board — nothing about its pixels, routing or projection.");
            }
        }
    }

    /// <summary>
    /// Scales a surface about one of its corners (TL, TR, BR, BL of its own rectangle) by the total
    /// <paramref name="factor"/> since the gesture began: the footprint, the corner pins (re-projected), the
    /// measuring lines and the regions all scale together. Applied incrementally, so a live drag never
    /// compounds and never needs a per-frame restore of the whole subtree.
    /// </summary>
    private void ApplyBoardScale(Setup setup, Surface surface, Vector2 factor, int fixedCorner)
    {
        var increment = new Vector2(factor.X / MathF.Max(_boardScaleApplied.X, 0.0001f), factor.Y / MathF.Max(_boardScaleApplied.Y, 0.0001f));
        _boardScaleApplied = factor;

        SurfaceGeometry.LocalBounds(surface, out var min, out var max);
        var fixedPoint = fixedCorner switch
                             {
                                 0 => new Vector2(min.X, max.Y),
                                 1 => max,
                                 2 => new Vector2(max.X, min.Y),
                                 _ => min,
                             };
        var newMin = fixedPoint + (min - fixedPoint) * increment;
        var newMax = fixedPoint + (max - fixedPoint) * increment;
        SurfaceGeometry.ApplyBounds(surface, newMin, newMax);

        foreach (var annotation in surface.Annotations)
        {
            annotation.P1 = fixedPoint + (annotation.P1 - fixedPoint) * increment;
            annotation.P2 = fixedPoint + (annotation.P2 - fixedPoint) * increment;
        }

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != surface.Id)
                continue;

            child.LocalPosition = fixedPoint + (child.LocalPosition - fixedPoint) * increment;
            SetupActions.ScaleSurfaceMetric(setup, child, increment);
        }
    }

    /// <summary>
    /// The selected surface's edge handles, as on the Straight view: an edge crops the footprint (Ctrl
    /// stretches instead), the anchor — the card's placement — stays put, and the corner pin follows through
    /// the same <see cref="RunResizeDrag"/> skeleton, one undo step per drag.
    /// </summary>
    private void DrawBoardSurfaceEdges(Setup setup, Surface surface, Vector2 min, Vector2 max)
    {
        _boardEdgeQuad[0] = new Vector2(min.X, max.Y);
        _boardEdgeQuad[1] = max;
        _boardEdgeQuad[2] = new Vector2(max.X, min.Y);
        _boardEdgeQuad[3] = min;

        // Plain: an edge crops the footprint (squares). Ctrl: it scales the surface along that axis, aspect free
        // (circles) — the mode is read at the press and held for the drag.
        var scaling = _resizeOldState != null ? _boardEdgeScaling : ImGui.GetIO().KeyCtrl;
        ImGui.PushID(surface.Id.GetHashCode());
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true);
        style.EdgeHandleShape = scaling ? CanvasPointHandle.Shape.Circle : CanvasPointHandle.Shape.Square;
        var phase = CornerPinHandles.DrawEdgeHandles(_boardEdgeQuad, _boardProjection, style, out var edge, out var edgePos);
        ImGui.PopID();
        if (edge < 0)
            return;

        // The crop rides the setup snapshot rather than the resize command: a traced surface's trace crops along,
        // and that quad is not part of the resize state.
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                _resizeOldState = new ResizeSurfaceCommand.State(surface);
                _edgeDragSurfaceId = surface.Id;
                _boardEdgeScaling = scaling;
                _boardScaleApplied = Vector2.One;
                SurfaceGeometry.LocalBounds(surface, out _boardEdgeStartMin, out _boardEdgeStartMax);
                _boardGestureOldJson = setup.ToJsonString();
                if (surface.Reference is { Quad.Length: >= 4 })
                    Array.Copy(surface.Reference.Quad, _boardEdgeOldTrace, 4);

                break;

            case CanvasPointHandle.DragPhase.Dragging when _resizeOldState != null && _boardEdgeScaling:
            {
                // Scale along the dragged edge's axis, the opposite edge fixed; the trace is the same wall, so it stays.
                var origin = surface.BoardPlacement?.Position ?? Vector2.Zero;
                var pos = edgePos - origin;
                var startSize = Vector2.Max(_boardEdgeStartMax - _boardEdgeStartMin, new Vector2(SurfaceGeometry.MinSize));
                var factor = Vector2.One;
                var fixedCorner = 3;
                switch (edge)
                {
                    case 0: factor.Y = MathF.Max(pos.Y - _boardEdgeStartMin.Y, SurfaceGeometry.MinSize) / startSize.Y; fixedCorner = 3; break;
                    case 1: factor.X = MathF.Max(pos.X - _boardEdgeStartMin.X, SurfaceGeometry.MinSize) / startSize.X; fixedCorner = 3; break;
                    case 2: factor.Y = MathF.Max(_boardEdgeStartMax.Y - pos.Y, SurfaceGeometry.MinSize) / startSize.Y; fixedCorner = 1; break;
                    default: factor.X = MathF.Max(_boardEdgeStartMax.X - pos.X, SurfaceGeometry.MinSize) / startSize.X; fixedCorner = 1; break;
                }

                ApplyBoardScale(setup, surface, factor, fixedCorner);
                break;
            }

            case CanvasPointHandle.DragPhase.Dragging when _resizeOldState != null:
            {
                // Re-based on the pre-drag rectangle, so the edit doesn't compound; the anchor is the origin of
                // surface space and sits at the card's placement.
                _resizeOldState.Value.Restore(surface);
                var oldRect = SurfaceGeometry.LocalRect(surface);
                var origin = surface.BoardPlacement?.Position ?? Vector2.Zero;
                SurfaceGeometry.DragEdge(surface, edge, edgePos - origin, keepDimensions: false);

                // The trace is the same wall seen in the photo: the cropped rectangle maps through the old
                // rectangle's projection into the photo, so the traced quad crops with it.
                if (surface.Reference is { Quad.Length: >= 4 } binding
                    && Homography.TryComputeQuadToQuad(oldRect, _boardEdgeOldTrace, out var surfaceToPhoto))
                {
                    var newRect = SurfaceGeometry.LocalRect(surface);
                    for (var c = 0; c < 4; c++)
                        binding.Quad[c] = surfaceToPhoto.TransformPoint(newRect[c]);
                }

                break;
            }

            case CanvasPointHandle.DragPhase.Completed:
                _resizeOldState = null;
                _edgeDragSurfaceId = Guid.Empty;
                CommitBoardGesture(setup, _boardEdgeScaling ? "Scale surface" : "Crop surface");
                break;
        }
    }

    /// <summary>
    /// Arms a card's press → drag handoff. A plain press on a card that is already selected keeps the whole
    /// selection (so the drag moves the group) and defers the single-select to the release.
    /// </summary>
    private void GrabBoardCard(SetupEntitySelection.EntityKind kind, Guid id, bool hovered, bool isSelected)
    {
        if (!hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsAnyItemHovered()
            || _boardDragKind != SetupEntitySelection.EntityKind.None)
            return;

        var io = ImGui.GetIO();
        _boardGrabScreen = ImGui.GetMousePos();
        _boardGrabKind = kind;
        _boardGrabId = id;
        _boardGrabOnSelected = isSelected && !io.KeyCtrl && !io.KeyShift;
    }

    /// <summary>A labelled sub-rect inside a card (a slice, a region): thin inner outline, its own pick target.</summary>
    private void DrawBoardSubRect(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl,
                                  SetupEntitySelection.EntityKind kind, Guid id, Vector2 min, Vector2 max, string label)
    {
        var scale = T3Ui.UiScaleFactor;
        var fade = _boardLayerFade;
        var sMin = _boardProjection.CanvasToScreen(new Vector2(min.X, max.Y));
        var sMax = _boardProjection.CanvasToScreen(new Vector2(max.X, min.Y));
        var isSelected = selection?.IsSelected(kind, id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.GetPulse(id);

        if (isSelected)
            dl.AddRectFilled(sMin, sMax, UiColors.StatusActivated.Fade(0.12f * fade));
        else if (pulse > 0.001f)
            dl.AddRectFilled(sMin, sMax, UiColors.StatusActivated.Fade(pulse * 0.2f * fade));

        dl.AddRect(sMin, sMax, (isSelected ? UiColors.StatusActivated : PulseColor(UiColors.ForegroundFull.Fade(0.45f), pulse)).Fade(fade),
                   0, ImDrawFlags.None, (isSelected ? 2f : 1f) * scale);

        _boardQuad[0] = sMin;
        _boardQuad[1] = new Vector2(sMax.X, sMin.Y);
        _boardQuad[2] = sMax;
        _boardQuad[3] = new Vector2(sMin.X, sMax.Y);
        DrawEntityLabel(dl, kind, _boardQuad, id, label, isSelected, 0.9f * fade, pulse);
    }

    /// <summary>Regions nest inside their surface at their metre position from the parent's anchor, recursively —
    /// editable in place (corners, edges, label move) while selected.</summary>
    private void DrawBoardRegions(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, Surface parent, Vector2 parentAnchorOnBoard)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != parent.Id)
                continue;

            _regionProjection.View = _boardProjection;
            _regionProjection.Origin = parentAnchorOnBoard;
            _regionProjection.UseHomography = false;
            DrawRegionEditable(setup, dl, parent, child, _regionProjection, selection, _boardLayerFade);

            SurfaceGeometry.ChildBounds(child, out var localMin, out _);
            DrawBoardRegions(setup, selection, dl, child, parentAnchorOnBoard + localMin + child.AnchorInMeters);
        }
    }

    /// <summary>A prop as a figure at true scale, standing where its position says — the Board's ruler.</summary>
    private void DrawBoardProp(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, Prop prop)
    {
        var scale = T3Ui.UiScaleFactor;
        var fade = _boardLayerFade;
        var h = MathF.Max(prop.HeightInMeters, 0.1f);
        var foot = new Vector2(prop.Position.X, prop.Position.Y);
        var isSelected = selection?.IsSelected(SetupEntitySelection.EntityKind.Prop, prop.Id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.GetPulse(prop.Id);
        var color = (isSelected ? UiColors.StatusActivated : PulseColor(UiColors.TextMuted.Fade(0.8f), pulse)).Fade(fade);
        var thickness = (isSelected ? 2f : 1.5f) * scale;

        Vector2 P(float dx, float dy) => _boardProjection.CanvasToScreen(foot + new Vector2(dx * h, dy * h));

        dl.AddCircle(P(0, 0.91f), 0.08f * h * MathF.Abs(_boardCanvas.Scale.X), color, 16, thickness);
        dl.AddLine(P(0, 0.83f), P(0, 0.45f), color, thickness);
        dl.AddLine(P(0, 0.45f), P(-0.13f, 0), color, thickness);
        dl.AddLine(P(0, 0.45f), P(0.13f, 0), color, thickness);
        dl.AddLine(P(0, 0.75f), P(-0.2f, 0.5f), color, thickness);
        dl.AddLine(P(0, 0.75f), P(0.2f, 0.5f), color, thickness);

        // Its bounding box is the pick/grab area; the label sits at the head.
        PropBounds(prop, out var min, out var max);
        var sMin = _boardProjection.CanvasToScreen(new Vector2(min.X, max.Y));
        var sMax = _boardProjection.CanvasToScreen(new Vector2(max.X, min.Y));
        var interactive = fade >= 0.999f;
        var hovered = interactive && ImGui.IsMouseHoveringRect(sMin, sMax) && ImGui.IsWindowHovered();
        if (hovered)
            FrameStats.PulseItemWithId(prop.Id);

        var meta = BoardMeta(prop.Id) ?? "";
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(sMin.X, sMin.Y - Fonts.FontSmall.FontSize - 2 * scale),
                   (isSelected ? UiColors.StatusActivated : UiColors.TextMuted).Fade(fade), meta);

        if (!interactive)
            return;

        _picker.AddTarget(SetupEntitySelection.EntityKind.Prop, prop.Id, sMin, sMax, isBackground: true);
        _boardFenceCandidates.Add((SetupEntitySelection.EntityKind.Prop, prop.Id, new ImRect(sMin, sMax)));
        GrabBoardCard(SetupEntitySelection.EntityKind.Prop, prop.Id, hovered, isSelected);
    }

    private static void PropBounds(Prop prop, out Vector2 min, out Vector2 max)
    {
        var h = MathF.Max(prop.HeightInMeters, 0.1f);
        var foot = new Vector2(prop.Position.X, prop.Position.Y);
        min = foot + new Vector2(-0.22f * h, 0);
        max = foot + new Vector2(0.22f * h, h);
    }

    /// <summary>
    /// Move gesture for cards: a press arms it, a move past the click threshold starts it (with a setup
    /// snapshot for one undo step), every selected card follows the cursor in board metres, release commits.
    /// </summary>
    private void HandleBoardDrag(Setup setup, SetupEntitySelection? selection)
    {
        if (_boardDragKind == SetupEntitySelection.EntityKind.None)
        {
            if (_boardGrabScreen == null || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                return;

            if ((ImGui.GetMousePos() - _boardGrabScreen.Value).Length() <= UserSettings.Config.ClickThreshold)
                return;

            // The grabbed card's whole selection moves with it when it was already selected; otherwise the
            // grab selects it alone first.
            if (selection != null && !selection.IsSelected(_boardGrabKind, _boardGrabId))
                selection.Select(_boardGrabKind, _boardGrabId);

            _boardDragItems.Clear();
            if (selection != null)
            {
                for (var i = 0; i < selection.Targets.Count; i++)
                {
                    var target = selection.Targets[i];
                    if (TryGetBoardPosition(setup, target.Kind, target.EntityId, out var start))
                        _boardDragItems.Add((target.Kind, target.EntityId, start));
                }
            }
            else if (TryGetBoardPosition(setup, _boardGrabKind, _boardGrabId, out var start))
            {
                _boardDragItems.Add((_boardGrabKind, _boardGrabId, start));
            }

            _boardGrabScreen = null;
            _boardGrabOnSelected = false;
            if (_boardDragItems.Count == 0)
                return;

            _boardDragKind = _boardGrabKind;
            _boardDragId = _boardGrabId;
            _boardDragGrabOnBoard = _boardProjection.ScreenToCanvas(ImGui.GetMousePos());
            _boardGestureOldJson = setup.ToJsonString();
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = _boardProjection.ScreenToCanvas(ImGui.GetMousePos()) - _boardDragGrabOnBoard;
            for (var i = 0; i < _boardDragItems.Count; i++)
            {
                var (kind, id, start) = _boardDragItems[i];
                SetBoardPosition(setup, kind, id, start + delta);
            }

            return;
        }

        CommitBoardGesture(setup, _boardDragItems.Count > 1 ? "Move cards" : "Move card");
        _boardDragItems.Clear();
        _boardDragKind = SetupEntitySelection.EntityKind.None;
        _boardDragId = Guid.Empty;
    }

    /// <summary>
    /// Marquee over the cards: a drag on empty Board replaces the selection with every card it touches
    /// (shift adds, ctrl removes); a click on empty Board clears it. Never while a card gesture is live.
    /// </summary>
    private void HandleBoardFence(SetupEntitySelection? selection)
    {
        // Not IsAnyItemActive: a press on empty window space makes the window's move-id the active item, which
        // would veto every fence. The scale handle is the only other gesture, and it holds the snapshot.
        if (selection == null || _boardDragKind != SetupEntitySelection.EntityKind.None
            || _boardGrabScreen != null || _boardGestureOldJson != null || _resizeOldState != null
            || _sliceLabelDragging || _sliceDragOldRect != null)
        {
            _boardFence.Reset();
            return;
        }

        // A fence starts only on the frame the button goes down. A press that began on a card and was handed
        // to a label pick leaves the button down with no fence — picking it up mid-press would end as an
        // "empty click" that clears the selection just made.
        if (_boardFence.State == SelectionFence.States.Inactive
            && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        switch (_boardFence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.Updated:
            case SelectionFence.States.CompletedAsArea:
                ApplyBoardFence(selection, selectMode);
                break;

            case SelectionFence.States.CompletedAsClick:
                selection.Clear();
                break;
        }
    }

    private void ApplyBoardFence(SetupEntitySelection selection, SelectionFence.SelectModes selectMode)
    {
        // Replace rebuilds from scratch every update frame, so the marquee reads live.
        if (selectMode == SelectionFence.SelectModes.Replace)
            selection.Clear();

        var bounds = _boardFence.BoundsInScreen;
        for (var i = 0; i < _boardFenceCandidates.Count; i++)
        {
            var (kind, id, rect) = _boardFenceCandidates[i];
            if (!bounds.Overlaps(rect))
                continue;

            if (selectMode == SelectionFence.SelectModes.Remove)
                selection.Remove(kind, id);
            else
                selection.Add(kind, id);
        }
    }

    /// <summary>The focus key frames the selected cards, or the whole Board when nothing is selected.</summary>
    private void HandleBoardKeys(Setup setup, SetupEntitySelection? selection)
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || !UserActions.FocusSelection.Triggered())
            return;

        var subject = selection is { Count: > 0 } ? selection : null;
        if (TryGetBoardExtent(setup, subject, out var min, out var max)
            || TryGetBoardExtent(setup, null, out min, out max))
        {
            FitBoard(min, max, instant: false);
        }
    }

    private void CommitBoardGesture(Setup setup, string name)
    {
        if (_boardGestureOldJson == null)
            return;

        SetupActions.CommitGesture(setup, name, _boardGestureOldJson);
        _boardGestureOldJson = null;
    }

    // ---- placement model -----------------------------------------------------------------------------

    /// <summary>
    /// First-time layout for entities without a placement: kind-grouped columns on the floor — reference
    /// images, then content, then surfaces at true size, then outputs — each column stacking upward.
    /// Persisted once (not undoable: it is a default, not an edit).
    /// </summary>
    private void SeedBoardPlacements(Setup setup)
    {
        var seeded = false;
        const float gap = 0.5f;

        // Surfaces from x = 0 rightward, standing on the floor.
        var x = 0f;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId != Guid.Empty)
                continue;

            if (surface.BoardPlacement == null)
            {
                surface.BoardPlacement = new CanvasPlacement { Position = new Vector2(x, 0) + surface.AnchorInMeters };
                seeded = true;
            }

            x += surface.SizeInMeters.X + gap;
        }

        var surfacesRight = x;

        // Content to the left of the surfaces, stacked upward.
        var y = 0f;
        foreach (var source in setup.ContentSources)
        {
            var size = BoardPixelSize(setup, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId) / DefaultBoardPixelsPerMeter;
            if (source.BoardPlacement == null)
            {
                source.BoardPlacement = new CanvasPlacement { Position = new Vector2(-gap - size.X, y) };
                seeded = true;
            }

            y += size.Y + gap * 0.5f;
        }

        var contentLeft = -gap - 2f;
        foreach (var source in setup.ContentSources)
        {
            if (source.BoardPlacement != null)
                contentLeft = MathF.Min(contentLeft, source.BoardPlacement.Position.X);
        }

        // Reference images left of the content.
        y = 0f;
        foreach (var image in setup.ReferenceImages)
        {
            var size = BoardPixelSize(setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id) / PixelsPerMeterOf(image.BoardPlacement, image);
            if (image.BoardPlacement == null)
            {
                image.BoardPlacement = new CanvasPlacement { Position = new Vector2(contentLeft - gap - size.X, y) };
                seeded = true;
            }

            y += size.Y + gap * 0.5f;
        }

        // Outputs right of the surfaces, stacked upward.
        y = 0f;
        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Default)
                continue;

            var size = BoardPixelSize(setup, SetupEntitySelection.EntityKind.Output, output.Id) / DefaultBoardPixelsPerMeter;
            if (output.BoardPlacement == null)
            {
                output.BoardPlacement = new CanvasPlacement { Position = new Vector2(surfacesRight + gap, y) };
                seeded = true;
            }

            y += size.Y + gap * 0.5f;
        }

        if (seeded)
            OutputSetupHandling.SaveActive();
    }

    private void FitBoardIfNeeded(Setup setup)
    {
        if (_boardFittedSetupId == setup.Id)
            return;

        _boardFittedSetupId = setup.Id;
        if (!TryGetBoardExtent(setup, null, out var min, out var max))
        {
            min = new Vector2(-2, -0.5f);
            max = new Vector2(4, 3);
        }

        FitBoard(min, max, instant: true);
    }

    /// <summary>Frames a board-metre rect with a margin — instantly, or eased over the canvas' scope transition.</summary>
    private void FitBoard(Vector2 min, Vector2 max, bool instant)
    {
        // Board Y is up, the canvas' is down — the fit rect is given in canvas space.
        var margin = MathF.Max(max.X - min.X, max.Y - min.Y) * 0.1f + 0.3f;
        var canvasMin = new Vector2(min.X - margin, -(max.Y + margin));
        var canvasMax = new Vector2(max.X + margin, -(min.Y - margin));
        _boardCanvas.FitAreaOnCanvas(new ImRect(canvasMin, canvasMax));
        if (instant)
            _boardCanvas.SetScopeInstant(_boardCanvas.GetTargetScope());
    }

    /// <summary>
    /// The union of the cards' board rects — all of them, or only the selected ones (a selected slice, patch or
    /// region counts as its card). False when nothing contributes.
    /// </summary>
    private static bool TryGetBoardExtent(Setup setup, SetupEntitySelection? selection, out Vector2 min, out Vector2 max)
    {
        var any = false;
        min = max = Vector2.Zero;

        if (selection != null)
        {
            for (var i = 0; i < selection.Targets.Count; i++)
            {
                var target = selection.Targets[i];
                if (TryGetCardBounds(setup, target.Kind, target.EntityId, out var a, out var b))
                    Include(ref any, ref min, ref max, a, b);
            }

            return any;
        }

        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId == Guid.Empty && TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.Surface, surface.Id, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var source in setup.ContentSources)
        {
            if (TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Kind != OutputDefinition.Kinds.Default && TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.Output, output.Id, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var image in setup.ReferenceImages)
        {
            if (TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var prop in setup.Props)
        {
            PropBounds(prop, out var a, out var b);
            Include(ref any, ref min, ref max, a, b);
        }

        return any;
    }

    private static void Include(ref bool any, ref Vector2 min, ref Vector2 max, Vector2 a, Vector2 b)
    {
        if (!any)
        {
            min = a;
            max = b;
            any = true;
            return;
        }

        min = Vector2.Min(min, a);
        max = Vector2.Max(max, b);
    }

    /// <summary>The card an entity is drawn on: its own for the card kinds, the owning card for slices, patches and regions.</summary>
    private static bool TryGetCardBounds(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, out Vector2 min, out Vector2 max)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Prop:
            {
                var prop = setup.FindProp(id);
                min = max = Vector2.Zero;
                if (prop == null)
                    return false;

                PropBounds(prop, out min, out max);
                return true;
            }
            case SetupEntitySelection.EntityKind.Slice:
            {
                var source = setup.FindSource(setup.FindSlice(id)?.SourceId ?? Guid.Empty);
                return TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.ContentSource, source?.SymbolChildId ?? Guid.Empty, out min, out max);
            }
            case SetupEntitySelection.EntityKind.Patch:
            {
                setup.FindPatch(id, out var owner);
                return TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.Output, owner?.Id ?? Guid.Empty, out min, out max);
            }
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.FindSurface(id);
                for (var guard = 0; surface != null && surface.ParentId != Guid.Empty && guard < 16; guard++)
                    surface = setup.FindSurface(surface.ParentId);

                return TryGetBoardBounds(setup, kind, surface?.Id ?? Guid.Empty, out min, out max);
            }
            default:
                return TryGetBoardBounds(setup, kind, id, out min, out max);
        }
    }

    /// <summary>An entity's card rectangle in board metres, from its placement and its own size.</summary>
    private static bool TryGetBoardBounds(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, out Vector2 min, out Vector2 max)
    {
        min = max = Vector2.Zero;
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface?.BoardPlacement == null)
                    return false;

                min = surface.BoardPlacement.Position - surface.AnchorInMeters;
                max = min + surface.SizeInMeters;
                return true;
            }
            case SetupEntitySelection.EntityKind.ContentSource:
            {
                var source = setup.FindSourceByChildId(id);
                if (source?.BoardPlacement == null)
                    return false;

                min = source.BoardPlacement.Position;
                max = min + BoardPixelSize(setup, kind, id) / PixelsPerMeterOf(source.BoardPlacement);
                return true;
            }
            case SetupEntitySelection.EntityKind.Output:
            {
                var output = setup.FindOutput(id);
                if (output?.BoardPlacement == null)
                    return false;

                min = output.BoardPlacement.Position;
                max = min + BoardPixelSize(setup, kind, id) / PixelsPerMeterOf(output.BoardPlacement);
                return true;
            }
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                if (image?.BoardPlacement == null)
                    return false;

                min = image.BoardPlacement.Position;
                max = min + BoardPixelSize(setup, kind, id) / PixelsPerMeterOf(image.BoardPlacement, image);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The pixel extent a pixel card represents: the texture's, the canvas', the image's.</summary>
    private static Vector2 BoardPixelSize(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.ContentSource:
                if (OutputManager.TryGetSourceContent(id, out _, out var content) && content is { IsDisposed: false })
                    return new Vector2(Math.Max(1, content.Description.Width), Math.Max(1, content.Description.Height));

                return new Vector2(1920, 1080);

            case SetupEntitySelection.EntityKind.Output:
            {
                var output = setup.FindOutput(id);
                return output == null
                           ? new Vector2(1920, 1080)
                           : new Vector2(Math.Max(1, output.CanvasResolution.Width), Math.Max(1, output.CanvasResolution.Height));
            }
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                return image == null || image.Width <= 0 || image.Height <= 0
                           ? new Vector2(1600, 1200)
                           : new Vector2(image.Width, image.Height);
            }
            default:
                return Vector2.One;
        }
    }

    private static float PixelsPerMeterOf(CanvasPlacement? placement, ReferenceImage? image = null)
    {
        if (placement != null && placement.PixelsPerMeter > 0)
            return placement.PixelsPerMeter;

        // A plan knows its own scale; a photo is presentation only.
        if (image != null && image.MetersPerPixel > 0)
            return 1f / image.MetersPerPixel;

        return DefaultBoardPixelsPerMeter;
    }

    private static bool TryGetPlacement(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, out CanvasPlacement placement)
    {
        placement = kind switch
                        {
                            SetupEntitySelection.EntityKind.Surface => setup.FindSurface(id)?.BoardPlacement,
                            SetupEntitySelection.EntityKind.ContentSource => setup.FindSourceByChildId(id)?.BoardPlacement,
                            SetupEntitySelection.EntityKind.Output => setup.FindOutput(id)?.BoardPlacement,
                            SetupEntitySelection.EntityKind.ReferenceImage => setup.FindReferenceImage(id)?.BoardPlacement,
                            _ => null,
                        } ?? null!;
        return placement != null;
    }

    private static bool TryGetBoardPosition(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, out Vector2 position)
    {
        if (kind == SetupEntitySelection.EntityKind.Prop)
        {
            var prop = setup.FindProp(id);
            position = prop == null ? Vector2.Zero : new Vector2(prop.Position.X, prop.Position.Y);
            return prop != null;
        }

        if (TryGetPlacement(setup, kind, id, out var placement))
        {
            position = placement.Position;
            return true;
        }

        position = Vector2.Zero;
        return false;
    }

    private static void SetBoardPosition(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, Vector2 position)
    {
        if (kind == SetupEntitySelection.EntityKind.Prop)
        {
            var prop = setup.FindProp(id);
            if (prop != null)
                prop.Position = new System.Numerics.Vector3(position.X, position.Y, prop.Position.Z);

            return;
        }

        if (TryGetPlacement(setup, kind, id, out var placement))
            placement.Position = position;
    }

    // ---- per-structure caches ------------------------------------------------------------------------

    /// <summary>Card metadata strings (sizes, resolutions, bindings), rebuilt on a structure tick — not per frame.</summary>
    private void RefreshBoardMeta(Setup setup, MachineConfig machineConfig)
    {
        _boardMetaVersion = OutputSetupHandling.StructureVersion;
        _boardMeta.Clear();

        foreach (var surface in setup.Surfaces)
            _boardMeta[surface.Id] = $"{surface.SizeInMeters.X:0.##}×{surface.SizeInMeters.Y:0.##} m";

        foreach (var output in setup.Outputs)
        {
            var binding = machineConfig.TryGetBinding(output.Id);
            _boardMeta[output.Id] = binding == null
                                        ? $"{output.CanvasResolution.Width}×{output.CanvasResolution.Height}"
                                        : $"{output.CanvasResolution.Width}×{output.CanvasResolution.Height} → Display {binding.DisplayIndex + 1}";
        }

        foreach (var source in setup.ContentSources)
        {
            var px = BoardPixelSize(setup, SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId);
            _boardMeta[source.Id] = $"{px.X:0}×{px.Y:0}";
        }

        foreach (var image in setup.ReferenceImages)
            _boardMeta[image.Id] = image.Width > 0 ? $"{image.Width}×{image.Height}" : "no image";

        foreach (var prop in setup.Props)
            _boardMeta[prop.Id] = $"{prop.HeightInMeters:0.##} m";
    }

    private string? BoardMeta(Guid id) => _boardMeta.TryGetValue(id, out var meta) ? meta : null;

    /// <summary>Board metres (Y up) on the pan/zoom canvas, whose y runs down.</summary>
    private sealed class BoardProjection(ScalableCanvas canvas) : ICanvasProjection
    {
        public Vector2 CanvasToScreen(Vector2 posInCanvas) => canvas.TransformPositionFloat(new Vector2(posInCanvas.X, -posInCanvas.Y));

        public Vector2 ScreenToCanvas(Vector2 posOnScreen)
        {
            var p = canvas.InverseTransformPositionFloat(posOnScreen);
            return new Vector2(p.X, -p.Y);
        }
    }

    /// <summary>
    /// A space's pixels on the Board: canvas coordinates are the space's px (Y down, origin at its entity's
    /// card top-left), turned into board metres by the card's scale and then into screen through the Board's
    /// own projection. Every handle and label of a space draws through this, so a space is a place on the
    /// Board rather than a canvas of its own.
    /// </summary>
    private sealed class SpaceProjection(BoardProjection board) : ICanvasProjection
    {
        /// <summary>Board metres of the space's px (0,0): the card's top-left.</summary>
        public Vector2 Origin;

        public float PixelsPerMeter = DefaultBoardPixelsPerMeter;

        public Vector2 CanvasToBoard(Vector2 px) => new(Origin.X + px.X / PixelsPerMeter, Origin.Y - px.Y / PixelsPerMeter);
        public Vector2 BoardToCanvas(Vector2 metres) => new((metres.X - Origin.X) * PixelsPerMeter, (Origin.Y - metres.Y) * PixelsPerMeter);
        public Vector2 CanvasToScreen(Vector2 posInCanvas) => board.CanvasToScreen(CanvasToBoard(posInCanvas));
        public Vector2 ScreenToCanvas(Vector2 posOnScreen) => BoardToCanvas(board.ScreenToCanvas(posOnScreen));
    }

    /// <summary>The Board's canvas scale is screen px per metre, so the stock zoom range (meant for px-per-px
    /// canvases) would stop a metre-sized board at a thumbnail; this one spans a whole room down to a centimetre.</summary>
    private sealed class BoardCanvas : ScalableCanvas
    {
        internal protected override Vector2 ClampScaleToValidRange(Vector2 scale)
        {
            return new Vector2(Math.Clamp(scale.X, MinPixelsPerMeter, MaxPixelsPerMeter),
                               Math.Clamp(scale.Y, MinPixelsPerMeter, MaxPixelsPerMeter));
        }

        private const float MinPixelsPerMeter = 2;
        private const float MaxPixelsPerMeter = 200000;
    }

    private const float DefaultBoardPixelsPerMeter = 1000;

    private readonly BoardCanvas _boardCanvas = new() { FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion };
    private readonly BoardProjection _boardProjection;
    private Guid _boardFittedSetupId;
    private readonly Vector2[] _boardQuad = new Vector2[4];
    private readonly Vector2[] _boardEdgeQuad = new Vector2[4];
    private readonly Vector2[] _boardEdgeOldTrace = new Vector2[4];

    // Scale gestures (top-right handle, Ctrl + edge): the mode held for the drag, the start bounds, and the
    // total factor applied so far — the increment per frame is total / applied.
    private bool _boardEdgeScaling;
    private Vector2 _boardEdgeStartMin, _boardEdgeStartMax;
    private float _boardScaleStartWidth;
    private Vector2 _boardScaleApplied = Vector2.One;

    // Press → drag handoff for cards, and the live drag (every selected card, from its start position).
    private Vector2? _boardGrabScreen;
    private SetupEntitySelection.EntityKind _boardGrabKind;
    private Guid _boardGrabId;
    private bool _boardGrabOnSelected;
    private SetupEntitySelection.EntityKind _boardDragKind;
    private Guid _boardDragId;
    private Vector2 _boardDragGrabOnBoard;
    private readonly List<(SetupEntitySelection.EntityKind Kind, Guid Id, Vector2 Start)> _boardDragItems = [];
    private string? _boardGestureOldJson;

    // Marquee over the cards; candidates are collected as the cards draw (cleared per frame).
    private readonly SelectionFence _boardFence = new();
    private readonly List<(SetupEntitySelection.EntityKind Kind, Guid Id, ImRect Rect)> _boardFenceCandidates = [];

    private float _boardLayerFade = 1f; // 1 on the Board, toward 0 as a space comes in (set per frame)
    private int _boardMetaVersion = -1;
    private readonly Dictionary<Guid, string> _boardMeta = new();
    private EvaluationContext? _boardContext;
}
