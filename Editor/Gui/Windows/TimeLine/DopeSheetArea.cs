#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Animation;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine;

internal sealed class DopeSheetArea : AnimationParameterEditing, ITimeObjectManipulation, IValueSnapAttractor
{
    public DopeSheetArea(ValueSnapHandler snapHandler, TimeLineCanvas timeLineCanvas)
    {
        _snapHandler = snapHandler;
        TimeLineCanvas = timeLineCanvas;
    }

    private TimeLineCanvas.AnimationParameter _currentAnimationParameter;

    public void Draw(Instance compositionOp, List<TimeLineCanvas.AnimationParameter> animationParameters)
    {
        MouseClickChangedSelection = false;
        var symbolUi = compositionOp.GetSymbolUi();
        if (CurvesTablesNeedsRefresh)
        {
            RebuildCurveTables();
            CurvesTablesNeedsRefresh = false;
        }
                
        var drawList = ImGui.GetWindowDrawList();
            
        AnimationParameters = animationParameters;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectionCountBeforeClick = SelectedKeyframes.Count;
        }
            

        ImGui.BeginGroup();
        {
            if (KeyboardBinding.Triggered(UserActions.FocusSelection))
            {
                ViewAllOrSelectedKeys(alsoChangeTimeRange: true);
            }

            if (KeyboardBinding.Triggered(UserActions.Duplicate))
            {
                symbolUi.FlagAsModified();
                DuplicateSelectedKeyframes(TimeLineCanvas.Playback.TimeInBars);
            }

            if (KeyboardBinding.Triggered(UserActions.InsertKeyframe))
            {
                symbolUi.FlagAsModified();
                foreach (var p in AnimationParameters)
                {
                    InsertNewKeyframe(p, (float)TimeLineCanvas.Playback.TimeInBars);
                }
                    
            }

            if (KeyboardBinding.Triggered(UserActions.InsertKeyframeWithIncrement))
            {
                symbolUi.FlagAsModified();
                SelectedKeyframes.Clear();
                foreach (var p in AnimationParameters)
                {
                    InsertNewKeyframe(p, (float)TimeLineCanvas.Playback.TimeInBars, false, 1);
                }
            }

            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(0, 3)); // keep some padding 
            _minScreenPos = ImGui.GetCursorScreenPos();

            var compositionSymbolChildId = compositionOp.SymbolChildId;

            for (var index = 0; index < animationParameters.Count; index++)
            {
                var parameter = animationParameters[index];
                _currentAnimationParameter = parameter;
                ImGui.PushID(index);
                DrawProperty(parameter, compositionSymbolChildId, drawList, compositionOp);
                ImGui.PopID();
            }

            DrawContextMenu(compositionOp);
        }
        ImGui.EndGroup();
    }

    private void DrawProperty(TimeLineCanvas.AnimationParameter parameter, Guid compositionSymbolChildId, ImDrawListPtr drawList, Instance compositionOp)
    {

        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X, LayerHeight );
        drawList.AddRectFilled(new Vector2(min.X, max.Y),
                               new Vector2(max.X, max.Y + 1), UiColors.BackgroundFull);
            
        var mousePos = ImGui.GetMousePos();
        var mouseTime = TimeLineCanvas.InverseTransformX(mousePos.X);
        var layerArea = new ImRect(min, max);
        var layerHovered = ImGui.IsWindowHovered() && layerArea.Contains(mousePos);

        var isCurrentSelected = TimeLineCanvas.NodeSelection.GetSelectedInstanceWithoutComposition()?.SymbolChildId == parameter.Input.Parent.SymbolChildId;
        if (FrameStats.IsIdHovered(parameter.Input.Parent.SymbolChildId) || isCurrentSelected || layerHovered)
        {
            drawList.AddRectFilled(new Vector2(min.X, min.Y),
                                   new Vector2(max.X, max.Y), UiColors.ForegroundFull.Fade(0.04f));
            
        }

        // Draw label and pinning
        {
            var hash = parameter.Input.GetHashCode();
            ImGui.PushID(hash);
                
            var label = $"{parameter.ChildUi.SymbolChild.ReadableName}.{parameter.Input.Input.Name}";
            var opLabelSize = ImGui.CalcTextSize(label);
            var buttonSize = opLabelSize + new Vector2(16, 0);
            var isPinned = PinnedParameters.Contains(hash);
                
            if (UserSettings.Config.AutoPinAllAnimations)
            {
                PinnedParameters.Add(hash);
            }

            if (ImGui.InvisibleButton("label", buttonSize) && !UserSettings.Config.AutoPinAllAnimations)
            {
                if (!isPinned)
                {
                    PinnedParameters.Add(hash);
                }
                else
                {
                    PinnedParameters.Remove(hash);
                }
            }

            var lastPos = ImGui.GetItemRectMin();
            var iconColor = isPinned? UiColors.StatusAnimated : UiColors.Gray;
            iconColor = iconColor.Fade(ImGui.IsItemHovered() ? 1 : 0.8f);
                
            Icons.DrawIconAtScreenPosition(Icon.Pin, lastPos + new Vector2(2,5), drawList, iconColor);
            var labelColor = layerHovered
                                 ? UiColors.ForegroundFull
                                 : isPinned
                                     ? UiColors.StatusAnimated
                                     : UiColors.TextMuted;
            drawList.AddText( lastPos+ new Vector2(20,3), labelColor, label);
            ImGui.PopID();
        }
            
        if (layerHovered)
        {
            drawList.AddRectFilled(new Vector2(mousePos.X, min.Y),
                                   new Vector2(mousePos.X+1, max.Y), UiColors.StatusAnimated.Fade(0.4f));
            ImGui.BeginTooltip();

            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextUnformatted(parameter.Input.Input.Name);
                
            //@pixtur: Make sure this works
            //FrameStats.AddHoveredId(parameter.Input.Parent.SymbolChildId);
            FrameStats.AddHoveredId(parameter.Input.Parent.SymbolChildId);

            foreach (var curve in parameter.Curves)
            {
                var v = curve.GetSampledValue(mouseTime);
                ImGui.TextUnformatted($"{v:0.00}");
            }

            ImGui.PopFont();
            ImGui.EndTooltip();

            var isAnyKeysSelected = _selectionCountBeforeClick>0;
                
            var wasMouseDragging= ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).Length() > 2;
            var isMouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            var isLayerBackgroundClicked = !ImGui.IsAnyItemHovered() && isMouseReleased && !wasMouseDragging;
                
            
            if (!isAnyKeysSelected && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (compositionOp.Children.TryGetValue(parameter.ChildUi.SymbolChild.Id, out _))
                {
                    ProjectView.Focused?.NodeSelection.Clear();
                    ProjectView.Focused?.NodeSelection.SelectCompositionChild(compositionOp, parameter.ChildUi.Id);
                    FitViewToSelectionHandling.FitViewToSelection();
                }
            }
            
            // Select and focus parameter if no keyframes are selected
            if (isLayerBackgroundClicked)
            {
                if(ImGui.GetIO().KeyShift)
                {
                    var someKeysNotVisible = false;
                    foreach (var curve in parameter.Curves)
                    {
                        foreach(var k in curve.GetVDefinitions())
                        {
                            var x = TimeLineCanvas.Current.TransformX((float)k.U);
                            var isNotVisible = x < min.X || x > max.X;
                            if (isNotVisible)
                            {
                                someKeysNotVisible = true;
                                break;
                            }
                        }
                        SelectedKeyframes.UnionWith(curve.GetVDefinitions().Select(v => v));
                    }
                        
                    if (someKeysNotVisible)
                    {
                        ViewAllOrSelectedKeys();
                    }
                    MouseClickChangedSelection = true;
                }
                else if(ImGui.GetIO().KeyCtrl)
                {
                    foreach (var curve in parameter.Curves)
                    {
                        // remove keys from selection
                        SelectedKeyframes.ExceptWith(curve.GetVDefinitions().Select(v => v));
                    }
                    MouseClickChangedSelection = true;
                }
                else if (isAnyKeysSelected)
                {
                    SelectedKeyframes.Clear();
                    MouseClickChangedSelection = true;
                }
            }
        }
            
        // Draw curves and gradients...
        if (parameter.Curves.Count() == 4)
        {
            DrawCurveGradient(parameter, layerArea, drawList);
        }
        else
        {
            DrawCurveLines(parameter, layerArea, drawList);
        }

        HandleCreateNewKeyframes(parameter, layerArea);

        foreach (var curve in parameter.Curves)
        {
            var list = curve.GetPointTable();
            for (var index = 0; index < list.Count; index++)
            {
                var vDef = list[index].Value;
                var nextVDef = (index < list.Count - 1) ? list[index + 1].Value : null;
                DrawKeyframe(compositionSymbolChildId, vDef, layerArea, parameter, nextVDef, drawList);
            }
        }

        ImGui.SetCursorScreenPos(min + new Vector2(0, LayerHeight)); // Next Line
    }

    public readonly HashSet<int> PinnedParameters = new();

    private bool HandleCreateNewKeyframes(TimeLineCanvas.AnimationParameter parameter, ImRect layerArea)
    {
        var hoverNewKeyframe = !ImGui.IsAnyItemActive()
                               && ImGui.IsWindowHovered()
                               && ImGui.GetIO().KeyAlt
                               && layerArea.Contains(ImGui.GetMousePos());
        if (!hoverNewKeyframe)
            return false;

        var hoverTime = TimeLineCanvas.Current.InverseTransformX(ImGui.GetIO().MousePos.X);

        if (_snapHandler.TryCheckForSnapping(hoverTime, out var snappedValue, TimeLineCanvas.Current.Scale.X))
        {
            hoverTime = (float)snappedValue;
        }

        var changed = false;
        if (ImGui.IsMouseReleased(0))
        {
            var dragDistance = ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length();
            if (dragDistance < 2)
            {
                TimeLineCanvas.Current.ClearSelection();

                InsertNewKeyframe(parameter, hoverTime, setPlaybackTime: true);
                changed = true;
            }
        }
        else
        {
            var posOnScreen = new Vector2(
                                          TimeLineCanvas.Current.TransformX(hoverTime) - KeyframeIconWidth / 2 + 1,
                                          layerArea.Min.Y);
            Icons.Draw(Icon.DopeSheetKeyframeLinear, posOnScreen);
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return changed;
    }

    private void InsertNewKeyframe(TimeLineCanvas.AnimationParameter parameter, float time, bool setPlaybackTime = false, float increment = 0)
    {
        var curves = parameter.Curves;
        var newKeyframes = AnimationOperations.InsertKeyframeToCurves(curves, time, increment);

        foreach (var k in newKeyframes)
        {
            SelectedKeyframes.Add(k);
        }
            
        if (setPlaybackTime)
            TimeLineCanvas.Current.Playback.TimeInBars = time;
    }

    private static readonly Color _grayCurveColor = new(1f, 1f, 1.0f, 0.3f);

    internal static readonly Color[] CurveColors =
        {
            new(1f, 0.2f, 0.2f, 0.3f),
            new(0.1f, 1f, 0.2f, 0.3f),
            new(0.1f, 0.4f, 1.0f, 0.5f),
            _grayCurveColor,
        };

    internal static readonly string[] CurveNames =
        [
            "X", "Y", "Z", "W", "5", "6", "7", "8", "9", "10", "11", "12"
        ];
    internal static readonly string[] ColorCurveNames =
        [
            "R", "G", "B", "A"
        ];

    private static readonly List<Vector2> _positions = new(100);  // Reuse list to avoid allocations
        
    private static void DrawCurveLines(TimeLineCanvas.AnimationParameter parameter, ImRect layerArea, ImDrawListPtr drawList)
    {
        const float padding = 2;
        // Lines
        var curveIndex = 0;
        foreach (var curve in parameter.Curves)
        {
            var points = curve.GetPointTable();
            if (points.Count == 0)
                continue;

            _positions.Clear();

            // TODO: Scanning twice is expensive. We could scale the positions after initial scan 
            var minValue = float.PositiveInfinity;
            var maxValue = float.NegativeInfinity;
            foreach (var (_, vDef) in points)
            {
                if (minValue > vDef.Value)
                    minValue = (float)vDef.Value;
                if (maxValue < vDef.Value)
                    maxValue = (float)vDef.Value;
            }

            VDefinition? lastVDef = null;
            float lastValue = 0;
            float lastUOnScreen = 0;

            var pointCount = points.Count;
                
            for(var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var (u, vDef) = points[pointIndex];
                var uOnScreen = TimeLineCanvas.Current.TransformX((float)u) - 1;
                if (lastVDef != null && lastVDef.OutEditMode == VDefinition.EditMode.Constant)
                {
                    _positions.Add(new Vector2(
                                              uOnScreen,
                                              lastValue));
                }
                else if ( (uOnScreen - lastUOnScreen) > 15 &&  lastVDef != null 
                                                           && (lastVDef.OutEditMode != VDefinition.EditMode.Linear || 
                                                               vDef.OutEditMode != VDefinition.EditMode.Linear))
                {
                    const int curveSteps = 6;
                    for (var stepIndex = 0; stepIndex < curveSteps; stepIndex++)
                    {
                        var blendU = MathUtils.Lerp(lastVDef.U, u, (float)(stepIndex + 1) / (curveSteps + 1));
                            
                        var value1 = (float)curve.GetSampledValue(blendU);
                        var value = value1.RemapAndClamp(maxValue, minValue, layerArea.Min.Y + padding, layerArea.Max.Y - padding);
                            
                        _positions.Add(new Vector2(TimeLineCanvas.Current.TransformX((float)blendU),
                                                  value));
                    }
                } 

                lastValue = ((float)vDef.Value).RemapAndClamp(maxValue, minValue, layerArea.Min.Y + padding, layerArea.Max.Y - padding);
                _positions.Add(new Vector2(
                                          TimeLineCanvas.Current.TransformX((float)u),
                                          lastValue));

                lastVDef = vDef;
                lastUOnScreen = uOnScreen;
            }

            drawList.AddPolyline(
                                 ref _positions.ToArray()[0],
                                 _positions.Count,
                                 parameter.Curves.Count() > 1 ? CurveColors[curveIndex % 4] : _grayCurveColor,
                                 ImDrawFlags.None,
                                 0.5f);

            // Debug visualization...
            // foreach (var p in _positions)
            // {
            //     _drawList.AddCircle(p + new Vector2(0,+20), 2, Color.Green.Fade(0.5f));
            // }
            curveIndex++;
        }
    }

    private void DrawCurveGradient(TimeLineCanvas.AnimationParameter parameter, ImRect layerArea, ImDrawListPtr drawList)
    {
        if (parameter.Curves.Count() != 4)
            return;

        var curve = parameter.Curves.First();
        const float padding = 2;

        var points = curve.GetVDefinitions();
        var times = new float[points.Count];
        var colors = new Color[points.Count];

        var curves = parameter.Curves.ToList();

        var index = 0;
        foreach (var vDef in points)
        {
            times[index] = TimeLineCanvas.Current.TransformX((float)vDef.U);
            colors[index] = new Color(
                                      (float)vDef.Value,
                                      (float)curves[1].GetSampledValue(vDef.U),
                                      (float)curves[2].GetSampledValue(vDef.U),
                                      (float)curves[3].GetSampledValue(vDef.U)
                                     );
            index++;
        }

        for (var index2 = 0; index2 < times.Length - 1; index2++)
        {
            drawList.AddRectFilledMultiColor(new Vector2(times[index2], layerArea.Min.Y + padding),
                                             new Vector2(times[index2 + 1], layerArea.Max.Y - padding),
                                             colors[index2],
                                             colors[index2 + 1],
                                             colors[index2 + 1],
                                             colors[index2]);
        }
    }

    private void DrawKeyframe(in Guid compositionSymbolId, VDefinition vDef, ImRect layerArea, TimeLineCanvas.AnimationParameter parameter,
                              VDefinition? nextVDef, ImDrawListPtr drawList)
    {
        var vDefU = (float)vDef.U;
        if (vDefU < Playback.Current.TimeInBars)
        {
            FrameStats.Current.HasKeyframesBeforeCurrentTime = true;
        }
        if (vDefU > Playback.Current.TimeInBars)
        {
            FrameStats.Current.HasKeyframesAfterCurrentTime = true;
        }
            
        var posOnScreen = new Vector2(
                                      TimeLineCanvas.Current.TransformX(vDefU) - KeyframeIconWidth / 2 + 1,
                                      layerArea.Min.Y);

        if (vDef.OutEditMode == VDefinition.EditMode.Constant)
        {
            var availableSpace = nextVDef != null
                                     ? TimeLineCanvas.Current.TransformX((float)nextVDef.U) - posOnScreen.X
                                     : 9999;

            if (availableSpace > 30)
            {
                var labelPos = new Vector2(posOnScreen.X + KeyframeIconWidth / 2 + 1,
                                           layerArea.Min.Y + 5);

                var color = UiColors.StatusAnimated.Fade(availableSpace.RemapAndClamp(30, 50, 0, 1).Clamp(0, 1));
                ImGui.PushFont(Fonts.FontSmall);
                drawList.AddText(labelPos, color, $"{vDef.Value:G3}");
                ImGui.PopFont();
            }
        }

        var keyHash = vDef.GetHashCode();
        ImGui.PushID(keyHash);
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Color.White.Rgba);
            var isSelected = SelectedKeyframes.Contains(vDef);
            if (vDef.OutEditMode == VDefinition.EditMode.Constant)
            {
                Icons.Draw(isSelected ? Icon.ConstantKeyframeSelected : Icon.ConstantKeyframe, posOnScreen);
            }
            else if (vDef.OutEditMode == VDefinition.EditMode.Horizontal)
            {
                Icons.Draw(isSelected ? Icon.DopeSheetKeyframeHorizontalSelected : Icon.DopeSheetKeyframeHorizontal, posOnScreen);
            }
            else if (vDef.OutEditMode == VDefinition.EditMode.Cubic)
            {
                Icons.Draw(isSelected ? Icon.DopeSheetKeyframeCubicSelected : Icon.DopeSheetKeyframeCubic, posOnScreen);
            }
            else if (vDef.OutEditMode == VDefinition.EditMode.Smooth)
            {
                Icons.Draw(isSelected ? Icon.DopeSheetKeyframeSmoothSelected : Icon.DopeSheetKeyframeSmooth, posOnScreen);
            }
            else
            {
                Icons.Draw(isSelected ? Icon.DopeSheetKeyframeLinearSelected : Icon.DopeSheetKeyframeLinear, posOnScreen);
            }
            ImGui.PopStyleColor();

            ImGui.SetCursorScreenPos(posOnScreen);

            // Click released
            var keyframeSize = new Vector2(10, 24);
            if (ImGui.InvisibleButton("##key", keyframeSize))
            {
                var justClicked = ImGui.GetMouseDragDelta().Length() < UserSettings.Config.ClickThreshold;
                if (justClicked)
                {
                    UpdateSelectionOnClickOrDrag(vDef, isSelected);
                    _clickedKeyframeHash = keyHash;

                    if (Math.Abs(TimeLineCanvas.Playback.PlaybackSpeed) < 0.001f)
                    {
                        TimeLineCanvas.Current.Playback.TimeInBars = vDef.U;
                    }
                }

                if (_changeKeyframesCommand != null)
                    TimeLineCanvas.Current.CompleteDragCommand();
            }

            HandleCurvePointDragging(compositionSymbolId, vDef, isSelected);

            // Draw value input
            var valueInputVisible = isSelected && keyHash == _clickedKeyframeHash;
            if (valueInputVisible)
            {
                var symbolUi = parameter.ChildUi.SymbolChild.Symbol.GetSymbolUi();
                var inputUi = symbolUi.InputUis[parameter.Input.Id];
                if (inputUi is FloatInputUi floatInputUi)
                {
                    var size = new Vector2(60, 25);
                    ImGui.SetCursorScreenPos(posOnScreen + new Vector2(-size.X / 2, keyframeSize.Y - 5));
                    ImGui.BeginChildFrame((uint)keyHash, size, ImGuiWindowFlags.NoScrollbar);
                    ImGui.PushFont(Fonts.FontSmall);
                    var tmp = (float)vDef.Value;

                    var result = floatInputUi.DrawEditControl(ref tmp);
                    if (result == InputEditStateFlags.Started)
                    {
                        _changeKeyframesCommand = new ChangeKeyframesCommand(SelectedKeyframes, _currentAnimationParameter.Curves);
                    }

                    if ((result & InputEditStateFlags.Modified) == InputEditStateFlags.Modified)
                    {
                        foreach (var k in SelectedKeyframes)
                        {
                            k.Value = tmp;
                        }
                    }

                    if ((result & InputEditStateFlags.Finished) == InputEditStateFlags.Finished && _changeKeyframesCommand != null)
                    {
                        _changeKeyframesCommand.StoreCurrentValues();
                        UndoRedoStack.AddAndExecute(_changeKeyframesCommand);
                        _changeKeyframesCommand = null;
                    }

                    //vDef.Value = tmp;
                    ImGui.PopFont();
                    ImGui.EndChildFrame();
                }
                else if (inputUi is IntInputUi intInputUi)
                {
                    var size = new Vector2(60, 25);
                    ImGui.SetCursorScreenPos(posOnScreen + new Vector2(-size.X / 2, keyframeSize.Y - 5));
                    ImGui.BeginChildFrame((uint)keyHash, size, ImGuiWindowFlags.NoScrollbar);
                    ImGui.PushFont(Fonts.FontSmall);
                    var tmp = (int)vDef.Value;
                    var result = intInputUi.DrawEditControl(ref tmp);
                    if (result == InputEditStateFlags.Started)
                    {
                        _changeKeyframesCommand = new ChangeKeyframesCommand(SelectedKeyframes, _currentAnimationParameter.Curves);
                    }

                    if ((result & InputEditStateFlags.Modified) == InputEditStateFlags.Modified)
                    {
                        foreach (var k in SelectedKeyframes)
                        {
                            k.Value = tmp;
                        }
                    }

                    if ((result & InputEditStateFlags.Finished) == InputEditStateFlags.Finished && _changeKeyframesCommand != null)
                    {
                        _changeKeyframesCommand.StoreCurrentValues();
                        UndoRedoStack.AddAndExecute(_changeKeyframesCommand);
                        _changeKeyframesCommand = null;
                    }

                    //vDef.Value = tmp;
                    ImGui.PopFont();
                    ImGui.EndChildFrame();
                }
            }

            ImGui.PopID();
        }
    }

    private int _clickedKeyframeHash;

    protected internal override void HandleCurvePointDragging(in Guid compositionSymbolId, VDefinition vDef, bool isSelected)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }

        if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(0, 1f))
        {
            _draggedKeyframe = null;
            return;
        }

        _draggedKeyframe = vDef;
            
        if (UpdateSelectionOnClickOrDrag(vDef, isSelected))
            return;

        if (_changeKeyframesCommand == null)
        {
            TimeLineCanvas.Current.StartDragCommand(compositionSymbolId);
        }

        var newDragTime = TimeLineCanvas.Current.InverseTransformX(ImGui.GetIO().MousePos.X);

        if (!ImGui.GetIO().KeyShift)
        {
            //var ignored= new List<IValueSnapAttractor>() { vDef };
            if (_snapHandler.TryCheckForSnapping(newDragTime, out var snappedValue, TimeLineCanvas.Current.Scale.X))
            {
                newDragTime = (float)snappedValue;
            }
        }

        TimeLineCanvas.Current.UpdateDragCommand(newDragTime - vDef.U, 0);
    }

    private bool UpdateSelectionOnClickOrDrag(VDefinition vDef, bool isSelected)
    {
        // Deselect
        if (ImGui.GetIO().KeyCtrl)
        {
            if (!isSelected)
                return true;

            foreach (var k in FindParameterKeysAtPosition(vDef.U))
            {
                SelectedKeyframes.Remove(k);
            }

            return true;
        }

        if (!isSelected)
        {
            if (!ImGui.GetIO().KeyShift)
            {
                TimeLineCanvas.Current.ClearSelection();
            }

            foreach (var k in FindParameterKeysAtPosition(vDef.U))
            {
                SelectedKeyframes.Add(k);
            }
        }

        return false;
    }

    private IEnumerable<VDefinition> FindParameterKeysAtPosition(double u)
    {
        foreach (var curve in _currentAnimationParameter.Curves)
        {
            var matchingKey = curve.GetVDefinitions().FirstOrDefault(vDef2 => Math.Abs(vDef2.U - u) < 1 / 120f);
            if (matchingKey != null)
                yield return matchingKey;
        }
    }

    #region implement interface --------------------------------------------
    void ITimeObjectManipulation.ClearSelection()
    {
        SelectedKeyframes.Clear();
    }

    public void UpdateSelectionForArea(ImRect screenArea, SelectionFence.SelectModes selectMode)
    {
        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            SelectedKeyframes.Clear();
            _clickedKeyframeHash = 0;
        }

        var startTime = TimeLineCanvas.Current.InverseTransformX(screenArea.Min.X);
        var endTime = TimeLineCanvas.Current.InverseTransformX(screenArea.Max.X);

        var layerMinIndex = (screenArea.Min.Y - _minScreenPos.Y) / LayerHeight - 1;
        var layerMaxIndex = (screenArea.Max.Y - _minScreenPos.Y) / LayerHeight;

        var index = 0;
        foreach (var parameter in AnimationParameters)
        {
            if (index >= layerMinIndex && index <= layerMaxIndex)
            {
                foreach (var c in parameter.Curves)
                {
                    var matchingItems = c.GetPointTable()
                                         .Select(pair => pair.Value)
                                         .ToList()
                                         .FindAll(key => key.U <= endTime && key.U >= startTime);
                    switch (selectMode)
                    {
                        case SelectionFence.SelectModes.Add:
                        case SelectionFence.SelectModes.Replace:
                            SelectedKeyframes.UnionWith(matchingItems);
                            break;
                        case SelectionFence.SelectModes.Remove:
                            SelectedKeyframes.ExceptWith(matchingItems);
                            break;
                    }
                }
            }

            index++;
        }
    }

    ICommand ITimeObjectManipulation.StartDragCommand(in Guid compositionSymbolId)
    {
        _changeKeyframesCommand = new ChangeKeyframesCommand(SelectedKeyframes, GetAllCurves());
        return _changeKeyframesCommand;
    }

    void ITimeObjectManipulation.UpdateDragCommand(double dt, double dv)
    {
        foreach (var vDefinition in SelectedKeyframes)
        {
            vDefinition.U += dt;
        }

        RebuildCurveTables();
    }

    void ITimeObjectManipulation.UpdateDragAtStartPointCommand(double dt, double dv)
    {
    }

    void ITimeObjectManipulation.UpdateDragAtEndPointCommand(double dt, double dv)
    {
    }

    void ITimeObjectManipulation.CompleteDragCommand()
    {
        if (_changeKeyframesCommand == null)
            return;

        // Update reference in Macro-command
        _changeKeyframesCommand.StoreCurrentValues();
        _changeKeyframesCommand = null;
    }

    void ITimeObjectManipulation.DeleteSelectedElements(Instance compositionOp)
    {
        AnimationOperations.DeleteSelectedKeyframesFromAnimationParameters(SelectedKeyframes, AnimationParameters, compositionOp);
        RebuildCurveTables();
    }
    #endregion

    /// <summary>
    /// Snap to all non-selected Clips
    /// </summary>
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        
        foreach (var vDefinition in GetAllKeyframes())
        {
            if (SelectedKeyframes.Contains(vDefinition))
                continue;
                
            if(_draggedKeyframe == vDefinition)
                continue;

            snapResult.TryToImproveWithAnchorValue(vDefinition.U);
        }
    }

    private VDefinition? _draggedKeyframe;   // ignore snapping to self
    private const float KeyframeIconWidth = 10;
    private Vector2 _minScreenPos;
    private static ChangeKeyframesCommand? _changeKeyframesCommand;
    public const int LayerHeight = 25;
    private readonly ValueSnapHandler _snapHandler;
    private int _selectionCountBeforeClick;
    public bool MouseClickChangedSelection;
}