using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.Variations;

internal static class VariationThumbnail
{
    public static bool Draw(VariationBaseCanvas canvas, Variation variation, Instance instanceForBlending, ImDrawListPtr drawList, ShaderResourceView canvasSrv, ImRect uvRect)
    {
        var components = ProjectView.Focused;
        if (components == null)
            return false;
        
        if (VariationForRenaming == variation)
        {
            ImGui.PushID(variation.ActivationIndex);
            ImGui.SetCursorScreenPos(new Vector2(30, ImGui.GetFrameHeight()) + ImGui.GetWindowPos());
            ImGui.SetKeyboardFocusHere();
            ImGui.InputText("##label", ref variation.Title, 256);

            if (ImGui.IsItemDeactivatedAfterEdit() || ImGui.IsItemDeactivated())
            {
                VariationForRenaming = null;
                if (VariationHandling.ActivePoolForPresets != null)
                {
                    VariationHandling.ActivePoolForPresets.SaveVariationsToFile();
                }
                else if (VariationHandling.ActiveInstanceForSnapshots != null)
                {
                    VariationHandling.ActivePoolForSnapshots.SaveVariationsToFile();
                }
            }

            ImGui.PopID();
        }

        var focusOpacity = 1f;
            
        _canvas = canvas;
        var pMin = canvas.TransformPosition(variation.PosOnCanvas);
        var sizeOnScreen = canvas.TransformDirectionFloored(ThumbnailSize);
        var pMax = pMin + sizeOnScreen;

        var areaOnScreen = new ImRect(pMin, pMax);
        drawList.AddRectFilled(pMin, pMax, UiColors.Gray.Fade(0.1f * focusOpacity));
        CustomComponents.FillWithStripes(drawList, areaOnScreen, canvas.Scale.X);

        drawList.AddImage((IntPtr)canvasSrv,
                          pMin,
                          pMax,
                          uvRect.Min,
                          uvRect.Max, Color.White.Fade(focusOpacity)
                         );

        drawList.AddRect(pMin, pMax, UiColors.Gray.Fade(0.2f * focusOpacity));

        variation.IsSelected = CanvasElementSelection.IsNodeSelected(variation);
        if (variation.IsSelected)
        {
            drawList.AddRect(pMin - Vector2.One, pMax + Vector2.One, UiColors.Selection);
        }

        const int bottomPadding = 15;
        drawList.AddRectFilledMultiColor(pMin + new Vector2(1, sizeOnScreen.Y - bottomPadding - 20),
                                         pMax - Vector2.One,
                                         UiColors.BackgroundFull.Fade(0),
                                         UiColors.BackgroundFull.Fade(0),
                                         UiColors.BackgroundFull.Fade(0.6f),
                                         UiColors.BackgroundFull.Fade(0.6f)
                                        );
        ImGui.PushClipRect(pMin, pMax, true);
        ImGui.PushFont(Fonts.FontSmall);

        var fade = MathUtils.RemapAndClamp(canvas.Scale.X, 0.3f, 0.6f, 0, 1) * focusOpacity;
        drawList.AddText(pMin + new Vector2(4, sizeOnScreen.Y - bottomPadding),
                         UiColors.Text.Fade(1f * fade),
                         string.IsNullOrEmpty(variation.Title) ? "Untitled" : variation.Title);

        drawList.AddText(pMin + new Vector2(sizeOnScreen.X - bottomPadding, sizeOnScreen.Y - bottomPadding),
                         UiColors.Text.Fade(0.3f * fade),
                         $"{variation.ActivationIndex:00}");

        ImGui.PopFont();

        if (variation.State == Variation.States.Active)
        {
            drawList.AddCircleFilled(pMax - Vector2.One * 4, 2, UiColors.WidgetActiveLine);
        }
            
        ImGui.SetCursorScreenPos(pMin);
        ImGui.PushID(variation.Id.GetHashCode());

        ImGui.InvisibleButton("##thumbnail", pMax-pMin);
        var isHovered = ImGui.IsItemVisible() && ImGui.IsItemHovered();
        if (isHovered)
        {
            //Log.Debug("here");
            drawList.AddRect(pMin + Vector2.One, pMax - Vector2.One, UiColors.ForegroundFull.Fade(0.2f));
        }

        if (_canvas.IsBlendingActive)
        {
            if (_canvas.TryGetBlendWeight(variation, out var weight))
            {
                DrawBlendIndicator(drawList, areaOnScreen, weight);
            }
        }
        else
        {
            // Handle hover
            if (isHovered)
            {
                
                if (variation.IsSnapshot)
                {
                    var nodeSelection = components.NodeSelection;
                    foreach (var childId in variation.ParameterSetsForChildIds.Keys)
                    {
                        FrameStats.AddHoveredId(childId);
                    }
                }

                if (UserSettings.Config.VariationHoverPreview)
                {
                    if (_hoveredVariation == null)
                    {
                        _hoveredVariation = variation;
                        _canvas.StartHover(variation, instanceForBlending);
                    }
                    
                    
                    if (ImGui.GetIO().KeyAlt)
                    {
                        var mouseX = ImGui.GetMousePos().X;
                        var blend = (mouseX - pMin.X) / sizeOnScreen.X;
                        _canvas.StartBlendTo(variation, blend, instanceForBlending);
                        DrawBlendIndicator(drawList, areaOnScreen, blend);
                    }
                }
            }
            else
            {
                if (_hoveredVariation == variation)
                {
                    _canvas.StopHover();
                    _hoveredVariation = null;
                }
            }
        }

        var modified = false;
        if (!_canvas.IsBlendingActive)
        {
            modified |= HandleMovement(variation, instanceForBlending);
        }

        ImGui.PopID();
        ImGui.PopClipRect();
        return modified;
    }

    private static void DrawBlendIndicator(ImDrawListPtr drawList, ImRect areaOnScreen, float blend)
    {
        var x = areaOnScreen.Min.X + areaOnScreen.GetWidth() * blend;
        drawList.AddRectFilled(new Vector2(x, areaOnScreen.Min.Y),
                               areaOnScreen.Max,
                               new Color(0.1f, 0.1f, 0.1f, 0.7f));
        drawList.AddRectFilled(new Vector2(x, areaOnScreen.Min.Y),
                               new Vector2(x, areaOnScreen.Max.Y),
                               new Color(0.9f, 0.9f, 0.9f, 0.5f));

        ImGui.PushFont(Fonts.FontLarge);
        var label = $"{blend * 100:0}%";
        var labelSize = ImGui.CalcTextSize(label);
        drawList.AddText(areaOnScreen.GetCenter() - labelSize / 2, UiColors.ForegroundFull, label);
        ImGui.PopFont();
    }

    private static bool HandleMovement(Variation variation, Instance instanceForBlending)
    {
        if (ImGui.IsItemActive())
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _draggedNodeId = variation.Id;
                if (variation.IsSelected)
                {
                    _draggedNodes = CanvasElementSelection.GetSelectedNodes<ISelectableCanvasObject>().ToList();
                }
                else
                {
                    _draggedNodes.Add(variation);
                }

                _moveCommand = new ModifyCanvasElementsCommand(_canvas, _draggedNodes, CanvasElementSelection);
            }

            HandleNodeDragging(variation);
        }
        else if (ImGui.IsMouseReleased(0) && _moveCommand != null)
        {
            if (_draggedNodeId != variation.Id)
                return false;

            _draggedNodeId = Guid.Empty;
            _draggedNodes.Clear();

            var wasDragging = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() > UserSettings.Config.ClickThreshold;
            if (wasDragging)
            {
                _moveCommand.StoreCurrentValues();
                UndoRedoStack.Add(_moveCommand);
                return true;
            }

            if (!CanvasElementSelection.IsNodeSelected(variation))
            {
                if (!ImGui.GetIO().KeyShift)
                {
                    CanvasElementSelection.Clear();
                    _hoveredVariation = null;
                    _canvas.Apply(variation, instanceForBlending);
                }

                CanvasElementSelection.AddSelection(variation);
            }
            else
            {
                if (ImGui.GetIO().KeyShift)
                {
                    CanvasElementSelection.DeselectNode(variation);
                }
                else
                {
                    _hoveredVariation = null;
                    _canvas.Apply(variation, instanceForBlending);
                }
            }

            _moveCommand = null;
        }

        return false;
    }

    private static void HandleNodeDragging(ISelectableCanvasObject draggedNode)
    {
        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _isDragging = false;
            return;
        }

        if (!_isDragging)
        {
            _dragStartDelta = ImGui.GetMousePos() - _canvas.TransformPosition(draggedNode.PosOnCanvas);
            _isDragging = true;
        }

        var newDragPos = ImGui.GetMousePos() - _dragStartDelta;
        var newDragPosInCanvas = _canvas.InverseTransformPositionFloat(newDragPos);

        // Implement snapping to others
        var bestDistanceInCanvas = float.PositiveInfinity;
        var targetSnapPositionInCanvas = Vector2.Zero;

        foreach (var offset in _snapOffsetsInCanvas)
        {
            foreach (var neighbor in _canvas.GetSelectables())
            {
                if (neighbor == draggedNode || _draggedNodes.Contains(neighbor))
                    continue;

                var snapToNeighborPos = neighbor.PosOnCanvas + offset;

                var d = Vector2.Distance(snapToNeighborPos, newDragPosInCanvas);
                if (!(d < bestDistanceInCanvas))
                    continue;

                targetSnapPositionInCanvas = snapToNeighborPos;
                bestDistanceInCanvas = d;
            }
        }

        var snapDistanceInCanvas = _canvas.InverseTransformDirection(new Vector2(6, 6)).Length();
        var isSnapping = bestDistanceInCanvas < snapDistanceInCanvas;

        var moveDeltaOnCanvas = isSnapping
                                    ? targetSnapPositionInCanvas - draggedNode.PosOnCanvas
                                    : newDragPosInCanvas - draggedNode.PosOnCanvas;

        // Drag selection
        foreach (var e in _draggedNodes)
        {
            e.PosOnCanvas += moveDeltaOnCanvas;
        }
    }

    private static Variation _hoveredVariation;
    private static bool _isDragging;

    private static VariationBaseCanvas _canvas;
    private static CanvasElementSelection CanvasElementSelection => _canvas.CanvasElementSelection;
    private static Guid _draggedNodeId;
    private static List<ISelectableCanvasObject> _draggedNodes = new();
    public static readonly Vector2 ThumbnailSize = new(160, (int)(160 / 16f * 9));

    public static readonly Vector2 SnapPadding = new(3, 3);

    private static readonly Vector2[] _snapOffsetsInCanvas =
        {
            new(ThumbnailSize.X + SnapPadding.X, 0),
            new(-ThumbnailSize.X - SnapPadding.X, 0),
            new(0, ThumbnailSize.Y + SnapPadding.Y),
            new(0, -ThumbnailSize.Y - SnapPadding.Y)
        };

    private static ModifyCanvasElementsCommand _moveCommand;
    private static Vector2 _dragStartDelta;
    public static Variation VariationForRenaming;
}