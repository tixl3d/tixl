using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Editor.Gui.OpUis;
using T3.Editor.Gui.Graph;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    private void DrawNode(MagGraphItem item, ImDrawListPtr drawList, GraphUiContext context)
    {
        if (item.Variant == MagGraphItem.Variants.Placeholder || item.Instance == null)
            return;

        if (!IsRectVisible(item.Area))
            return;

        var idleFadeFactor = 1f;
        var idleFactor = 0f;
        if (item.Variant == MagGraphItem.Variants.Operator && item.Instance != null)
        {
            var framesSinceLastUpdate = 100;
            for (var index = 0; index < item.Instance.Outputs.Count; index++)
            {
                var output = item.Instance.Outputs[index];
                framesSinceLastUpdate = Math.Min(framesSinceLastUpdate, output.DirtyFlag.FramesSinceLastUpdate);
            }

            idleFadeFactor = MathUtils.RemapAndClamp(framesSinceLastUpdate, 0f, 60f, 1f, 0.6f);
            idleFactor = MathUtils.RemapAndClamp(framesSinceLastUpdate, 0f, 60f, 0f, 1f);
        }

        var smallFontScaleFactor = CanvasScale.Clamp(0.5f, 2);
        
        var typeUiProperties = TypeUiRegistry.GetPropertiesForType(item.PrimaryType);

        var typeColor = typeUiProperties.Color.Fade(_context.GraphOpacity);
        var labelColor = ColorVariations.OperatorLabel.Apply(typeColor);

        var pMin = TransformPosition(item.DampedPosOnCanvas);
        var pMax = TransformPosition(item.DampedPosOnCanvas + item.Size);
        var pMinVisible = pMin;
        var pMaxVisible = pMax;

        // Adjust size when snapped
        var snappedBorders = Borders.None;
        {
            for (var index = 0; index < 1 && index < item.InputLines.Length; index++)
            {
                ref var il = ref item.InputLines[index];
                var c = il.ConnectionIn;
                if (c != null)
                {
                    if (c.IsSnapped)
                    {
                        switch (c.Style)
                        {
                            case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical:
                                snappedBorders |= Borders.Up;
                                break;
                            case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal:
                            case MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal:
                                snappedBorders |= Borders.Left;
                                break;
                        }
                    }
                }
            }

            for (var index = 0; index < 1 && index < item.OutputLines.Length; index++)
            {
                ref var ol = ref item.OutputLines[index];
                foreach (var c in ol.ConnectionsOut)
                {
                    if (c.IsSnapped && c.SourceItem == item)
                    {
                        switch (c.Style)
                        {
                            case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical:
                                snappedBorders |= Borders.Down;
                                break;
                            case MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal:
                            case MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal:
                                snappedBorders |= Borders.Right;
                                break;
                        }
                    }
                }
            }

            // There is probably a better method than this...
            const int snapPadding = 2;
            if ((snappedBorders & Borders.Down) == 0) pMaxVisible.Y -= (int)(snapPadding * 2 * CanvasScale);
            if ((snappedBorders & Borders.Right) == 0) pMaxVisible.X -= (int)(snapPadding * CanvasScale);
        }

        // Background and Outline
        var borders = (int)snappedBorders % 16;
        var imDrawFlags = _borderRoundings[borders];

        drawList.AddRectFilled(pMinVisible,
                               pMaxVisible,
                               Color.Mix(
                                         ColorVariations.OperatorBackground.Apply(typeColor),
                                         ColorVariations.OperatorBackgroundIdle.Apply(typeColor),
                                         idleFactor), CanvasScale < 0.5f ? 0 : 5 * CanvasScale,
                               imDrawFlags);

        // Snapped borders
        if ((snappedBorders & Borders.Down) != 0)
        {
            drawList.AddRectFilled(new Vector2(pMinVisible.X, pMaxVisible.Y),
                                   pMaxVisible - new Vector2(0, 2),
                                   ColorVariations.OperatorOutline.Apply(typeColor));
        }

        if ((snappedBorders & Borders.Right) != 0)
        {
            drawList.AddRectFilled(new Vector2(pMaxVisible.X - 2, pMinVisible.Y),
                                   pMaxVisible,
                                   ColorVariations.OperatorOutline.Apply(typeColor));
        }

        var isSelected = _context.Selector.IsSelected(item);
        if (isSelected)
        {
            drawList.AddRect(pMinVisible, pMaxVisible, UiColors.ForegroundFull.Fade(_context.GraphOpacity),
                             CanvasScale < 0.5 ? 0 : 5 * CanvasScale,
                             imDrawFlags);
        }
        
        var isHighlighted = item.Variant == MagGraphItem.Variants.Operator && 
                            FrameStats.IsIdHovered(item.Instance.SymbolChildId) && !IsHovered;
        if (isHighlighted)
        {
            drawList.AddRect(pMinVisible, pMaxVisible, UiColors.ForegroundFull.Fade(Blink),
                             CanvasScale < 0.5 ? 0 : 5 * CanvasScale,
                             imDrawFlags);
        }


        OpUi.CustomUiResult customUiResult = OpUi.CustomUiResult.None;
        if (Scale.X > 0.3f)
        {
            // Custom Ui
            if (item.Variant == MagGraphItem.Variants.Operator)
            {
                var additionalRightPadding = item.Instance.Outputs.Count> 1 ? 6 * CanvasScale:0; 
                customUiResult = item.Instance.DrawCustomUi(drawList, 
                                                            new ImRect(pMinVisible + Vector2.One, 
                                                                       pMaxVisible - Vector2.One - new Vector2(additionalRightPadding,0)), this, ref item.OpUiBinding);
                if ((customUiResult & OpUi.CustomUiResult.IsActive) != 0)
                {
                    context.ItemWithActiveCustomUi = item;
                }
            }
        }

        // ImGUI element for selection
        ImGui.SetCursorScreenPos(pMin);
        ImGui.PushID(item.Id.GetHashCode());
        ImGui.InvisibleButton(string.Empty, pMax - pMin);
        var isItemHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup
                                                | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        if (_context.StateMachine.CurrentState == GraphStates.Default
            && isItemHovered
            && (customUiResult & OpUi.CustomUiResult.IsActive) == 0)
            _context.ActiveItem = item;

        // if ((customUiResult & OpUi.CustomUiResult.IsActive) != 0)
        // {
        //     //context.StateMachine.SetState(GraphStates.Default, context);
        // }
        
        {
            ParameterPopUp.HandleOpenParameterPopUp(item.ChildUi, item.Instance, customUiResult, new ImRect(pMinVisible, pMaxVisible));
        }

        ImGui.PopID();
        // Todo: We eventually need to handle right clicking to select and open context menu when dragging with right mouse button. 
        // var wasDraggingRight = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Length() > UserSettings.Config.ClickThreshold;
        // if (ImGui.IsMouseReleased(ImGuiMouseButton.Right)
        //     && !wasDraggingRight
        //     && ImGui.IsItemHovered()
        //     && !_nodeSelection.IsNodeSelected(item))
        // {
        //     item.Select(_nodeSelection);
        // }

        if ((customUiResult == OpUi.CustomUiResult.None 
             || (customUiResult&OpUi.CustomUiResult.AllowThumbnail) != 0
             ) && CanvasScale > 0.2f)
        {
            // Draw Texture thumbnail
            var hasPreview = TryDrawTexturePreview(item, pMinVisible, pMaxVisible, drawList, typeColor);

            if ((customUiResult & OpUi.CustomUiResult.AllowThumbnail) == 0)
            {

                // Label...
                var name = item.ReadableName;
                if (item.Variant == MagGraphItem.Variants.Output)
                {
                    var height = pMaxVisible.Y - pMinVisible.Y;
                    var width = pMaxVisible.X - pMinVisible.X;

                    _inputIndicatorPoints[0] = pMinVisible;
                    _inputIndicatorPoints[1] = pMinVisible + new Vector2(0.9f * width, 0);
                    _inputIndicatorPoints[2] = pMinVisible + new Vector2(0.9f * width + height / 4, height / 2);
                    _inputIndicatorPoints[3] = pMinVisible + new Vector2(0.9f * width, height);
                    _inputIndicatorPoints[4] = pMinVisible + new Vector2(0, height);

                    drawList.AddConvexPolyFilled(ref _inputIndicatorPoints[0], 5, ColorVariations.Highlight.Apply(typeColor).Fade(0.5f));
                }
                else if (item.Variant == MagGraphItem.Variants.Input)
                {
                    var t = pMaxVisible.Y - pMinVisible.Y;

                    var framesSinceLastUpdate = (item.OutputLines.Length > 0)
                                                    ? (float)(item.OutputLines[0].Output.DirtyFlag.FramesSinceLastUpdate)
                                                    : 1000;
                    var fade = framesSinceLastUpdate.RemapAndClamp(0, 120, 1f, 0.4f);
                    if (framesSinceLastUpdate == 0)
                        fade -= Blink * 0.2f;

                    _inputIndicatorPoints[0] = pMinVisible;
                    _inputIndicatorPoints[1] = pMinVisible + new Vector2(0.2f, 0) * t;
                    _inputIndicatorPoints[2] = pMinVisible + new Vector2(0.5f, 0.5f) * t;
                    _inputIndicatorPoints[3] = pMinVisible + new Vector2(0.2f, 1f) * t;
                    _inputIndicatorPoints[4] = pMinVisible + new Vector2(0.0f, 1f) * t;
                    drawList.AddConvexPolyFilled(ref _inputIndicatorPoints[0], 5, ColorVariations.Highlight.Apply(typeColor).Fade(fade));
                    name = "   " + name;
                }

                ImGui.PushFont(Fonts.FontNormal);
                var labelSize = ImGui.CalcTextSize(name.AddSpacesForImGuiOutput());
                ImGui.PopFont();

                var paddingForPreview = hasPreview ? MagGraphItem.LineHeight + 15 : 0;
                var downScale =  MathF.Min(1f,  T3Ui.UiScaleFactor * (MagGraphItem.Width - paddingForPreview) * 0.9f / labelSize.X);

                var fontSize = Fonts.FontNormal.FontSize * (downScale / T3Ui.UiScaleFactor) * CanvasScale.Clamp(0.1f, 2f);
                
                var visibleLineHeight = Math.Min((pMaxVisible.Y - pMinVisible.Y), MagGraphItem.LineHeight * CanvasScale);
                var yCenter = pMin.Y + visibleLineHeight / 2 - fontSize / 2;
                var labelPos = new Vector2(pMin.X + 8 * CanvasScale, yCenter);

                labelPos = new Vector2(MathF.Round(labelPos.X), MathF.Round(labelPos.Y));
                drawList.AddText(Fonts.FontNormal,
                                 fontSize,
                                 labelPos,
                                 labelColor.Fade(CanvasScale.RemapAndClamp(0.3f, 0.7f, 0, 1)),
                                 name.AddSpacesForImGuiOutput());
            }
        }

        // Indicate hidden matching inputs...
        if (_context.DraggedPrimaryOutputType != null
            && item.Variant == MagGraphItem.Variants.Operator
            && _context.StateMachine.CurrentState == GraphStates.DragConnectionEnd
            && !context.ItemMovement.IsItemDragged(item)
            && _context.ActiveSourceItem != null)
        {
            Debug.Assert(item.Instance != null); // should be true to operator variant

            var hasMatchingTypes = false;
            for (var inputIndex = 0; inputIndex < item.Instance.Inputs.Count; inputIndex++)
            {
                var inputSlot = item.Instance.Inputs[inputIndex];

                if (inputSlot.ValueType == _context.DraggedPrimaryOutputType
                    && !inputSlot.HasInputConnections)
                {
                    hasMatchingTypes = true;
                    break;
                }
            }

            if (hasMatchingTypes && item != _context.ActiveItem)
            {
                var indicatorPos = new Vector2(pMinVisible.X + 5 * CanvasScale, pMaxVisible.Y - 5 * CanvasScale);

                var isSnappedToAnInput = InputSnapper.BestInputMatch.Item != null; 
                
                if (!isItemHovered || isSnappedToAnInput)
                {
                    drawList.AddCircle(indicatorPos, 3, UiColors.ForegroundFull.Fade(Blink));
                }
                else
                {
                    if (_hoveredForInputPickingId != item.Id)
                    {
                        _hoverPickingProgress = 0;
                        _hoveredForInputPickingId = item.Id;
                    }

                    // Small animation from indicator to cursor pos
                    {
                        var animDuration = 0.2f;
                        _hoverPickingProgress = (_hoverPickingProgress + 1 / (ImGui.GetIO().Framerate * animDuration)).Clamp(0, 1);
                        var smoothedProgress = MathF.Pow(_hoverPickingProgress, 4);
                        var centerLerp = MathUtils.SmootherStep(0, 1, _hoverPickingProgress);
                        var center = Vector2.Lerp(indicatorPos, ImGui.GetMousePos(), centerLerp);
                        var radius = 3 + smoothedProgress * 3;
                        var circleColor = UiColors.ForegroundFull.Fade(1 - _hoverPickingProgress * 0.5f);
                        CircleInBoxHelper.DrawClippedCircle(pMinVisible, pMaxVisible, center, radius, circleColor, drawList);
                    }

                    // Draw selection outline...
                    drawList.AddRect(pMinVisible, pMaxVisible, UiColors.ForegroundFull.Fade(_hoverPickingProgress * 0.4f), CanvasScale < 0.5 ? 0 : 5 * CanvasScale, imDrawFlags);

                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3);
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3, 4));
                    ImGui.BeginTooltip();
                    var childUi = item.SymbolUi;
                    if (childUi != null)
                    {
                        if (!TypeNameRegistry.Entries.TryGetValue(_context.DraggedPrimaryOutputType, out var typeName))
                            typeName = _context.DraggedPrimaryOutputType.Name;

                        ImGui.PushFont(Fonts.FontSmall);
                        ImGui.TextColored(UiColors.TextMuted, typeName + " inputs");
                        ImGui.PopFont();

                        var inputIndex = 0;
                        foreach (var inputUi in childUi.InputUis.Values)
                        {
                            if (inputIndex <= item.Instance!.Inputs.Count)
                            {
                                var input = item.Instance!.Inputs[inputIndex];
                                if (inputUi.Type == context.DraggedPrimaryOutputType)
                                {
                                    var isConnected = input.HasInputConnections;
                                    var prefix = isConnected ? "× " : "   ";
                                    ImGui.Selectable(prefix + inputUi.InputDefinition.Name);
                                }
                            }

                            inputIndex++;
                        }
                    }

                    ImGui.EndTooltip();
                    ImGui.PopStyleVar(3);
                }
            }
        }

        // 8.5 -> 7.9ms
        //return;

        // Missing primary input indicator
        if (item.InputLines.Length > 0)
        {
            var inputLine = item.InputLines[0];

            // ReSharper disable once ConstantConditionalAccessQualifier
            var isMissing = inputLine.InputUi?.Relevancy == Relevancy.Required && inputLine.ConnectionIn == null;
            if (isMissing)
            {
                DrawMissingInputIndicator(drawList, pMin, inputLine);
            }
        }

        var borderColor = ColorVariations.OperatorOutline.Apply(typeColor);
        IReadOnlyList<ISlot> multiInputSlots = null;
        if (CanvasScale > 0.25f)
        {
            // Input labels...
            if ((customUiResult & OpUi.CustomUiResult.PreventInputLabels) == 0)
            {
                int inputIndex;
                var itemWidth = pMax.X - pMin.X;

                for (inputIndex = 0; inputIndex < item.InputLines.Length; inputIndex++)
                {
                    var inputLine = item.InputLines[inputIndex];

                    // Draw multi-input region
                    if (item.Variant == MagGraphItem.Variants.Operator)
                    {
                        var inputSlot = item.Instance.GetInput(inputLine.Input.Id);
                        var isMultiInput = inputLine.InputUi.InputDefinition.IsMultiInput;


                        // Start new multiInputGroup
                        if (isMultiInput && inputLine.MultiInputIndex == 0)
                        {
                            multiInputSlots = inputSlot.TryGetAsMultiInput(out var multiInput) 
                                                  ? multiInput.GetCollectedInputs() 
                                                  : null;

                            var slotCount =  multiInputSlots?.Count ?? 0;
                            if (slotCount == 0)
                                slotCount = 1;
                            
                            var y2 = pMin.Y + GridSizeOnScreen.Y * (inputIndex);
                            var rMin = new Vector2(pMin.X + (isSelected ? 1 : 0), y2 + 0.2f * GridSizeOnScreen.Y);
                            var size = new Vector2(pMin.X + itemWidth * 0.04f, y2 + (slotCount - 0.2f) * GridSizeOnScreen.Y) - rMin;
                            drawList.AddQuadFilled(rMin,
                                                   rMin + new Vector2(size.X, size.X),
                                                   rMin + new Vector2(size.X, size.Y - size.X),
                                                   rMin + new Vector2(0, size.Y),
                                                   borderColor.Fade(0.7f)
                                                  );
                        }

                        if (!isMultiInput)
                        {
                            multiInputSlots = null;
                        }
                    }

                    // Skip label for first primary input
                    if (inputIndex == 0)
                        continue;

                    var isMissing = inputLine.InputUi.Relevancy == Relevancy.Required && inputLine.ConnectionIn == null;
                    if (isMissing)
                    {
                        DrawMissingInputIndicator(drawList, pMin + new Vector2(0, GridSizeOnScreen.Y * inputIndex), inputLine);
                    }

                    var inputLabelFontSize = Fonts.FontSmall.FontSize / T3Ui.UiScaleFactor * Fonts.FontSmall.Scale * smallFontScaleFactor;
                    var yCenter = pMin.Y + GridSizeOnScreen.Y * (inputIndex + 0.5f) - inputLabelFontSize / 2 - 2;
                    var labelPos = new Vector2(pMin.X + 8 * CanvasScale, yCenter);
                    var label = inputLine.InputUi.InputDefinition.Name ?? "?";
                    if (inputLine.MultiInputIndex > 0)
                    {
                        label = " +" + inputLine.MultiInputIndex;

                        // Draw 16 byte padding borders
                        if (inputLine.MultiInputIndex % 4 == 0)
                        {
                            var y2 = pMin.Y + GridSizeOnScreen.Y * (inputIndex);

                            drawList.AddRectFilled(new Vector2(pMin.X + itemWidth * 0.04f, y2),
                                                   new Vector2(pMin.X + itemWidth * 0.20f, y2 + 1),
                                                   borderColor.Fade(0.8f)
                                                  );
                        }
                    }

                    drawList.AddText(Fonts.FontSmall,
                                     inputLabelFontSize,
                                     labelPos,
                                     labelColor.Fade(0.7f * CanvasScale.RemapAndClamp(0.3f,0.7f, 0,1) ),
                                     label
                                    );

                    // Draw Value if possible
                    if (CanvasScale > 0.4f)
                    {
                        string valueAsString;
                        if (multiInputSlots != null && inputLine.MultiInputIndex < multiInputSlots.Count)
                        {
                            var multiInputSlot = multiInputSlots[inputLine.MultiInputIndex];
                            valueAsString = ValueUtils.GetValueString(multiInputSlot);
                        }
                        else
                        {
                            valueAsString = ValueUtils.GetValueString(item.Instance.GetInput(inputLine.Input.Id));
                        }

                        if (!string.IsNullOrWhiteSpace(valueAsString))
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
                            var valueColor = labelColor;
                            valueColor.Rgba.W *= 0.6f;

                            ImGui.PushFont(Fonts.FontSmall);
                            var labelSize = ImGui.CalcTextSize(valueAsString) * smallFontScaleFactor;
                            ImGui.PopFont();

                            var valuePos = new Vector2(pMin.X + (item.Size.X - 5) * CanvasScale - labelSize.X, labelPos.Y);

                            if (!string.IsNullOrEmpty(valueAsString))
                            {
                                drawList.AddText(Fonts.FontSmall,
                                                 inputLabelFontSize,
                                                 valuePos,
                                                 labelColor.Fade(0.5f),
                                                 valueAsString
                                                );
                            }

                            ImGui.PopStyleColor();
                        }
                    }
                }
                
                // Draw output labels...
                for (var outputIndex = 1; outputIndex < item.OutputLines.Length; outputIndex++)
                {
                    var outputLine = item.OutputLines[outputIndex];
                    if (outputLine.OutputUi == null)
                        continue;

                    ImGui.PushFont(Fonts.FontSmall);
                    var outputDefinitionName = outputLine.OutputUi.OutputDefinition.Name.AddSpacesForImGuiOutput();
                    var outputLabelSize = ImGui.CalcTextSize(outputDefinitionName) * smallFontScaleFactor;
                    ImGui.PopFont();

                    drawList.AddText(Fonts.FontSmall,
                                     Fonts.FontSmall.FontSize * smallFontScaleFactor,
                                     pMin
                                     + new Vector2(-8, 9) * CanvasScale.Clamp(0.1f, 2f)
                                     + new Vector2(0, GridSizeOnScreen.Y * outputLine.VisibleIndex)
                                     + new Vector2(MagGraphItem.Width * CanvasScale - outputLabelSize.X, 0),
                                     labelColor.Fade(0.7f),
                                     outputDefinitionName);
                }
            }
        }

        if (item.Variant == MagGraphItem.Variants.Operator)
        {
            // Animation indicator
            var indicatorCount = 0;
            if (item.Instance.Parent.Symbol.Animator.IsInstanceAnimated(item.Instance))
            {
                DrawIndicator(drawList, UiColors.StatusAnimated, idleFadeFactor, pMin, pMax, CanvasScale, ref indicatorCount);
            }

            // Pinned indicator
            if (context.Selector.PinnedIds.Contains(item.Instance.SymbolChildId))
            {
                DrawIndicator(drawList, UiColors.Selection, idleFadeFactor, pMin, pMax, CanvasScale, ref indicatorCount);
            }

            // Snapshot indicator
            {
                if (item.ChildUi.EnabledForSnapshots)
                {
                    DrawIndicator(drawList, UiColors.StatusAutomated, idleFadeFactor, pMin, pMax, CanvasScale, ref indicatorCount);
                }
            }

            // Disabled indicator
            if (item.SymbolChild.IsDisabled)
            {
                DrawUtils.DrawOverlayLine(drawList, idleFadeFactor, Vector2.Zero, Vector2.One, pMinVisible, pMaxVisible);
                DrawUtils.DrawOverlayLine(drawList, idleFadeFactor, new Vector2(1, 0), new Vector2(0, 1), pMinVisible, pMaxVisible);
            }

            // Bypass indicator
            if (item.SymbolChild.IsBypassed)
            {
                DrawUtils.DrawOverlayLine(drawList, idleFadeFactor, new Vector2(0.05f, 0.5f), new Vector2(0.4f, 0.5f), pMinVisible, pMaxVisible);
                DrawUtils.DrawOverlayLine(drawList, idleFadeFactor, new Vector2(0.6f, 0.5f), new Vector2(0.95f, 0.5f), pMinVisible, pMaxVisible);

                DrawUtils.DrawOverlayLine(drawList, idleFadeFactor, new Vector2(0.35f, 0.1f), new Vector2(0.65f, 0.9f), pMinVisible, pMaxVisible);
                DrawUtils.DrawOverlayLine(drawList, idleFadeFactor, new Vector2(0.65f, 0.1f), new Vector2(0.35f, 0.9f), pMinVisible, pMaxVisible);
            }

            if (!string.IsNullOrEmpty(item.ChildUi.Comment))
            {
                ImGui.SetCursorScreenPos(new Vector2(pMax.X, pMin.Y) + Vector2.Round(new Vector2(-5, -1) * (T3Ui.UiScaleFactor + CanvasScale)));
                if (ImGui.InvisibleButton("#comment", new Vector2(15, 15)))
                {
                    context.Selector.SetSelection(item.ChildUi, item.Instance);
                    context.EditCommentDialog.ShowNextFrame();
                }

                Icons.DrawIconOnLastItem(Icon.Comment, UiColors.ForegroundFull);
                CustomComponents.TooltipForLastItem(UiColors.Text, item.ChildUi.Comment, null, false);
            }
        }

        // Hide additional UI elements when custom ui-op is hovered with control
        if (isItemHovered && ImGui.GetIO().KeyCtrl && customUiResult != OpUi.CustomUiResult.None || context.ItemWithActiveCustomUi != null)
        {
            return;
        }

        if (CanvasScale < 0.5f)
            return;

        // Draw free input sockets...
        MagGraphItem.InputAnchorPoint inputAnchor = default;

        var anchorWidth = 1.5f * 2;
        var anchorHeight = 2f * 2;

        var inputAnchorCount = item.GetInputAnchorCount();
        for (var inputIndex = 0; inputIndex < inputAnchorCount; inputIndex++)
        {
            item.GetInputAnchorAtIndex(inputIndex, ref inputAnchor);

            var isMultiInput = inputAnchor.InputLine.InputUi != null && inputAnchor.InputLine.InputUi.InputDefinition.IsMultiInput;
            //var isAlreadyUsed = inputAnchor.SnappedConnectionHash != MagGraphItem.FreeAnchor;

            var type2UiProperties = TypeUiRegistry.GetPropertiesForType(inputAnchor.ConnectionType);
            var center = TransformPosition(inputAnchor.PositionOnCanvas);

            var isInputHovered = //isItemHovered && 
                Vector2.Distance(ImGui.GetMousePos(), center) < 7 * CanvasScale &&
                context.StateMachine.CurrentState == GraphStates.Default;

            var fillColor = isInputHovered
                                ? ColorVariations.Highlight.Apply(type2UiProperties.Color)
                                : ColorVariations.OperatorOutline.Apply(type2UiProperties.Color);
            var anchorOutlineColor = ColorVariations.OperatorOutline.Apply(type2UiProperties.Color);

            if (inputAnchor.Direction == MagGraphItem.Directions.Vertical)
            {
                
                var pp = new Vector2(center.X, pMinVisible.Y);
                if (isInputHovered)
                {
                    //var showTriangleAnchor = true;
                    var hoverOutputFactor = isInputHovered ? 1.3f : 1;

                    drawList.AddCircleFilled(center, (3 + 2 * hoverOutputFactor) * CanvasScale - 1, fillColor, 12);
                    drawList.AddCircle(center, (3 + 2 * hoverOutputFactor) * CanvasScale, anchorOutlineColor, 12);
                    //showTriangleAnchor = false;

                    var e = MathF.Round(2 * CanvasScale);
                    drawList.AddRectFilled(center + new Vector2(-e, 0),
                                           center + new Vector2(e + 1, 1),
                                           anchorOutlineColor);

                    drawList.AddRectFilled(center + new Vector2(0, -e),
                                           center + new Vector2(1, e + 1),
                                           anchorOutlineColor);

                    // This will later be used for by DefaultState to create a connection
                    
                    var hasSnappedConnection = inputAnchor.InputLine.ConnectionIn is { IsSnapped: true };
                    if (hasSnappedConnection)
                    {
                        // Snapped connections are handled by outputs
                    }
                    else
                    {
                        _context.ActiveItem = item;
                        _context.ActiveInputDirection = inputAnchor.Direction;
                        _context.ActiveTargetInputId = inputAnchor.SlotId;
                        _context.ActiveTargetItem = item;

                        // Show tooltip with output name and type
                        if (item.Variant == MagGraphItem.Variants.Operator)
                        {
                            var inputLine = inputAnchor.InputLine;
                            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted(inputLine.InputUi.InputDefinition.Name);
                            var type = inputLine.InputUi.InputDefinition.ValueType;
                            var typeName = type.Name;
                            if (typeName == "Single")
                                typeName = "Float";

                            var uiProperties = TypeUiRegistry.GetPropertiesForType(type);
                            ImGui.PushStyleColor(ImGuiCol.Text, uiProperties.Color.Rgba);
                            ImGui.TextUnformatted(typeName);
                            ImGui.PopStyleColor();
                            CustomComponents.HelpText("Click or drag to add");
                            ImGui.EndTooltip();
                            ImGui.PopStyleVar();
                        }
                    }
                    
                }
                else
                {
                    drawList.AddTriangleFilled(pp + new Vector2(-anchorWidth, 0) * CanvasScale,
                                               pp + new Vector2(anchorWidth, 0) * CanvasScale,
                                               pp + new Vector2(0, anchorHeight) * CanvasScale,
                                               fillColor);
                }
            }
            else
            {
                var showTriangleAnchor = true;

                var hasSnappedConnection = inputAnchor.InputLine.ConnectionIn is { IsSnapped: true };
                if (!hasSnappedConnection && isInputHovered)
                {
                    var hoverOutputFactor = isInputHovered ? 1.3f : 1;

                    drawList.AddCircleFilled(center, (1 + 2 * hoverOutputFactor) * CanvasScale - 1, fillColor, 12);
                    drawList.AddCircle(center, (1 + 2 * hoverOutputFactor) * CanvasScale, anchorOutlineColor, 12);
                    showTriangleAnchor = false;

                    var e = MathF.Round(2 * CanvasScale);
                    drawList.AddRectFilled(center + new Vector2(-e, 0),
                                           center + new Vector2(e + 1, 1),
                                           anchorOutlineColor);

                    drawList.AddRectFilled(center + new Vector2(0, -e),
                                           center + new Vector2(1, e + 1),
                                           anchorOutlineColor);

                    // This will later be used for by DefaultState to create a connection
                    //var contextActiveSourceOutputId = inputAnchor.SlotId;
                    // TODO: Only do this if not snapped. Otherwise output is preferred.
                    _context.ActiveTargetInputId = inputAnchor.SlotId;
                    _context.ActiveInputDirection = MagGraphItem.Directions.Horizontal;
                    _context.ActiveTargetItem = item;
                    _context.ActiveItem = item;

                    // Show tooltip with output name and type
                    if (item.Variant == MagGraphItem.Variants.Operator)
                    {
                        var inputLine = inputAnchor.InputLine;
                        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(inputLine.InputUi.InputDefinition.Name);
                        var type = inputLine.InputUi.InputDefinition.ValueType;
                        var typeName = type.Name;
                        if (typeName == "Single")
                            typeName = "Float";

                        var uiProperties = TypeUiRegistry.GetPropertiesForType(type);
                        ImGui.PushStyleColor(ImGuiCol.Text, uiProperties.Color.Rgba);
                        ImGui.TextUnformatted(typeName);
                        ImGui.PopStyleColor();
                        CustomComponents.HelpText("Click or drag to add");
                        ImGui.EndTooltip();
                        ImGui.PopStyleVar();
                    }
                }

                var isPotentialConnectionEndDropTarget = _context.StateMachine.CurrentState == GraphStates.DragConnectionEnd
                                                         && _context.DraggedPrimaryOutputType == inputAnchor.ConnectionType;

                if (isMultiInput)
                {
                    var isConnected = inputAnchor.InputLine.ConnectionIn != null;
                    if (isPotentialConnectionEndDropTarget)
                    {
                        fillColor = ColorVariations.Highlight.Apply(type2UiProperties.Color);
                        DrawMultiInputIndicator(item, inputAnchor.SlotId, inputAnchor.InputLine.MultiInputIndex, drawList, inputAnchor.PositionOnCanvas,
                                                fillColor,
                                                InputSnapper.InputSnapTypes.ReplaceMultiInput);

                        if (isConnected)
                        {
                            if (inputAnchor.InputLine.MultiInputIndex == 0)
                                DrawMultiInputIndicator(item, inputAnchor.SlotId, inputAnchor.InputLine.MultiInputIndex, drawList, inputAnchor.PositionOnCanvas,
                                                        fillColor, InputSnapper.InputSnapTypes.InsertBeforeMultiInput);

                            DrawMultiInputIndicator(item, inputAnchor.SlotId, inputAnchor.InputLine.MultiInputIndex, drawList, inputAnchor.PositionOnCanvas,
                                                    fillColor, InputSnapper.InputSnapTypes.InsertAfterMultiInput);
                        }

                        showTriangleAnchor = false;
                    }
                }
                else
                {
                    // Register for input snapping...
                    if (isPotentialConnectionEndDropTarget && item != _context.ActiveItem)
                    {
                        fillColor = ColorVariations.Highlight.Apply(type2UiProperties.Color).Fade(Blink);
                        InputSnapper.RegisterAsPotentialTargetInput(item, center, inputAnchor.SlotId);
                    }
                    else if (inputAnchor.InputLine.ConnectionIn != null)
                    {
                        fillColor = fillColor = ColorVariations.OperatorLabel.Apply(type2UiProperties.Color).Fade(0.1f);
                    }
                }

                if (showTriangleAnchor)
                {
                    var pp = new Vector2(pMinVisible.X - 1, center.Y);
                    drawList.AddTriangleFilled(pp + new Vector2(1, 0) + new Vector2(-0, -anchorWidth) * CanvasScale,
                                               pp + new Vector2(1, 0) + new Vector2(anchorHeight, 0) * CanvasScale,
                                               pp + new Vector2(1, 0) + new Vector2(0, anchorWidth) * CanvasScale,
                                               fillColor);
                }
            }
        }

        // Draw output sockets...
        var count = item.GetOutputAnchorCount();
        MagGraphItem.OutputAnchorPoint outputAnchor = default;
        for (var index = 0; index < count; index++)
        {
            item.GetOutputAnchorAtIndex(index, ref outputAnchor);

            var type2UiProperties = TypeUiRegistry.GetPropertiesForType(outputAnchor.ConnectionType);

            var posOnCanvas = TransformPosition(outputAnchor.PositionOnCanvas);

            Vector2 center;
            bool isPotentialConnectionStartDropTarget = false;
            // ...below
            if (outputAnchor.Direction == MagGraphItem.Directions.Vertical)
            {
                center = new Vector2(posOnCanvas.X, pMaxVisible.Y);
                center += new Vector2(0, -1);
            }
            // ... on the right
            else
            {
                var hasOutputSnappedConnection = false;
                foreach (var c in item.OutputLines[outputAnchor.OutputLineIndex].ConnectionsOut)
                {
                    if (c.IsSnapped && c.Style is MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal
                            or MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal)
                    {
                        hasOutputSnappedConnection = true;
                        break;
                    }
                }

                // Register for output snapping...
                isPotentialConnectionStartDropTarget = _context.StateMachine.CurrentState == GraphStates.DragConnectionBeginning
                                                       && _context.DraggedPrimaryOutputType == outputAnchor.ConnectionType
                                                       && !hasOutputSnappedConnection;
                //&& outputAnchor.SnappedConnectionHash == MagGraphItem.FreeAnchor;

                if (isPotentialConnectionStartDropTarget)
                {
                    OutputSnapper.RegisterAsPotentialTargetOutput(context, item, outputAnchor);
                }

                center = new Vector2(pMaxVisible.X, posOnCanvas.Y);
                center += new Vector2(-2, 0);
            }

            var hasSnappedConnection = false;
            
            foreach (var c in item.OutputLines[outputAnchor.OutputLineIndex].ConnectionsOut)
            {
                if (!c.IsSnapped)
                    continue;

                if (outputAnchor.Direction == MagGraphItem.Directions.Vertical &&
                    c.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical
                    || outputAnchor.Direction == MagGraphItem.Directions.Horizontal &&
                    c.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal)
                {
                    hasSnappedConnection = true;
                    break;
                }
            }

            var isOutputHovered = //isItemHovered
                                  //&& 
                                  Vector2.Distance(ImGui.GetMousePos(), center) < 7 * CanvasScale
                                  && context.StateMachine.CurrentState == GraphStates.Default;
            
            var anchorOutlineColor = ColorVariations.OperatorOutline.Apply(type2UiProperties.Color);

            var fillColor = ColorVariations.OperatorBackground.Apply(type2UiProperties.Color);
            if (isOutputHovered)
            {
                fillColor = ColorVariations.Highlight.Apply(type2UiProperties.Color);
            }
            else if (isPotentialConnectionStartDropTarget)
            {
                fillColor = ColorVariations.ConnectionLines.Apply(type2UiProperties.Color).Fade(Blink);
            }

            var hoverOutputFactor = isOutputHovered ? 1.3f : 1;
 
            if (!hasSnappedConnection
                || isItemHovered
                || isOutputHovered)
            {
                var radius = MathF.Min((2 + 2 * hoverOutputFactor) , 10) * CanvasScale;
                drawList.AddCircleFilled(center, radius - 1, fillColor, 12);
                drawList.AddCircle(center, radius, anchorOutlineColor, 12);
            }

            if (isOutputHovered)
            {
                var e = MathF.Round(2 * CanvasScale);
                drawList.AddRectFilled(center + new Vector2(-e, 0),
                                       center + new Vector2(e + 1, 1),
                                       anchorOutlineColor);

                drawList.AddRectFilled(center + new Vector2(0, -e),
                                       center + new Vector2(1, e + 1),
                                       anchorOutlineColor);

                // This will later be used for by DefaultState to create a connection
                var contextActiveSourceOutputId = outputAnchor.SlotId;
                _context.ActiveSourceItem = item;
                _context.ActiveSourceOutputId = contextActiveSourceOutputId;
                _context.ActiveOutputDirection = outputAnchor.Direction;

                // Show tooltip with output name and type
                if (item.Variant == MagGraphItem.Variants.Operator)
                {
                    var outputLine = item.OutputLines.FirstOrDefault(o => o.Id == contextActiveSourceOutputId);

                    if (outputLine.Id == contextActiveSourceOutputId)
                    {
                        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(outputLine.OutputUi.OutputDefinition.Name);
                        var type = outputLine.OutputUi.OutputDefinition.ValueType;
                        var typeName = type.Name;
                        if (typeName == "Single")
                            typeName = "Float";

                        var uiProperties = TypeUiRegistry.GetPropertiesForType(type);
                        ImGui.PushStyleColor(ImGuiCol.Text, uiProperties.Color.Rgba);
                        ImGui.TextUnformatted(typeName);
                        ImGui.PopStyleColor();
                        if (outputLine.Output.DirtyFlag.Trigger != DirtyFlagTrigger.None)
                        {
                            if (outputLine.Output.DirtyFlag.Trigger != DirtyFlagTrigger.Animated)
                            {
                                ImGui.TextUnformatted("(Cache invalidating)");
                            }
                            else if (outputLine.Output.DirtyFlag.Trigger != DirtyFlagTrigger.Always)
                            {
                                ImGui.TextUnformatted("(always evaluated)");
                            }

                        }
                        ToolTipContentDrawer.DrawForOutput(outputLine.Output, out var xxx);

                        ImGui.EndTooltip();
                        ImGui.PopStyleVar();
                    }
                }
            }
        }

        // Draw additional output indicator
        if (item.Variant == MagGraphItem.Variants.Operator)
        {
            if (item.HasHiddenOutputs)
            {
                if (_context.StateMachine.CurrentState == GraphStates.PickOutput && _context.ActiveItem == item)
                {
                    OutputPicking.DrawAdditionOutputSelector(context);
                }
                else
                {
                    var padding = 0.1f;
                    var p = pMaxVisible - new Vector2(GridSizeOnScreen.Y * padding, GridSizeOnScreen.Y * padding);
                    var area = new ImRect(p + new Vector2(-0.4f, -0.5f) * CanvasScale * 7,
                                          p + new Vector2(0.4f, 0.5f) * CanvasScale * 7);

                    var isToggleHovered = area.Contains(ImGui.GetMousePos());

                    //var opacity = hoverProgress.RemapAndClamp(0, 1, 0.1f, 0.7f);
                    drawList.AddTriangleFilled(
                                               p + new Vector2(-0.4f, -0.5f) * CanvasScale * 4,
                                               p + new Vector2(0.4f, 0) * CanvasScale * 4,
                                               p + new Vector2(-0.4f, 0.5f) * CanvasScale * 4,
                                               UiColors.ForegroundFull.Fade(isToggleHovered ? 1 : 0.5f)
                                              );

                    if (isToggleHovered)
                    {
                        ImGui.SetNextWindowPos(p - new Vector2(15, 15));
                        if (CustomComponents.BeginTooltip())
                        {
                            CustomComponents.HintLabel("Show more outputs...");
                            foreach (var o in item.SymbolUi!.OutputUis.Values)
                            {
                                var isVisible = false;
                                foreach (var visibleOutput in item.OutputLines)
                                {
                                    if (visibleOutput.Id != o.Id) continue;
                                    isVisible = true;
                                    break;
                                }

                                if (isVisible)
                                    continue;

                                ImGui.TextUnformatted(o.OutputDefinition.Name);
                                ImGui.SameLine();
                                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                                ImGui.TextUnformatted($" <{o.OutputDefinition.ValueType.Name}>");
                                ImGui.PopStyleColor();
                            }

                            CustomComponents.EndTooltip();
                        }

                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                            || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            ImGui.OpenPopup("pickOutput");
                            _context.StateMachine.SetState(GraphStates.PickOutput, context);
                            _context.ActiveItem = item;
                        }
                    }
                }
            }
        }

        if (item.Variant == MagGraphItem.Variants.Operator)
        {
            var instance = item.Instance;
            if (instance is IStatusProvider statusProvider)
            {
                var statusLevel = statusProvider.GetStatusLevel();
                if (statusLevel != IStatusProvider.StatusLevel.Success && statusLevel != IStatusProvider.StatusLevel.Undefined)
                {
                    ImGui.SetCursorScreenPos(pMinVisible + new Vector2(8, -7));
                    ImGui.InvisibleButton("#warning", new Vector2(15, 15));
                    var color = statusLevel switch
                                    {
                                        IStatusProvider.StatusLevel.Notice  => UiColors.StatusAttention,
                                        IStatusProvider.StatusLevel.Warning => UiColors.StatusWarning,
                                        IStatusProvider.StatusLevel.Error   => UiColors.StatusError,
                                        _                                   => UiColors.StatusError
                                    };
                    Icons.DrawIconOnLastItem(Icon.Warning, color);
                    CustomComponents.TooltipForLastItem(UiColors.StatusWarning, statusLevel.ToString(), statusProvider.GetStatusMessage(), false);
                }
            }

            if (UserSettings.Config.ShowOperatorStats)
            {
                var s = item.Size.Y * 0.15f * CanvasScale;
                ImGui.SetCursorScreenPos(pMinVisible + new Vector2(s, pMaxVisible.Y - pMinVisible.Y - s));
                ImGui.InvisibleButton("#status", new Vector2(s, s));
                Icons.DrawIconOnLastItem(Icon.HelpOutline, Color.White with {A = 0.1f});

                if (ImGui.BeginItemTooltip())
                {
                    DrawInstanceStatus(instance);
                    ImGui.EndTooltip();
                }
            }
        }

        return;
        static void DrawInstanceStatus(Instance instance)
        {
            var status = instance.Status;
            var id = Symbol.Child.HashCodeOf(instance.InstancePath);
            ImGui.BeginDisabled();
            DrawFlags(status, id);
           /* instance.Children.GetExistingCounts(out var cound, out var existingCount, out var childFlags, out var nestedCount, out var nestedExistingCount, out var nestedFlags);
            ImGui.TextUnformatted($"My Children: {existingCount}/{cound}, Nested Children: {nestedExistingCount}/{nestedCount}");
            DrawFlags(nestedFlags, id + 1);
            */
            ImGui.EndDisabled();
            return;
            
            static void DrawFlags(Instance.InstanceStatus status, int id)
            {
                var isInitialized = (status & Instance.InstanceStatus.Initialized) == Instance.InstanceStatus.Initialized;
                ImGui.Checkbox($"Initialized ##initflag{id}" , ref isInitialized);
            
                var isActive = (status & Instance.InstanceStatus.Active) == Instance.InstanceStatus.Active;
                ImGui.Checkbox($"Active ##activeflag{id}", ref isActive);
            
                var isBypassed = (status & Instance.InstanceStatus.Bypassed) == Instance.InstanceStatus.Bypassed;
                ImGui.Checkbox($"Bypassed ##bypassflag{id}", ref isBypassed);
            
                var isConnectedInternally = (status & Instance.InstanceStatus.ConnectedInternally) == Instance.InstanceStatus.ConnectedInternally;
                ImGui.Checkbox($"ConnectedInternally ##connectedflag{id}", ref isConnectedInternally);
            
                var isDisposed = (status & Instance.InstanceStatus.Disposed) == Instance.InstanceStatus.Disposed;
                ImGui.Checkbox($"Disposed ##disposedflag{id}", ref isDisposed);
            
                var isResourceFoldersDirty = (status & Instance.InstanceStatus.ResourceFoldersDirty) == Instance.InstanceStatus.ResourceFoldersDirty;
                ImGui.Checkbox($"ResourceFoldersDirty ##resourcedirtyflag{id}", ref isResourceFoldersDirty);
            }
        }
    }



    private void DrawMissingInputIndicator(ImDrawListPtr drawList, Vector2 pMin, MagGraphItem.InputLine inputLine)
    {
        var s = GridSizeOnScreen.Y;
        var c = pMin + new Vector2(-s * 0.2f, s * 0.45f);
        var s2 = s * 0.4f;
        drawList.AddTriangleFilled(c + new Vector2(0, -0.2f) * s2,
                                   c + new Vector2(-0.2f, 0.2f) * s2,
                                   c + new Vector2(0.2f, 0.2f) * s2,
                                   //+ new Vector2(8, 9) * CanvasScale 
                                   UiColors.StatusAttention);

        ImGui.SetCursorScreenPos(c - Vector2.One * s2 / 2);
        ImGui.InvisibleButton("warningArea", new Vector2(s2, s2));
        if (ImGui.IsItemHovered())
        {
            CustomComponents.TooltipForLastItem("Requires " + inputLine.InputUi.InputDefinition.Name);
        }
    }

    private void DrawMultiInputIndicator(MagGraphItem item, Guid slotId, int multiInputIndex, ImDrawListPtr drawList, Vector2 inputPosOnCanvas, Color color,
                                         InputSnapper.InputSnapTypes snapType)
    {
        var verticalOffset = snapType switch
                                 {
                                     InputSnapper.InputSnapTypes.Normal                 => 0,
                                     InputSnapper.InputSnapTypes.InsertBeforeMultiInput => -1f,
                                     InputSnapper.InputSnapTypes.ReplaceMultiInput      => 0,
                                     InputSnapper.InputSnapTypes.InsertAfterMultiInput  => +1f,
                                     _                                                  => 0f
                                 };

        var padding = MagGraphItem.GridSize.Y * 0.15f;
        var snapPosOnCanvas = inputPosOnCanvas
                              + new Vector2(padding, MagGraphItem.LineHeight * 0.5f * verticalOffset);

        snapPosOnCanvas.Y = snapPosOnCanvas.Y.Clamp(item.PosOnCanvas.Y + padding, item.PosOnCanvas.Y + item.Size.Y - padding);

        if (snapType != InputSnapper.InputSnapTypes.ReplaceMultiInput)
            drawList.AddCircleFilled(TransformPosition(snapPosOnCanvas), 2 * CanvasScale, color.Fade(0.5f + 0.5f * Blink), 8);

        var pOnScreen = TransformPosition(snapPosOnCanvas);
        InputSnapper.RegisterAsPotentialTargetInput(item, pOnScreen, slotId, snapType, multiInputIndex);
    }

    private static void DrawIndicator(ImDrawListPtr drawList, Color color, float opacity, Vector2 areaMin, Vector2 areaMax, float canvasScale,
                                      ref int indicatorCount)
    {
        const int s = 4;
        var dx = (s + 1) * indicatorCount;

        var pMin = new Vector2(s + dx,
                               s) * canvasScale + areaMin;

        var pMax = pMin + new Vector2(4, 3) * canvasScale;

        drawList.AddRectFilled(pMin, pMax, color.Fade(opacity));
        
        drawList.AddRect(pMin - Vector2.One,
                         pMax + Vector2.One,
                         UiColors.WindowBackground.Fade(0.4f * opacity));
        indicatorCount++;
    }
    

    private bool TryDrawTexturePreview(MagGraphItem item, Vector2 itemMin, Vector2 itemMax, ImDrawListPtr drawList, Color typeColor)
    {
        if (item.Variant != MagGraphItem.Variants.Operator)
            return false;

        var instance = item.Instance;
        if (instance == null || instance.Outputs.Count == 0)
            return false;

        var firstOutput = instance.Outputs[0];
        if (firstOutput is not Slot<Texture2D> textureSlot)
            return false;

        var texture = textureSlot.Value;
        if (texture == null || texture.IsDisposed)
            return false;

        var previewTextureView = SrvManager.GetSrvForTexture(texture);

        var aspect = (float)texture.Description.Width / texture.Description.Height;

        var unitScreenHeight = (MagGraphItem.GridSize.Y - 5) * CanvasScale;
        var previewSize = new Vector2(unitScreenHeight * aspect, unitScreenHeight);

        var maxAspect = 1.6f;
        if (previewSize.X > unitScreenHeight * maxAspect)
        {
            previewSize *= unitScreenHeight / (previewSize.X) * maxAspect;
        }

        var min = new Vector2(itemMax.X - previewSize.X - 2 * CanvasScale,
                              itemMin.Y
                              + (unitScreenHeight - previewSize.Y) / 2
                              + 1 * CanvasScale);

        if (previewTextureView == null)
            return false;

        drawList.AddImage((IntPtr)previewTextureView, min,
                          min + previewSize,
                          Vector2.Zero,
                          Vector2.One,
                          Color.White);
        if (CanvasScale > 0.5f)
        {
            drawList.AddRect(min - Vector2.One * 0.5f * CanvasScale,
                             min + previewSize + Vector2.One * 0.5f * CanvasScale,
                             ColorVariations.ConnectionLines.Apply(typeColor),
                             2 * CanvasScale,
                             ImDrawFlags.RoundCornersAll,
                             1 * CanvasScale);
        }

        return true;
    }

    private static Guid _hoveredForInputPickingId;
    private static float _hoverPickingProgress;

    private static readonly Vector2[] _inputIndicatorPoints = new Vector2[5];
}

internal static class CircleInBoxHelper
{
    public static void DrawClippedCircle(Vector2 pMin, Vector2 pMax, Vector2 center, float radius, Color color, ImDrawListPtr drawList)
    {
        if (!new ImRect(pMin, pMax).Contains(center))
            return;

        // Bottom right quadrant (0 to π/2)
        {
            var dx = pMax.X - center.X;
            var start = dx < 0 ? MathF.PI / 2
                        : dx < radius ? MathF.Acos(dx / radius) : 0;

            var dy = pMax.Y - center.Y;
            var end = dy < 0 ? 0
                      : dy < radius ? MathF.PI / 2 - MathF.Acos(dy / radius) : MathF.PI / 2;

            if (start < end)
            {
                drawList.PathArcTo(center, radius, start, end);
            }
            else
            {
                drawList.PathLineTo(new Vector2(pMax.X, center.Y));
                drawList.PathLineTo(pMax);
            }
        }

        // Bottom left quadrant (π/2 to π)
        {
            var dy = pMax.Y - center.Y;
            var start = dy < 0
                            ? MathF.PI / 2
                            : dy < radius
                                ? MathF.PI / 2 + MathF.Acos(dy / radius)
                                : MathF.PI / 1.95f;

            var dx = center.X - pMin.X;
            var end = dx < 0
                          ? MathF.PI
                          : dx < radius
                              ? MathF.PI - MathF.Acos(dx / radius)
                              : MathF.PI * 0.95f;

            if (start < end)
            {
                drawList.PathArcTo(center, radius, start, end);
            }
            else
            {
                drawList.PathLineTo(new Vector2(pMin.X, pMax.Y));
            }
        }

        // Top left quadrant (π to 3π/2)
        {
            var dx = center.X - pMin.X;
            var start = dx < 0 ? MathF.PI
                        : dx < radius ? MathF.PI + MathF.Acos(dx / radius) : MathF.PI;

            var dy = center.Y - pMin.Y;
            var end = dy < 0
                          ? MathF.PI * 1.1f
                          : dy < radius
                              ? 3 * MathF.PI / 2 - MathF.Acos(dy / radius)
                              : 2.95f * MathF.PI / 2;

            if (start < end)
            {
                drawList.PathArcTo(center, radius, start, end);
            }
            else
            {
                drawList.PathLineTo(pMin);
            }
        }

        // Top right quadrant (3π/2 to 2π)
        {
            var dy = center.Y - pMin.Y;
            var start = dy < 0
                            ? 3 * MathF.PI / 2
                            : dy < radius
                                ? 3 * MathF.PI / 2 + MathF.Acos(dy / radius)
                                : 3 * MathF.PI / 2;

            var dx = pMax.X - center.X;
            var end = dx < 0
                          ? 2 * MathF.PI
                          : dx < radius
                              ? 2 * MathF.PI - MathF.Acos(dx / radius)
                              : 2 * MathF.PI;

            if (start < end)
            {
                drawList.PathArcTo(center, radius, start, end);
            }
            else
            {
                drawList.PathLineTo(new Vector2(pMax.X, pMin.Y));
                drawList.PathLineTo(new Vector2(pMax.X, center.Y));
            }
        }

        //drawList.PathStroke(color, ImDrawFlags.Closed , 2);
        drawList.PathFillConvex(color);
    }
}