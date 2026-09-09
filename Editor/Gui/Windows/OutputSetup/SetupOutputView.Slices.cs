#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Slice editing for <see cref="SetupOutputView"/>: the flat source/atlas canvas where a send's slices are
/// laid out, and the axis-aligned rect editor (edges crop, corners scale with the aspect held, the label
/// moves it, everything snapping to the source's own borders). No perspective is involved — which is exactly
/// why slices are edited here on the flat source rather than warped onto the wall.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>
    /// The source seen flat, with every slice cut from it. Reached by selecting a CONTENT row: a slice belongs
    /// to the send, not to a surface, so this is where an atlas gets laid out — all the sends sharing this
    /// texture at once, so overlaps and gaps are visible. No perspective is involved, which is exactly why
    /// slices are arranged here rather than on the wall.
    /// </summary>
    public void DrawSourceCanvas(Guid contentChildId, SetupEntitySelection? selection = null, Guid selectedSliceId = default)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        var source = setup.FindSourceByChildId(contentChildId);
        OpenedReferenceImageId = Guid.Empty;
        var title = SetupActions.TryGetContentName(contentChildId) ?? "Content";
        if (!DeferHeader(HeaderKinds.Return, title: title))
            DrawBoardReturnHeader(title);

        var canvasTop = ImGui.GetCursorScreenPos();
        _boardCanvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        // Clip to the region below the toolbar — the canvas draws to the window list and would spill up over it.
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        // The source's space is its card: the texture lands exactly where the card's thumbnail was.
        SeedBoardPlacements(setup);
        Texture2D? content = null;
        var hasContent = source != null && OutputManager.TryGetSourceContent(contentChildId, out _, out content) && content is { IsDisposed: false };
        EnterSpace(setup, SetupEntitySelection.EntityKind.ContentSource, contentChildId, hasContent);
        DrawBoardLayer(setup, machineConfig, selection);

        if (!hasContent || _spaceBlend <= 0.001f)
        {
            if (!hasContent)
                CustomComponents.EmptyWindowMessage("No content yet — connect a texture to this\nSendToOutput to lay out its slices.");

            ResolvePicking(setup, selection);
            dl.PopClipRect();
            return;
        }

        var textureSize = new Vector2(Math.Max(1, content!.Description.Width), Math.Max(1, content.Description.Height));
        FitToArea(textureSize, EditMode.Board, contentChildId);

        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(textureSize);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));

        var srv = SrvManager.GetSrvForTexture(content);
        if (srv is { IsDisposed: false })
            dl.AddImage(srv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));

        // A grab that never turned into a drag (released before the editor picked it up) must not linger.
        if (_sliceLabelGrabPending && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _sliceLabelGrabPending = false;

        // Every slice cut from this source. The primary one is editable; the other selected ones read as
        // selected (multi-select), and the rest are context you can click.
        Slice? selected = null;
        foreach (var slice in setup.Slices)
        {
            if (slice.SourceId != source!.Id)
                continue;

            if (slice.Id == selectedSliceId)
            {
                selected = slice;
                continue;
            }

            // The implicit full-frame slice is the texture itself — nothing to draw over it.
            if (SetupRelations.IsImplicitSlice(setup, slice))
                continue;

            var rect = slice.UvRect;
            var sliceMin = _projection.CanvasToScreen(new Vector2(rect.X, rect.Y) * textureSize);
            var sliceMax = _projection.CanvasToScreen(new Vector2(rect.Z, rect.W) * textureSize);

            // Hovered from the sidebar — the slice's own row or any surface/patch showing it — pulses the rect,
            // so "which slice is that wall showing?" is answered on the texture. Hovering the rect returns the favour.
            var slicePulse = MathF.Max(FrameStats.GetPulse(slice.Id), ConsumerPulse(setup, slice.Id));
            if (ImGui.IsWindowHovered() && IsMouseInRect(sliceMin, sliceMax))
                PulseConsumers(setup, slice.Id);

            var sliceHue = SetupColors.ForKind(SetupEntitySelection.EntityKind.Slice);
            if (slicePulse > 0.001f)
                dl.AddRectFilled(sliceMin, sliceMax, sliceHue.Fade(slicePulse * 0.2f));

            // A multi-selected (non-primary) slice reads selected, like the primary's frame — only editing
            // stays with the primary.
            var isSelected = selection != null && selection.IsSelected(SetupEntitySelection.EntityKind.Slice, slice.Id);
            dl.AddRect(sliceMin, sliceMax,
                       isSelected ? sliceHue : PulseColor(sliceHue.Fade(0.6f), slicePulse),
                       0, ImDrawFlags.None, (isSelected ? 2f : 1f) * T3Ui.UiScaleFactor);

            Span<Vector2> corners =
                [sliceMin, new Vector2(sliceMax.X, sliceMin.Y), sliceMax, new Vector2(sliceMin.X, sliceMax.Y)];
            var sliceName = SetupActions.SliceLabel(setup, slice);
            CornerPinHandles.DrawCenteredLabel(dl, corners, sliceName,
                                               isSelected ? UiColors.ForegroundFull : UiColors.Text.Fade(0.7f),
                                               isSelected ? sliceHue.Fade(0.6f) : UiColors.BackgroundFull.Fade(0.6f));
            DrawSliceConsumers(dl, setup, slice.Id, CornerPinHandles.GetCenteredLabelRect(corners, sliceName), 0.7f);
            _picker.AddTarget(SetupEntitySelection.EntityKind.Slice, slice.Id, sliceMin, sliceMax);

            // Grab-to-move without the select-first click: pressing a slice's label selects it and starts its
            // move in the same gesture — the editor takes over next frame, mouse still held.
            if (!_sliceLabelDragging && !_sliceLabelGrabPending
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
            {
                var (labelMin, labelMax) = CornerPinHandles.GetCenteredLabelRect(corners, sliceName);
                var mouse = ImGui.GetMousePos();
                if (mouse.X >= labelMin.X && mouse.X <= labelMax.X && mouse.Y >= labelMin.Y && mouse.Y <= labelMax.Y)
                {
                    SelectPicked(selection, SetupEntitySelection.EntityKind.Slice, slice.Id);
                    _sliceLabelGrabPending = true;
                }
            }
        }

        // No scrim here: on an atlas every slice matters equally. No fallback to the first slice either — with
        // the source selected (and no slice), nothing is framed.
        if (selected != null && _spaceBlend >= 1f)
            EditSlice(setup, dl, selected, selected.UvRect, Vector2.Zero, textureSize, Guid.Empty, dimOutside: false);

        if (_spaceBlend >= 1f)
            HandleSliceDraft(setup, selection, dl, source!, textureSize, min, max);

        ResolvePicking(setup, selection);
        dl.PopClipRect();
    }

    /// <summary>
    /// Dragging on empty source area cuts a new slice there — the atlas gesture, instead of adding a full-frame
    /// slice and shrinking it. Armed by a press that lands on the texture but on no slice; a press that never
    /// becomes a drag stays a click. The moving corner snaps like every other slice edit.
    /// </summary>
    private void HandleSliceDraft(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, ContentSource source,
                                  Vector2 textureSize, Vector2 sourceMin, Vector2 sourceMax)
    {
        var mouse = ImGui.GetMousePos();
        var drafting = _gesture.Is(GestureKinds.SliceDraft, source.Id);

        if (!drafting && _sliceDraftStart == null
            && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && !_gesture.IsLive
            && !_sliceLabelDragging && !_sliceLabelGrabPending
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && IsMouseInRect(sourceMin, sourceMax) && !IsMouseOverAnySlice(setup, source.Id, textureSize))
        {
            _sliceDraftStart = _projection.ScreenToCanvas(mouse) / textureSize;
        }

        if (_sliceDraftStart == null)
            return;

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !drafting)
        {
            // Released below the drag threshold: it was a click.
            _sliceDraftStart = null;
            return;
        }

        if (!drafting)
        {
            if (ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() <= UserSettings.Config.ClickThreshold)
                return;

            BeginGesture(setup, GestureKinds.SliceDraft, "Add slice", source.Id);
            drafting = true;
        }

        var start = _sliceDraftStart.Value;
        var current = _projection.ScreenToCanvas(mouse) / textureSize;
        if (!ImGui.GetIO().KeyShift)
        {
            CollectSliceSnapCandidates(setup, source.Id, Guid.Empty);
            var perPixel = (_projection.ScreenToCanvas(new Vector2(1, 0)) - _projection.ScreenToCanvas(Vector2.Zero)).X;
            var thresholdX = 7 * T3Ui.UiScaleFactor * perPixel / MathF.Max(textureSize.X, 0.0001f);
            var thresholdY = 7 * T3Ui.UiScaleFactor * perPixel / MathF.Max(textureSize.Y, 0.0001f);
            Span<float> x = [current.X];
            if (SurfaceGeometry.TrySnapOffset(_sliceSnapXs, x, thresholdX, out var offsetX, out var targetX))
            {
                current.X += offsetX;
                DrawSliceSnapGuide(dl, Vector2.Zero, textureSize, vertical: true, targetX);
            }

            Span<float> y = [current.Y];
            if (SurfaceGeometry.TrySnapOffset(_sliceSnapYs, y, thresholdY, out var offsetY, out var targetY))
            {
                current.Y += offsetY;
                DrawSliceSnapGuide(dl, Vector2.Zero, textureSize, vertical: false, targetY);
            }
        }

        current = Vector2.Clamp(current, Vector2.Zero, Vector2.One);
        var uvMin = Vector2.Min(start, current);
        var uvMax = Vector2.Max(start, current);

        var screenMin = _projection.CanvasToScreen(uvMin * textureSize);
        var screenMax = _projection.CanvasToScreen(uvMax * textureSize);
        var hue = SetupColors.ForKind(SetupEntitySelection.EntityKind.Slice);
        dl.AddRectFilled(screenMin, screenMax, hue.Fade(0.12f));
        dl.AddRect(screenMin, screenMax, hue, 0, ImDrawFlags.None, 1.5f * T3Ui.UiScaleFactor);

        // The cut's size in source pixels, so an atlas can be laid out to numbers.
        var px = (uvMax - uvMin) * textureSize;
        _sliceDraftLabel.Clear();
        _sliceDraftLabel.Append((int)MathF.Round(px.X)).Append('×').Append((int)MathF.Round(px.Y)).Append(" px");
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, screenMax + new Vector2(6, 4) * T3Ui.UiScaleFactor,
                   UiColors.Text.Fade(0.8f), _sliceDraftLabel.ToString());

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        _sliceDraftStart = null;
        if (uvMax.X - uvMin.X < MinSliceSize || uvMax.Y - uvMin.Y < MinSliceSize)
        {
            CancelGesture();
            return;
        }

        // Unnamed like every added slice: its label derives from the source, so a later op rename follows.
        var slice = new Slice { SourceId = source.Id, UvRect = new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y) };
        setup.Slices.Add(slice);
        EndGesture(setup);
        selection?.Select(SetupEntitySelection.EntityKind.Slice, slice.Id);
    }

    private bool IsMouseOverAnySlice(Setup setup, Guid sourceId, Vector2 textureSize)
    {
        foreach (var slice in setup.Slices)
        {
            if (slice.SourceId != sourceId || SetupRelations.IsImplicitSlice(setup, slice))
                continue;

            var rect = slice.UvRect;
            var min = _projection.CanvasToScreen(new Vector2(rect.X, rect.Y) * textureSize);
            var max = _projection.CanvasToScreen(new Vector2(rect.Z, rect.W) * textureSize);
            if (IsMouseInRect(min, max))
                return true;
        }

        return false;
    }

    private static bool IsMouseInRect(Vector2 min, Vector2 max)
    {
        var mouse = ImGui.GetMousePos();
        return mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
    }

    /// <summary>The strongest sidebar pulse among the surfaces and patches showing this slice.</summary>
    private static float ConsumerPulse(Setup setup, Guid sliceId)
    {
        var pulse = 0f;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                pulse = MathF.Max(pulse, FrameStats.GetPulse(surface.Id));
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    pulse = MathF.Max(pulse, FrameStats.GetPulse(patch.Id));
            }
        }

        return pulse;
    }

    /// <summary>Lights the rows of everything showing this slice — the answer to "where does this go?" on hover.</summary>
    private static void PulseConsumers(Setup setup, Guid sliceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                FrameStats.PulseItemWithId(surface.Id);
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    FrameStats.PulseItemWithId(patch.Id);
            }
        }
    }

    /// <summary>A small line under the slice's label naming what shows it ("→ Left wall, Patch 2"), or "unused".</summary>
    private void DrawSliceConsumers(ImDrawListPtr dl, Setup setup, Guid sliceId, (Vector2 Min, Vector2 Max) labelRect, float alpha)
    {
        var text = ConsumerLabel(setup, sliceId);
        var scale = T3Ui.UiScaleFactor;
        var pos = new Vector2(labelRect.Min.X, labelRect.Max.Y + 2 * scale);
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, pos, UiColors.TextMuted.Fade(alpha), text);
    }

    /// <summary>Consumer names per slice, rebuilt only when the setup's structure changes — labels are per frame otherwise.</summary>
    private string ConsumerLabel(Setup setup, Guid sliceId)
    {
        if (_consumerLabelsVersion != OutputSetupHandling.StructureVersion)
        {
            _consumerLabels.Clear();
            _consumerLabelsVersion = OutputSetupHandling.StructureVersion;
        }

        if (_consumerLabels.TryGetValue(sliceId, out var cached))
            return cached;

        _sliceDraftLabel.Clear();
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId != sliceId)
                continue;

            _sliceDraftLabel.Append(_sliceDraftLabel.Length == 0 ? "→ " : ", ").Append(surface.Name);
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId != sliceId)
                    continue;

                _sliceDraftLabel.Append(_sliceDraftLabel.Length == 0 ? "→ " : ", ").Append(SetupActions.PatchLabel(output, patch));
            }
        }

        var label = _sliceDraftLabel.Length == 0 ? "unused" : _sliceDraftLabel.ToString();
        _consumerLabels[sliceId] = label;
        return label;
    }

    /// <summary>
    /// At the Content end the view has zoomed out onto the whole source, so the slice becomes a plain
    /// axis-aligned rect on it — draggable by its edges, moveable by its middle, snapping to the source's own
    /// bounds. Edits go straight to the send's SourceRect. No perspective is involved here: the source is
    /// shown flat, which is exactly why slices are edited at this end and not on the wall.
    /// </summary>
    /// <summary>
    /// The slice as an editable rect on its source, in plain canvas space: edges reshape, corners scale with
    /// the aspect held, the middle moves it, everything snapping to the source's borders and midlines. Shared
    /// by the surface's Content view and the source view, which differ only in framing.
    /// </summary>
    private void EditSlice(Setup setup, ImDrawListPtr dl, Slice slice, Vector4 uv,
                           Vector2 sourceOrigin, Vector2 sourceSize, Guid targetId, bool dimOutside)
    {
        if (sourceSize.X <= 0.0001f || sourceSize.Y <= 0.0001f)
            return;

        var min = _projection.CanvasToScreen(sourceOrigin + new Vector2(uv.X, uv.Y) * sourceSize);
        var max = _projection.CanvasToScreen(sourceOrigin + new Vector2(uv.Z, uv.W) * sourceSize);

        // Dim the source outside the slice — this view exists to judge one surface's crop, so everything the
        // surface won't show recedes. (The atlas view, where every slice matters equally, won't do this.)
        if (dimOutside)
        {
            var sourceMin = _projection.CanvasToScreen(sourceOrigin);
            var sourceMax = _projection.CanvasToScreen(sourceOrigin + sourceSize);
            CanvasDraw.ScrimOutside(dl, sourceMin, sourceMax, min, max, UiColors.BackgroundFull.Fade(0.3f));
        }

        // No tint inside: the crop now reads from the dimmed surround, and colouring the content would work
        // against judging it.
        dl.AddRect(min, max, SetupColors.ForKind(SetupEntitySelection.EntityKind.Slice), 0, ImDrawFlags.None, 2 * T3Ui.UiScaleFactor);


        // Canvas space, like every other handle — the projection subtracts the framing origin itself.
        var sliceMin = sourceOrigin + new Vector2(uv.X, uv.Y) * sourceSize;
        var sliceMax = sourceOrigin + new Vector2(uv.Z, uv.W) * sourceSize;
        _sliceQuadBuffer[0] = sliceMin;
        _sliceQuadBuffer[1] = new Vector2(sliceMax.X, sliceMin.Y);
        _sliceQuadBuffer[2] = sliceMax;
        _sliceQuadBuffer[3] = new Vector2(sliceMin.X, sliceMax.Y);

        ImGui.PushID("slice");
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true, hue: SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface));
        var edgePhase = CornerPinHandles.DrawEdgeHandles(_sliceQuadBuffer, _projection, style, out var edge, out var edgePos);

        // The slice's name label doubles as its move handle, the same as a surface — so there's no separate
        // centre dot, and the selected slice reads its name on the canvas like everything else.
        Span<Vector2> labelCorners = stackalloc Vector2[4];
        labelCorners[0] = min;
        labelCorners[1] = new Vector2(max.X, min.Y);
        labelCorners[2] = max;
        labelCorners[3] = new Vector2(min.X, max.Y);
        var sliceName = SetupActions.SliceLabel(setup, slice);
        DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Slice, labelCorners, slice.Id, sliceName, isSelected: true, emphasis: 1f);
        DrawSliceConsumers(dl, setup, slice.Id, CornerPinHandles.GetCenteredLabelRect(labelCorners, sliceName), 0.9f);
        if (ImGui.IsWindowHovered() && IsMouseInRect(min, max))
            PulseConsumers(setup, slice.Id);

        // Move is detected by hand rather than an InvisibleButton, so the label stays a plain draw and the
        // frame-label pick pass (which selects and opens the context menu) isn't blocked by a hovered item.
        var (labelMin, labelMax) = CornerPinHandles.GetCenteredLabelRect(labelCorners, sliceName);
        var mousePos = ImGui.GetMousePos();
        var overLabel = mousePos.X >= labelMin.X && mousePos.X <= labelMax.X
                        && mousePos.Y >= labelMin.Y && mousePos.Y <= labelMax.Y;
        if (overLabel && !ImGui.IsAnyItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var movePhase = CanvasPointHandle.DragPhase.None;
        if (_sliceLabelGrabPending)
        {
            // The grab from the source-canvas label (select + move in one gesture) lands here one frame
            // later, once this slice is the edited one.
            _sliceLabelGrabPending = false;
            _sliceLabelDragging = true;
            movePhase = CanvasPointHandle.DragPhase.Started;
        }
        else if (_sliceLabelDragging)
        {
            movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left) ? CanvasPointHandle.DragPhase.Dragging
                                                                 : CanvasPointHandle.DragPhase.Completed;
            if (movePhase == CanvasPointHandle.DragPhase.Completed)
                _sliceLabelDragging = false;
        }
        else if (overLabel && !ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _sliceLabelDragging = true;
            movePhase = CanvasPointHandle.DragPhase.Started;
        }

        var centreInCanvas = _projection.ScreenToCanvas(mousePos);
        ImGui.PopID();

        // One screen pixel in UV — per axis, since UV is normalized: on a non-square source the same 7px is
        // a different UV distance horizontally than vertically.
        var perPixel = (_projection.ScreenToCanvas(new Vector2(1, 0)) - _projection.ScreenToCanvas(Vector2.Zero)).X;
        var thresholdX = 7 * T3Ui.UiScaleFactor * perPixel / MathF.Max(sourceSize.X, 0.0001f);
        var thresholdY = 7 * T3Ui.UiScaleFactor * perPixel / MathF.Max(sourceSize.Y, 0.0001f);
        var snapping = !ImGui.GetIO().KeyShift;

        if (edge >= 0 && edgePhase is not CanvasPointHandle.DragPhase.None)
        {
            // Capture the pre-drag rect before this frame's apply; the commit runs after it (below).
            if (edgePhase == CanvasPointHandle.DragPhase.Started)
                RunSliceDrag(edgePhase, setup, slice);

            var inSource = (edgePos - sourceOrigin) / sourceSize;
            var next = uv;
            switch (edge)
            {
                case 0: next.Y = MathF.Min(inSource.Y, uv.W - MinSliceSize); break;
                case 1: next.Z = MathF.Max(inSource.X, uv.X + MinSliceSize); break;
                case 2: next.W = MathF.Max(inSource.Y, uv.Y + MinSliceSize); break;
                default: next.X = MathF.Min(inSource.X, uv.Z - MinSliceSize); break;
            }

            // Snap to the source's bounds/midlines and the sibling slices' edges and centres — the same
            // vocabulary a surface edge snaps to (parent + siblings), so the two feel alike.
            if (snapping)
            {
                CollectSliceSnapCandidates(setup, slice.SourceId, slice.Id);
                var movesX = edge is 1 or 3;
                Span<float> anchor = [edge switch { 0 => next.Y, 1 => next.Z, 2 => next.W, _ => next.X }];
                if (SurfaceGeometry.TrySnapOffset(movesX ? _sliceSnapXs : _sliceSnapYs, anchor, movesX ? thresholdX : thresholdY,
                                                  out var snapOffset, out var snapTarget))
                {
                    switch (edge)
                    {
                        case 0: next.Y += snapOffset; break;
                        case 1: next.Z += snapOffset; break;
                        case 2: next.W += snapOffset; break;
                        default: next.X += snapOffset; break;
                    }

                    DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, movesX, snapTarget);
                }
            }

            ApplySliceRect(slice, new Vector4(Math.Clamp(next.X, 0, 1), Math.Clamp(next.Y, 0, 1),
                                           Math.Clamp(next.Z, 0, 1), Math.Clamp(next.W, 0, 1)));
            if (edgePhase != CanvasPointHandle.DragPhase.Started)
                RunSliceDrag(edgePhase, setup, slice);

            return;
        }

        // Corners resize about the opposite corner and hold the slice's aspect, so a slice cut to a surface's
        // shape keeps that shape while it's scaled.
        ImGui.PushID("sliceCorners");
        var cornerStyle = CanvasPointHandle.Style.Default(style.HandleColor);
        cornerStyle.OutlineColor = style.HandleOutlineColor;

        var cornerPhase = CanvasPointHandle.DragPhase.None;
        var draggedCorner = -1;
        var cornerPos = Vector2.Zero;
        for (var i = 0; i < 4; i++)
        {
            ImGui.PushID(i);
            var point = _sliceQuadBuffer[i];
            var phase = CanvasPointHandle.Draw(ref point, _projection, cornerStyle);
            if (phase is not CanvasPointHandle.DragPhase.None)
            {
                cornerPhase = phase;
                draggedCorner = i;
                cornerPos = point;
            }

            ImGui.PopID();
        }

        ImGui.PopID();

        if (draggedCorner >= 0 && cornerPhase is not CanvasPointHandle.DragPhase.None)
        {
            // Capture the pre-drag rect before this frame's apply; the commit runs after it (below).
            if (cornerPhase == CanvasPointHandle.DragPhase.Started)
                RunSliceDrag(cornerPhase, setup, slice);

            var dragged = (cornerPos - sourceOrigin) / sourceSize;
            var currentWidth = MathF.Max(uv.Z - uv.X, 0.0001f);
            var currentHeight = MathF.Max(uv.W - uv.Y, 0.0001f);

            // Corners are TL, TR, BR, BL — each scales away from the one diagonally across.
            var fixedCorner = draggedCorner switch
                                  {
                                      0 => new Vector2(uv.Z, uv.W),
                                      1 => new Vector2(uv.X, uv.W),
                                      2 => new Vector2(uv.X, uv.Y),
                                      _ => new Vector2(uv.Z, uv.Y),
                                  };

            var scale = MathF.Max(MathF.Abs(dragged.X - fixedCorner.X) / currentWidth,
                                  MathF.Abs(dragged.Y - fixedCorner.Y) / currentHeight);
            var width = MathF.Max(currentWidth * scale, MinSliceSize);
            var height = MathF.Max(currentHeight * scale, MinSliceSize);

            var moved = fixedCorner + new Vector2(draggedCorner is 1 or 2 ? width : -width,
                                                  draggedCorner is 2 or 3 ? height : -height);
            var cornerMin = Vector2.Min(fixedCorner, moved);
            var cornerMax = Vector2.Max(fixedCorner, moved);
            ApplySliceRect(slice, new Vector4(Math.Clamp(cornerMin.X, 0, 1), Math.Clamp(cornerMin.Y, 0, 1),
                                           Math.Clamp(cornerMax.X, 0, 1), Math.Clamp(cornerMax.Y, 0, 1)));
            if (cornerPhase != CanvasPointHandle.DragPhase.Started)
                RunSliceDrag(cornerPhase, setup, slice);

            return;
        }

        // The atlas (no target) reaches its slice menus through the frame-label pick pass instead, so only the
        // surface-content view (which can "match target aspect") opens the in-rect menu here.
        if (targetId != Guid.Empty)
            DrawSliceMenu(setup, targetId, slice, uv, min, max);

        var cursorUv = (centreInCanvas - sourceOrigin) / sourceSize;
        RunSliceDrag(movePhase, setup, slice);
        switch (movePhase)
        {
            case CanvasPointHandle.DragPhase.Started:
                // Recomputed from this snapshot each frame, so the move can't accumulate drift.
                _sliceMoveStart = (cursorUv, uv);
                break;

            case CanvasPointHandle.DragPhase.Dragging when _sliceMoveStart != null:
            {
                var (origin, startUv) = _sliceMoveStart.Value;
                var size = new Vector2(startUv.Z - startUv.X, startUv.W - startUv.Y);
                var delta = cursorUv - origin;

                // Same axis lock as a region move: directional, but capped at a constant screen budget so
                // it can't widen with drag distance.
                var lockX = false;
                var lockY = false;
                if (snapping)
                {
                    lockX = MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 4 && MathF.Abs(delta.Y) < thresholdY * 1.5f;
                    lockY = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 4 && MathF.Abs(delta.X) < thresholdX * 1.5f;
                    if (lockX)
                        delta.Y = 0;
                    else if (lockY)
                        delta.X = 0;
                }

                var sliceOrigin = new Vector2(startUv.X, startUv.Y) + delta;

                if (snapping)
                {
                    // Either edge, or the centre, may catch — whichever is closest wins per axis. Candidates
                    // are the source bounds plus the sibling slices, same as a surface snapping to siblings.
                    CollectSliceSnapCandidates(setup, slice.SourceId, slice.Id);
                    Span<float> xs = [sliceOrigin.X, sliceOrigin.X + size.X * 0.5f, sliceOrigin.X + size.X];
                    Span<float> ys = [sliceOrigin.Y, sliceOrigin.Y + size.Y * 0.5f, sliceOrigin.Y + size.Y];
                    if (SurfaceGeometry.TrySnapOffset(_sliceSnapXs, xs, thresholdX, out var offsetX, out var targetX))
                    {
                        sliceOrigin.X += offsetX;
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: true, targetX);
                    }

                    if (SurfaceGeometry.TrySnapOffset(_sliceSnapYs, ys, thresholdY, out var offsetY, out var targetY))
                    {
                        sliceOrigin.Y += offsetY;
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: false, targetY);
                    }

                    // The locked movement axis, drawn across the source so it reads as a guide (surface parity).
                    if (lockX)
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: false, sliceOrigin.Y + size.Y * 0.5f);
                    else if (lockY)
                        DrawSliceSnapGuide(dl, sourceOrigin, sourceSize, vertical: true, sliceOrigin.X + size.X * 0.5f);
                }

                sliceOrigin.X = Math.Clamp(sliceOrigin.X, 0, MathF.Max(1 - size.X, 0));
                sliceOrigin.Y = Math.Clamp(sliceOrigin.Y, 0, MathF.Max(1 - size.Y, 0));
                ApplySliceRect(slice, new Vector4(sliceOrigin.X, sliceOrigin.Y, sliceOrigin.X + size.X, sliceOrigin.Y + size.Y));
                break;
            }

            case CanvasPointHandle.DragPhase.Completed:
                _sliceMoveStart = null;
                break;
        }
    }

    /// <summary>
    /// Right-click inside a slice (on release, so it doesn't fight right-drag panning) for actions that are
    /// awkward by hand — chiefly cutting it to the shape of the surface it feeds, so the content lands
    /// undistorted.
    /// </summary>
    private void DrawSliceMenu(Setup setup, Guid targetId, Slice slice, Vector4 uv, Vector2 min, Vector2 max)
    {
        var mouse = ImGui.GetMousePos();
        var inside = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
        var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        if (inside && !wasDraggingRight && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && !ImGui.IsAnyItemHovered())
            ImGui.OpenPopup(SliceMenuId);

        if (!ImGui.BeginPopup(SliceMenuId))
            return;

        if (CustomComponents.DrawMenuItem(1, "Match target aspect"))
            MatchSliceToTargetAspect(setup, targetId, slice, uv);

        ImGui.EndPopup();
    }

    /// <summary>
    /// Reshapes the slice so its pixels have the same aspect as the surface it feeds — keeping its centre, and
    /// shrinking to fit if the new shape would run off the source.
    /// </summary>
    private void MatchSliceToTargetAspect(Setup setup, Guid targetId, Slice slice, Vector4 uv)
    {
        var surface = setup.FindSurface(targetId);
        if (surface == null || !OutputManager.TryGetSurfaceSlice(targetId, out _, out var sourceTexture, out _)
            || sourceTexture is not { IsDisposed: false })
            return;

        var surfaceAspect = surface.SizeInMeters.X / MathF.Max(surface.SizeInMeters.Y, 0.0001f);
        var textureWidth = MathF.Max(sourceTexture.Description.Width, 1);
        var textureHeight = MathF.Max(sourceTexture.Description.Height, 1);

        // Want (width·texW)/(height·texH) == surfaceAspect; keep the width and solve for the height.
        var width = MathF.Max(uv.Z - uv.X, MinSliceSize);
        var height = width * textureWidth / (textureHeight * MathF.Max(surfaceAspect, 0.0001f));

        var fit = MathF.Min(1f, MathF.Min(1f / MathF.Max(width, 0.0001f), 1f / MathF.Max(height, 0.0001f)));
        width *= fit;
        height *= fit;

        var centreX = (uv.X + uv.Z) * 0.5f;
        var centreY = (uv.Y + uv.W) * 0.5f;
        var minX = Math.Clamp(centreX - width * 0.5f, 0, MathF.Max(1 - width, 0));
        var minY = Math.Clamp(centreY - height * 0.5f, 0, MathF.Max(1 - height, 0));

        UndoRedoStack.AddAndExecute(new ChangeSliceRectCommand(slice.Id, slice.UvRect,
                                                               new Vector4(minX, minY, minX + width, minY + height)));
    }

    /// <summary>Live drag application — a plain field write. Persistence and undo happen once, on the drag's
    /// Completed phase (<see cref="RunSliceDrag"/>), not per mouse-move frame.</summary>
    private static void ApplySliceRect(Slice slice, Vector4 rect)
    {
        slice.UvRect = rect;
    }

    /// <summary>The one drag lifecycle for slice-rect edits (edge crop, corner scale, label move): a gesture like every other.</summary>
    private void RunSliceDrag(CanvasPointHandle.DragPhase phase, Setup setup, Slice slice)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                BeginGesture(setup, GestureKinds.Slice, "Edit slice", slice.Id);
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_gesture.Is(GestureKinds.Slice, slice.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>Snap targets for slice edits, in source UV: the source's bounds and midlines plus every
    /// sibling slice's edges and centres — the same vocabulary a surface edit snaps to (parent + siblings).</summary>
    private static void CollectSliceSnapCandidates(Setup setup, Guid sourceId, Guid excludeSliceId)
    {
        _sliceSnapXs.Clear();
        _sliceSnapYs.Clear();
        _sliceSnapXs.Add(0);
        _sliceSnapXs.Add(0.5f);
        _sliceSnapXs.Add(1);
        _sliceSnapYs.Add(0);
        _sliceSnapYs.Add(0.5f);
        _sliceSnapYs.Add(1);

        foreach (var other in setup.Slices)
        {
            if (other.Id == excludeSliceId || other.SourceId != sourceId)
                continue;

            var rect = other.UvRect;
            _sliceSnapXs.Add(rect.X);
            _sliceSnapXs.Add((rect.X + rect.Z) * 0.5f);
            _sliceSnapXs.Add(rect.Z);
            _sliceSnapYs.Add(rect.Y);
            _sliceSnapYs.Add((rect.Y + rect.W) * 0.5f);
            _sliceSnapYs.Add(rect.W);
        }
    }

    /// <summary>A caught snap line across the source (extended past its bounds, matching the surface guides).</summary>
    private void DrawSliceSnapGuide(ImDrawListPtr dl, Vector2 sourceOrigin, Vector2 sourceSize, bool vertical, float uvCoordinate)
    {
        var from = vertical ? new Vector2(uvCoordinate, -1f) : new Vector2(-1f, uvCoordinate);
        var to = vertical ? new Vector2(uvCoordinate, 2f) : new Vector2(2f, uvCoordinate);
        var a = _projection.CanvasToScreen(sourceOrigin + from * sourceSize);
        var b = _projection.CanvasToScreen(sourceOrigin + to * sourceSize);
        dl.AddLine(a, b, UiColors.StatusAnimated.Fade(0.6f), 1 * T3Ui.UiScaleFactor);
    }

    // Slice-editing state (the Content-stage framing lives with the morph in the core partial, which writes it).
    private bool _sliceLabelDragging;
    private bool _sliceLabelGrabPending; // label pressed on a not-yet-selected slice; the editor starts the move next frame
    private const float MinSliceSize = 0.01f;
    private const string SliceMenuId = "##sliceMenu";
    private readonly Vector2[] _sliceQuadBuffer = new Vector2[4];
    private (Vector2 Origin, Vector4 Uv)? _sliceMoveStart;

    // Sends cutting from the same source — rebuilt per frame in the source view.
    private readonly List<(Guid ChildId, IOutputSink Sink, Vector4 SourceRect)> _sharingSinks = [];

    // The source's own borders and centre, in UV — what a slice snaps against.
    private static readonly List<float> _sliceSnapXs = [];
    private static readonly List<float> _sliceSnapYs = [];

    // Where a draw-to-cut press landed, in source UV; null while nothing is armed.
    private Vector2? _sliceDraftStart;
    private readonly System.Text.StringBuilder _sliceDraftLabel = new();
    private readonly Dictionary<Guid, string> _consumerLabels = [];
    private int _consumerLabelsVersion = -1;
}
