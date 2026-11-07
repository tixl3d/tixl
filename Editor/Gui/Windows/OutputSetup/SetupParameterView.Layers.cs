#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Utils;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The output card's Layers list: everything that lands on this canvas, front first, as image editors list
/// layers. Drag a grip to reorder, pick a blend mode, set opacity and tint, or disconnect.
/// </summary>
internal static partial class SetupParameterView
{
    /** Draws the list, or nothing for an output nothing lands on; returns whether it drew. */
    private static bool DrawLayerList(Setup setup, OutputDefinition output)
    {
        OutputLayers.Collect(setup, output, _cardLayers);
        if (_cardLayers.Count == 0)
            return false;

        var scale = T3Ui.UiScaleFactor;
        var padding = PatchHeaderPadding * scale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText("Layers", Fonts.FontBold, UiColors.TextMuted);
        CustomComponents.TooltipForLastItem("What lands on this output, front first.",
                                            "Drag a grip to reorder. Each layer combines with the ones below it by its blend mode; "
                                            + "Multiply with a black-and-white layer masks what's under it.");
        FormInputs.AddVerticalSpace(2);

        ImGui.PushID("layers");
        var listWidth = ImGui.GetContentRegionAvail().X - InputArea.ValueEditRightMargin;
        var listScreenX = ImGui.GetCursorScreenPos().X;
        var rowHeight = FormInputs.ListRowHeight;
        var gripWidth = FormInputs.ListRowGripWidth;
        var blendWidth = LayerBlendColumnWidth * scale;
        var opacityWidth = LayerOpacityColumnWidth * scale;
        var nameWidth = MathF.Max(listWidth - gripWidth - blendWidth - opacityWidth - rowHeight * 2, 1);
        var drawList = ImGui.GetWindowDrawList();
        var selection = GlobalSelectionHandling.SetupEntities;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, MathF.Round(PatchRowGap * scale)));
        var rowsTop = MathF.Floor(ImGui.GetCursorScreenPos().Y);
        var rowStride = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        var count = _cardLayers.Count;
        var moveFromRow = -1;
        var moveToRow = -1;
        var disconnectIndex = -1;

        for (var row = 0; row < count; row++)
        {
            // The list shows the front first; the layers run back to front.
            var index = count - 1 - row;
            var layer = _cardLayers[index];
            ImGui.PushID(layer.Id.GetHashCode());
            ImGui.SetCursorScreenPos(new Vector2(listScreenX, rowsTop + row * rowStride));

            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowMax = rowMin + new Vector2(listWidth, rowHeight).Floor();

            // The output's implicit full-canvas patch is the output itself, nowhere else shown: its layer stands
            // for the content routed straight onto the output, and picks that content.
            var isDirectContent = layer.Patch != null && SetupRelations.IsImplicitPatch(output, layer.Patch)
                                  && TryGetPatchSource(setup, layer.Patch) is not null;
            var kind = layer.Patch == null ? SetupEntityKinds.Surface : isDirectContent ? SetupEntityKinds.ContentSource : SetupEntityKinds.Patch;
            var pickId = isDirectContent ? TryGetPatchSource(setup, layer.Patch!)!.SymbolChildId : layer.Id;
            var isCurrent = selection.IsSelected(kind, pickId);

            // "Which one is that?": a hovered row lights its frame on the canvas and its Flow Outliner item, as
            // hovering either of those lights the other.
            var isRowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) && ImGui.IsMouseHoveringRect(rowMin, rowMax);
            if (isRowHovered)
                FrameStats.RequestCrossHighlight(pickId);

            var isDragging = FormInputs.DrawRowDragGrip(row, count, rowHeight, ref moveFromRow, ref moveToRow);
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging, drawGrip: count > 1);
            if (isCurrent)
                drawList.AddRect(rowMin, rowMax, UiColors.Selection.Fade(0.6f), 4 * scale);

            // Kind icon and name; a click shows the layer's own card.
            ImGui.SameLine(0, 0);
            ImGui.InvisibleButton("##name", new Vector2(nameWidth, rowHeight));
            if (ImGui.IsItemClicked())
                selection.Select(kind, pickId);

            CustomComponents.TooltipForLastItem(kind switch
                                                    {
                                                        SetupEntityKinds.ContentSource => LayerContentTooltip,
                                                        SetupEntityKinds.Patch         => LayerPatchTooltip,
                                                        _                              => LayerSurfaceTooltip,
                                                    });
            var nameMin = ImGui.GetItemRectMin();
            var iconSize = Icons.FontSize;
            Icons.DrawIconAtScreenPosition(SetupEntityKindInfo.Of(kind).Icon,
                                           new Vector2(nameMin.X, nameMin.Y + (rowHeight - iconSize) * 0.5f).Floor(), drawList,
                                           UiColors.TextMuted);
            var isRendered = layer.Surface?.IsRendered ?? true;
            var nameFont = isCurrent ? Fonts.FontBold : Fonts.FontNormal;
            drawList.PushClipRect(nameMin, ImGui.GetItemRectMax(), true);
            drawList.AddText(nameFont, nameFont.FontSize,
                             new Vector2(nameMin.X + iconSize + 4 * scale, nameMin.Y + (rowHeight - nameFont.FontSize) * 0.5f).Floor(),
                             isRendered ? isCurrent ? UiColors.Text : UiColors.TextMuted : UiColors.TextDisabled,
                             LayerLabel(setup, output, layer));
            drawList.PopClipRect();

            // Blend mode.
            ImGui.SameLine(0, 0);
            ImGui.SetNextItemWidth(blendWidth);
            var blendMode = layer.BlendMode;
            if (ImGui.BeginCombo("##blend", _blendModeNames[(int)blendMode], ImGuiComboFlags.HeightLarge))
            {
                for (var m = 0; m < _blendModeNames.Length; m++)
                {
                    if (!ImGui.Selectable(_blendModeNames[m], m == (int)blendMode))
                        continue;

                    var chosen = (LayerBlendModes)m;
                    SetupUndo.RunUndoable("Change layer blend mode", setup, () => SetLayerBlendMode(layer, chosen));
                }

                ImGui.EndCombo();
            }

            CustomComponents.TooltipForLastItem("Blend mode", LayerBlendTooltip);

            // Opacity: the tint's alpha, as a percentage.
            ImGui.SameLine(0, 0);
            ImGui.SetNextItemWidth(opacityWidth);
            var color = layer.Color;
            var opacityPercent = color.W * 100f;
            ImGui.DragFloat("##opacity", ref opacityPercent, 0.5f, 0f, 100f, "%.0f%%", ImGuiSliderFlags.AlwaysClamp);
            var opacityState = EditStateOfLastItem();
            BeginFieldUndo(setup, opacityState);
            if ((opacityState & InputEditStateFlags.Modified) != 0)
                SetLayerColor(layer, color with { W = Math.Clamp(opacityPercent / 100f, 0f, 1f) });

            CommitFieldUndo(setup, "Change layer opacity", opacityState);
            CustomComponents.TooltipForLastItem("Opacity");

            // Tint, alpha included.
            ImGui.SameLine(0, 0);
            var swatchInset = 3 * scale;
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X + swatchInset, rowMin.Y + swatchInset));
            var tint = layer.Color;
            var tintState = ColorEditButton.Draw(ref tint, new Vector2(rowHeight - swatchInset * 2));
            BeginFieldUndo(setup, tintState);
            if ((tintState & InputEditStateFlags.Modified) != 0)
                SetLayerColor(layer, tint);

            CommitFieldUndo(setup, "Change layer tint", tintState);
            CustomComponents.TooltipForLastItem("Tint", "Multiplies the layer's picture. Its alpha is the opacity.");

            // Disconnect.
            ImGui.SameLine(0, 0);
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - rowHeight, rowMin.Y));
            if (CustomComponents.IconButton(Icon.Close, new Vector2(rowHeight)))
                disconnectIndex = index;

            CustomComponents.TooltipForLastItem(kind switch
                                                    {
                                                        SetupEntityKinds.ContentSource => "Disconnect this content from the output",
                                                        SetupEntityKinds.Patch         => "Remove this patch",
                                                        _                              => "Disconnect this surface from the output",
                                                    },
                                                kind == SetupEntityKinds.Surface ? "Its alignment is kept, and comes back when it is connected again." : null);
            ImGui.PopID();
        }

        ImGui.SetCursorScreenPos(new Vector2(listScreenX, rowsTop + count * rowStride));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopStyleVar();
        ImGui.PopID();
        FormInputs.AddVerticalSpace(4);

        // Rows run front first, so a row's move is the opposite move in the back-to-front order. One undo step
        // for the whole drag.
        if (moveFromRow >= 0)
        {
            _layerReorderOldJson ??= setup.ToJsonString();
            OutputLayers.Move(output, _cardLayers, count - 1 - moveFromRow, count - 1 - moveToRow);
            OutputSetupHandling.NotifyLiveEdit();
        }

        if (_layerReorderOldJson != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            SetupUndo.CommitGesture(setup, "Reorder layers", _layerReorderOldJson);
            _layerReorderOldJson = null;
        }

        if (disconnectIndex >= 0)
            DisconnectLayer(setup, output, _cardLayers[disconnectIndex]);

        return true;
    }

    private static string LayerLabel(Setup setup, OutputDefinition output, OutputLayers.Layer layer)
    {
        if (layer.Surface != null)
            return layer.Surface.Name;

        // A patch shows content; the whole of it reads as the content's own name, as in the outliner.
        var patch = layer.Patch!;
        if (setup.FindSlice(patch.SliceId) is not { } slice)
            return SetupLabels.PatchLabel(output, patch);

        return SetupRelations.IsImplicitSlice(setup, slice) && setup.FindSource(slice.SourceId) is { } source
                   ? source.Name
                   : SetupLabels.SliceLabel(setup, slice);
    }

    /** The content a patch shows whole, through its source's implicit full-frame slice; null for a cut slice. */
    private static ContentSource? TryGetPatchSource(Setup setup, OutputDefinition.Patch patch)
    {
        return setup.FindSlice(patch.SliceId) is { } slice && SetupRelations.IsImplicitSlice(setup, slice)
                   ? setup.FindSource(slice.SourceId)
                   : null;
    }

    private static void SetLayerBlendMode(OutputLayers.Layer layer, LayerBlendModes mode)
    {
        if (layer.Patch != null)
            layer.Patch.BlendMode = mode;
        else if (layer.Mapping != null)
            layer.Mapping.BlendMode = mode;
    }

    private static void SetLayerColor(OutputLayers.Layer layer, Vector4 color)
    {
        if (layer.Patch != null)
            layer.Patch.LayerColor = color;
        else if (layer.Mapping != null)
            layer.Mapping.LayerColor = color;
    }

    /** A patch is removed; a surface's mapping is parked, so its alignment comes back with the connection. */
    private static void DisconnectLayer(Setup setup, OutputDefinition output, OutputLayers.Layer layer)
    {
        if (layer.Patch != null)
        {
            SetupActions.DeleteEntity(setup, SetupEntityKinds.Patch, layer.Patch.Id);
            return;
        }

        var surface = layer.Surface!;
        var outputId = output.Id;
        SetupUndo.RunUndoable("Disconnect surface", setup, () => SetupActions.ParkMappings(surface, m => m.OutputId == outputId));
    }

    /** Swaps two layers in an ordered output, so reordering patches in their own list keeps their draw order. */
    private static void SwapInLayerOrder(OutputDefinition output, Guid a, Guid b)
    {
        var order = output.LayerOrder;
        var indexA = order.IndexOf(a);
        var indexB = order.IndexOf(b);
        if (indexA >= 0 && indexB >= 0)
            (order[indexA], order[indexB]) = (order[indexB], order[indexA]);
    }

    /** The edit state of the widget just drawn, for widgets that don't report one. */
    private static InputEditStateFlags EditStateOfLastItem()
    {
        var state = InputEditStateFlags.Nothing;
        if (ImGui.IsItemActivated())
            state |= InputEditStateFlags.Started;

        if (ImGui.IsItemEdited())
            state |= InputEditStateFlags.Modified;

        if (ImGui.IsItemDeactivatedAfterEdit())
            state |= InputEditStateFlags.Finished;

        return state;
    }

    private const float LayerBlendColumnWidth = 82f;
    private const float LayerOpacityColumnWidth = 48f;

    private const string LayerContentTooltip = "Content shown on the whole canvas. Click for its settings.";
    private const string LayerPatchTooltip = "A patch: content placed on part of the canvas, in pixels. Click for its settings.";
    private const string LayerSurfaceTooltip = "A surface mapped onto this output, with the regions riding its pin. Click for its settings.";

    private const string LayerBlendTooltip =
        "Normal covers what's below by its opacity. Add adds its light. Multiply darkens: white leaves what's below unchanged, black blacks it out. "
        + "Screen brightens without clipping as hard as Add.";

    private static readonly string[] _blendModeNames = Enum.GetNames<LayerBlendModes>();
    private static readonly List<OutputLayers.Layer> _cardLayers = [];
    private static string? _layerReorderOldJson; // the setup before a layer drag started, until the button is released
}
