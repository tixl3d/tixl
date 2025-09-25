using System.Runtime.CompilerServices;
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Editor.Gui.OpUis;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Graph.Legacy.Interaction;
using T3.Editor.Gui.Graph.Legacy.Interaction.Connections;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Color = T3.Core.DataTypes.Vector.Color;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;
using T3.Editor.Gui.Interaction.Keyboard;


namespace T3.Editor.Gui.Graph.Legacy;

/// <summary>
/// Renders a graphic representation of a SymbolChild within the current GraphWindow.
/// </summary>
internal sealed class GraphNode
{
    public GraphNode(ProjectView projectView, GraphView view, Legacy.Graph.ConnectionSorter sorter)
    {
        _projectView = projectView;
        _view = view;
        _sorter = sorter;

        lock (DraggedIds)
        {
            if (!DraggedIds.TryGetValue(_view, out _dragInfo))
            {
                _dragInfo = new DraggedIdInfo();
                DraggedIds[_view] = _dragInfo;
            }
        }
    }
        
    public void Draw(ImDrawListPtr drawList, float opacity, bool isSelected, SymbolUi.Child childUi, Instance instance, bool preventInteraction)
    {
        var symbolUi = instance.GetSymbolUi();
        var nodeHasHiddenMatchingInputs = false;
        var visibleInputUis = FindVisibleInputUis(symbolUi, childUi, ref nodeHasHiddenMatchingInputs);
        var tempConnections = ConnectionMaker.GetTempConnectionsFor(_view);
        bool hasConnections = tempConnections.Count > 0;
        bool inActiveWindow = _view == ProjectView.Focused?.GraphView;

        var framesSinceLastUpdate = 100;
        foreach (var output in instance.Outputs)
        {
            framesSinceLastUpdate = Math.Min(framesSinceLastUpdate, output.DirtyFlag.FramesSinceLastUpdate);
        }

        OpUi.CustomUiResult customUiResult  = OpUi.CustomUiResult.None;

        var newNodeSize = ComputeNodeSize(childUi, visibleInputUis, _sorter);
        AdjustGroupLayoutAfterResize(childUi, newNodeSize, instance.Parent.GetSymbolUi());
        _usableScreenRect = _view.TransformRect(new ImRect(childUi.PosOnCanvas,
                                                             childUi.PosOnCanvas + childUi.Size));
        _selectableScreenRect = _usableScreenRect;
        if (UserSettings.Config.ShowThumbnails)
            PreparePreviewAndExpandSelectableArea(instance);

        _isVisible = ImGui.IsRectVisible(_selectableScreenRect.Min, _selectableScreenRect.Max);

            
        var isNodeHovered = false;
        ImGui.PushID(childUi.SymbolChild.Id.GetHashCode());
        {
            if (_isVisible)
            {
                if (instance is IStatusProvider statusProvider)
                {
                    var statusLevel = statusProvider.GetStatusLevel();
                    if (statusLevel != IStatusProvider.StatusLevel.Success && statusLevel != IStatusProvider.StatusLevel.Undefined)
                    {
                        ImGui.SetCursorScreenPos(_usableScreenRect.Min - new Vector2(10, 12) * T3Ui.UiScaleFactor);
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

                if (!string.IsNullOrEmpty(childUi.Comment))
                {
                    ImGui.SetCursorScreenPos(new Vector2(_usableScreenRect.Max.X,  _usableScreenRect.Min.Y) -  new Vector2(3, 12) * T3Ui.UiScaleFactor * T3Ui.UiScaleFactor);
                    if (ImGui.InvisibleButton("#comment", new Vector2(15, 15)))
                    {
                        _projectView.NodeSelection.SetSelection(childUi, instance);
                        _view.EditCommentDialog.ShowNextFrame();
                    }
                    Icons.DrawIconOnLastItem(Icon.Comment, UiColors.ForegroundFull);
                    CustomComponents.TooltipForLastItem( UiColors.Text, childUi.Comment, null, false);
                }
                    
                // Resize indicator
                if (childUi.Style == SymbolUi.Child.Styles.Resizable)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
                    ImGui.SetCursorScreenPos(_usableScreenRect.Max - new Vector2(10, 10) * T3Ui.UiScaleFactor);
                    ImGui.Button("##resize", new Vector2(10, 10) * T3Ui.UiScaleFactor);
                    if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                    {
                        var delta = _view.InverseTransformDirection(ImGui.GetIO().MouseDelta);
                        childUi.Size += delta;
                    }

                    ImGui.SetMouseCursor(ImGuiMouseCursor.Arrow);
                }

                // Rendering

                var typeColor = (childUi.SymbolChild.Symbol.OutputDefinitions.Count > 0
                                     ? TypeUiRegistry.GetPropertiesForType(childUi.SymbolChild.Symbol.OutputDefinitions[0].ValueType).Color
                                     : UiColors.Gray).Fade(opacity);

                var backgroundColor = typeColor;

                // Background
                var isHighlighted = FrameStats.IsIdHovered(instance.SymbolChildId);
                if (framesSinceLastUpdate > 2)
                {
                    var fadeFactor = MathUtils.RemapAndClamp(framesSinceLastUpdate, 0f, 60f, 0f, 1.0f);
                    var mutedColor = ColorVariations.OperatorBackgroundIdle.Apply(backgroundColor).Fade(opacity);
                    backgroundColor = Color.Mix(backgroundColor, mutedColor, fadeFactor);
                }

                var backgroundColorWithHover = isHighlighted
                                                   ? ColorVariations.OperatorBackgroundHover.Apply(backgroundColor)
                                                   : ColorVariations.OperatorBackground.Apply(backgroundColor);

                drawList.AddRectFilled(_usableScreenRect.Min, _usableScreenRect.Max,
                                       backgroundColorWithHover.Fade(opacity));

                // Custom ui
                customUiResult =  instance.DrawCustomUi(drawList, _selectableScreenRect, _view);

                // Size toggle
                if (customUiResult == OpUi.CustomUiResult.None && _view.Scale.X > 0.7f)
                {
                    var pos = new Vector2(_usableScreenRect.Max.X - 15, _usableScreenRect.Min.Y + 2);

                    var transparentWhite = new Color(1, 1, 1, 0.3f * opacity).Rgba;
                    ImGui.SetCursorScreenPos(pos);
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
                    ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba * opacity);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, transparentWhite);
                    ImGui.PushStyleColor(ImGuiCol.Text, transparentWhite);
                    ImGui.PushFont(Icons.IconFont);

                    if (childUi.Style == SymbolUi.Child.Styles.Default)
                    {
                        if (ImGui.Button(_unfoldLabel, new Vector2(16, 16)))
                        {
                            childUi.Style = SymbolUi.Child.Styles.Expanded;
                        }
                    }
                    else if (childUi.Style != SymbolUi.Child.Styles.Default)
                    {
                        if (ImGui.Button(_foldLabel, new Vector2(16, 16)))
                        {
                            childUi.Style = SymbolUi.Child.Styles.Default;
                        }
                    }

                    ImGui.PopFont();
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(3);
                }

                // Disabled indicator
                if (childUi.SymbolChild.IsDisabled)
                {
                    DrawUtils.DrawOverlayLine(drawList, opacity, Vector2.Zero, Vector2.One, _usableScreenRect.Min, _usableScreenRect.Max);
                    DrawUtils.DrawOverlayLine(drawList, opacity, new Vector2(1,0), new Vector2(0,1), _usableScreenRect.Min, _usableScreenRect.Max);
                }
                    
                // Bypass indicator
                if (childUi.SymbolChild.IsBypassed)
                {
                    DrawUtils.DrawOverlayLine(drawList, opacity, new Vector2(0.05f,0.5f), new Vector2(0.4f,0.5f), _usableScreenRect.Min, _usableScreenRect.Max);
                    DrawUtils.DrawOverlayLine(drawList, opacity, new Vector2(0.6f,0.5f), new Vector2(0.95f,0.5f), _usableScreenRect.Min, _usableScreenRect.Max);

                    DrawUtils.DrawOverlayLine(drawList, opacity, new Vector2(0.35f,0.1f), new Vector2(0.65f,0.9f), _usableScreenRect.Min, _usableScreenRect.Max);
                    DrawUtils.DrawOverlayLine(drawList, opacity, new Vector2(0.65f,0.1f), new Vector2(0.35f,0.9f), _usableScreenRect.Min, _usableScreenRect.Max);
                }

                // Interaction
                if (preventInteraction)
                {
                    ImGui.SetCursorScreenPos( new Vector2(-5000,-5000));
                }
                else
                {
                    ImGui.SetCursorScreenPos(_selectableScreenRect.Min);
                }

                //--------------------------------------------------------------------------
                ImGui.InvisibleButton("node", _selectableScreenRect.GetSize());
                //--------------------------------------------------------------------------

                if(!preventInteraction)
                    _view.SelectableNodeMovement.Handle(childUi, instance);

                isNodeHovered = !preventInteraction 
                                && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) 
                                && !_view.SymbolBrowser.IsOpen
                                && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup);
                    
                // Tooltip
                if (!isNodeHovered)
                {
                    //FrameStats.Current.HoveredIds.Remove(childUi.SymbolChild.Id);
                }
                else if (UserSettings.Config.EditorHoverPreview
                         && (customUiResult & OpUi.CustomUiResult.PreventTooltip) != OpUi.CustomUiResult.PreventTooltip)
                {
                    if (UserSettings.Config.SmartGroupDragging)
                        _view.SelectableNodeMovement.HighlightSnappedNeighbours(childUi);

                    //ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    FrameStats.AddHoveredId(childUi.SymbolChild.Id);

                    if (UserSettings.Config.HoverMode != UserSettings.GraphHoverModes.Disabled
                        && !ImGui.IsMouseDragging(ImGuiMouseButton.Left)
                        && !RenamingOperator.IsOpen)
                    {
                        ImGui.BeginTooltip();
                        {
                            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 200*9/16f), new Vector2(200, 200*9/16f));
                            ImGui.BeginChild("##innerTooltip");
                            {
                                TransformGizmoHandling.SetDrawList(drawList);
                                _imageCanvasForTooltips.Update();
                                _imageCanvasForTooltips.SetAsCurrent();
                                if (instance.Outputs.Count > 0)
                                {
                                    var firstOutput = instance.Outputs[0];
                                    IOutputUi outputUi = symbolUi.OutputUis[firstOutput.Id];
                                    _evaluationContext.Reset();
                                    _evaluationContext.RequestedResolution = new Int2(1280 / 2, 720 / 2);
                                    outputUi.DrawValue(firstOutput,
                                                       _evaluationContext,
                                                       "thumbnail",
                                                       recompute: UserSettings.Config.HoverMode == UserSettings.GraphHoverModes.Live);
                                        
                                }
                                ImageOutputCanvas.Deactivate();
                                TransformGizmoHandling.RestoreDrawList();
                            }
                            ImGui.EndChild();

                        }
                        ImGui.EndTooltip();
                    }
                }

                // A work around to detect if node is below mouse while dragging end of new connection
                if (_selectableScreenRect.Contains(ImGui.GetMousePos()))
                {
                    _hoveredNodeIdForConnectionTarget = childUi.Id;
                }

                var hovered = inActiveWindow && (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) ||
                                                 FrameStats.Current.HoveredIds.Contains(instance.SymbolChildId));

                // A horrible work around to prevent exception because CompositionOp changed during drawing.
                // A better solution would defer setting the compositionOp to the beginning of next frame.
                var justOpenedChild = false;
                if (hovered && (ImGui.IsMouseDoubleClicked(0) || UserActions.OpenOperator.Triggered())
                            && !RenamingOperator.IsOpen
                            && (customUiResult & OpUi.CustomUiResult.PreventOpenSubGraph) == 0)
                {
                    if (ImGui.IsWindowFocused() || ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                    {
                        var blocked = false;
                        if (UserSettings.Config.WarnBeforeLibEdit && instance.Symbol.Namespace.StartsWith("Lib."))
                        {
                            if (UserSettings.Config.WarnBeforeLibEdit)
                            {
                                var count = Structure.CollectDependingSymbols(instance.Symbol).Count();
                                LibWarningDialog.DependencyCount = count;
                                LibWarningDialog.HandledInstance = instance;
                                _view.LibWarningDialog.ShowNextFrame();
                                blocked = true;
                            }
                        }

                        if (!blocked)
                        {
                            _projectView.TrySetCompositionOpToChild(instance.SymbolChildId); ///////////////////////////
                            ImGui.CloseCurrentPopup();
                            justOpenedChild = true;
                        }
                    }
                }

                if (!justOpenedChild)
                {
                    ParameterPopUp.HandleOpenParameterPopUp(childUi, instance, customUiResult, _selectableScreenRect);
                }

                DrawPreview(drawList, opacity);

                // Outline shadow
                drawList.AddRect(_selectableScreenRect.Min,
                                 _selectableScreenRect.Max + Vector2.One,
                                 ColorVariations.OperatorOutline.Apply(typeColor),
                                 rounding: 0,
                                 ImDrawFlags.None);

                if (isHighlighted)
                {
                    drawList.AddRect(_selectableScreenRect.Min - Vector2.One  * 2,
                                     _selectableScreenRect.Max + Vector2.One * 3 ,
                                     UiColors.ForegroundFull.Fade(0.6f),
                                     rounding: 0,
                                     ImDrawFlags.None);
                }

                // Animation indicator
                var indicatorCount = 0;
                if (instance.Symbol.Animator.IsInstanceAnimated(instance))
                {
                    DrawIndicator(drawList, UiColors.StatusAnimated, opacity, ref indicatorCount);
                }

                // Pinned indicator
                if (_projectView.NodeSelection.PinnedIds.Contains(instance.SymbolChildId))
                {
                    DrawIndicator(drawList, UiColors.Selection, opacity, ref indicatorCount);
                }

                // Snapshot indicator
                {
                    if (childUi.EnabledForSnapshots)
                    {
                        DrawIndicator(drawList, UiColors.StatusAutomated, opacity, ref indicatorCount);
                    }
                }
                    

                // Hidden inputs indicator
                if (nodeHasHiddenMatchingInputs)
                {
                    var blink = (float)(Math.Sin(ImGui.GetTime() * 10) / 2f + 0.8f);
                    var colorForType = TypeUiRegistry.GetPropertiesForType(tempConnections[0].ConnectionType).Color;
                    colorForType.Rgba.W *= blink;
                    drawList.AddRectFilled(
                                           new Vector2(_usableScreenRect.Min.X, _usableScreenRect.Max.Y + 3),
                                           new Vector2(_usableScreenRect.Min.X + 10, _usableScreenRect.Max.Y + 5),
                                           colorForType);
                }

                // Label
                if (customUiResult == OpUi.CustomUiResult.None
                    && _selectableScreenRect.GetHeight() > 8)
                {
                    drawList.PushClipRect(_usableScreenRect.Min, _usableScreenRect.Max, true);
                    var useSmallFont = _view.Scale.X < 1 * T3Ui.UiScaleFactor;
                    var font = Fonts.FontBold;
                        
                    var isRenamed = !string.IsNullOrEmpty(childUi.SymbolChild.Name);
                    var fade = MathUtils.SmootherStep(0.2f, 0.6f, _view.Scale.X);
                        
                    drawList.AddText(font,
                                     font.FontSize * ( useSmallFont ?  _view.Scale.X : 1) ,
                                     _usableScreenRect.Min + LabelPos,
                                     ColorVariations.OperatorLabel.Apply(typeColor).Fade(fade),
                                     isRenamed ? $"\"{childUi.SymbolChild.ReadableName}\"" : childUi.SymbolChild.ReadableName);
                        
                    drawList.PopClipRect();
                }

                if (isSelected)
                {
                    drawList.AddRect(_selectableScreenRect.Min - Vector2.One , _selectableScreenRect.Max + Vector2.One * 2, 
                                     UiColors.BackgroundFull.Fade(opacity));
                    drawList.AddRect(_selectableScreenRect.Min , _selectableScreenRect.Max + Vector2.One * 1, 
                                     UiColors.Selection.Fade(opacity));
                }
            }
        }
        ImGui.PopID();
            
        var connectionBorderArea = _selectableScreenRect;
        connectionBorderArea.Min.X -= 4;

        // Input Sockets...
        for (var inputIndex = 0; inputIndex < visibleInputUis.Count; inputIndex++)
        {
            var inputUi = visibleInputUis[inputIndex];
            var inputDefinition = inputUi.InputDefinition;

            var usableSlotArea = GetUsableInputSlotSize(inputIndex, visibleInputUis.Count);

            ImGui.PushID(childUi.SymbolChild.Id.GetHashCode() + inputDefinition.GetHashCode());
            ImGui.SetCursorScreenPos(usableSlotArea.Min);
            ImGui.InvisibleButton("input", usableSlotArea.GetSize());
            DrawUtils.DebugItemRect("input-slot");
                
            // Note: isItemHovered does not work when a connection is being dragged from another item
            var hovered = hasConnections
                              ? usableSlotArea.Contains(ImGui.GetMousePos())
                              : ImGui.IsItemHovered();
                
            hovered &= inActiveWindow;
                
            var isPotentialConnectionTarget = ConnectionMaker.IsMatchingInputType(_view, inputDefinition.DefaultValue.ValueType);
            var colorForType = ColorForInputType(inputDefinition).Fade(opacity);
                
            var connectedLines = _sorter.GetLinesToNodeInputSlot(childUi, inputDefinition.Id);
                
            // Render input Label
            if ((customUiResult & OpUi.CustomUiResult.PreventInputLabels) == 0)
            {
                var inputLabelOpacity = MathUtils.RemapAndClamp(_view.Scale.X,
                                                                0.75f, 1.5f,
                                                                0f, 1f) * opacity;
                
                var screenCursor = usableSlotArea.GetCenter() + new Vector2(14, -7);
                if (inputLabelOpacity > 0)
                {
                    ImGui.PushFont(Fonts.FontSmall);
                    var labelColor = ColorVariations.OperatorLabel.Apply(colorForType);
                    labelColor.Rgba.W = inputLabelOpacity;
                    var label = inputDefinition.Name;
                    if (inputDefinition.IsMultiInput)
                    {
                        label = $"  {label} [...]";
                    }
                
                    var labelSize = ImGui.CalcTextSize(label);
                    drawList.AddText(screenCursor, labelColor, label);
                
                    screenCursor += new Vector2(labelSize.X + 8, 0);
                
                    // Value
                    ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
                    //var inputSlot = instance.Inputs.Single(slot => inputDefinition.Id == slot.Id);
                    var inputSlot = instance.GetInput(inputDefinition.Id);
                    //var valueAsString = inputUi.GetSlotValue(inputSlot);
                    var valueAsString = ValueUtils.GetValueString(inputSlot);
                    // if (inputSlot is InputSlot<float> f)
                    // {
                    //     var xxx = f.TypedInputValue.Value;
                    // } 

                    var valueColor = labelColor;
                    valueColor.Rgba.W *= 0.6f;
                
                    // Avoid clipping because it increases ImGui draw calls and is expensive
                    var estimatedValueWidth = valueAsString.Length * 8;
                    var needClipping = estimatedValueWidth > _usableScreenRect.Max.X - screenCursor.X;
                    if (needClipping)
                    {
                        drawList.PushClipRect(_usableScreenRect.Min, _usableScreenRect.Max, true);
                    }
                
                    if(!string.IsNullOrEmpty(valueAsString))
                        drawList.AddText(screenCursor, valueColor, valueAsString);
                        
                    if (needClipping)
                        drawList.PopClipRect();
                
                    ImGui.PopStyleColor();
                
                    ImGui.PopFont();
                }
            }
                
            // Draw input slots
            if (inputDefinition.IsMultiInput)
            {
                var showGaps = isPotentialConnectionTarget;
                
                var socketCount = showGaps
                                      ? connectedLines.Count * 2 + 1
                                      : connectedLines.Count;
                
                var socketHeight = (usableSlotArea.GetHeight() + 1) / socketCount;
                var targetPos = new Vector2(usableSlotArea.Max.X - 4,
                                            usableSlotArea.Min.Y + socketHeight * 0.5f);
                
                var topLeft = new Vector2(usableSlotArea.Min.X, usableSlotArea.Min.Y);
                var socketSize = new Vector2(usableSlotArea.GetWidth(), socketHeight - SlotGaps);
                
                var reactiveSlotColor = GetReactiveSlotColor(inputDefinition.DefaultValue.ValueType, colorForType, SocketDirections.Input);
                
                for (var socketIndex = 0; socketIndex < socketCount; socketIndex++)
                {
                    var usableSocketArea = new ImRect(topLeft, topLeft + socketSize);
                
                    var isSocketHovered = usableSocketArea.Contains(ImGui.GetMousePos());
                    ConnectionSnapEndHelper.RegisterAsPotentialTarget(_view, childUi, inputUi, socketIndex, usableSocketArea);
                
                    bool isGap = false;
                    if (showGaps)
                    {
                        isGap = (socketIndex & 1) == 0;
                    }
                
                    if (!isGap)
                    {
                        var line = showGaps
                                       ? connectedLines[socketIndex >> 1]
                                       : connectedLines[socketIndex];
                        if (_isVisible && socketHeight > 10)
                        {
                            ImGui.PushStyleVar(ImGuiStyleVar.Alpha,
                                               MathUtils.RemapAndClamp(socketHeight, 10, 20, 0, 0.5f).Clamp(0, 0.5f) * ImGui.GetStyle().Alpha);
                            ImGui.PushFont(Fonts.FontSmall);
                            //ImGui.SetCursorScreenPos(targetPos +  new Vector2(0, -ImGui.GetFontSize()/2));
                            //ImGui.Value(socketIndex % 4 == 0 ? ">" : "", socketIndex);
                
                            var sockedInputIndex = showGaps ? socketIndex / 2 : socketIndex;
                            var isFourGroupStart = sockedInputIndex > 0 && sockedInputIndex % 4 == 0;
                            var markerForFourAligned = isFourGroupStart ? " <" : "";
                            
                            if(isFourGroupStart)
                                drawList.AddRectFilled(topLeft + new Vector2(socketSize.X * 0.5f,-2), 
                                                       topLeft + new Vector2(_usableScreenRect.GetWidth()*0.25f,0), UiColors.ForegroundFull.Fade(0.1f));
                            
                            drawList.AddText(targetPos + new Vector2(7, -ImGui.GetFontSize() / 2),
                                             new Color(MathUtils.RemapAndClamp(socketHeight, 10, 20, 0, 0.5f).Clamp(0, 0.5f)),
                                             $"{sockedInputIndex}" + markerForFourAligned);
                            ImGui.PopFont();
                            ImGui.PopStyleVar();
                        }

                        var isChildSelected = _projectView.NodeSelection.IsNodeSelected(childUi);
                
                        line.TargetPosition = targetPos;
                        line.TargetNodeArea = connectionBorderArea;
                        line.IsSelected |= isChildSelected | isSocketHovered | isNodeHovered;
                        line.FramesSinceLastUsage = framesSinceLastUpdate;
                        line.IsAboutToBeReplaced = ConnectionSnapEndHelper.IsNextBestTarget(childUi, inputDefinition.Id, socketIndex);
                    }
                
                    DrawMultiInputSocket(drawList, childUi, inputDefinition, usableSocketArea, isSocketHovered, socketIndex, isGap, colorForType, reactiveSlotColor, instance);
                
                    targetPos.Y += socketHeight;
                    topLeft.Y += socketHeight;
                }
                
                if (_isVisible)
                {
                    drawList.AddRectFilled(new Vector2(usableSlotArea.Max.X - 8, usableSlotArea.Min.Y),
                                           new Vector2(usableSlotArea.Max.X - 1, usableSlotArea.Min.Y + 2),
                                           reactiveSlotColor);
                
                    drawList.AddRectFilled(new Vector2(usableSlotArea.Max.X - 8, usableSlotArea.Max.Y - 2),
                                           new Vector2(usableSlotArea.Max.X - 1, usableSlotArea.Max.Y),
                                           reactiveSlotColor);
                    if (socketCount == 0)
                    {
                        drawList.AddRectFilled(new Vector2(usableSlotArea.Max.X - 2, usableSlotArea.Min.Y + 1),
                                               new Vector2(usableSlotArea.Max.X - 1, usableSlotArea.Max.Y-2),
                                               reactiveSlotColor);
                            
                        if (inputUi.Relevancy == Relevancy.Required)
                        {
                            var pos = usableSlotArea.GetCenter();
                                
                            var size = _view.Scale.X * 3;
                                
                            drawList.AddCircleFilled(
                                                     pos + new Vector2(-5, -(size *1f)),
                                                     MathF.Min(3, size - 1),
                                                     reactiveSlotColor
                                                    );
                            drawList.AddCircleFilled(
                                                     pos + new Vector2(-5, +(size * 1f)),
                                                     MathF.Min(3, size - 1),
                                                     reactiveSlotColor
                                                    );                                
                        }
                    }
                }
            }
            else
            {
                ConnectionSnapEndHelper.RegisterAsPotentialTarget(_view, childUi, inputUi, 0, usableSlotArea);
                //ConnectionMaker.ConnectionSnapEndHelper.IsNextBestTarget(targetUi, inputDef.Id,0)
                var isAboutToBeReconnected = ConnectionSnapEndHelper.IsNextBestTarget(childUi, inputDefinition.Id, 0);
                var isChildSelected = _projectView.NodeSelection.IsNodeSelected(childUi);
                foreach (var line in connectedLines)
                {
                    line.TargetPosition = new Vector2(usableSlotArea.Max.X - 4,
                                                      usableSlotArea.GetCenter().Y);
                    line.TargetNodeArea = connectionBorderArea;
                    line.IsSelected |= isChildSelected | hovered | isNodeHovered;
                    line.IsAboutToBeReplaced = isAboutToBeReconnected;
                    line.FramesSinceLastUsage = framesSinceLastUpdate;
                }
                
                if (_isVisible)
                {
                    var isMissing = inputUi.Relevancy == Relevancy.Required && connectedLines.Count == 0;
                    DrawInputSlot(drawList, childUi, inputDefinition, usableSlotArea, colorForType, hovered, isMissing, instance);
                }
            }

            ImGui.PopID();
        }

        // Outputs sockets...
        var outputIndex = 0;
        //foreach(var output in instance.Outputs)
        var canvasScale = _view.Scale;
        foreach (var outputDef in childUi.SymbolChild.Symbol.OutputDefinitions)
        {
            var output = instance.Outputs[outputIndex];
            var usableArea = GetUsableOutputSlotArea(canvasScale, childUi, outputIndex);


            if (!preventInteraction)
            {
                ImGui.SetCursorScreenPos(usableArea.Min);
            }
                
            ImGui.PushID(childUi.SymbolChild.Id.GetHashCode() + outputDef.Id.GetHashCode());
            ImGui.InvisibleButton("output", usableArea.GetSize());
            DrawUtils.DebugItemRect();
            var valueType = outputDef.ValueType;
            var colorForType = TypeUiRegistry.GetPropertiesForType(valueType).Color.Fade(opacity);
            
            //Note: isItemHovered does not work when dragging is active
            var hovered = inActiveWindow &&
                          hasConnections
                              ? usableArea.Contains(ImGui.GetMousePos())
                              : ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup);
            
            // Update connection lines
            var dirtyFlagNumUpdatesWithinFrame = output.DirtyFlag.NumUpdatesWithinFrame;

            var isChildSelected = _projectView.NodeSelection.IsNodeSelected(childUi);
            foreach (var line in _sorter.GetLinesFromNodeOutput(childUi, outputDef.Id))
            {
                line.SourcePosition = new Vector2(usableArea.Max.X, usableArea.GetCenter().Y);
                line.SourceNodeArea = _selectableScreenRect;
                line.IsSelected |= hovered;
                line.ColorForType = colorForType;
                line.UpdateCount = output.DirtyFlag.NumUpdatesWithinFrame;
            
                if (childUi.ConnectionStyleOverrides.ContainsKey(outputDef.Id))
                {
                    line.ColorForType.Rgba.W = 0.1f;
                }
            
                line.IsSelected |= isChildSelected || hovered | isNodeHovered;
            }
            
            {
            
                // Visualize update
                if (_isVisible)
                {
                    // Draw update indicator
                    var trigger = output.DirtyFlag.Trigger;
                    if (trigger != DirtyFlagTrigger.None && usableArea.GetHeight() > 6)
                    {
                        var r = usableArea.GetWidth() / 4;
                        var center = new Vector2(usableArea.Max.X + 2*r, usableArea.GetCenter().Y - 3*r);
                        if (trigger == DirtyFlagTrigger.Always)
                        {
                            drawList.AddCircle(center, r, colorForType,3);
                        }
                        else if (trigger == DirtyFlagTrigger.Animated)
                        {
                            drawList.AddCircleFilled(center, r, colorForType, 3);
                        }
                    }
                        
                    DrawOutput(drawList, childUi, outputDef, usableArea, colorForType, hovered, instance);
                    if (dirtyFlagNumUpdatesWithinFrame > 0)
                    {
                        var movement = (float)(ImGui.GetTime() * dirtyFlagNumUpdatesWithinFrame) % 1f * (usableArea.GetWidth() - 1);
                        drawList.AddRectFilled(new Vector2(usableArea.Min.X + movement - 1, usableArea.Min.Y),
                                               new Vector2(usableArea.Min.X + movement + 1, usableArea.Max.Y),
                                               new Color(0.2f));
                    }
                }
            }
            
            outputIndex++;
            ImGui.PopID();
        }
            
    }

        
    // // todo - move outta here
    // internal static OpUi.CustomUiResult DrawCustomUi(Instance instance, ImDrawListPtr drawList, ImRect selectableScreenRect, Vector2 canvasScale)
    // {
    //     var type = instance.Type;
    //     // var result = CustomChildUiRegistry.TryGetValue(instance.Symbol.Id, out var drawFunction) 
    //     //                  ? drawFunction(instance, drawList, selectableScreenRect, canvasScale) 
    //     //                  : OpUi.CustomUiResult.None;
    //         
    //     return result;
    // }

    private void DrawIndicator(ImDrawListPtr drawList, Color color, float opacity, ref int indicatorCount)
    {
        const int s = 10;
        var dx = (s + 1) * indicatorCount;

        var pMin = new Vector2(_usableScreenRect.Max.X - 2 - s - dx,
                               (_usableScreenRect.Max.Y - 2 - s).Clamp(_usableScreenRect.Min.Y + 2, _usableScreenRect.Max.Y));
        var pMax = new Vector2(_usableScreenRect.Max.X - 2 - dx, _usableScreenRect.Max.Y - 2);
        drawList.AddRectFilled(pMin, pMax, color.Fade(opacity));
        drawList.AddRect(pMin-Vector2.One, 
                         pMax+Vector2.One, 
                         UiColors.WindowBackground.Fade(0.4f * opacity));
        indicatorCount++;
    }

    private void AdjustGroupLayoutAfterResize(ISelectableCanvasObject childUi, Vector2 newNodeSize, SymbolUi parentUi)
    {
        if (childUi.Size == newNodeSize)
            return;

        var groupMembers = _view.SelectableNodeMovement.FindSnappedNeighbours(childUi);
        if (groupMembers.Count > 0)
        {
            var heightDelta = newNodeSize.Y - childUi.Size.Y;
            var offset = new Vector2(0, heightDelta);
            if (heightDelta > 0)
            {
                foreach (var neighbour in groupMembers)
                {
                    if (neighbour == childUi)
                        return;

                    if (neighbour.PosOnCanvas.Y > childUi.PosOnCanvas.Y
                        && Math.Abs(neighbour.PosOnCanvas.X - childUi.PosOnCanvas.X) < SelectableNodeMovement.Tolerance)
                    {
                        neighbour.PosOnCanvas += offset;
                    }
                }
            }

            else if (heightDelta < 0)
            {
                foreach (var neighbour in groupMembers)
                {
                    if (neighbour == childUi)
                        return;

                    if (neighbour.PosOnCanvas.Y > childUi.PosOnCanvas.Y
                        && Math.Abs(neighbour.PosOnCanvas.X - childUi.PosOnCanvas.X) < SelectableNodeMovement.Tolerance)
                    {
                        neighbour.PosOnCanvas += offset;
                    }
                }
            }
        }

        childUi.Size = newNodeSize;
    }

    // Find visible input slots.
    // TODO: this is a major performance hot spot and needs optimization
    private List<IInputUi> FindVisibleInputUis(SymbolUi symbolUi, SymbolUi.Child childUi, ref bool nodeHasHiddenMatchingInputs)
    {
        var connectionsToNode = _sorter.GetLinesIntoNode(childUi);

        if (childUi.Style == SymbolUi.Child.Styles.Expanded)
        {
            return symbolUi.InputUis.Values.ToList();
        }

        var tempConnections = ConnectionMaker.GetTempConnectionsFor(_view);

        var isNodeHoveredAsConnectionTarget = _hoveredNodeIdForConnectionTarget == childUi.Id
                                              && tempConnections.Count == 1
                                              && tempConnections[0].TargetParentOrChildId == ConnectionMaker.NotConnectedId
                                              && tempConnections[0].SourceParentOrChildId != childUi.Id;

        _visibleInputs.Clear();
        foreach (var inputUi in symbolUi.InputUis.Values)
        {
            bool inputIsConnectionTarget = false;
            for (int i = 0; i < connectionsToNode.Count; i++)
            {
                if (connectionsToNode[i].Connection.TargetSlotId == inputUi.Id)
                {
                    inputIsConnectionTarget = true;
                    break;
                }
            }

            if (inputUi.Relevancy != Relevancy.Optional || inputIsConnectionTarget)
            {
                _visibleInputs.Add(inputUi);
            }
            else if (ConnectionMaker.IsMatchingInputType(_view, inputUi.Type))
            {
                if (isNodeHoveredAsConnectionTarget)
                {
                    _visibleInputs.Add(inputUi);
                }
                else
                {
                    nodeHasHiddenMatchingInputs = true;
                }
            }
        }

        return _visibleInputs;
    }

    private enum SocketDirections
    {
        Input,
        Output,
    }

    private Color GetReactiveSlotColor(Type type, Color colorForType, SocketDirections direction)
    {
        var style = direction == SocketDirections.Input
                        ? ColorVariations.ConnectionLines
                        : ColorVariations.OperatorBackground;
        if (ConnectionMaker.GetTempConnectionsFor(_view).Count > 0)
        {
            if (direction == SocketDirections.Input
                    ? ConnectionMaker.IsMatchingInputType(_view, type)
                    : ConnectionMaker.IsMatchingOutputType(_view, type))
            {
                var blink = (float)(Math.Sin(ImGui.GetTime() * 10) / 2f + 0.8f);
                colorForType.Rgba.W *= blink;
                style = ColorVariations.Highlight;
            }
            else
            {
                style = ColorVariations.OperatorBackgroundIdle;
            }
        }

        return style.Apply(colorForType);
    }

    /// <summary>
    /// Set
    /// </summary>
    /// <param name="instance"></param>
    private void PreparePreviewAndExpandSelectableArea(Instance instance)
    {
        _previewTextureView = null;
        if (instance.Outputs.Count == 0)
            return;

        var firstOutput = instance.Outputs[0];
        if (firstOutput is not Slot<Texture2D> textureSlot)
            return;

        var texture = textureSlot.Value;
        if (texture == null || texture.IsDisposed)
            return;

        _previewTextureView = SrvManager.GetSrvForTexture(texture);

        var aspect = (float)texture.Description.Width / texture.Description.Height;
        var opWidth = _usableScreenRect.GetWidth();
        var previewSize = new Vector2(opWidth, opWidth / aspect);

        if (previewSize.Y > opWidth)
        {
            previewSize *= opWidth / previewSize.Y;
        }

        var min = new Vector2(_usableScreenRect.Min.X, _usableScreenRect.Min.Y - previewSize.Y - 1);
        var max = new Vector2(_usableScreenRect.Min.X + previewSize.X, _usableScreenRect.Min.Y - 1);
        _selectableScreenRect.Add(min);
        _previewArea = new ImRect(min, max);
    }

    private static ImRect _previewArea;
    private static ShaderResourceView _previewTextureView;

    private void DrawPreview(ImDrawListPtr drawList, float opacity)
    {
        if (_previewTextureView == null)
            return;

        drawList.AddImage((IntPtr)_previewTextureView, _previewArea.Min, 
                          _previewArea.Max,
                          Vector2.Zero,
                          Vector2.One,
                          Color.White.Fade(opacity));
    }

    private Vector2 ComputeNodeSize(SymbolUi.Child childUi, List<IInputUi> visibleInputUis, Legacy.Graph.ConnectionSorter connectionSorter)
    {
        if (childUi.Style == SymbolUi.Child.Styles.Resizable)
        {
            return childUi.Size;
        }

        var additionalMultiInputSlots = 0;
        foreach (var input in visibleInputUis)
        {
            if (!input.InputDefinition.IsMultiInput)
                continue;

            //TODO: this should be refactored, because it's very slow and is later repeated
            var connectedLines = connectionSorter.GetLinesToNodeInputSlot(childUi, input.Id);
            additionalMultiInputSlots += connectedLines.Count;
        }

        return new Vector2(SymbolUi.Child.DefaultOpSize.X,
                           23 + (visibleInputUis.Count + additionalMultiInputSlots) * 13);
    }

    private void DrawOutput(ImDrawListPtr drawList, SymbolUi.Child childUi, Symbol.OutputDefinition outputDef, ImRect usableArea, Color colorForType,
                            bool hovered, Instance instance)
    {
        if (ConnectionMaker.IsOutputSlotCurrentConnectionSource(_view, childUi, outputDef))
        {
            drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                   ColorVariations.Highlight.Apply(colorForType));
                
            var isMouseReleasedWithoutDrag =
                ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() < UserSettings.Config.ClickThreshold;
            if (isMouseReleasedWithoutDrag)
            {
                //Graph.Connections.GetLinesFromNodeOutput(childUi, outputDef.Id);

                // This method is complex, because it has to handle several edge cases and has potential to remove previous user data:
                // - We have to preserve the previous state.
                // - We have to make space -> Shift all connected operators towards the right.
                // - We have to convert all existing connections from the output into temporary connections.
                // - We have to insert a new temp connection line between output and symbol browser
                //
                // - If the user completes the symbol browser, it must complete the previous connections from the temp connections.
                // - If the user cancels the operation, the previous state has to be restored. This might be tricky
                ConnectionMaker.InitSymbolBrowserAtPosition(_view, _view.SymbolBrowser,
                                                            childUi.PosOnCanvas + new Vector2(childUi.Size.X + SelectableNodeMovement.SnapPadding.X, 0));
            }
        }
        else if (hovered)
        {
            if (ConnectionMaker.IsMatchingOutputType(_view, outputDef.ValueType))
            {
                drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                       ColorVariations.OperatorBackgroundHover.Apply(colorForType));

                if (ImGui.IsMouseReleased(0))
                {
                    ConnectionMaker.CompleteAtOutputSlot(_view, instance, childUi, outputDef);
                }
            }
            else
            {
                if (_isVisible)
                {
                    drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                           ColorVariations.OperatorBackgroundHover.Apply(colorForType));

                    var output = instance.Outputs.Single(output2 => output2.Id == outputDef.Id);

                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($".{outputDef.Name}");
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextColored(UiColors.Gray, $"<{TypeNameRegistry.Entries[outputDef.ValueType]}>\n{output.DirtyFlag.NumUpdatesWithinFrame} Updates\n({output.DirtyFlag.Trigger})");
                    ImGui.PopFont();
                    ImGui.EndTooltip();

                    if(ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _dragInfo.DraggedOutputOpId = childUi.Id;
                        _dragInfo.DraggedOutputDefId = outputDef.Id;
                    }

                    // Clicked
                    else
                    {
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            _dragInfo.DraggedOutputOpId = Guid.Empty;
                            _dragInfo.DraggedOutputDefId = Guid.Empty;
                            if (ImGui.GetMouseDragDelta().Length() < UserSettings.Config.ClickThreshold)
                            {
                                ConnectionMaker.OpenSymbolBrowserAtOutput(_projectView, childUi, instance, output.Id);
                            }
                        }
                        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right) && ImGui.GetIO().KeyCtrl)
                        {
                            _view.EditNodeOutputDialog.OpenForOutput(_projectView.CompositionInstance.Symbol, childUi, outputDef);
                        }
                    }
                }
            }
        }
        else if (_dragInfo.DraggedOutputOpId == childUi.Id && _dragInfo.DraggedOutputDefId == outputDef.Id)
        {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left)
                && ImGui.GetMouseDragDelta().Length() > UserSettings.Config.ClickThreshold)
            {
                _dragInfo.DraggedOutputOpId = Guid.Empty;
                _dragInfo.DraggedOutputDefId = Guid.Empty;
                ConnectionMaker.StartFromOutputSlot(_view, _projectView.NodeSelection, childUi, outputDef);
            }
        }
        else
        {
            var color = GetReactiveSlotColor(outputDef.ValueType, colorForType, SocketDirections.Output);
            var pos = usableArea.Min;
            drawList.AddRectFilled(
                                   pos,
                                   usableArea.Max,
                                   color
                                  );
        }
    }

    private ImRect GetUsableOutputSlotArea(Vector2 canvasScale, SymbolUi.Child targetUi, int outputIndex)
    {
        var thickness = (int)MathUtils.RemapAndClamp(canvasScale.X, 0.5f, 1.2f, (int)(UsableSlotThickness * 0.5f), UsableSlotThickness ) * T3Ui.UiScaleFactor ;

        var opRect = _usableScreenRect;
        var outputCount = targetUi.SymbolChild.Symbol.OutputDefinitions.Count;
        var outputHeight = outputCount == 0
                               ? opRect.GetHeight()
                               : (opRect.GetHeight() - 1 + SlotGaps) / outputCount - SlotGaps;
        if (outputHeight <= 0)
            outputHeight = 1;

        return ImRect.RectWithSize(
                                   new Vector2(
                                               opRect.Max.X + 1, // - GraphNode._usableSlotThickness,
                                               opRect.Min.Y + (outputHeight + SlotGaps) * outputIndex + 1
                                              ),
                                   new Vector2(
                                               thickness,
                                               outputHeight
                                              ));
    }

    /// <summary>
    /// Draws slot for non multi-input
    /// </summary>
    private void DrawInputSlot(ImDrawListPtr drawList, SymbolUi.Child targetUi, Symbol.InputDefinition inputDef, ImRect usableArea, Color colorForType, bool hovered,
                               bool isMissing, Instance instance)
    {
        var parentSymbol = instance.Parent.Symbol;
            
        if (ConnectionMaker.IsInputSlotCurrentConnectionTarget(_view, targetUi, inputDef))
        {
        }
        else if (ConnectionSnapEndHelper.IsNextBestTarget(targetUi, inputDef.Id, 0) || hovered)
        {
            if (ConnectionMaker.IsMatchingInputType(_view, inputDef.DefaultValue.ValueType))
            {
                drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                       ColorVariations.OperatorBackgroundHover.Apply(colorForType));

                if (ImGui.IsMouseReleased(0))
                {
                    ConnectionMaker.CompleteAtInputSlot(_view, instance, targetUi, inputDef);
                }
            }
            else
            {
                drawList.AddRectFilled(
                                       usableArea.Min,
                                       usableArea.Max,
                                       ColorVariations.OperatorBackgroundHover.Apply(colorForType)
                                      );

                Symbol.Connection connection;
                Symbol.Child sourceOp = null;
                Symbol.Child.Output output = null;
                ImGui.BeginTooltip();
                {
                    DrawInputSources(targetUi, inputDef, instance);

                        
                    connection = parentSymbol.Connections.SingleOrDefault(c => c.TargetParentOrChildId == targetUi.Id
                                                                               && c.TargetSlotId == inputDef.Id);
                    if (connection != null)
                    {
                        if (parentSymbol.Children.TryGetValue(connection.SourceParentOrChildId, out sourceOp))
                        {
                            if (!sourceOp.Outputs.TryGetValue(connection.SourceSlotId, out output))
                            {
                                parentSymbol.Connections.Remove(connection);
                            }
                            //output = sourceOp.Outputs[connection.SourceSlotId];
                        }
                    }

                    ImGui.TextUnformatted($".{inputDef.Name}");
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextColored(UiColors.Gray, $"<{TypeNameRegistry.Entries[inputDef.DefaultValue.ValueType]}>");
                    ImGui.PopFont();
                    if (isMissing)
                    {
                        FormInputs.AddVerticalSpace(5);
                        ImGui.TextColored(UiColors.StatusAttention, $"Requires input");   
                    }
                }
                ImGui.EndTooltip();

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    var createCopy = ImGui.GetIO().KeyCtrl && connection != null;
                    if (createCopy)
                    {
                        if (sourceOp != null)
                        {
                            Log.Debug("Cloning connection from source op...");
                            var sourceOpUi = parentSymbol.GetSymbolUi().ChildUis[sourceOp.Id]!;
                            ConnectionMaker.StartFromOutputSlot(_view, _projectView.NodeSelection, sourceOpUi, output.OutputDefinition);
                        }
                        else if (connection.IsConnectedToSymbolInput)
                        {
                            Log.Debug("Cloning connection from input node...");
                            var inputDef2 = parentSymbol.InputDefinitions.Single(id => id.Id == connection.SourceSlotId);
                            ConnectionMaker.StartFromInputNode(_view, inputDef2);
                        }
                        else
                        {
                            Log.Warning("This should not happen. Please contact customer support.");
                        }
                    }
                    else
                    {
                        _dragInfo.DraggedInputOpId = targetUi.Id;
                        _dragInfo.DraggedInputDefId = inputDef.Id;
                    }
                }
                else
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        _dragInfo.DraggedInputOpId = Guid.Empty;
                        _dragInfo.DraggedInputDefId = Guid.Empty;
                        if (ImGui.GetMouseDragDelta().Length() < UserSettings.Config.ClickThreshold)
                        {
                            ConnectionMaker.StartFromInputSlot(_view, targetUi.SymbolChild.Parent, targetUi, inputDef);
                            if (SymbolUiRegistry.TryGetSymbolUi(parentSymbol.Id, out var compositionUi))
                            {
                                var freePosition = NodeGraphLayouting.FindPositionForNodeConnectedToInput(compositionUi, targetUi);
                                ConnectionMaker.InitSymbolBrowserAtPosition(_view, _view.SymbolBrowser, freePosition);
                            }
                        }
                        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right) && ImGui.GetIO().KeyCtrl)
                        {
                            ConnectionMaker.StartFromInputSlot(_view, parentSymbol, targetUi, inputDef);
                        }
                    }
                }
            }
        }
        else if (_dragInfo.DraggedInputOpId == targetUi.Id && _dragInfo.DraggedInputDefId == inputDef.Id)
        {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left)
                && ImGui.GetMouseDragDelta().Length() > UserSettings.Config.ClickThreshold)
            {
                _dragInfo.DraggedInputOpId = Guid.Empty;
                _dragInfo.DraggedInputDefId = Guid.Empty;
                ConnectionMaker.StartFromInputSlot(_view, parentSymbol, targetUi, inputDef);
            }
        }            
        else
        {
            var connectionColor = GetReactiveSlotColor(inputDef.DefaultValue.ValueType, colorForType, SocketDirections.Input);
            var pos = new Vector2(
                                  usableArea.Max.X - GraphNode.InputSlotThickness - InputSlotMargin,
                                  usableArea.Min.Y
                                 );
            var size = new Vector2(GraphNode.InputSlotThickness, usableArea.GetHeight());
            drawList.AddRectFilled(
                                   pos,
                                   pos + size,
                                   connectionColor
                                  );

            if (isMissing)
            {
                drawList.AddCircleFilled(
                                         pos + new Vector2(-6, size.Y / 2),
                                         MathF.Min(3, size.Y - 1),
                                         connectionColor
                                        );
            }
        }
    }

    private void DrawInputSources(SymbolUi.Child targetUi, Symbol.InputDefinition inputDef, Instance instance, int inputIndex = 0)
    {
        var sources = CollectSourcesForInput(instance.Parent, targetUi, inputDef, inputIndex);
        if (sources.Count <= 0)
            return;

        ImGui.PushFont(Fonts.FontSmall);
        foreach (var source in sources)
        {
            ImGui.TextColored(UiColors.Gray, source);
        }

        ImGui.PopFont();
    }

    /// <summary>
    /// Crawl down the graph and collect a list of inputs that contributed to the given input   
    /// </summary>
    private List<string> CollectSourcesForInput(Instance compositionOp, SymbolUi.Child targetUi,
                                                Symbol.InputDefinition inputDef, int inputIndex)
    {
        var sources = new List<string>();
        var compositionUi = compositionOp.GetSymbolUi();

        while (true)
        {
            Symbol.Connection connection = null;
            if (inputDef.IsMultiInput)
            {
                var connections = compositionOp.Symbol.Connections.Where(c => c.TargetParentOrChildId == targetUi.Id
                                                                              && c.TargetSlotId == inputDef.Id).ToList();
                if (connections.Count > 0 && connections.Count > inputIndex)
                {
                    connection = connections[inputIndex];
                }
            }
            else
            {
                connection = compositionUi.Symbol.Connections.FirstOrDefault(c => c.TargetParentOrChildId == targetUi.Id
                                                                                  && c.TargetSlotId == inputDef.Id
                                                                            );
            }

            if (connection == null)
                break;

            if (connection.IsConnectedToSymbolInput)
            {
                var compInputDef = compositionUi.Symbol.InputDefinitions.SingleOrDefault(inp => inp.Id == connection.SourceSlotId);
                var input = compositionOp.Inputs.SingleOrDefault(inp => inp.Id == connection.SourceSlotId);
                sources.Insert(0, $". {compInputDef?.Name}  " + ValueUtils.GetValueString(input?.Input.Value));
                break;
            }

            var connectionSourceId = connection.SourceParentOrChildId;

            if (compositionUi.ChildUis.TryGetValue(connectionSourceId, out var connectionSourceUi) && 
                compositionOp.Children.TryGetChildInstance(connectionSourceId, out var instance))
            {
                var outputDef = connectionSourceUi.SymbolChild.Symbol.OutputDefinitions.SingleOrDefault(outp => outp.Id == connection.SourceSlotId);
                var output = instance.Outputs.SingleOrDefault(outp => outp.Id == connection.SourceSlotId);

                var outputName = (instance.Outputs.Count > 1 && outputDef?.Name != "Output" && outputDef?.Name != "Result")
                                     ? "." + outputDef?.Name
                                     : "";
                sources.Insert(0, $"{connectionSourceUi?.SymbolChild.ReadableName} {outputName}  " + GetValueString(output));
            }

            if (connectionSourceUi?.SymbolChild.Symbol.InputDefinitions.Count > 0)
            {
                targetUi = connectionSourceUi;
                inputDef = connectionSourceUi?.SymbolChild.Symbol.InputDefinitions[0]; // FIXME: this should pick the first connected.
            }
            else
            {
                break;
            }
        }

        return sources;
    }

    private static string GetValueString(ISlot outputSlot)
    {
        return outputSlot switch
                   {
                       Slot<float> f                    => $"{f.Value:0.00}",
                       Slot<int> i                      => $"{i.Value:G3}",
                       Slot<Int3> i                     => $"{i.Value:G3}",
                       Slot<bool> b                     => $"{b.Value}",
                       Slot<System.Numerics.Vector3> v3 => $"{v3.Value:0.0}",
                       Slot<System.Numerics.Vector2> v2 => $"{v2.Value:0.0}",
                       Slot<string> s                   => s.Value.Truncate(),
                       _                                => ""
                   };
    }

    private void DrawMultiInputSocket(ImDrawListPtr drawList, SymbolUi.Child targetUi, Symbol.InputDefinition inputDef, ImRect usableArea,
                                      bool isInputHovered, int multiInputIndex, bool isGap, Color colorForType,
                                      Color reactiveSlotColor, Instance instance)
    {
        if (ConnectionMaker.IsInputSlotCurrentConnectionTarget(_view, targetUi, inputDef, multiInputIndex))
        {
        }
        else if (ConnectionSnapEndHelper.IsNextBestTarget(targetUi, inputDef.Id, multiInputIndex) || isInputHovered)
        {
            if (ConnectionMaker.IsMatchingInputType(_view, inputDef.DefaultValue.ValueType))
            {
                drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                       ColorVariations.OperatorBackgroundHover.Apply(colorForType));

                if (ImGui.IsMouseReleased(0))
                {
                    ConnectionMaker.CompleteAtInputSlot(_view, instance, targetUi, inputDef, multiInputIndex, true);
                }
            }
            else
            {
                drawList.AddRectFilled(
                                       usableArea.Min,
                                       usableArea.Max,
                                       ColorVariations.OperatorBackgroundHover.Apply(colorForType)
                                      );

                ImGui.BeginTooltip();
                {
                    DrawInputSources(targetUi, inputDef, instance, multiInputIndex);

                    // var connectionSource = "";
                    // var connections = GraphCanvas.Current.CompositionOp.Symbol.Connections.Where(c => c.TargetParentOrChildId == targetUi.Id
                    //                                                                                  && c.TargetSlotId == inputDef.Id).ToList();
                    // if (connections.Count > 0 && connections.Count > multiInputIndex)
                    // {
                    //     var connection = connections[multiInputIndex];
                    //
                    //     // var sourceOp =
                    //     //     GraphCanvas.Current.CompositionOp.Symbol.Children.SingleOrDefault(child => child.Id == connection.SourceParentOrChildId);
                    //     // if (sourceOp != null)
                    //     // {
                    //     //     var output = sourceOp.Outputs[connection.SourceSlotId];
                    //     //     connectionSource = sourceOp.ReadableName + "." + output.OutputDefinition.Name;
                    //     //     //connectionSource = sourceOp.ReadableName;
                    //     // }
                    // }

                    // if (!string.IsNullOrEmpty(connectionSource))
                    // {
                    //     ImGui.PushFont(Fonts.FontSmall);
                    //     ImGui.TextColored(Color.Gray, $"{connectionSource} -> ");
                    //     ImGui.PopFont();
                    // }

                    ImGui.TextUnformatted($".{inputDef.Name}");
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextColored(UiColors.Gray, $"<{TypeNameRegistry.Entries[inputDef.DefaultValue.ValueType]}>");
                    ImGui.PopFont();
                    //ImGui.PopStyleVar();
                }
                ImGui.EndTooltip();
                //ImGui.SetTooltip($"-> .{inputDef.Name}[{multiInputIndex}] <{TypeNameRegistry.Entries[inputDef.DefaultValue.ValueType]}>");
                if (ImGui.IsItemClicked(0))
                {
                    ConnectionMaker.StartFromInputSlot(_view, instance.Parent.Symbol, targetUi, inputDef, multiInputIndex);
                    Log.Debug("started connection at MultiInputIndex:" + multiInputIndex);
                }
            }
        }
        else
        {
            //var pos = usableArea.Min + Vector2.UnitY * GraphNode._inputSlotMargin;
            var gapOffset = isGap ? new Vector2(2, 0) : Vector2.Zero;
            var pos = new Vector2(usableArea.Max.X - InputSlotMargin - InputSlotThickness,
                                  usableArea.Min.Y) - gapOffset;
            var size = new Vector2(InputSlotThickness, usableArea.GetHeight()) + gapOffset;
            drawList.AddRectFilled(
                                   pos,
                                   pos + size,
                                   reactiveSlotColor
                                  );
        }
    }

    private ImRect GetUsableInputSlotSize(int inputIndex, int visibleSlotCount)
    {
        var areaForParams = new ImRect(new Vector2(
                                                   _usableScreenRect.Min.X,
                                                   _usableScreenRect.Min.Y + NodeTitleHeight),
                                       _usableScreenRect.Max);
        var inputHeight = visibleSlotCount == 0
                              ? areaForParams.GetHeight()
                              : (areaForParams.GetHeight() + SlotGaps) / visibleSlotCount - SlotGaps;
        if (inputHeight <= 0)
            inputHeight = 1;

        return ImRect.RectWithSize(
                                   new Vector2(
                                               areaForParams.Min.X - UsableSlotThickness,
                                               Math.Min(_selectableScreenRect.Max.Y, areaForParams.Min.Y + (inputHeight + SlotGaps) * inputIndex)
                                              ),
                                   new Vector2(
                                               UsableSlotThickness,
                                               inputHeight
                                              ));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color ColorForInputType(Symbol.InputDefinition inputDef)
    {
        return TypeUiRegistry.GetPropertiesForType(inputDef.DefaultValue.ValueType).Color;
    }

    #region style variables
    public static Vector2 LabelPos = new(4, 2);
    public static float UsableSlotThickness = 10;
    public static float InputSlotThickness = 3;
    public static float InputSlotMargin = 1;
    public static float SlotGaps = 2;
    public static float OutputSlotMargin = 1;
    #endregion

    private static readonly string _unfoldLabel = (char)Icon.ChevronLeft + "##size";
    private static readonly string _foldLabel = (char)Icon.ChevronDown + "##size";
    private static readonly List<IInputUi> _visibleInputs = new(15); // A static variable to avoid GC allocations

    private static readonly EvaluationContext _evaluationContext = new();

    private static readonly ImageOutputCanvas _imageCanvasForTooltips = new() { DisableDamping = true };
    private static Guid _hoveredNodeIdForConnectionTarget;

    private readonly GraphView _view;
    private readonly ProjectView _projectView;
    private readonly Legacy.Graph.ConnectionSorter _sorter;
    private readonly DraggedIdInfo _dragInfo;
    private static readonly Dictionary<IGraphView, DraggedIdInfo> DraggedIds = new();
        
    private ImRect _usableScreenRect;
    private ImRect _selectableScreenRect;
    private bool _isVisible;
    private const float NodeTitleHeight = 22;

    private sealed class DraggedIdInfo()
    {
        public Guid DraggedOutputOpId;
        public Guid DraggedOutputDefId;
            
        public Guid DraggedInputOpId;
        public Guid DraggedInputDefId;
    }
}