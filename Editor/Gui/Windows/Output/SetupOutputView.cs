#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Interactive editor for a selected output, in two modes: the Output canvas (corner-pin each surface's
/// quad into the output, over the live composite) and the Content canvas (drag each surface's source
/// slice over the incoming content texture). Both reuse <see cref="CornerPinHandles"/> and the
/// <see cref="ScalableCanvas"/> pan/zoom; drags go through undo commands and persist. One per output window.
/// </summary>
internal sealed class SetupOutputView
{
    private enum EditMode
    {
        Output,
        Content,
    }

    public SetupOutputView()
    {
        _canvas.FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion;
        _projection = new ScalableCanvasProjection(_canvas);
    }

    public void Draw(Guid outputId, Guid focusedSurfaceId = default)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var output = setup.Outputs.Find(o => o.Id == outputId);
        if (output == null)
            return;

        _focusedSurfaceId = focusedSurfaceId;

        DrawHeader(setup, output, outputId);

        _canvas.UpdateCanvas(out _);

        if (_editMode == EditMode.Content)
            DrawContentCanvas(setup, outputId);
        else
            DrawOutputCanvas(setup, output, outputId);
    }

    private void DrawOutputCanvas(Setup setup, OutputDefinition output, Guid outputId)
    {
        var canvasSize = new Vector2(Math.Max(1, output.CanvasResolution.Width),
                                     Math.Max(1, output.CanvasResolution.Height));
        FitToArea(canvasSize, EditMode.Output, outputId);

        var dl = ImGui.GetWindowDrawList();
        var frameMin = _projection.CanvasToScreen(Vector2.Zero);
        var frameMax = _projection.CanvasToScreen(canvasSize);
        dl.AddRectFilled(frameMin, frameMax, UiColors.BackgroundFull.Fade(0.4f));

        // Live composite behind the handles: what the output manager would send to this output.
        var composite = OutputManager.RenderOutput(outputId);
        var hasContent = false;
        if (composite is { IsDisposed: false })
        {
            var srv = SrvManager.GetSrvForTexture(composite);
            if (srv is { IsDisposed: false })
            {
                dl.AddImage(srv.NativePointer, frameMin, frameMax);
                hasContent = true;
            }
        }

        dl.AddRect(frameMin, frameMax, UiColors.ForegroundFull.Fade(0.25f));

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var mappingData = surface.OutputMappings.Find(m => m.OutputId == outputId);
            if (mappingData == null)
                continue;

            ImGui.PushID(surface.Id.GetHashCode());
            // The checker is only a stand-in; drop it once the real composite fills the surface.
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable: true);
            style.DrawChecker = !hasContent;
            if (surface.Id == _focusedSurfaceId)
                style.EdgeColor = UiColors.ForegroundFull;

            var phase = CornerPinHandles.Draw(mappingData.Quad, _projection, style, out _);
            HandleDrag(phase, surface.Id, outputId, mappingData.Quad, isSource: false);
            ImGui.PopID();
        }
    }

    private void DrawContentCanvas(Setup setup, Guid outputId)
    {
        var content = OutputManager.TryGetOutputContent(outputId);
        if (content is not { IsDisposed: false })
        {
            CustomComponents.EmptyWindowMessage("No content yet — connect a texture to a\nSendToOutput bound to this output.");
            return;
        }

        var texSize = new Vector2(Math.Max(1, content.Description.Width), Math.Max(1, content.Description.Height));
        FitToArea(texSize, EditMode.Content, outputId);

        var dl = ImGui.GetWindowDrawList();
        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(texSize);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));

        var srv = SrvManager.GetSrvForTexture(content);
        if (srv is { IsDisposed: false })
            dl.AddImage(srv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var mappingData = surface.OutputMappings.Find(m => m.OutputId == outputId);
            if (mappingData == null || mappingData.SourceQuad.Length < 4)
                continue;

            ImGui.PushID(surface.Id.GetHashCode());

            // The source quad is stored in UV [0..1]; edit it in the content's pixel space, then map back.
            var source = mappingData.SourceQuad;
            for (var c = 0; c < 4; c++)
                _sourcePixels[c] = source[c] * texSize;

            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable: true);
            style.DrawChecker = false;
            if (surface.Id == _focusedSurfaceId)
                style.EdgeColor = UiColors.ForegroundFull;

            var phase = CornerPinHandles.Draw(_sourcePixels, _projection, style, out _);

            if (phase == CanvasPointHandle.DragPhase.Dragging)
            {
                for (var c = 0; c < 4; c++)
                    source[c] = _sourcePixels[c] / texSize;
            }

            HandleDrag(phase, surface.Id, outputId, source, isSource: true);
            ImGui.PopID();
        }
    }

    private void DrawHeader(Setup setup, OutputDefinition output, Guid outputId)
    {
        CustomComponents.StylizedText($"{output.Name} · {output.CanvasResolution.Width}×{output.CanvasResolution.Height}",
                                      Fonts.FontSmall, UiColors.TextMuted);

        ImGui.SameLine();
        if (CustomComponents.StateButton("Output", ModeButtonState(EditMode.Output)))
            _editMode = EditMode.Output;

        ImGui.SameLine();
        if (CustomComponents.StateButton("Content", ModeButtonState(EditMode.Content)))
            _editMode = EditMode.Content;

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.OutputMappings.Exists(m => m.OutputId == outputId))
                continue;

            ImGui.SameLine();
            ImGui.PushID(surface.Id.GetHashCode());
            var label = string.IsNullOrEmpty(surface.Name) ? "untitled" : surface.Name;
            if (ImGui.SmallButton("+ " + label))
            {
                AddMapping(surface, output, outputId);
                OutputSetupHandling.SaveActive();
            }

            CustomComponents.TooltipForLastItem("Map this surface onto the output",
                                                "Drops a centered corner-pin quad you can then drag into place.");
            ImGui.PopID();
        }
    }

    private CustomComponents.ButtonStates ModeButtonState(EditMode mode)
    {
        return _editMode == mode ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default;
    }

    private void FitToArea(Vector2 size, EditMode mode, Guid outputId)
    {
        // Instant fit whenever the framed area changes (output, mode, or content size) — no jump-then-settle.
        var key = (outputId, mode, size);
        if (_fitKey == key)
            return;

        _canvas.FitAreaOnCanvas(ImRect.RectWithSize(Vector2.Zero, size));
        _canvas.SetScopeInstant(_canvas.GetTargetScope());
        _fitKey = key;
    }

    private static void AddMapping(Surface surface, OutputDefinition output, Guid outputId)
    {
        var canvasW = Math.Max(1, output.CanvasResolution.Width);
        var canvasH = Math.Max(1, output.CanvasResolution.Height);

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
        var quad = new[]
                       {
                           new Vector2(cx - w * 0.5f, cy - h * 0.5f), // top-left
                           new Vector2(cx + w * 0.5f, cy - h * 0.5f), // top-right
                           new Vector2(cx + w * 0.5f, cy + h * 0.5f), // bottom-right
                           new Vector2(cx - w * 0.5f, cy + h * 0.5f), // bottom-left
                       };

        surface.OutputMappings.Add(new Surface.OutputMapping { OutputId = outputId, Quad = quad });
    }

    private void HandleDrag(CanvasPointHandle.DragPhase phase, Guid surfaceId, Guid outputId, Vector2[] liveQuad, bool isSource)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                // The quad still holds its pre-drag value on the activation frame.
                _dragOldQuad = (Vector2[])liveQuad.Clone();
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_dragOldQuad != null)
                {
                    ICommand command = isSource
                                           ? new ChangeSourceQuadCommand(surfaceId, outputId, _dragOldQuad, liveQuad)
                                           : new ChangeOutputMappingQuadCommand(surfaceId, outputId, _dragOldQuad, liveQuad);
                    UndoRedoStack.Add(command); // value already applied live during the drag
                    OutputSetupHandling.SaveActive();
                    _dragOldQuad = null;
                }

                break;
        }
    }

    private readonly ScalableCanvas _canvas = new();
    private readonly ScalableCanvasProjection _projection;
    private EditMode _editMode = EditMode.Output;
    private Guid _focusedSurfaceId;
    private (Guid, EditMode, Vector2) _fitKey;

    // Scratch for editing the source quad in pixel space; only one output view draws at a time.
    private readonly Vector2[] _sourcePixels = new Vector2[4];

    // Pre-drag quad snapshot; only one corner drags at a time, so a single slot suffices.
    private Vector2[]? _dragOldQuad;
}
