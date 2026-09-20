#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Input;
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
    public bool ShowsBoard => _editMode == EditModes.Board;

    // A content source's space (its texture with the slices laid out) is entered from its card and left by
    // "Board"; it is not one of the tabs, so it rides beside the mode.


    /// <summary>The reference image whose space was entered from the Board (double-click); Empty while none is.
    /// Cleared by every other entry point, so leaving it is a matter of showing anything else.</summary>
    public Guid OpenedReferenceImageId { get; private set; }

    /// <summary>Debug-protocol hook: the header tab by name.</summary>
    public bool TrySetEditMode(string name)
    {
        if (!Enum.TryParse<EditModes>(name, true, out var mode))
            return false;

        _editMode = mode;
        return true;
    }

    /// <summary>Header for the canvases without tabs (the source canvas): the way back to the Board, then the title.</summary>
    private void DrawBoardReturnHeader(string title)
    {
        if (CustomComponents.StateButton("Board", CustomComponents.ButtonStates.Emphasized))
        {
            _editMode = EditModes.Board;
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
        // The grid fills whatever is visible of the canvas — the clip the caller set, not the canvas' own
        // rectangle, which stops short of the toolbar's edge.
        var screenMin = dl.GetClipRectMin();
        var screenMax = dl.GetClipRectMax();
        var onBoard = _spaceBlend.Value <= 0.001f;
        _boardLayerFade = 1f - _spaceBlend.Value;

        FitBoardIfNeeded(setup);
        ReleasePressHandoff(selection);
        _hoveredPlanHandlePlanId = Guid.Empty;

        var pixelsPerMeter = MathF.Abs(_boardCanvas.Scale.X);
        MetricGridRaster.Draw(dl, _boardProjection, screenMin, screenMax, pixelsPerMeter, _boardDragKind != SetupEntityKinds.None ? 1f : 0.6f);

        // A live edge crop changes the size the hot surface's metadata shows — only that entry follows it per frame.
        if (_boardMetaVersion != OutputSetupHandling.StructureVersion)
        {
            RefreshBoardMeta(setup, machineConfig);
        }
        else if (_gesture.Kind == GestureKinds.SurfaceResize && setup.FindSurface(_gesture.HotId) is { } resizedSurface)
        {
            RefreshSurfaceMeta(resizedSurface);
        }

        // Fully inside a space nothing of the Board is left to draw or to click.
        if (_boardLayerFade <= 0.001f)
        {
            _boardFenceCandidates.Clear();
            return;
        }

        _boardFenceCandidates.Clear();

        // Draw order is stacking order: reference images at the back, then content, surfaces, outputs, props.
        foreach (var image in setup.ReferenceImages)
        {
            if (IsDrawnBySpace(setup, SetupEntityKinds.ReferenceImage, image.Id)
                || !TryGetBoardBounds(setup, SetupEntityKinds.ReferenceImage, image.Id, out var min, out var max))
                continue;

            var srv = TryGetReferenceSrv(image);
            DrawBoardCard(setup, selection, dl, SetupEntityKinds.ReferenceImage, image.Id, min, max,
                          image.Name, BoardMeta(image.Id), srv, true);
            DrawBoardTraces(setup, selection, dl, image, min, max);
            DrawBoardScaleLine(setup, dl, image, min, max);
        }

        DrawScaleLengthPopup(setup);

        foreach (var source in setup.ContentSources)
        {
            if (IsDrawnBySpace(setup, SetupEntityKinds.ContentSource, source.SymbolChildId)
                || !TryGetBoardBounds(setup, SetupEntityKinds.ContentSource, source.SymbolChildId, out var min, out var max))
                continue;

            var srv = OutputContentResolver.TryGetSourceContent(source.SymbolChildId, out _, out var content) && content is { IsDisposed: false }
                          ? SrvManager.GetSrvForTexture(content)
                          : null;
            DrawBoardCard(setup, selection, dl, SetupEntityKinds.ContentSource, source.SymbolChildId, min, max,
                          source.Name, BoardMeta(source.Id), srv, true);

            // Slices are cuts of the card: sub-rects in the texture's own (Y-down) uv, mapped into the card. The
            // primary one is edited in place — edges crop, corners scale, the label moves — through the source
            // space's editor pointed at the card.
            var size = max - min;
            var textureSize = BoardPixelSize(setup, SetupEntityKinds.ContentSource, source.SymbolChildId);
            var editableSliceId = _boardLayerFade >= 0.999f && selection != null
                                  && selection.TryResolve(setup, out var primaryKind, out var primaryId)
                                  && primaryKind == SetupEntityKinds.Slice
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
                    if (_sliceLabelDragging || _gesture.Kind == GestureKinds.Slice)
                        _pressHandoff.Cancel();

                    continue;
                }

                // The implicit full-frame slice is the whole card; drawing it would only add a second frame.
                if (SetupRelations.IsImplicitSlice(setup, slice))
                    continue;

                var uv = slice.UvRect;
                var sliceMin = new Vector2(min.X + uv.X * size.X, min.Y + (1 - uv.W) * size.Y);
                var sliceMax = new Vector2(min.X + uv.Z * size.X, min.Y + (1 - uv.Y) * size.Y);
                DrawBoardSubRect(setup, selection, dl, SetupEntityKinds.Slice, slice.Id, sliceMin, sliceMax,
                                 CachedSliceLabel(setup, slice));
            }
        }

        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId != Guid.Empty || IsDrawnBySpace(setup, SetupEntityKinds.Surface, surface.Id))
                continue;

            if (!TryGetBoardBounds(setup, SetupEntityKinds.Surface, surface.Id, out var min, out var max))
                continue;

            // A traced surface wears the straightened crop of its photo — what the wall looks like.
            TryGetTracedFragment(setup, surface, out var fragment, out var uvMin, out var uvMax);
            DrawBoardCard(setup, selection, dl, SetupEntityKinds.Surface, surface.Id, min, max,
                          surface.Name, BoardMeta(surface.Id), fragment, true, uvMin, uvMax);
            DrawBoardRegions(setup, selection, dl, surface, min + surface.AnchorInMeters);
            if (SurfaceMetrics.CountPoints(surface) > 0)
            {
                _boardPointProjection.View = _boardProjection;
                _boardPointProjection.Origin = min + surface.AnchorInMeters;
                _boardPointProjection.HasHomography = false;
                DrawReferencePointMarks(dl, surface, Homography.Identity, _boardPointProjection, _boardLayerFade);
            }
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Default || IsDrawnBySpace(setup, SetupEntityKinds.Output, output.Id))
                continue;

            if (!TryGetBoardBounds(setup, SetupEntityKinds.Output, output.Id, out var min, out var max))
                continue;

            var composite = OutputCompositor.RenderOutput(output.Id);
            var srv = composite is { IsDisposed: false } ? SrvManager.GetSrvForTexture(composite) : null;
            DrawBoardCard(setup, selection, dl, SetupEntityKinds.Output, output.Id, min, max,
                          output.Name, BoardMeta(output.Id), srv, true);

            // The pixel map over the card's composite, as on the output canvas, so patches are placed against it here too.
            var pixelMap = setup.FindReferenceImage(output.ReferenceImageId);
            if (pixelMap != null && output.ReferenceOpacity > 0f && TryGetReferenceSrv(pixelMap) is { IsDisposed: false } mapSrv)
            {
                dl.AddImage(mapSrv.NativePointer, _boardProjection.CanvasToScreen(new Vector2(min.X, max.Y)), _boardProjection.CanvasToScreen(new Vector2(max.X, min.Y)),
                            Vector2.Zero, Vector2.One, UiColors.ForegroundFull.Fade(output.ReferenceOpacity * _boardLayerFade));
            }

            // Patches are cuts of the canvas, edited on the card the way they are on the output canvas: the card
            // borrows the space projection at the canvas' pixel scale, and the same corner and edge handles apply.
            var canvasSize = output.CanvasSize;
            _projection.Origin = new Vector2(min.X, max.Y);
            _projection.PixelsPerMeter = canvasSize.X / MathF.Max(max.X - min.X, 0.0001f);
            DrawPatches(setup, output, selection, dl, Homography.Identity, Homography.Identity, Vector2.Zero, canvasSize,
                        editable: onBoard && !IsDrawingPlan, fade: _boardLayerFade, hasContent: srv != null);

            // A patch gesture is the patch's, not the card's — the card must not come along.
            if (_gesture.Kind is GestureKinds.PatchQuad or GestureKinds.PatchMove)
                _pressHandoff.Cancel();
        }

        foreach (var plan in setup.FloorPlans)
            DrawBoardFloorPlan(setup, selection, dl, plan);

        foreach (var prop in setup.Props)
            DrawBoardProp(setup, selection, dl, prop);

        // The card sub-editors above (a slice on its content card, the traces on each image card) borrow the
        // space projection for their own card. The space drawn after the Board layer is the entered one, so
        // put its origin and scale back — otherwise, while both layers show during a fold, the space renders
        // where the last card drawn happened to be. Before the gesture gate below: a fold is exactly when
        // the layer is only looked at.
        _projection.Origin = _spaceOrigin;
        _projection.PixelsPerMeter = _spacePixelsPerMeter;

        // The Board's own gestures belong to the Board; a fading layer is only looked at.
        if (!onBoard)
            return;

        // While walls are being drawn or a scale line is being set, the tool owns every click and key on the Board.
        HandlePlanDraw(setup, selection);
        if (IsDrawingPlan || IsSettingScale)
            return;

        HandleBoardDrag(setup, selection);
        _boardSetupForFence = setup;
        HandleBoardFence(selection);
        DrawBoardSnapGuides();
        HandleBoardHierarchyKeys(setup, selection);
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
                               SetupEntityKinds kind, Guid id, Vector2 min, Vector2 max,
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
        var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(id);

        // The name label above the card belongs to it: hovering, picking and grabbing work there as on the card.
        var pad = 4 * scale;
        var rounding = 3 * scale;
        var nameFont = isSelected ? Fonts.FontBold : Fonts.FontSmall;
        ImGui.PushFont(nameFont);
        var nameSize = ImGui.CalcTextSize(name);
        ImGui.PopFont();
        var labelMin = new Vector2(sMin.X, sMin.Y - nameSize.Y - 2 * pad);
        var labelMax = labelMin + nameSize + new Vector2(2 * pad, 2 * pad);

        var hovered = interactive && ImGui.IsWindowHovered()
                      && (ImGui.IsMouseHoveringRect(sMin, sMax) || ImGui.IsMouseHoveringRect(labelMin, labelMax));
        if (hovered)
            FrameStats.RequestCrossHighlight(id);

        // A reference image shows at its own opacity, so a backdrop can be dimmed under what is traced over it.
        var imageOpacity = kind == SetupEntityKinds.ReferenceImage && setup.FindReferenceImage(id) is { } shownImage ? shownImage.Opacity : 1f;
        if (srv is { IsDisposed: false })
            dl.AddImage(srv.NativePointer, sMin, sMax, uvMin, uvMax ?? Vector2.One, UiColors.ForegroundFull.Fade(fade * imageOpacity));
        else
            dl.AddRectFilled(sMin, sMax, UiColors.BackgroundPopup.Fade(0.85f * fade));

        // A surface's content over its photo backdrop, at the preview opacity — the same look as on the traced quad.
        var preview = UserSettings.Config.OutputSetupContentPreviewOpacity;
        if (kind == SetupEntityKinds.Surface && preview > 0.01f
            && OutputContentResolver.TryGetSurfaceSlice(id, out _, out var surfaceContent, out var contentUv) && surfaceContent is { IsDisposed: false })
        {
            var contentSrv = SrvManager.GetSrvForTexture(surfaceContent);
            if (contentSrv is { IsDisposed: false })
                dl.AddImage(contentSrv.NativePointer, sMin, sMax, new Vector2(contentUv.X, contentUv.Y), new Vector2(contentUv.Z, contentUv.W),
                            UiColors.ForegroundFull.Fade(preview * fade));
        }
        else if (kind == SetupEntityKinds.Surface && preview > 0.01f && setup.FindSurface(id) is { } canvasFedSurface
                 && TryFindCanvasQuad(setup, canvasFedSurface, _canvasFedQuad, out var feedingOutputId)
                 && OutputCompositor.RenderOutput(feedingOutputId) is { IsDisposed: false } composite
                 && SrvManager.GetSrvForTexture(composite) is { IsDisposed: false } compositeSrv)
        {
            // No content of its own, but the surface has a place on an output's canvas: the wall shows what the
            // canvas holds there, which is what will land on it when the canvas is sent as one picture.
            dl.AddImageQuad(compositeSrv.NativePointer, sMin, new Vector2(sMax.X, sMin.Y), sMax, new Vector2(sMin.X, sMax.Y),
                            _canvasFedQuad[0], _canvasFedQuad[1], _canvasFedQuad[2], _canvasFedQuad[3],
                            UiColors.ForegroundFull.Fade(preview * fade));
        }

        // The frame is the kind's hue, rounded; hovering lifts it. Selection is the white outline just outside
        // it — never a hue, so a selected card still says what it is.
        var kindColor = SetupColors.ForKind(kind);
        if (pulse > 0.001f)
            dl.AddRectFilled(sMin, sMax, kindColor.Fade(pulse * 0.15f * fade), 3 * scale);

        dl.AddRect(sMin, sMax, PulseColor(kindColor.Fade(hovered ? 1f : 0.7f), pulse).Fade(fade), rounding, ImDrawFlags.None, 1 * scale);
        if (isSelected)
        {
            var outset = new Vector2(1.5f * scale);
            dl.AddRect(sMin - outset, sMax + outset, kindColor.Fade(fade), rounding, ImDrawFlags.None, 3 * scale);
        }

        // Name above the card's top-left, in the kind's label hue (bold while selected) on a faint shade; the
        // metadata only while hovered or selected — it answers a question, it doesn't label.
        dl.AddRectFilled(labelMin, labelMax, UiColors.BackgroundFull.Fade(0.3f * fade), rounding);
        dl.AddText(nameFont, nameFont.FontSize, labelMin + new Vector2(pad, pad), SetupColors.LabelFor(kind).Fade(fade), name);
        if (meta != null && (hovered || isSelected))
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(labelMax.X + pad, labelMax.Y - pad - Fonts.FontSmall.FontSize),
                       UiColors.TextMuted.Fade(0.5f * fade), meta);

        // A locked image is a backdrop: it shows its lock beside the name and takes no press of any kind.
        if (kind == SetupEntityKinds.ReferenceImage && setup.FindReferenceImage(id) is { IsLocked: true })
        {
            Icons.DrawIconAtScreenPosition(Icon.Locked, new Vector2(labelMax.X + pad, labelMin.Y + pad * 0.5f));
            return;
        }

        if (!interactive)
            return;

        // The whole card is its pick and grab area (the picker cycles stacked cards on repeated clicks), and
        // what the fence catches. A selected surface card hands the pick down to the region under the cursor.
        // Resolved only under the cursor — anywhere else the card's background target can't be hit.
        var cardSurface = kind == SetupEntityKinds.Surface ? setup.FindSurface(id) : null;
        var pickId = id;
        if (cardSurface != null && ImGui.IsMouseHoveringRect(sMin, sMax))
            pickId = ResolveBoardPickInCard(setup, selection, cardSurface, min + cardSurface.AnchorInMeters, _boardProjection.ScreenToCanvas(ImGui.GetMousePos()));

        _picker.AddTarget(kind, pickId, sMin, sMax, isBackground: true);
        // The label is a foreground target: it wins over any card stacked beneath it.
        _picker.AddTarget(kind, id, labelMin, labelMax);
        _boardFenceCandidates.Add((kind, id, new ImRect(sMin, sMax)));
        if (pickId == id)
            GrabBoardCard(kind, id, hovered, isSelected);

        // Double-click enters the card's space: the canvas that edits it.
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            selection?.Select(kind, id);
            if (kind == SetupEntityKinds.ReferenceImage)
                OpenedReferenceImageId = id;

            _editMode = kind switch
                            {
                                SetupEntityKinds.Surface => EditModes.Straight,
                                SetupEntityKinds.Output => EditModes.Output,
                                _ => _editMode,
                            };
        }

        if (cardSurface != null && isSelected)
            DrawBoardSurfaceEdges(setup, cardSurface, min, max);

        // The scale handle at the top-right corner. On a pixel card (square) it is presentation only — the
        // card's px-per-metre, never resolution, routing or projection. On a surface (circle) it is physical:
        // the wall grows or shrinks with its aspect kept, everything on it scaling along.
        if (scalable && isSelected)
        {
            var isSurface = kind == SetupEntityKinds.Surface;
            var corner = new Vector2(max.X, max.Y);
            var style = CanvasPointHandle.Style.Default(UiColors.ForegroundFull, isSurface ? CanvasPointHandle.Shapes.Circle : CanvasPointHandle.Shapes.Square, true);
            ImGui.PushID(id.GetHashCode());
            var phase = CanvasPointHandle.Draw(ref corner, _boardProjection, style);
            ImGui.PopID();
            if (phase == CanvasPointHandle.DragPhases.Started)
            {
                BeginGesture(setup, GestureKinds.BoardScale, isSurface ? "Scale surface" : "Scale card", id);
                _boardScaleStartWidth = max.X - min.X;
                _boardScaleApplied = Vector2.One;
            }

            if (phase == CanvasPointHandle.DragPhases.Dragging)
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
                    placement.CardScale = Math.Clamp(pixelWidth / newWidth, 10f, 100000f);
                }
            }

            if (phase == CanvasPointHandle.DragPhases.Completed)
                EndGesture(setup);

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
        // Scaling the card declares how big the surface really is, exactly like typing into Size (m) — so it
        // leaves every projection where it was aimed instead of dragging the pins along.
        SurfaceGeometry.ApplyBounds(surface, newMin, newMax, RectangleEdits.Declared);

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
            SurfaceMetrics.ScaleSurfaceMetric(setup, child, increment);
        }
    }

    /// <summary>
    /// The selected surface's edge handles: an edge crops the footprint, the anchor — the card's placement —
    /// stays put, and the corner pin follows through the shared gesture skeleton, one undo step per drag.
    /// <para>Ctrl here <b>scales</b> the surface along that axis (the wall really is bigger, so its lines and
    /// regions grow with it), where Ctrl on the projector and Straight canvases <b>stretches</b> (the declared
    /// rectangle is kept and only its mapping changes). Both wear the circle handle; they differ because the
    /// Board is where a surface's real size is stated, and the canvases are where it is aimed.</para>
    /// </summary>
    private void DrawBoardSurfaceEdges(Setup setup, Surface surface, Vector2 min, Vector2 max)
    {
        _boardEdgeQuad[0] = new Vector2(min.X, max.Y);
        _boardEdgeQuad[1] = max;
        _boardEdgeQuad[2] = new Vector2(max.X, min.Y);
        _boardEdgeQuad[3] = min;

        // Plain: an edge crops the footprint (squares). Ctrl: it scales the surface along that axis, aspect free
        // (circles) — the mode is read at the press and held for the drag.
        var scaling = _gesture.Is(GestureKinds.SurfaceResize, surface.Id) ? _boardEdgeScaling : ImGui.GetIO().KeyCtrl;
        ImGui.PushID(surface.Id.GetHashCode());
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true, hue: SetupColors.ForKind(SetupEntityKinds.Surface));
        style.EdgeHandleShape = scaling ? CanvasPointHandle.Shapes.Circle : CanvasPointHandle.Shapes.Square;
        var phase = CornerPinHandles.DrawEdgeHandles(_boardEdgeQuad, _boardProjection, style, out var edge, out var edgePos);
        ImGui.PopID();
        if (edge < 0)
            return;

        // The dragged edge snaps to the other cards' edges and the floor (Shift drags free) — for a crop and a
        // scale alike, since both put that edge where the cursor is.
        if (phase == CanvasPointHandle.DragPhases.Dragging && !ImGui.GetIO().KeyShift)
        {
            CollectBoardSnapCandidates(setup, surface.Id, excludeDragItems: false);
            var threshold = BoardSnapThreshold();
            if (edge is 1 or 3)
            {
                Span<float> x = [edgePos.X];
                if (_snapping.TrySnap(RectSnapping.Axes.X, x, threshold, out var offsetX, out var targetX))
                {
                    edgePos.X += offsetX;
                    _boardSnapGuideX = targetX;
                }
            }
            else
            {
                Span<float> y = [edgePos.Y];
                if (_snapping.TrySnap(RectSnapping.Axes.Y, y, threshold, out var offsetY, out var targetY))
                {
                    edgePos.Y += offsetY;
                    _boardSnapGuideY = targetY;
                }
            }
        }

        // The crop rides the setup snapshot rather than the resize command: a traced surface's trace crops along,
        // and that quad is not part of the resize state.
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.SurfaceResize, scaling ? "Scale surface" : "Crop surface", surface.Id, surface);
                if (!scaling)
                    BeginContentEdit(setup, surface);

                _boardEdgeScaling = scaling;
                _boardScaleApplied = Vector2.One;
                SurfaceGeometry.LocalBounds(surface, out _boardEdgeStartMin, out _boardEdgeStartMax);

                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, surface.Id) && _boardEdgeScaling:
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

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, surface.Id):
            {
                // Re-based on the pre-drag rectangle, so the edit doesn't compound; the anchor is the origin of
                // surface space and sits at the card's placement.
                _gesture.Snapshot!.Restore(surface);
                SurfaceGeometry.LocalBounds(surface, out var oldMin, out var oldMax);
                var origin = surface.BoardPlacement?.Position ?? Vector2.Zero;
                SurfaceGeometry.DragEdge(surface, edge, edgePos - origin, keepDimensions: false);

                // The wall's pixels stay put: the slice window shrinks with the rectangle.
                SurfaceGeometry.LocalBounds(surface, out var newMin, out var newMax);
                KeepContentInPlace(setup, oldMin, oldMax, newMin, newMax);


                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                EndGesture(setup);
                break;
        }
    }

    /// <summary>
    /// Arms a card's press → drag handoff. A plain press on a card that is already selected keeps the whole
    /// selection (so the drag moves the group) and defers the single-select to the release.
    /// </summary>
    private void GrabBoardCard(SetupEntityKinds kind, Guid id, bool hovered, bool isSelected)
    {
        if (!hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsAnyItemHovered()
            || _boardDragKind != SetupEntityKinds.None || IsDrawingPlan || IsSettingScale)
            return;

        var io = ImGui.GetIO();
        _pressHandoff.Arm(ImGui.GetMousePos(), PressOrigins.BoardCard, kind, id, keepsSelection: isSelected && !io.KeyCtrl && !io.KeyShift);
    }

    /// <summary>A labelled sub-rect inside a card (a slice, a region): thin inner outline, its own pick target.</summary>
    private void DrawBoardSubRect(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl,
                                  SetupEntityKinds kind, Guid id, Vector2 min, Vector2 max, string label)
    {
        var scale = T3Ui.UiScaleFactor;
        var fade = _boardLayerFade;
        var sMin = _boardProjection.CanvasToScreen(new Vector2(min.X, max.Y));
        var sMax = _boardProjection.CanvasToScreen(new Vector2(max.X, min.Y));
        var isSelected = selection?.IsSelected(kind, id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(id);

        var kindColor = SetupColors.ForKind(kind);
        if (pulse > 0.001f)
            dl.AddRectFilled(sMin, sMax, kindColor.Fade(pulse * 0.15f * fade));

        dl.AddRect(sMin, sMax, (isSelected ? kindColor : PulseColor(kindColor.Fade(0.7f), pulse)).Fade(fade),
                   0, ImDrawFlags.None, (isSelected ? 2f : 1f) * scale);

        _boardQuad[0] = sMin;
        _boardQuad[1] = new Vector2(sMax.X, sMin.Y);
        _boardQuad[2] = sMax;
        _boardQuad[3] = new Vector2(sMin.X, sMax.Y);
        DrawEntityLabel(dl, kind, _boardQuad, id, label, isSelected, 0.9f * fade, pulse);

        // A slice says what shows it, and lights those rows while the cursor is on it.
        if (kind != SetupEntityKinds.Slice)
            return;

        DrawSliceConsumers(dl, setup, id, CornerPinHandles.GetCenteredLabelRect(_boardQuad, label), 0.7f * fade);
        if (ImGui.IsWindowHovered() && CanvasDraw.Contains(sMin, sMax, ImGui.GetMousePos()))
            PulseConsumers(setup, id);
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
            _regionProjection.HasHomography = false;
            DrawRegionEditable(setup, dl, parent, child, _regionProjection, selection, _boardLayerFade);

            SurfaceGeometry.RegionBounds(child, out var localMin, out _);
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
        var isSelected = selection?.IsSelected(SetupEntityKinds.Prop, prop.Id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(prop.Id);
        var color = (isSelected ? UiColors.Text : PulseColor(UiColors.TextMuted.Fade(0.8f), pulse)).Fade(fade);
        var thickness = (isSelected ? 2.5f : 1.5f) * scale;

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
            FrameStats.RequestCrossHighlight(prop.Id);

        var meta = BoardMeta(prop.Id) ?? "";
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(sMin.X, sMin.Y - Fonts.FontSmall.FontSize - 2 * scale),
                   (isSelected ? UiColors.Text : UiColors.TextMuted).Fade(fade), meta);

        if (!interactive)
            return;

        _picker.AddTarget(SetupEntityKinds.Prop, prop.Id, sMin, sMax, isBackground: true);
        _boardFenceCandidates.Add((SetupEntityKinds.Prop, prop.Id, new ImRect(sMin, sMax)));
        GrabBoardCard(SetupEntityKinds.Prop, prop.Id, hovered, isSelected);
    }

    /// <summary>
    /// A floor plan's card: its run of segments at true scale, walls thick and named, open sides thin, a light
    /// fill for a floor; each corner a handle that drags the plan and re-derives its surfaces as it goes.
    /// </summary>
    private void DrawBoardFloorPlan(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, FloorPlan plan)
    {
        var scale = T3Ui.UiScaleFactor;
        var fade = _boardLayerFade;
        var origin = plan.BoardPlacement?.Position ?? Vector2.Zero;
        var isSelected = selection?.IsSelected(SetupEntityKinds.FloorPlan, plan.Id) ?? false;
        var pulse = isSelected ? 0f : FrameStats.CrossHighlightAmount(plan.Id);
        var hue = SetupColors.ForKind(SetupEntityKinds.FloorPlan);
        var wallColor = (isSelected ? hue : PulseColor(hue.Fade(0.7f), pulse)).Fade(fade);
        var openColor = UiColors.TextMuted.Fade(0.5f * fade);
        var count = plan.Vertices.Count;

        if (_boardPlanPoints.Length < count)
            _boardPlanPoints = new Vector2[Math.Max(count, _boardPlanPoints.Length * 2)];

        for (var i = 0; i < count; i++)
            _boardPlanPoints[i] = _boardProjection.CanvasToScreen(origin + plan.Vertices[i]);

        // A faint fill says the frame is a card: dragging anywhere inside moves it, like every other card.
        PlanBounds(plan, out var fillMin, out var fillMax);
        dl.AddRectFilled(_boardProjection.CanvasToScreen(new Vector2(fillMin.X, fillMax.Y)), _boardProjection.CanvasToScreen(new Vector2(fillMax.X, fillMin.Y)),
                         hue.Fade(0.05f * fade), 3 * scale);

        if (plan.IsClosed && count >= 3 && setup.FindSurface(plan.RaisedFloorId) != null)
        {
            // Triangulated, so an L-shaped or notched room fills its footprint and nothing outside it.
            plan.Triangulate(_planTriangles);
            var fill = hue.Fade(0.08f * fade);
            for (var t = 0; t + 2 < _planTriangles.Count; t += 3)
                dl.AddTriangleFilled(_boardPlanPoints[_planTriangles[t]], _boardPlanPoints[_planTriangles[t + 1]], _boardPlanPoints[_planTriangles[t + 2]], fill);
        }

        // Walls face the room, so their labels sit on the other side of the line, outside; an open run's
        // labels sit on the right of its drawing direction, which is the outside of a wall facing left.
        var facesLeft = !plan.IsClosed || plan.SignedAreaTwice() >= 0;
        for (var segment = 0; segment < plan.SegmentCount; segment++)
        {
            var a = _boardPlanPoints[segment];
            var b = _boardPlanPoints[(segment + 1) % count];
            var wall = setup.FindSurface(plan.WallOf(segment));
            var wallSelected = wall != null && (selection?.IsSelected(SetupEntityKinds.Surface, wall.Id) ?? false);
            if (wall == null)
            {
                dl.AddLine(a, b, openColor, 1f * scale);
            }
            else
            {
                var wallPulse = wallSelected ? 0f : FrameStats.CrossHighlightAmount(wall.Id);
                dl.AddLine(a, b, wallSelected ? UiColors.Selection.Fade(fade) : PulseColor(wallColor, wallPulse), (wallSelected ? 4f : 3f) * scale);
            }

            // The name and length along the segment, offset to its outside, so the plan reads as the room's spec sheet.
            plan.GetSegment(segment, out var start, out var end);
            var along = b - a;
            if (along.LengthSquared() < 1f)
                continue;

            along /= along.Length();
            // Screen y runs down, so the plan's left of travel is (-y, x) turned the other way: (y, -x) is outside.
            var outward = facesLeft ? new Vector2(along.Y, -along.X) : new Vector2(-along.Y, along.X);
            var label = wall == null ? $"{(end - start).Length():0.##} m" : $"{wall.Name} · {(end - start).Length():0.##} m";
            CanvasDraw.TextAlong(dl, Fonts.FontSmall, Fonts.FontSmall.FontSize, (a + b) * 0.5f + outward * 10 * scale, along,
                                 (wallSelected ? UiColors.Text : UiColors.TextMuted).Fade(fade), label);
        }

        // The card's frame is the run's bounds with a margin: the pick and grab area. Its name sits above the
        // top-left like every other card's, and hovering, picking and grabbing work there as on the card.
        PlanBounds(plan, out var min, out var max);
        var sMin = _boardProjection.CanvasToScreen(new Vector2(min.X, max.Y));
        var sMax = _boardProjection.CanvasToScreen(new Vector2(max.X, min.Y));
        var pad = 4 * scale;
        var rounding = 3 * scale;
        var nameFont = isSelected ? Fonts.FontBold : Fonts.FontSmall;
        ImGui.PushFont(nameFont);
        var nameSize = ImGui.CalcTextSize(plan.Name);
        ImGui.PopFont();
        var labelMin = new Vector2(sMin.X, sMin.Y - nameSize.Y - 2 * pad);
        var labelMax = labelMin + nameSize + new Vector2(2 * pad, 2 * pad);

        var interactive = fade >= 0.999f;
        var hovered = interactive && ImGui.IsWindowHovered()
                      && (ImGui.IsMouseHoveringRect(sMin, sMax) || ImGui.IsMouseHoveringRect(labelMin, labelMax));
        if (hovered)
            FrameStats.RequestCrossHighlight(plan.Id);

        dl.AddRect(sMin, sMax, PulseColor(hue.Fade(hovered ? 1f : 0.7f), pulse).Fade(fade), rounding, ImDrawFlags.None, 1 * scale);
        if (isSelected)
        {
            var outset = new Vector2(1.5f * scale);
            dl.AddRect(sMin - outset, sMax + outset, hue.Fade(fade), rounding, ImDrawFlags.None, 3 * scale);
        }

        dl.AddRectFilled(labelMin, labelMax, UiColors.BackgroundFull.Fade(0.3f * fade), rounding);
        dl.AddText(nameFont, nameFont.FontSize, labelMin + new Vector2(pad, pad), SetupColors.LabelFor(SetupEntityKinds.FloorPlan).Fade(fade), plan.Name);
        var meta = BoardMeta(plan.Id);
        if (meta != null && (hovered || isSelected))
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(labelMax.X + pad, labelMax.Y - pad - Fonts.FontSmall.FontSize),
                       UiColors.TextMuted.Fade(0.5f * fade), meta);

        if (!interactive)
            return;

        // Corners drag in board metres; a corner near a neighbour's line snaps onto it, so rooms stay square.
        // Drawn before the card's grab, so a press on a handle is an item press and never arms a card drag.
        // While drawing, the corners are only shown: a click near one plants the next corner or closes the room.
        var handleStyle = CanvasPointHandle.Style.Default(isSelected ? hue : hue.Fade(0.6f), CanvasPointHandle.Shapes.Square, editable: !IsDrawingPlan);
        ImGui.PushID(plan.Id.GetHashCode());
        DrawPlanDrawEntries(plan, origin, isSelected);
        for (var i = 0; i < count; i++)
        {
            ImGui.PushID(i);
            var pos = origin + plan.Vertices[i];
            var phase = CanvasPointHandle.Draw(ref pos, _boardProjection, handleStyle);
            ImGui.PopID();

            // Tested by distance rather than by item, so the corner keeps its menu whatever else claims the hover.
            var cornerHovered = interactive && !IsDrawingPlan
                                && (ImGui.GetMousePos() - _boardProjection.CanvasToScreen(origin + plan.Vertices[i])).Length() < handleStyle.Radius * 1.5f * scale;
            if (cornerHovered)
            {
                _hoveredPlanHandlePlanId = plan.Id;
                _hoveredPlanCorner = i;
            }

            switch (phase)
            {
                case CanvasPointHandle.DragPhases.Started:
                    BeginGesture(setup, GestureKinds.PlanVertex, "Move corner", plan.Id);
                    break;

                case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.PlanVertex, plan.Id):
                {
                    var local = pos - origin;
                    if (!ImGui.GetIO().KeyShift)
                        SnapPlanVertex(plan, i, ref local);

                    plan.Vertices[i] = local;
                    CanvasPointHandle.ReportSnappedPosition(_boardProjection, origin + local);
                    FloorPlanSync.Apply(setup, plan, i);
                    break;
                }

                case CanvasPointHandle.DragPhases.Completed:
                {
                    // An open run's end corner dropped onto its other end closes the room: the two corners merge and
                    // a wall stands on the edge that now joins them.
                    var isEnd = !plan.IsClosed && (i == 0 || i == count - 1) && count >= 3;
                    var other = i == 0 ? plan.Vertices[count - 1] : plan.Vertices[0];
                    if (isEnd && (plan.Vertices[i] - other).Length() < BoardSnapThreshold())
                    {
                        var dropped = i;
                        FloorPlanSync.CloseByMerging(setup, plan, dropped);
                    }

                    EndGesture(setup);
                    break;
                }
            }

            // A corner's own menu; the picker leaves a hovered corner alone, so the card's menu doesn't open with it.
            if (cornerHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _planMenuVertex = i;
                ImGui.OpenPopup(PlanCornerMenuId);
            }

            // Double-click takes the corner out, the mirror of splitting an edge by double-click.
            if (cornerHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && count > (plan.IsClosed ? 3 : 2))
            {
                var vertex = i;
                CancelGesture();
                SetupUndo.RunUndoable("Remove corner", setup, () => FloorPlanSync.RemoveVertex(setup, plan, vertex));
                break;
            }
        }

        if (ImGui.BeginPopup(PlanCornerMenuId))
        {
            var canRemove = count > (plan.IsClosed ? 3 : 2);
            if (CustomComponents.DrawMenuItem(1, "Remove Corner", isEnabled: canRemove))
            {
                var vertex = _planMenuVertex;
                SetupUndo.RunUndoable("Remove corner", setup, () => FloorPlanSync.RemoveVertex(setup, plan, vertex));
            }

            ImGui.EndPopup();
        }

        // Each edge's middle is a handle too: a click raises or takes down the wall on it (the dot is filled while
        // one stands there), a drag slides the edge sideways with the neighbouring walls following along their
        // own lines, and a right-click offers to split the edge.
        for (var segment = 0; segment < plan.SegmentCount; segment++)
        {
            plan.GetSegment(segment, out var start, out var end);
            var startMid = (start + end) * 0.5f;
            var dragging = _gesture.Is(GestureKinds.PlanEdge, plan.Id) && _planEdgeSegment == segment;
            var mid = origin + (dragging ? _planEdgeMidNow : startMid);
            var hasWall = setup.FindSurface(plan.WallOf(segment)) != null;
            var edgeStyle = CanvasPointHandle.Style.Default(hasWall ? (isSelected ? hue.Fade(0.9f) : hue.Fade(0.5f)) : UiColors.BackgroundFull.Fade(0.7f),
                                                            CanvasPointHandle.Shapes.Circle, editable: !IsDrawingPlan);
            edgeStyle.Radius = 4;
            edgeStyle.OutlineColor = hasWall ? T3.Core.DataTypes.Vector.Color.TransparentBlack : (isSelected ? hue.Fade(0.9f) : hue.Fade(0.5f));

            ImGui.PushID(1000 + segment);
            var phase = CanvasPointHandle.Draw(ref mid, _boardProjection, edgeStyle);
            ImGui.PopID();

            var edgeHovered = interactive && !IsDrawingPlan
                              && (ImGui.GetMousePos() - _boardProjection.CanvasToScreen(origin + startMid)).Length() < edgeStyle.Radius * 2f * scale;
            if (edgeHovered)
            {
                // The dot stands for the wall: hovering it lights the wall up wherever else it is shown.
                _hoveredPlanHandlePlanId = plan.Id;
                if (hasWall)
                    FrameStats.RequestCrossHighlight(plan.WallOf(segment));
            }

            switch (phase)
            {
                case CanvasPointHandle.DragPhases.Started:
                    BeginGesture(setup, GestureKinds.PlanEdge, "Move edge", plan.Id);
                    _planEdgeSegment = segment;
                    _planEdgeStartMid = startMid;
                    _planEdgeMidNow = startMid;
                    _planEdgeMoved = false;
                    _planEdgeStartVertices.Clear();
                    _planEdgeStartVertices.AddRange(plan.Vertices);
                    break;

                case CanvasPointHandle.DragPhases.Dragging when dragging:
                {
                    // Only the sideways part of the drag moves the edge; along itself it has nowhere to go.
                    var direction = end - start;
                    if (direction.LengthSquared() > 0.000001f)
                    {
                        direction /= direction.Length();
                        var normal = new Vector2(-direction.Y, direction.X);
                        var offset = normal * Vector2.Dot(mid - origin - _planEdgeStartMid, normal);
                        if (offset.Length() > 0.001f)
                            _planEdgeMoved = true;

                        _planEdgeMidNow = _planEdgeStartMid + offset;
                        CanvasPointHandle.ReportSnappedPosition(_boardProjection, origin + _planEdgeMidNow);
                        FloorPlanSync.MoveSegment(setup, plan, segment, _planEdgeStartVertices, offset);
                    }

                    break;
                }

                case CanvasPointHandle.DragPhases.Completed:
                {
                    EndGesture(setup);
                    _planEdgeSegment = -1;

                    // A press that never moved is a click: the wall on this edge comes or goes.
                    if (!_planEdgeMoved)
                    {
                        var index = segment;
                        var raise = !hasWall;
                        SetupUndo.RunUndoable(raise ? "Raise wall" : "Take down wall", setup, () => FloorPlanSync.SetWall(setup, plan, index, raise));
                    }

                    break;
                }
            }

            if (edgeHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _planMenuSegment = segment;
                ImGui.OpenPopup(PlanEdgeMenuId);
            }
        }

        if (ImGui.BeginPopup(PlanEdgeMenuId))
        {
            var menuSegment = _planMenuSegment;
            if (menuSegment >= 0 && menuSegment < plan.SegmentCount)
            {
                var menuWall = setup.FindSurface(plan.WallOf(menuSegment));
                var menuHasWall = menuWall != null;
                if (menuHasWall && selection != null && CustomComponents.DrawMenuItem(3, "Rename Wall"))
                    OutlinerItem.BeginRename(selection, SetupEntityKinds.Surface, menuWall!.Id, menuWall.Name);

                if (CustomComponents.DrawMenuItem(1, menuHasWall ? "Take Down Wall" : "Raise Wall"))
                    SetupUndo.RunUndoable(menuHasWall ? "Take down wall" : "Raise wall", setup, () => FloorPlanSync.SetWall(setup, plan, menuSegment, !menuHasWall));

                if (CustomComponents.DrawMenuItem(2, "Split Edge"))
                {
                    plan.GetSegment(menuSegment, out var a, out var b);
                    var point = (a + b) * 0.5f;
                    SetupUndo.RunUndoable("Split edge", setup, () => FloorPlanSync.InsertVertex(setup, plan, menuSegment, point));
                }

                if (CustomComponents.DrawMenuItem(4, "Set Length..."))
                {
                    plan.GetSegment(menuSegment, out var a, out var b);
                    _planLengthSegment = menuSegment;
                    _planLengthValue = (b - a).Length();
                    _planLengthPopupPending = true;
                }
            }

            ImGui.EndPopup();
        }

        // Opened once the edge menu has closed, since a popup opened from inside another closes with it.
        if (_planLengthPopupPending)
        {
            _planLengthPopupPending = false;
            ImGui.OpenPopup(PlanLengthPopupId);
        }

        ImGui.SetNextWindowSize(new Vector2(240 * scale, 0));
        if (ImGui.BeginPopup(PlanLengthPopupId))
        {
            CustomComponents.StylizedText("Wall length", Fonts.FontBold, UiColors.Text);
            FormInputs.AddFloat("Length (m)", ref _planLengthValue, 0.01f, 1000, 0.01f, clampMin: true, clampMax: true,
                                "The next corner moves along the wall; the walls after it follow: a turning corner lengthens its wall, a straight one shifts it.");
            FormInputs.AddVerticalSpace(4);
            if (ImGui.Button("Apply") || ImGui.IsKeyPressed(ImGuiKey.Enter, false))
            {
                var index = _planLengthSegment;
                var value = _planLengthValue;
                SetupUndo.RunUndoable("Set wall length", setup, () => FloorPlanSync.SetSegmentLength(setup, plan, index, value));
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        ImGui.PopID();

        _picker.AddTarget(SetupEntityKinds.FloorPlan, plan.Id, sMin, sMax, isBackground: true);
        // The label is a foreground target: it wins over any card stacked beneath it.
        _picker.AddTarget(SetupEntityKinds.FloorPlan, plan.Id, labelMin, labelMax);
        _boardFenceCandidates.Add((SetupEntityKinds.FloorPlan, plan.Id, new ImRect(sMin, sMax)));
        GrabBoardCard(SetupEntityKinds.FloorPlan, plan.Id, hovered, isSelected);
    }

    /// <summary>Aligns a dragged corner with its two neighbours' x or y when within the snap threshold.</summary>
    private void SnapPlanVertex(FloorPlan plan, int index, ref Vector2 local)
    {
        var count = plan.Vertices.Count;
        var threshold = BoardSnapThreshold();
        for (var step = -1; step <= 1; step += 2)
        {
            var neighbourIndex = index + step;
            if (!plan.IsClosed && (neighbourIndex < 0 || neighbourIndex >= count))
                continue;

            var neighbour = plan.Vertices[(neighbourIndex + count) % count];
            if (MathF.Abs(local.X - neighbour.X) < threshold)
                local.X = neighbour.X;

            if (MathF.Abs(local.Y - neighbour.Y) < threshold)
                local.Y = neighbour.Y;
        }
    }

    private static void PlanBounds(FloorPlan plan, out Vector2 min, out Vector2 max)
    {
        const float margin = 1f;
        var origin = plan.BoardPlacement?.Position ?? Vector2.Zero;
        if (!plan.TryGetBounds(out min, out max))
            min = max = Vector2.Zero;

        min += origin - new Vector2(margin);
        max += origin + new Vector2(margin);
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
        if (_boardDragKind == SetupEntityKinds.None)
        {
            if (_pressHandoff.Origin != PressOrigins.BoardCard || !_pressHandoff.TryPromote(UserSettings.Config.ClickThreshold))
                return;

            var grabKind = _pressHandoff.Kind;
            var grabId = _pressHandoff.Id;
            _pressHandoff.Cancel();

            // The grabbed card's whole selection moves with it when it was already selected; otherwise the
            // grab selects it alone first.
            if (selection != null && !selection.IsSelected(grabKind, grabId))
                selection.Select(grabKind, grabId);

            // Alt: the drag moves copies, the originals stay. Copied inside the gesture's snapshot, so copy and
            // move undo as one step; each copy starts where its original is, under the cursor.
            var duplicating = ImGui.GetIO().KeyAlt && selection != null;
            if (duplicating)
            {
                BeginGesture(setup, GestureKinds.BoardCard, selection!.Count > 1 ? "Duplicate cards" : "Duplicate card", grabId);
                _boardDupOriginals.Clear();
                for (var i = 0; i < selection.Targets.Count; i++)
                    _boardDupOriginals.Add(selection.Targets[i]);

                _boardDupCopies.Clear();
                var grabCopyId = Guid.Empty;
                foreach (var original in _boardDupOriginals)
                {
                    if (!SetupActions.CanDuplicate(original.Kind) || !TryGetBoardPosition(setup, original.Kind, original.EntityId, out var originalPosition))
                        continue;

                    SetupActions.DuplicateEntityInternal(selection, setup, original.Kind, original.EntityId);
                    if (!selection.TryResolve(setup, out var copyKind, out var copyId) || copyId == original.EntityId)
                        continue;

                    SetBoardPosition(setup, copyKind, copyId, originalPosition);
                    _boardDupCopies.Add(new SelectionTarget(copyKind, copyId));
                    if (original.EntityId == grabId)
                        grabCopyId = copyId;
                }

                selection.Clear();
                foreach (var copy in _boardDupCopies)
                    selection.Add(copy.Kind, copy.EntityId);

                if (grabCopyId != Guid.Empty)
                    grabId = grabCopyId;
            }

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
            else if (TryGetBoardPosition(setup, grabKind, grabId, out var start))
            {
                _boardDragItems.Add((grabKind, grabId, start));
            }

            if (_boardDragItems.Count == 0)
            {
                if (duplicating)
                    EndGesture(setup);

                return;
            }

            _boardDragKind = grabKind;
            _boardDragId = grabId;
            _boardDragGrabOnBoard = _boardProjection.ScreenToCanvas(ImGui.GetMousePos());
            if (!duplicating)
                BeginGesture(setup, GestureKinds.BoardCard, _boardDragItems.Count > 1 ? "Move cards" : "Move card", _boardDragId);
            else
                _gesture.HotId = _boardDragId;
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = _boardProjection.ScreenToCanvas(ImGui.GetMousePos()) - _boardDragGrabOnBoard;
            for (var i = 0; i < _boardDragItems.Count; i++)
            {
                var (kind, id, start) = _boardDragItems[i];
                SetBoardPosition(setup, kind, id, start + delta);
            }

            // The moved group's outer edges snap to the other cards' edges and to the floor; Shift drags free.
            if (!ImGui.GetIO().KeyShift && TryGetDragGroupBounds(setup, out var groupMin, out var groupMax))
            {
                CollectBoardSnapCandidates(setup, Guid.Empty, excludeDragItems: true);
                var threshold = BoardSnapThreshold();
                var snap = Vector2.Zero;
                Span<float> xs = [groupMin.X, groupMax.X];
                if (_snapping.TrySnap(RectSnapping.Axes.X, xs, threshold, out var offsetX, out var targetX))
                {
                    snap.X = offsetX;
                    _boardSnapGuideX = targetX;
                }

                Span<float> ys = [groupMin.Y, groupMax.Y];
                if (_snapping.TrySnap(RectSnapping.Axes.Y, ys, threshold, out var offsetY, out var targetY))
                {
                    snap.Y = offsetY;
                    _boardSnapGuideY = targetY;
                }

                if (snap != Vector2.Zero)
                {
                    for (var i = 0; i < _boardDragItems.Count; i++)
                    {
                        var (kind, id, start) = _boardDragItems[i];
                        SetBoardPosition(setup, kind, id, start + delta + snap);
                    }
                }
            }

            return;
        }

        EndGesture(setup);
        _boardDragItems.Clear();
        _boardDragKind = SetupEntityKinds.None;
        _boardDragId = Guid.Empty;
    }

    /// <summary>
    /// Snap candidates on the Board: every card's left/right edges as x, bottom/top as y, plus the floor line —
    /// what physical things stand on. The card being edited is left out, as is the whole group of a card drag.
    /// Regions are not cards here: they snap inside their parent through the region editor.
    /// </summary>
    private void CollectBoardSnapCandidates(Setup setup, Guid excludeId, bool excludeDragItems)
    {
        _snapping.Clear();
        _snapping.AddY(0f); // the floor

        for (var i = 0; i < setup.Surfaces.Count; i++)
            AddBoardSnapCandidate(setup, SetupEntityKinds.Surface, setup.Surfaces[i].Id, excludeId, excludeDragItems);

        for (var i = 0; i < setup.ContentSources.Count; i++)
            AddBoardSnapCandidate(setup, SetupEntityKinds.ContentSource, setup.ContentSources[i].SymbolChildId, excludeId, excludeDragItems);

        for (var i = 0; i < setup.Outputs.Count; i++)
            AddBoardSnapCandidate(setup, SetupEntityKinds.Output, setup.Outputs[i].Id, excludeId, excludeDragItems);

        for (var i = 0; i < setup.ReferenceImages.Count; i++)
            AddBoardSnapCandidate(setup, SetupEntityKinds.ReferenceImage, setup.ReferenceImages[i].Id, excludeId, excludeDragItems);
    }

    private void AddBoardSnapCandidate(Setup setup, SetupEntityKinds kind, Guid id, Guid excludeId, bool excludeDragItems)
    {
        if (id == excludeId || (excludeDragItems && IsBoardDragItem(id)))
            return;

        // Regions live inside their parent's card; only top-level surfaces are cards.
        if (kind == SetupEntityKinds.Surface && setup.FindSurface(id)?.ParentId != Guid.Empty)
            return;

        if (!TryGetBoardBounds(setup, kind, id, out var min, out var max))
            return;

        _snapping.AddRectEdges(min, max);
    }

    private bool IsBoardDragItem(Guid id)
    {
        for (var i = 0; i < _boardDragItems.Count; i++)
        {
            if (_boardDragItems[i].Id == id)
                return true;
        }

        return false;
    }

    /// <summary>The bounding box of every card in the live drag, at its current (already moved) position.</summary>
    private bool TryGetDragGroupBounds(Setup setup, out Vector2 min, out Vector2 max)
    {
        var any = false;
        min = max = Vector2.Zero;
        for (var i = 0; i < _boardDragItems.Count; i++)
        {
            var (kind, id, _) = _boardDragItems[i];
            if (TryGetBoardBounds(setup, kind, id, out var itemMin, out var itemMax))
                Include(ref any, ref min, ref max, itemMin, itemMax);
        }

        return any;
    }

    /// <summary>A few screen pixels, in Board metres at the current zoom.</summary>
    private float BoardSnapThreshold()
    {
        return RectSnapping.ThresholdFor(_boardProjection, Vector2.Zero, 1f).X;
    }

    /// <summary>The snapped-to lines across the whole view, for the frame a gesture snapped. Cleared afterwards.</summary>
    private void DrawBoardSnapGuides()
    {
        if (_boardSnapGuideX == null && _boardSnapGuideY == null)
            return;

        var dl = ImGui.GetWindowDrawList();
        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        var color = UiColors.StatusAnimated.Fade(0.6f);
        if (_boardSnapGuideX != null)
        {
            var x = _boardProjection.CanvasToScreen(new Vector2(_boardSnapGuideX.Value, 0)).X;
            dl.AddLine(new Vector2(x, windowMin.Y), new Vector2(x, windowMax.Y), color, 1 * T3Ui.UiScaleFactor);
        }

        if (_boardSnapGuideY != null)
        {
            var y = _boardProjection.CanvasToScreen(new Vector2(0, _boardSnapGuideY.Value)).Y;
            dl.AddLine(new Vector2(windowMin.X, y), new Vector2(windowMax.X, y), color, 1 * T3Ui.UiScaleFactor);
        }

        _boardSnapGuideX = null;
        _boardSnapGuideY = null;
    }

    /// <summary>
    /// Marquee over the cards: a drag on empty Board replaces the selection with every card it touches
    /// (shift adds, ctrl removes); a click on empty Board clears it. Never while a card gesture is live.
    /// </summary>
    private void HandleBoardFence(SetupEntitySelection? selection)
    {
        // Not IsAnyItemActive: a press on empty window space makes the window's move-id the active item, which
        // would veto every fence. The scale handle is the only other gesture, and it holds the snapshot.
        if (selection == null || _boardDragKind != SetupEntityKinds.None
            || _pressHandoff.Origin == PressOrigins.BoardCard || _gesture.IsLive || _sliceLabelDragging)
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

        // The fence's hover rules include child windows, and the outliner strip is one: a press on a row up
        // there must not start a fence here — its release would read as an empty click and clear the selection
        // the row just made.
        if (_boardFence.State == SelectionFence.States.Inactive && !IsMouseOverCanvas())
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

            // A container only partly inside the fence offers its children instead; wholly inside, it is the
            // thing meant. Cards without regions are plain items.
            if (kind == SetupEntityKinds.Surface && !Contains(bounds, rect)
                && _boardSetupForFence?.FindSurface(id) is { } container && HasRegions(_boardSetupForFence, container.Id)
                && TryGetBoardBounds(_boardSetupForFence, kind, id, out var cardMin, out _))
            {
                FenceRegions(selection, selectMode, bounds, container, cardMin + container.AnchorInMeters);
                continue;
            }

            if (selectMode == SelectionFence.SelectModes.Remove)
                selection.Remove(kind, id);
            else
                selection.Add(kind, id);
        }
    }

    /// <summary>The same rule one level down: a region wholly inside (or without regions of its own) is taken; one
    /// partly inside hands over to its regions.</summary>
    private void FenceRegions(SetupEntitySelection selection, SelectionFence.SelectModes selectMode, ImRect bounds, Surface parent, Vector2 originOnBoard)
    {
        var setup = _boardSetupForFence!;
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != parent.Id)
                continue;

            SurfaceGeometry.RegionBounds(child, out var min, out var max);
            var rect = new ImRect(_boardProjection.CanvasToScreen(originOnBoard + new Vector2(min.X, max.Y)),
                                  _boardProjection.CanvasToScreen(originOnBoard + new Vector2(max.X, min.Y)));
            if (!bounds.Overlaps(rect))
                continue;

            if (!Contains(bounds, rect) && HasRegions(setup, child.Id))
            {
                FenceRegions(selection, selectMode, bounds, child, originOnBoard + min + child.AnchorInMeters);
                continue;
            }

            if (selectMode == SelectionFence.SelectModes.Remove)
                selection.Remove(SetupEntityKinds.Surface, child.Id);
            else
                selection.Add(SetupEntityKinds.Surface, child.Id);
        }
    }

    private static bool Contains(ImRect outer, ImRect inner)
    {
        return outer.Min.X <= inner.Min.X && outer.Min.Y <= inner.Min.Y && outer.Max.X >= inner.Max.X && outer.Max.Y >= inner.Max.Y;
    }

    private static bool HasRegions(Setup setup, Guid surfaceId)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == surfaceId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// What a click on a surface card picks: the card itself — unless it is selected, in which case a region
    /// under the cursor, and so on down while each level is selected (the first click selects the container and
    /// unlocks the next level). Among overlapping regions of one level, repeated clicks cycle: the click after
    /// the selected one goes to the next in draw order — unless the selected one has a region of its own under
    /// the cursor, which is the deeper level and wins. Ctrl pushes straight through to the deepest, topmost one.
    /// </summary>
    private Guid ResolveBoardPickInCard(Setup setup, SetupEntitySelection? selection, Surface card, Vector2 originOnBoard, Vector2 pointOnBoard)
    {
        var pushThrough = ImGui.GetIO().KeyCtrl;
        var current = card;
        var origin = originOnBoard;
        while (pushThrough || (selection?.IsSelected(SetupEntityKinds.Surface, current.Id) ?? false))
        {
            // The regions of this level under the cursor, in draw order.
            _pickHits.Clear();
            for (var i = 0; i < setup.Surfaces.Count; i++)
            {
                var child = setup.Surfaces[i];
                if (child.ParentId == current.Id && ContainsInParentSpace(child, pointOnBoard - origin))
                    _pickHits.Add(child);
            }

            if (_pickHits.Count == 0)
                break;

            Surface next;
            if (pushThrough)
            {
                next = _pickHits[^1];
            }
            else
            {
                var selectedIndex = -1;
                for (var i = 0; i < _pickHits.Count; i++)
                {
                    if (selection!.IsSelected(SetupEntityKinds.Surface, _pickHits[i].Id))
                        selectedIndex = i;
                }

                if (selectedIndex < 0)
                {
                    next = _pickHits[^1]; // nothing of this level selected yet: the topmost
                }
                else
                {
                    var selected = _pickHits[selectedIndex];
                    var selectedOrigin = OriginOf(selected, origin);
                    if (HasRegionUnder(setup, selected, pointOnBoard - selectedOrigin))
                        next = selected; // its own region is under the cursor: go deeper
                    else if (_pickHits.Count == 1)
                        return selected.Id; // alone and selected: stays
                    else
                        return _pickHits[(selectedIndex + 1) % _pickHits.Count].Id; // cycle the stack
                }
            }

            origin = OriginOf(next, origin);
            current = next;
        }

        return current.Id;
    }

    private static bool ContainsInParentSpace(Surface child, Vector2 pointInParent)
    {
        SurfaceGeometry.RegionBounds(child, out var min, out var max);
        return CanvasDraw.Contains(min, max, pointInParent);
    }

    /// <summary>A region's own origin (its anchor) on the Board, given its parent's.</summary>
    private static Vector2 OriginOf(Surface child, Vector2 parentOrigin)
    {
        return parentOrigin + child.LocalPosition + child.AnchorInMeters;
    }

    private static bool HasRegionUnder(Setup setup, Surface parent, Vector2 pointInParent)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId == parent.Id && ContainsInParentSpace(child, pointInParent))
                return true;
        }

        return false;
    }

    /// <summary>Enter: the selected containers' regions. Escape: their parents — or, with nothing nested selected, nothing at all.</summary>
    private void HandleBoardHierarchyKeys(Setup setup, SetupEntitySelection? selection)
    {
        if (selection == null || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput)
            return;

        var enter = ImGui.IsKeyPressed(ImGuiKey.Enter, false);
        var escape = ImGui.IsKeyPressed(ImGuiKey.Escape, false);
        if (!enter && !escape)
            return;

        _hierarchyStep.Clear();
        for (var t = 0; t < selection.Targets.Count; t++)
        {
            var target = selection.Targets[t];
            if (target.Kind != SetupEntityKinds.Surface)
                continue;

            if (enter)
            {
                for (var i = 0; i < setup.Surfaces.Count; i++)
                {
                    if (setup.Surfaces[i].ParentId == target.EntityId && !_hierarchyStep.Contains(setup.Surfaces[i].Id))
                        _hierarchyStep.Add(setup.Surfaces[i].Id);
                }
            }
            else if (setup.FindSurface(target.EntityId) is { ParentId: var parentId } && parentId != Guid.Empty && !_hierarchyStep.Contains(parentId))
            {
                _hierarchyStep.Add(parentId);
            }
        }

        if (_hierarchyStep.Count == 0)
        {
            if (escape)
                selection.Clear();

            return;
        }

        selection.Clear();
        for (var i = 0; i < _hierarchyStep.Count; i++)
            selection.Add(SetupEntityKinds.Surface, _hierarchyStep[i]);
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
                surface.BoardPlacement = new BoardPlacement { Position = new Vector2(x, 0) + surface.AnchorInMeters };
                seeded = true;
            }

            x += surface.SizeInMeters.X + gap;
        }

        var surfacesRight = x;

        // Content to the left of the surfaces, stacked upward.
        var y = 0f;
        foreach (var source in setup.ContentSources)
        {
            var size = BoardPixelSize(setup, SetupEntityKinds.ContentSource, source.SymbolChildId) / DefaultBoardPixelsPerMeter;
            if (source.BoardPlacement == null)
            {
                source.BoardPlacement = new BoardPlacement { Position = new Vector2(-gap - size.X, y) };
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
            var size = BoardPixelSize(setup, SetupEntityKinds.ReferenceImage, image.Id) / PixelsPerMeterOf(image.BoardPlacement, image);
            if (image.BoardPlacement == null)
            {
                image.BoardPlacement = new BoardPlacement { Position = new Vector2(contentLeft - gap - size.X, y) };
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

            var size = BoardPixelSize(setup, SetupEntityKinds.Output, output.Id) / DefaultBoardPixelsPerMeter;
            if (output.BoardPlacement == null)
            {
                output.BoardPlacement = new BoardPlacement { Position = new Vector2(surfacesRight + gap, y) };
                seeded = true;
            }

            y += size.Y + gap * 0.5f;
        }

        // Floor plans below the floor line, side by side — the stage seen from above, under the walls seen head-on.
        x = 0f;
        foreach (var plan in setup.FloorPlans)
        {
            plan.TryGetBounds(out var planMin, out var planMax);
            if (plan.BoardPlacement == null)
            {
                plan.BoardPlacement = new BoardPlacement { Position = new Vector2(x - planMin.X, -gap * 2 - planMax.Y) };
                FloorPlanSync.Apply(setup, plan);
                seeded = true;
            }

            x += planMax.X - planMin.X + gap;
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
            if (surface.ParentId == Guid.Empty && TryGetBoardBounds(setup, SetupEntityKinds.Surface, surface.Id, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var source in setup.ContentSources)
        {
            if (TryGetBoardBounds(setup, SetupEntityKinds.ContentSource, source.SymbolChildId, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Kind != OutputDefinition.Kinds.Default && TryGetBoardBounds(setup, SetupEntityKinds.Output, output.Id, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var image in setup.ReferenceImages)
        {
            if (TryGetBoardBounds(setup, SetupEntityKinds.ReferenceImage, image.Id, out var a, out var b))
                Include(ref any, ref min, ref max, a, b);
        }

        foreach (var prop in setup.Props)
        {
            PropBounds(prop, out var a, out var b);
            Include(ref any, ref min, ref max, a, b);
        }

        foreach (var plan in setup.FloorPlans)
        {
            PlanBounds(plan, out var a, out var b);
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
    private static bool TryGetCardBounds(Setup setup, SetupEntityKinds kind, Guid id, out Vector2 min, out Vector2 max)
    {
        switch (kind)
        {
            case SetupEntityKinds.Prop:
            {
                var prop = setup.FindProp(id);
                min = max = Vector2.Zero;
                if (prop == null)
                    return false;

                PropBounds(prop, out min, out max);
                return true;
            }
            case SetupEntityKinds.Slice:
            {
                var source = setup.FindSource(setup.FindSlice(id)?.SourceId ?? Guid.Empty);
                return TryGetBoardBounds(setup, SetupEntityKinds.ContentSource, source?.SymbolChildId ?? Guid.Empty, out min, out max);
            }
            case SetupEntityKinds.Patch:
            {
                setup.FindPatch(id, out var owner);
                return TryGetBoardBounds(setup, SetupEntityKinds.Output, owner?.Id ?? Guid.Empty, out min, out max);
            }
            case SetupEntityKinds.Surface:
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
    private static bool TryGetBoardBounds(Setup setup, SetupEntityKinds kind, Guid id, out Vector2 min, out Vector2 max)
    {
        min = max = Vector2.Zero;
        switch (kind)
        {
            case SetupEntityKinds.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface?.BoardPlacement == null)
                    return false;

                min = surface.BoardPlacement.Position - surface.AnchorInMeters;
                max = min + surface.SizeInMeters;
                return true;
            }
            case SetupEntityKinds.ContentSource:
            {
                var source = setup.FindSourceByChildId(id);
                if (source?.BoardPlacement == null)
                    return false;

                min = source.BoardPlacement.Position;
                max = min + BoardPixelSize(setup, kind, id) / PixelsPerMeterOf(source.BoardPlacement);
                return true;
            }
            case SetupEntityKinds.Output:
            {
                var output = setup.FindOutput(id);
                if (output?.BoardPlacement == null)
                    return false;

                min = output.BoardPlacement.Position;
                max = min + BoardPixelSize(setup, kind, id) / PixelsPerMeterOf(output.BoardPlacement);
                return true;
            }
            case SetupEntityKinds.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                if (image?.BoardPlacement == null)
                    return false;

                min = image.BoardPlacement.Position;
                max = min + BoardPixelSize(setup, kind, id) / PixelsPerMeterOf(image.BoardPlacement, image);
                return true;
            }
            case SetupEntityKinds.FloorPlan:
            {
                var plan = setup.FindFloorPlan(id);
                if (plan?.BoardPlacement == null)
                    return false;

                PlanBounds(plan, out min, out max);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The pixel extent a pixel card represents: the texture's, the canvas', the image's.</summary>
    private static Vector2 BoardPixelSize(Setup setup, SetupEntityKinds kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntityKinds.ContentSource:
                if (OutputContentResolver.TryGetSourceContent(id, out _, out var content) && content is { IsDisposed: false })
                    return new Vector2(Math.Max(1, content.Description.Width), Math.Max(1, content.Description.Height));

                return new Vector2(1920, 1080);

            case SetupEntityKinds.Output:
            {
                var output = setup.FindOutput(id);
                return output == null
                           ? new Vector2(1920, 1080)
                           : output.CanvasSize;
            }
            case SetupEntityKinds.ReferenceImage:
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

    private static float PixelsPerMeterOf(BoardPlacement? placement, ReferenceImage? image = null)
    {
        if (placement != null && placement.CardScale > 0)
            return placement.CardScale;

        // A plan knows its own scale; a photo is presentation only.
        if (image != null && image.MetersPerPixel > 0)
            return 1f / image.MetersPerPixel;

        return DefaultBoardPixelsPerMeter;
    }

    private static bool TryGetPlacement(Setup setup, SetupEntityKinds kind, Guid id, out BoardPlacement placement)
    {
        placement = kind switch
                        {
                            SetupEntityKinds.Surface => setup.FindSurface(id)?.BoardPlacement,
                            SetupEntityKinds.ContentSource => setup.FindSourceByChildId(id)?.BoardPlacement,
                            SetupEntityKinds.Output => setup.FindOutput(id)?.BoardPlacement,
                            SetupEntityKinds.ReferenceImage => setup.FindReferenceImage(id)?.BoardPlacement,
                            SetupEntityKinds.FloorPlan => setup.FindFloorPlan(id)?.BoardPlacement,
                            _ => null,
                        } ?? null!;
        return placement != null;
    }

    private static bool TryGetBoardPosition(Setup setup, SetupEntityKinds kind, Guid id, out Vector2 position)
    {
        if (kind == SetupEntityKinds.Prop)
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

    private static void SetBoardPosition(Setup setup, SetupEntityKinds kind, Guid id, Vector2 position)
    {
        if (kind == SetupEntityKinds.Prop)
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
            RefreshSurfaceMeta(surface);

        foreach (var output in setup.Outputs)
        {
            var binding = machineConfig.FindBinding(output.Id);
            _boardMeta[output.Id] = binding == null
                                        ? $"{output.ResolvedResolution.Width}×{output.ResolvedResolution.Height}"
                                        : $"{output.ResolvedResolution.Width}×{output.ResolvedResolution.Height} → {Plugs.BindingLabel(machineConfig, binding)}";
        }

        foreach (var source in setup.ContentSources)
        {
            var px = BoardPixelSize(setup, SetupEntityKinds.ContentSource, source.SymbolChildId);
            _boardMeta[source.Id] = $"{px.X:0}×{px.Y:0}";
        }

        foreach (var image in setup.ReferenceImages)
            _boardMeta[image.Id] = image.Width > 0 ? $"{image.Width}×{image.Height}" : "no image";

        foreach (var prop in setup.Props)
            _boardMeta[prop.Id] = $"{prop.HeightInMeters:0.##} m";

        foreach (var plan in setup.FloorPlans)
        {
            plan.TryGetBounds(out var min, out var max);
            _boardMeta[plan.Id] = $"{max.X - min.X:0.##}×{max.Y - min.Y:0.##} m · {(plan.IsClosed ? "closed" : "open")}";
        }
    }

    private void RefreshSurfaceMeta(Surface surface)
    {
        _boardMeta[surface.Id] = $"{surface.SizeInMeters.X:0.##}×{surface.SizeInMeters.Y:0.##} m";
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
        /** Alt + drag copies cards here. */
        internal protected override bool PansWithAltDrag => false;

        internal protected override Vector2 ClampScaleToValidRange(Vector2 scale)
        {
            return new Vector2(Math.Clamp(scale.X, MinPixelsPerMeter, MaxPixelsPerMeter),
                               Math.Clamp(scale.Y, MinPixelsPerMeter, MaxPixelsPerMeter));
        }

        private const float MinPixelsPerMeter = 2;
        private const float MaxPixelsPerMeter = 200000;
    }

    private const float DefaultBoardPixelsPerMeter = 1000;

    // The Board camera, its projection, and the setup it was last fitted to.
    private readonly BoardCanvas _boardCanvas = new() { FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion };
    private readonly BoardProjection _boardProjection;
    private readonly RegionProjection _boardPointProjection = new();
    private Guid _boardFittedSetupId;

    // Per-frame layer state: the Board's fade as a space comes in, and the Board coordinates a gesture snapped to.
    private float _boardLayerFade = 1f;
    private float? _boardSnapGuideX, _boardSnapGuideY;

    // Card quad scratch, reused every frame: a card or sub-rect, and the selected surface's edges.
    private readonly Vector2[] _boardQuad = new Vector2[4];
    private readonly Vector2[] _boardEdgeQuad = new Vector2[4];

    // Scale gestures (top-right handle, Ctrl + edge): the mode held for the drag, the start bounds, and the
    // total factor applied so far — the increment per frame is total / applied.
    private bool _boardEdgeScaling;
    private Vector2 _boardEdgeStartMin, _boardEdgeStartMax;
    private float _boardScaleStartWidth;
    private Vector2 _boardScaleApplied = Vector2.One;

    // The live card drag (every selected card, from its start position); the press → drag handoff is _pressHandoff.
    private SetupEntityKinds _boardDragKind;
    private Guid _boardDragId;
    private Vector2 _boardDragGrabOnBoard;
    private readonly List<(SetupEntityKinds Kind, Guid Id, Vector2 Start)> _boardDragItems = [];
    private readonly List<SelectionTarget> _boardDupOriginals = [];
    private readonly List<SelectionTarget> _boardDupCopies = [];
    private Vector2[] _boardPlanPoints = new Vector2[8]; // a plan's corners on screen, grown to the largest plan
    private readonly List<int> _planTriangles = [];

    // A plan edge being slid: which segment, its middle at the press and now, and every corner at the press.
    private int _planEdgeSegment = -1;
    private Vector2 _planEdgeStartMid;
    private Vector2 _planEdgeMidNow;
    private readonly List<Vector2> _planEdgeStartVertices = [];
    private int _planMenuVertex;
    private const string PlanCornerMenuId = "##planCornerMenu";

    // The plan whose corner or edge dot is under the cursor this frame: the handle takes the click before the card does.
    private Guid _hoveredPlanHandlePlanId;
    private int _hoveredPlanCorner;
    private int _planMenuSegment;
    private bool _planEdgeMoved;
    private const string PlanEdgeMenuId = "##planEdgeMenu";
    private const string PlanLengthPopupId = "##planEdgeLength";
    private int _planLengthSegment;
    private float _planLengthValue;
    private bool _planLengthPopupPending;

    // Marquee over the cards: candidates are collected as the cards draw (cleared per frame), and the fence
    // resolves containers against the setup set before it runs.
    /// <summary>
    /// Where a surface sits on an output's canvas (TL, TR, BR, BL in canvas 0..1): its first mapping, else a patch
    /// carrying its name, in the patch's turned corner order — a venue's pixel map names its areas after the walls.
    /// </summary>
    private static bool TryFindCanvasQuad(Setup setup, Surface surface, Vector2[] quad, out Guid outputId)
    {
        if (surface.OutputMappings.Count > 0 && surface.OutputMappings[0].Quad.Length >= 4)
        {
            Array.Copy(surface.OutputMappings[0].Quad, quad, 4);
            outputId = surface.OutputMappings[0].OutputId;
            return true;
        }

        outputId = Guid.Empty;
        if (string.IsNullOrEmpty(surface.Name))
            return false;

        for (var o = 0; o < setup.Outputs.Count; o++)
        {
            var patches = setup.Outputs[o].Patches;
            for (var p = 0; p < patches.Count; p++)
            {
                if (patches[p].Quad.Length < 4 || !string.Equals(patches[p].Name, surface.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                patches[p].CopyTurnedCorners(quad);
                outputId = setup.Outputs[o].Id;
                return true;
            }
        }

        return false;
    }

    private readonly Vector2[] _canvasFedQuad = new Vector2[4];
    private readonly SelectionFence _boardFence = new();
    private readonly List<(SetupEntityKinds Kind, Guid Id, ImRect Rect)> _boardFenceCandidates = [];
    private Setup? _boardSetupForFence;

    // Hierarchy picking scratch: one level's regions under the cursor, and the selection step down a hierarchy.
    private readonly List<Surface> _pickHits = [];
    private readonly List<Guid> _hierarchyStep = [];

    // Card metadata strings by entity id, rebuilt per structure change.
    private readonly Dictionary<Guid, string> _boardMeta = new();
    private int _boardMetaVersion = -1;
}
