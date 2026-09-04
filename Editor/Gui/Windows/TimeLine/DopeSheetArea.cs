#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Animation;
using T3.Editor.Gui.Interaction.Keyboard;
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

[HelpUiID("DopeSheet")]
internal sealed class DopeSheetArea : AnimationParameterEditing, ITimeObjectManipulation, IValueSnapAttractor
{
    public DopeSheetArea(ValueSnapHandler snapHandler, TimeLineCanvas timeLineCanvas)
        : base(timeLineCanvas.SharedSelectedKeyframes)
    {
        _snapHandler = snapHandler;
        TimeLineCanvas = timeLineCanvas;
    }

    private TimeLineCanvas.AnimationParameter? _currentAnimationParameter;

    // Curve-time ↔ playback-time mapping of the row currently being drawn (set at the top of
    // DrawProperty; valid for the per-row draw/interaction helpers called from there).
    private TimeLineCanvas.ParamTimeMapping _currentParamMapping;

    public void Draw(Instance compositionOp, List<TimeLineCanvas.AnimationParameter> animationParameters)
    {
        MouseClickChangedSelection = false;
        var symbolUi = compositionOp.GetSymbolUi();

        var drawList = ImGui.GetWindowDrawList();

        AnimationParameters = animationParameters;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectionCountBeforeClick = SelectedKeyframes.Count;
        }

        ImGui.BeginGroup();
        {
            if (UserActions.FocusSelection.Triggered())
            {
                ViewAllOrSelectedKeys(alsoChangeTimeRange: true);
            }

            if (UserActions.Duplicate.Triggered())
            {
                symbolUi.FlagAsModified();
                DuplicateSelectedKeyframes();
            }

            if (UserActions.InsertKeyframe.Triggered())
            {
                symbolUi.FlagAsModified();
                foreach (var p in AnimationParameters)
                {
                    // Curves are sampled in each op's local time — insert where playback will read.
                    InsertNewKeyframe(p, (float)Animator.GetLocalAnimationTime(p.Instance, TimeLineCanvas.Playback.TimeInBars));
                }
            }

            if (UserActions.InsertKeyframeWithIncrement.Triggered())
            {
                symbolUi.FlagAsModified();
                SelectedKeyframes.Clear();
                foreach (var p in AnimationParameters)
                {
                    // Increment-tapping (step markers) only makes sense on scalar parameters — spraying
                    // +1 keys onto every visible vector (e.g. a Color fade) wrecks their animation.
                    if (p.Curves.Length != 1)
                        continue;

                    InsertNewKeyframe(p, (float)Animator.GetLocalAnimationTime(p.Instance, TimeLineCanvas.Playback.TimeInBars), increment: 1);
                }
            }

            ImGui.Dummy(new Vector2(1, 3)); // top padding (was SetCursorPos+3, but ImGui 1.91 needs an item)
            _minScreenPos = ImGui.GetCursorScreenPos();

            var compositionSymbolChildId = compositionOp.SymbolChildId;

            // Draw Top boundary
            {
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(ImGui.GetContentRegionAvail().X, LayerHeight);
                drawList.AddRectFilled(new Vector2(min.X, max.Y),
                    new Vector2(max.X, max.Y + 1), UiColors.BackgroundFull.Fade(0.3f));
            }
            
            for (var index = 0; index < animationParameters.Count; index++)
            {
                var parameter = animationParameters[index];

                // The clip area draws (and deletes) before the dope sheet in the same frame, so the
                // parameter list can hold ops removed moments ago until it's rebuilt next frame —
                // dereferencing their dead SymbolChild would throw.
                if (!compositionOp.Children.TryGetChildInstance(parameter.ChildUi.Id, out _))
                    continue;

                _currentAnimationParameter = parameter;
                ImGui.PushID(index);
                DrawProperty(parameter, compositionSymbolChildId, drawList, compositionOp);
                ImGui.PopID();
            }

            DrawContextMenu(compositionOp);
        }
        ImGuiUtils.ResetCursorForExtentCheck();
        ImGui.EndGroup();
    }

    /// <summary>
    /// Widest parameter header (pin + curve icons + name label) in screen px. "View All" reserves this
    /// as left padding so keyframes at the content start aren't hidden behind the header overlay.
    /// </summary>
    internal float GetMaxHeaderWidth()
    {
        if (AnimationParameters.Count == 0)
            return 0;

        return 120 * T3Ui.UiScaleFactor;
    }

    private void DrawProperty(TimeLineCanvas.AnimationParameter parameter, Guid compositionSymbolChildId, ImDrawListPtr drawList, Instance compositionOp)
    {
        Debug.Assert(TimeLineCanvas.Current != null);

        // Curves live in the op's local time; the canvas is in playback time. Everything this row draws
        // or hit-tests converts through this mapping so keys sit where they take effect during playback.
        _currentParamMapping = parameter.BuildTimeMapping();

        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X, LayerHeight);
        drawList.AddRectFilled(new Vector2(min.X, max.Y),
                               new Vector2(max.X, max.Y + 1), UiColors.BackgroundFull.Fade(0.3f));

        var mousePos = ImGui.GetMousePos();
        var mouseTime = _currentParamMapping.ToLocal(TimeLineCanvas.InverseTransformX(mousePos.X));
        var layerArea = new ImRect(min, max);
        var layerHovered = ImGui.IsWindowHovered() && layerArea.Contains(mousePos);

        var isCurrentSelected = TimeLineCanvas.NodeSelection.GetSelectedInstanceWithoutComposition()?.SymbolChildId == parameter.Input.Parent.SymbolChildId;
        if (FrameStats.IsIdHovered(parameter.Input.Parent.SymbolChildId) || isCurrentSelected || layerHovered)
        {
            var fade = isCurrentSelected ? 0.05f : 0.02f;
            drawList.AddRectFilled(new Vector2(min.X, min.Y),
                                   new Vector2(max.X, max.Y), UiColors.ForegroundFull.Fade(fade));
        }

        // Layer-row hover publishes the hovered parameter so CEA curves of other params fade.
        // Keyframe / component-toggle hovers (rendered later below) overwrite with more-specific
        // values — more-specific wins because it writes last.
        if (layerHovered)
        {
            TimeLineCanvas.HoveredParameterHash = parameter.Hash;
            TimeLineCanvas.HoveredComponentBit = 0;
        }
        
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        // Three-segment parameter header: pin | curve-expand | name
        {
            var hash = parameter.Hash;
            ImGui.PushID(hash);

            var isEditedInCurveArea = TimeLineCanvas.CurveEditingParamHashes.Contains(hash);
            ImGui.PushFont((isCurrentSelected || isEditedInCurveArea)  ? Fonts.FontBold : Fonts.FontNormal);

            var label = $"{parameter.ChildUi.SymbolChild.ReadableName}.{parameter.Input.Input.Name}";
            var opLabelSize = ImGui.CalcTextSize(label);

            var iconSize = 15 * T3Ui.UiScaleFactor;
            var iconSpacing = 2 * T3Ui.UiScaleFactor;
            var iconBlockWidth = iconSize + iconSpacing;
            var rowHeight = LayerHeight;
            var headerMin = ImGui.GetCursorScreenPos();
            var reservedWidth = iconBlockWidth * 2 + opLabelSize.X + 8 * T3Ui.UiScaleFactor;

            var isPinned = PinnedParametersHashes.Contains(hash);
            if (UserSettings.Config.AutoPinAllAnimations)
            {
                PinnedParametersHashes.Add(hash);
                isPinned = true;
            }

            // Backdrop so keyframes drawn on channel 0 don't bleed through the header
            drawList.AddRectFilled(headerMin+new Vector2(0,1), headerMin + new Vector2(reservedWidth, rowHeight), UiColors.EditorBackground.Fade(1.5f));

            // --- Pin toggle ---
            ImGui.SetCursorScreenPos(headerMin);
            if (ImGui.InvisibleButton("pin", new Vector2(iconBlockWidth, rowHeight)) && !UserSettings.Config.AutoPinAllAnimations)
            {
                if (isPinned)
                    PinnedParametersHashes.Remove(hash);
                else
                    PinnedParametersHashes.Add(hash);
            }
            var pinIconColor = isPinned ? UiColors.StatusActivated : UiColors.ForegroundFull.Fade(0.3f);
            pinIconColor = pinIconColor.Fade(ImGui.IsItemHovered() ? 1 : 0.8f);
            Icons.DrawIconAtScreenPosition(isPinned ? Icon.Pin : Icon.PinOutline,
                                           headerMin + new Vector2(2, 5) * T3Ui.UiScaleFactor, drawList, pinIconColor);

            // --- Curve-expand toggle ---
            var curveBtnPos = headerMin + new Vector2(iconBlockWidth, 0);
            ImGui.SetCursorScreenPos(curveBtnPos);

            if (ImGui.InvisibleButton("curve", new Vector2(iconBlockWidth, rowHeight)))
            {
                if (isEditedInCurveArea)
                {
                    TimeLineCanvas.CurveEditingParamHashes.Remove(hash);
                    TimeLineCanvas.VisibleComponentMask.Remove(hash);
                    isEditedInCurveArea = false;
                }
                else
                {
                    TimeLineCanvas.CurveEditingParamHashes.Add(hash);
                    isEditedInCurveArea = true;
                }
            }
            var curveIconColor = isEditedInCurveArea ? UiColors.StatusActivated : UiColors.ForegroundFull.Fade(0.3f);
            curveIconColor = curveIconColor.Fade(ImGui.IsItemHovered() ? 1 : 0.8f);
            Icons.DrawIconAtScreenPosition(Icon.InterpolateBrokenTangents,
                                           curveBtnPos + new Vector2(0, 5) * T3Ui.UiScaleFactor, drawList, curveIconColor);

            // --- Name button: replace / shift-add / ctrl-remove on parameter's keyframes ---
            var namePos = curveBtnPos + new Vector2(iconBlockWidth + 4 * T3Ui.UiScaleFactor, 0);
            ImGui.SetCursorScreenPos(namePos);
            var nameSize = new Vector2(opLabelSize.X + 4 * T3Ui.UiScaleFactor, rowHeight);
            if (ImGui.InvisibleButton("name", nameSize))
            {
                var io = ImGui.GetIO();
                if (io.KeyCtrl)
                {
                    foreach (var curve in parameter.Curves)
                        SelectedKeyframes.ExceptWith(curve.GetVDefinitions());
                }
                else if (io.KeyShift)
                {
                    foreach (var curve in parameter.Curves)
                        SelectedKeyframes.UnionWith(curve.GetVDefinitions());
                }
                else
                {
                    SelectedKeyframes.Clear();
                    foreach (var curve in parameter.Curves)
                        SelectedKeyframes.UnionWith(curve.GetVDefinitions());
                }
                MouseClickChangedSelection = true;
            }
            var nameHovered = ImGui.IsItemHovered();

            // Opacity rule: 60% default, +20% when any of this parameter's keyframes is selected,
            // +15% on hover (layer / operator in graph / cross-view curve-line hover).
            var anyKeySelected = false;
            foreach (var c in parameter.Curves)
            {
                foreach (var v in c.GetVDefinitions())
                {
                    if (!SelectedKeyframes.Contains(v))
                        continue;
                    anyKeySelected = true;
                    break;
                }
                if (anyKeySelected) break;
            }

            var isRelatedHovered = layerHovered
                                   || nameHovered
                                   || FrameStats.IsIdHovered(parameter.Input.Parent.SymbolChildId)
                                   || TimeLineCanvas.HoveredParameterHash == hash;

            var labelOpacity = 0.60f;
            if (anyKeySelected) labelOpacity += 0.20f;
            if (isRelatedHovered) labelOpacity += 0.15f;
            if (labelOpacity > 1f) labelOpacity = 1f;

            var labelColor =  isEditedInCurveArea ? UiColors.StatusActivated : UiColors.ForegroundFull.Fade(labelOpacity);
            
            drawList.AddText(namePos + new Vector2(0, 3) * T3Ui.UiScaleFactor, labelColor, label);

            // --- Component visibility toggles (X/Y/Z/W or R/G/B/A) for expanded vector params ---
            // One small button per curve; click toggles its bit in VisibleComponentMask. Hover
            // drives the cross-view fade via TimeLineCanvas.Hovered{Parameter,Component}{Bit,Hash}.
            if (isEditedInCurveArea && parameter.Curves.Length > 1)
            {
                var componentNames = parameter.Input.ValueType == typeof(System.Numerics.Vector4)
                                         ? ColorCurveNames
                                         : CurveNames;
                var compSize = new Vector2(14 * T3Ui.UiScaleFactor, rowHeight);
                var compGap = 1 * T3Ui.UiScaleFactor;
                var compStart = namePos + new Vector2(opLabelSize.X + 6 * T3Ui.UiScaleFactor, 0);

                for (var i = 0; i < parameter.Curves.Length && i < componentNames.Length; i++)
                {
                    var bit = 1 << i;
                    var isVisible = IsComponentVisible(hash, bit, parameter.Curves.Length);
                    var pos = compStart + new Vector2(i * (compSize.X + compGap), 0);

                    ImGui.PushID(i);
                    ImGui.SetCursorScreenPos(pos);
                    if (ImGui.InvisibleButton("comp", compSize))
                        ToggleComponentVisibility(hash, bit, parameter.Curves.Length);
                    var compHovered = ImGui.IsItemHovered();
                    if (compHovered)
                    {
                        TimeLineCanvas.HoveredParameterHash = hash;
                        TimeLineCanvas.HoveredComponentBit = bit;
                    }
                    ImGui.PopID();

                    var baseColor = CurveColors[i % CurveColors.Length];
                    var alpha = isVisible ? 1f : 0.25f;
                    if (compHovered) alpha = 1f;
                    var color = baseColor.Fade(alpha);

                    var letter = componentNames[i];
                    var textSize = ImGui.CalcTextSize(letter);
                    var textPos = pos + (compSize - textSize) * 0.5f;
                    drawList.AddText(textPos, color, letter);
                }
            }

            ImGui.PopFont();
            ImGui.PopID();
        }
        drawList.ChannelsSetCurrent(0);

        //if (layerHovered && mousePos.X > ImGui.GetItemRectMax().X)
        if (layerHovered)
        {
            drawList.AddRectFilled(new Vector2(mousePos.X, min.Y),
                                   new Vector2(mousePos.X + 1, max.Y), UiColors.StatusAnimated.Fade(0.4f));
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

            var isAnyKeysSelected = _selectionCountBeforeClick > 0;

            var wasMouseDragging = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).Length() > 2;
            var isMouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            var isLayerBackgroundClicked = !ImGui.IsAnyItemHovered() && isMouseReleased && !wasMouseDragging;

            if (!isAnyKeysSelected && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (compositionOp.Children.TryGetChildInstance(parameter.ChildUi.SymbolChild.Id, out _))
                {
                    ProjectView.Focused?.NodeSelection.Clear();
                    ProjectView.Focused?.NodeSelection.TrySelectCompositionChild(compositionOp, parameter.ChildUi.Id);
                    FitViewToSelectionHandling.FitViewToSelection();
                }
            }

            // Select and focus parameter if no keyframes are selected
            if (isLayerBackgroundClicked)
            {
                if (ImGui.GetIO().KeyShift)
                {
                    var someKeysNotVisible = false;
                    foreach (var curve in parameter.Curves)
                    {
                        foreach (var k in curve.GetVDefinitions())
                        {
                            var x = TimeLineCanvas.Current.TransformX((float)_currentParamMapping.ToGlobal(k.U));
                            var isNotVisible = x < min.X || x > max.X;
                            if (isNotVisible)
                            {
                                someKeysNotVisible = true;
                                break;
                            }
                        }

                        SelectedKeyframes.UnionWith(curve.GetVDefinitions());
                    }

                    if (someKeysNotVisible)
                    {
                        ViewAllOrSelectedKeys();
                    }

                    MouseClickChangedSelection = true;
                }
                else if (ImGui.GetIO().KeyCtrl)
                {
                    foreach (var curve in parameter.Curves)
                    {
                        // remove keys from selection
                        SelectedKeyframes.ExceptWith(curve.GetVDefinitions());
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
        if (parameter.Curves.Length == 4)
        {
            DrawCurveGradient(parameter, layerArea, drawList, _currentParamMapping);
        }
        else
        {
            DrawCurveLines(parameter, layerArea, drawList, _currentParamMapping);
        }

        HandleCreateNewKeyframes(parameter, layerArea);

        foreach (var curve in parameter.Curves)
        {
            var list = curve.GetVDefinitions();
            for (var index = 0; index < list.Count; index++)
            {
                var vDef = list[index];
                var nextVDef = index < list.Count - 1 ? list[index + 1] : null;
                DrawKeyframe(compositionSymbolChildId, vDef, layerArea, parameter, nextVDef, drawList);
            }
        }

        ImGui.SetCursorScreenPos(min + new Vector2(0, LayerHeight)); // Next Line
        drawList.ChannelsMerge();
    }

    public readonly HashSet<int> PinnedParametersHashes = new();
    

    private bool HandleCreateNewKeyframes(TimeLineCanvas.AnimationParameter parameter, ImRect layerArea)
    {
        Debug.Assert(TimeLineCanvas.Current != null);

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

                // hoverTime is playback time (snapped against the shared timeline anchors); the key itself
                // is written in the row's local time.
                InsertNewKeyframe(parameter, (float)_currentParamMapping.ToLocal(hoverTime));
                TimeLineCanvas.Current.Playback.TimeInBars = hoverTime;
                changed = true;
            }
        }
        else
        {
            var posOnScreen = new Vector2(
                                          TimeLineCanvas.Current.TransformX(hoverTime) - KeyframeIconWidth / 2 + 1,
                                          layerArea.Min.Y);
            Icons.DrawIconAtScreenPosition(Icon.DopeSheetKeyframeLinear, posOnScreen);
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return changed;
    }

    private void InsertNewKeyframe(TimeLineCanvas.AnimationParameter parameter, float time, float increment = 0)
    {
        Debug.Assert(TimeLineCanvas.Current != null);

        var curves = parameter.Curves;
        var newKeyframes = AnimationOperations.InsertKeyframeToCurves(curves, time, increment);

        foreach (var k in newKeyframes)
        {
            SelectedKeyframes.Add(k);
        }
    }

    internal static readonly Color GrayCurveColor = new(0.8f, 1f, 1.0f, 0.3f);

    /// <summary>
    /// Is component <paramref name="bit"/> of the parameter (hash) currently visible?
    /// Missing entry in the mask means "all components visible" (the default on expand).
    /// </summary>
    private bool IsComponentVisible(int hash, int bit, int curveCount)
    {
        if (!TimeLineCanvas.VisibleComponentMask.TryGetValue(hash, out var mask))
            return true;
        return (mask & bit) != 0;
    }

    /// <summary>
    /// Isolate-on-first-click semantics:
    ///   • No mask entry (all visible) → isolate this component (only it stays visible).
    ///   • Mask entry already present → toggle this bit; if the result is "all visible" or
    ///     "none visible", drop the entry so we're back to the default "all visible".
    /// </summary>
    private void ToggleComponentVisibility(int hash, int bit, int curveCount)
    {
        var allOn = (1 << curveCount) - 1;

        if (!TimeLineCanvas.VisibleComponentMask.TryGetValue(hash, out var mask))
        {
            // All currently visible — isolate this component.
            if (bit == allOn)
                return; // only one component; nothing to isolate to
            TimeLineCanvas.VisibleComponentMask[hash] = bit;
            return;
        }

        var next = mask ^ bit;
        if (next == 0 || next == allOn)
            TimeLineCanvas.VisibleComponentMask.Remove(hash); // restore default (all visible)
        else
            TimeLineCanvas.VisibleComponentMask[hash] = next;
    }

    /// <summary>
    /// Cross-view alpha: full when no hover is set, dimmed when the hover picks a different
    /// parameter or (within the same parameter) a different component.
    /// </summary>
    internal static float HoverFadeAlpha(int paramHash, int bit)
    {
        var canvas = TimeLineCanvas.Current;
        if (canvas == null || canvas.HoveredParameterHash is not { } hoveredParam)
            return 1f;
        if (hoveredParam != paramHash)
            return 0.4f;
        if (canvas.HoveredComponentBit != 0 && canvas.HoveredComponentBit != bit)
            return 0.4f;
        return 1f;
    }

    internal static readonly Color[] CurveColors =
        {
            new(1f, 0.2f, 0.2f, 0.4f),
            new(0.1f, 1f, 0.2f, 0.4f),
            new(0.1f, 0.4f, 1.0f, 0.6f),
            GrayCurveColor,
        };

    internal static readonly string[] CurveNames =
        [
            "X", "Y", "Z", "W", "5", "6", "7", "8", "9", "10", "11", "12"
        ];

    internal static readonly string[] ColorCurveNames =
        [
            "R", "G", "B", "A"
        ];


    private static void DrawCurveLines(TimeLineCanvas.AnimationParameter parameter, ImRect layerArea, ImDrawListPtr drawList,
                                       in TimeLineCanvas.ParamTimeMapping mapping)
    {
        Debug.Assert(TimeLineCanvas.Current != null);

        const float padding = 2;
        var curveIndex = 0;
        var canvas = TimeLineCanvas.Current;
        // The sample cache works in curve-local time; convert the visible window (and the screen scale,
        // which drives sample density) through the row's mapping. A negative rate flips the bounds.
        var visibleStartGlobal = canvas.InverseTransformPositionFloat(canvas.WindowPos).X;
        var visibleEndGlobal = canvas.InverseTransformPositionFloat(canvas.WindowPos + new Vector2(canvas.WindowSize.X, 0)).X;
        var localA = mapping.ToLocal(visibleStartGlobal);
        var localB = mapping.ToLocal(visibleEndGlobal);
        var visibleStartU = Math.Min(localA, localB);
        var visibleEndU = Math.Max(localA, localB);
        var screenScaleX = (double)canvas.Scale.X * Math.Abs(mapping.Rate);

        var minValue = float.PositiveInfinity;
        var maxValue = float.NegativeInfinity;

        // Component visibility (expanded vector params can hide individual curves via the
        // dope-row toggle buttons); missing mask entry means "all visible".
        TimeLineCanvas.Current.VisibleComponentMask.TryGetValue(parameter.Hash, out var componentMask);
        var hasComponentMask = componentMask != 0;

        foreach (var curve in parameter.Curves)
        {
            var bit = 1 << curveIndex;
            if (hasComponentMask && (componentMask & bit) == 0)
            {
                curveIndex++;
                continue;
            }

            if (curve.Table.Count == 0)
                continue;

            curve.SampleCache.Update(curve, visibleStartU, visibleEndU, screenScaleX);
            var cache = curve.SampleCache;
            var firstKeyU = cache.FirstKeyU;
            var lastKeyU = cache.LastKeyU;

            if (double.IsNaN(firstKeyU))
            {
                curveIndex++;
                continue;
            }

            // Track min/max from ALL visible cached samples (including pre/post regions)
            var allVisiblePoints = cache.GetPointsInRange(visibleStartU, visibleEndU);
            for (var i = 0; i < allVisiblePoints.Length; i++)
            {
                var value = allVisiblePoints[i].Y;
                if (value < minValue)
                    minValue = value;
                if (value > maxValue)
                    maxValue = value;
            }

            var fade = HoverFadeAlpha(parameter.Hash, bit);
            var bodyColor = (parameter.Curves.Length > 1 ? CurveColors[curveIndex % 4] : GrayCurveColor).Fade(fade);
            var outsideColor = bodyColor.Fade(0.3f);

            // Always draw 3 segments: dimmed pre, full body, dimmed post
            // Use -/+Infinity for outer bounds to include all cached pre/post points beyond visible edges
            DrawDopeSheetPolyline(cache.GetPointsInRange(double.NegativeInfinity, firstKeyU), canvas, drawList, parameter, layerArea, padding, outsideColor, mapping);
            DrawDopeSheetPolyline(cache.GetPointsInRange(firstKeyU, lastKeyU), canvas, drawList, parameter, layerArea, padding, bodyColor, mapping);
            DrawDopeSheetPolyline(cache.GetPointsInRange(lastKeyU, double.PositiveInfinity), canvas, drawList, parameter, layerArea, padding, outsideColor, mapping);

            curveIndex++;
        }
        minValue = parameter.DampedMinValue.DampTowards(minValue);
        maxValue = parameter.DampedMaxValue.DampTowards(maxValue);
    }

    private static void DrawDopeSheetPolyline(ReadOnlySpan<Vector2> points, TimeLineCanvas canvas,
                                                ImDrawListPtr drawList, TimeLineCanvas.AnimationParameter parameter,
                                                ImRect layerArea, float padding, Color color,
                                                in TimeLineCanvas.ParamTimeMapping mapping)
    {
        var pointCount = Math.Min(points.Length, TimelineCurveEditor.MaxPolylinePoints);
        if (pointCount < 2)
            return;

        var buf = TimelineCurveEditor._polylineBuffer;
        var valueRange = parameter.DampedMaxValue - parameter.DampedMinValue;
        var centerY = (layerArea.Min.Y + layerArea.Max.Y) * 0.5f;
        var isFlatRange = Math.Abs(valueRange) < 1e-6f;

        for (var i = 0; i < pointCount; i++)
        {
            var p = points[i];
            var screenX = canvas.TransformX((float)mapping.ToGlobal(p.X));
            var screenY = isFlatRange
                              ? centerY
                              : ((float)p.Y).RemapAndClamp(parameter.DampedMaxValue,
                                                           parameter.DampedMinValue,
                                                           layerArea.Min.Y + padding,
                                                           layerArea.Max.Y - padding);
            buf[i] = new Vector2(screenX, screenY);
        }

        drawList.AddPolyline(ref buf[0], pointCount, color, ImDrawFlags.None, 0.5f);
    }

    private static void DrawCurveGradient(TimeLineCanvas.AnimationParameter parameter, ImRect layerArea, ImDrawListPtr drawList,
                                          in TimeLineCanvas.ParamTimeMapping mapping)
    {
        Debug.Assert(TimeLineCanvas.Current != null);

        if (parameter.Curves.Length != 4)
            return;

        var curve = parameter.Curves[0];
        const float padding = 2;

        var points = curve.GetVDefinitions();
        var times = new float[points.Count];
        var colors = new Color[points.Count];

        var curves = parameter.Curves;

        var index = 0;
        foreach (var vDef in points)
        {
            times[index] = TimeLineCanvas.Current.TransformX((float)mapping.ToGlobal(vDef.U));
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
        Debug.Assert(TimeLineCanvas.Current != null);
        Debug.Assert(_currentAnimationParameter != null);

        var vDefGlobalU = (float)_currentParamMapping.ToGlobal(vDef.U);
        if (vDefGlobalU < Playback.Current.TimeInBars)
        {
            FrameStats.Current.HasKeyframesBeforeCurrentTime = true;
        }

        if (vDefGlobalU > Playback.Current.TimeInBars)
        {
            FrameStats.Current.HasKeyframesAfterCurrentTime = true;
        }

        var posOnScreen = new Vector2(
                                      TimeLineCanvas.Current.TransformX(vDefGlobalU) - KeyframeIconWidth * T3Ui.UiScaleFactor / 2 + 1,
                                      layerArea.Min.Y);

        if (vDef.OutInterpolation == VDefinition.KeyInterpolation.Constant)
        {
            var availableSpace = nextVDef != null
                                     ? TimeLineCanvas.Current.TransformX((float)_currentParamMapping.ToGlobal(nextVDef.U)) - posOnScreen.X
                                     : 9999;

            if (availableSpace > 30)
            {
                var labelPos = new Vector2(posOnScreen.X + KeyframeIconWidth / 2 + 6,
                                           layerArea.Min.Y + 3);

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
            if (vDef.OutInterpolation == VDefinition.KeyInterpolation.Constant)
            {
                Icons.DrawIconAtScreenPosition(isSelected ? Icon.ConstantKeyframeSelected : Icon.ConstantKeyframe, posOnScreen);
            }
            else if (vDef.OutInterpolation == VDefinition.KeyInterpolation.Horizontal)
            {
                Icons.DrawIconAtScreenPosition(isSelected ? Icon.DopeSheetKeyframeHorizontalSelected : Icon.DopeSheetKeyframeHorizontal, posOnScreen);
            }
            else if (vDef.OutInterpolation == VDefinition.KeyInterpolation.Cubic)
            {
                Icons.DrawIconAtScreenPosition(isSelected ? Icon.DopeSheetKeyframeCubicSelected : Icon.DopeSheetKeyframeCubic, posOnScreen);
            }
            else if (vDef.OutInterpolation == VDefinition.KeyInterpolation.Smooth)
            {
                Icons.DrawIconAtScreenPosition(isSelected ? Icon.DopeSheetKeyframeSmoothSelected : Icon.DopeSheetKeyframeSmooth, posOnScreen);
            }
            else
            {
                Icons.DrawIconAtScreenPosition(isSelected ? Icon.DopeSheetKeyframeLinearSelected : Icon.DopeSheetKeyframeLinear, posOnScreen);
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
                    if (_clickedKeyframeHash == keyHash)
                    {
                        _activeKeyframeHash = keyHash;

                        if (ProjectView.Focused?.CompositionInstance != null &&
                            ProjectView.Focused.NodeSelection.TrySelectCompositionChild(
                                ProjectView.Focused.CompositionInstance, parameter.ChildUi.Id, false))
                        {
                            FitViewToSelectionHandling.FitViewToSelection();
                        }
                    }
                    else
                    {
                        _activeKeyframeHash = -1;
                    }
                    _clickedKeyframeHash = keyHash;

                    if (Math.Abs(TimeLineCanvas.Playback.PlaybackSpeed) < 0.001f)
                    {
                        TimeLineCanvas.Current.Playback.TimeInBars = _currentParamMapping.ToGlobal(vDef.U);
                    }
                }

                if (_changeKeyframesCommand != null)
                    TimeLineCanvas.Current.CompleteDragCommand();
            }

            // Keyframe hover — set shared hover state so the curve pane can outline the
            // matching per-component keyframes, and draw an outline here if CEA/another
            // DSA key just set the hovered UniqueId.
            if (ImGui.IsItemHovered())
            {
                TimeLineCanvas.HoveredParameterHash = parameter.Hash;
                TimeLineCanvas.HoveredComponentBit = 0;
                TimeLineCanvas.HoveredKeyframeUniqueId = vDef.UniqueId;
            }
            else if (TimeLineCanvas.HoveredKeyframeUniqueId == vDef.UniqueId)
            {
                drawList.AddRect(posOnScreen - Vector2.One,
                                 posOnScreen + keyframeSize + Vector2.One,
                                 UiColors.ForegroundFull, rounding: 1f, ImDrawFlags.None, 1f);
            }

            HandleCurvePointDragging(compositionSymbolId, vDef, isSelected);

            // Draw value input
            var valueInputVisible = isSelected && keyHash == _activeKeyframeHash;
            if (valueInputVisible)
            {
                var symbolUi = parameter.ChildUi.SymbolChild.Symbol.GetSymbolUi();
                var inputUi = symbolUi.InputUis[parameter.Input.Id];
                if (inputUi is FloatInputUi floatInputUi)
                {
                    var size = new Vector2(60, 25);
                    ImGui.SetCursorScreenPos(posOnScreen + new Vector2(-size.X / 2, keyframeSize.Y - 5));
                    ImGui.BeginChild($"##kf{keyHash}", size, ImGuiChildFlags.FrameStyle, ImGuiWindowFlags.NoScrollbar);
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
                    ImGui.EndChild();
                }
                else if (inputUi is IntInputUi intInputUi)
                {
                    var size = new Vector2(60, 25);
                    ImGui.SetCursorScreenPos(posOnScreen + new Vector2(-size.X / 2, keyframeSize.Y - 5));
                    ImGui.BeginChild($"##kf{keyHash}", size, ImGuiChildFlags.FrameStyle, ImGuiWindowFlags.NoScrollbar);
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
                    ImGui.EndChild();
                }
            }

            ImGui.PopID();
        }
    }

    private int _clickedKeyframeHash;
    private int _activeKeyframeHash;



    protected internal override void HandleCurvePointDragging(in Guid compositionSymbolId, VDefinition vDef, bool isSelected)
    {
        Debug.Assert(TimeLineCanvas.Current != null);

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
            if (_snapHandler.TryCheckForSnapping(newDragTime, out var snappedValue, TimeLineCanvas.Current.Scale.X,
                                                 TimeLineCanvas.Current.SelectionDragSnapExclusions))
            {
                newDragTime = (float)snappedValue;
            }
        }

        // dt is a playback-time delta; UpdateDragCommand scales it into each row's local time.
        TimeLineCanvas.Current.UpdateDragCommand(newDragTime - _currentParamMapping.ToGlobal(vDef.U), 0);
    }

    private bool UpdateSelectionOnClickOrDrag(VDefinition vDef, bool isSelected)
    {
        if (TimeLineCanvas.Current == null)
            return false;

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
        if(_currentAnimationParameter == null)
            yield break;
        
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
        if (TimeLineCanvas.Current == null)
            return;

        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            SelectedKeyframes.Clear();
            _clickedKeyframeHash = 0;
        }

        var startTimeGlobal = TimeLineCanvas.Current.InverseTransformX(screenArea.Min.X);
        var endTimeGlobal = TimeLineCanvas.Current.InverseTransformX(screenArea.Max.X);

        var layerMinIndex = (screenArea.Min.Y - _minScreenPos.Y) / LayerHeight - 1;
        var layerMaxIndex = (screenArea.Max.Y - _minScreenPos.Y) / LayerHeight;

        var index = 0;
        foreach (var parameter in AnimationParameters)
        {
            if (index >= layerMinIndex && index <= layerMaxIndex)
            {
                // The fence is in playback time; each row's keys live in its own local time.
                var mapping = parameter.BuildTimeMapping();
                var localA = mapping.ToLocal(startTimeGlobal);
                var localB = mapping.ToLocal(endTimeGlobal);
                var startTime = Math.Min(localA, localB);
                var endTime = Math.Max(localA, localB);

                foreach (var c in parameter.Curves)
                {
                    var keysCount = c.Keys.Count;
                    if (keysCount == 0)
                        continue;
                    
                    var keyIndex = c.FindIndexBefore(startTime)+1;
                    
                    while (keyIndex != -1 && keyIndex < keysCount)
                    {
                        var key = c.Keys[keyIndex];
                        if (key.U > endTime)
                            break;
                        
                        switch (selectMode)
                        {
                            case SelectionFence.SelectModes.Add:
                            case SelectionFence.SelectModes.Replace:
                                SelectedKeyframes.Add(key);
                                break;
                            case SelectionFence.SelectModes.Remove:
                                SelectedKeyframes.Remove(key);
                                break;
                        }
                        
                        keyIndex++;
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
        // dt is a playback-time delta; a key inside a stretched clip must move by dt / rate in curve
        // time to travel dt on screen. Iterate per parameter so each row gets its own scale.
        foreach (var parameter in AnimationParameters)
        {
            var mapping = parameter.BuildTimeMapping();
            var localDt = Math.Abs(mapping.Rate) < 1e-9 ? dt : dt / mapping.Rate;
            foreach (var curve in parameter.Curves)
            {
                foreach (var vDefinition in curve.GetVDefinitions())
                {
                    if (SelectedKeyframes.Contains(vDefinition))
                        vDefinition.U += localDt;
                }
            }
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

    /// <summary>Stretch is defined in playback time; each key converts through its row's mapping.</summary>
    public override void UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV)
    {
        foreach (var parameter in AnimationParameters)
        {
            var mapping = parameter.BuildTimeMapping();
            foreach (var curve in parameter.Curves)
            {
                foreach (var vDefinition in curve.GetVDefinitions())
                {
                    if (!SelectedKeyframes.Contains(vDefinition))
                        continue;

                    var globalU = mapping.ToGlobal(vDefinition.U);
                    vDefinition.U = mapping.ToLocal(originU + (globalU - originU) * scaleU);
                }
            }
        }

        RebuildCurveTables();
    }

    /// <summary>Selection extent in playback time — the SRI and range overlays operate there.</summary>
    public override TimeRange GetSelectionTimeRange()
    {
        var timeRange = TimeRange.Undefined;
        foreach (var parameter in AnimationParameters)
        {
            var mapping = parameter.BuildTimeMapping();
            foreach (var curve in parameter.Curves)
            {
                foreach (var vDefinition in curve.GetVDefinitions())
                {
                    if (SelectedKeyframes.Contains(vDefinition))
                        timeRange.Unite((float)mapping.ToGlobal(vDefinition.U));
                }
            }
        }

        return timeRange;
    }

    public override TimeRange GetAllKeyframesTimeRange()
    {
        var range = TimeRange.Undefined;
        foreach (var parameter in AnimationParameters)
        {
            var mapping = parameter.BuildTimeMapping();
            foreach (var curve in parameter.Curves)
            {
                foreach (var vDefinition in curve.GetVDefinitions())
                {
                    range.Unite((float)mapping.ToGlobal(vDefinition.U));
                }
            }
        }

        return range;
    }

    void ITimeObjectManipulation.DeleteSelectedElements(Instance compositionOp)
    {
        AnimationOperations
           .DeleteSelectedKeyframesFromAnimationParameters(SelectedKeyframes,
                                                           AnimationParameters,
                                                           compositionOp);
        RebuildCurveTables();
    }
    #endregion

    /// <summary>
    /// Snap to all non-selected Clips
    /// </summary>
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        // While a clip drag is in flight, keys riding on the dragged (selected) clips move with them in
        // playback time — using them as anchors would make the clip chase its own keys and jitter.
        var excludeDraggedClipParams = TimeClips.TimeClipInteractions.IsDraggingClips;
        if (excludeDraggedClipParams)
        {
            _draggedClipIds.Clear();
            foreach (var clip in TimeLineCanvas.ClipArea.EnumerateSelectedClips())
            {
                _draggedClipIds.Add(clip.Id);
            }
        }

        // Anchors are published in playback time — the space drags and the playhead operate in.
        foreach (var parameter in AnimationParameters)
        {
            if (excludeDraggedClipParams && IsInstanceUnderAnyClip(parameter.Instance, _draggedClipIds))
                continue;

            var mapping = parameter.BuildTimeMapping();
            foreach (var curve in parameter.Curves)
            {
                foreach (var vDefinition in curve.GetVDefinitions())
                {
                    if (SelectedKeyframes.Contains(vDefinition))
                        continue;

                    if (_draggedKeyframe == vDefinition)
                        continue;

                    if (ExternalDragSnapExclusions != null && ContainsRef(ExternalDragSnapExclusions, vDefinition))
                        continue;

                    snapResult.TryToImproveWithAnchorValue(mapping.ToGlobal(vDefinition.U));
                }
            }
        }
    }

    private static bool IsInstanceUnderAnyClip(Instance? instance, HashSet<Guid> clipChildIds)
    {
        while (instance != null)
        {
            if (clipChildIds.Contains(instance.SymbolChildId))
                return true;

            instance = instance.Parent;
        }

        return false;
    }

    private static bool ContainsRef(IReadOnlyList<VDefinition> list, VDefinition target)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Keyframes for an external drag (e.g. a <see cref="KeySetStrip"/> bucket drag) that
    /// should not contribute snap anchors. Generalises <see cref="_draggedKeyframe"/> for callers
    /// that move several keys at once. Caller sets on drag start, clears on drag end.
    /// </summary>
    internal IReadOnlyList<VDefinition>? ExternalDragSnapExclusions { get; set; }

    private VDefinition? _draggedKeyframe; // ignore snapping to self
    private readonly HashSet<Guid> _draggedClipIds = new(8);
    private const float KeyframeIconWidth = 10;
    private Vector2 _minScreenPos;
    private static ChangeKeyframesCommand? _changeKeyframesCommand;
    public  static int LayerHeight => (int)(25f * T3Ui.UiScaleFactor);
    private readonly ValueSnapHandler _snapHandler;
    private int _selectionCountBeforeClick;
    public bool MouseClickChangedSelection;
}