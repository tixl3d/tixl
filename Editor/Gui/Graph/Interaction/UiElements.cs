using System.IO;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.SymbolLib;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Interaction;

internal sealed class UiElements
{
    public static void DrawExampleOperator(SymbolUi symbolUi, string label)
    {
        var color = symbolUi.Symbol.OutputDefinitions.Count > 0
                        ? TypeUiRegistry.GetPropertiesForType(symbolUi.Symbol.OutputDefinitions[0]?.ValueType).Color
                        : UiColors.Gray;

        ImGui.PushStyleColor(ImGuiCol.Button, ColorVariations.OperatorBackground.Apply(color).Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

        ImGui.SameLine();

        var restSpace = ImGui.GetWindowWidth() - ImGui.GetCursorPos().X;
        if (restSpace < 100)
        {
            ImGui.Dummy(new Vector2(10,10));
        }

        ImGui.Button(label);
        SymbolLibrary.HandleDragAndDropForSymbolItem(symbolUi.Symbol);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
            
        if (!string.IsNullOrEmpty(symbolUi.Description))
        {
            CustomComponents.TooltipForLastItem(symbolUi.Description);
        }

        ImGui.PopStyleColor(4);
    }

    public static void DrawMiniMap(ProjectView.InstanceView compositionOp, ScalableCanvas canvas)
    {
        var widgetSize = new Vector2(200, 200);
        var localPos = new Vector2(ImGui.GetWindowWidth() - widgetSize.X, 0);
        ImGui.SetCursorPos(localPos);
        var widgetPos = ImGui.GetCursorScreenPos();

        if (ImGui.BeginChild("##minimap", widgetSize, false,
                             ImGuiWindowFlags.NoScrollbar
                             | ImGuiWindowFlags.NoMove
                             | ImGuiWindowFlags.NoScrollWithMouse
                             | ImGuiWindowFlags.NoDecoration
                             | ImGuiWindowFlags.NoTitleBar
                             | ImGuiWindowFlags.ChildWindow))
        {
            var dl = ImGui.GetWindowDrawList();

            dl.AddRectFilled(widgetPos, widgetPos + widgetSize, UiColors.BackgroundFull.Fade(0.8f));
            dl.AddRect(widgetPos, widgetPos + widgetSize, UiColors.BackgroundFull.Fade(0.9f));

            var symbolUi = compositionOp.SymbolUi;
            {
                var hasChildren = false;
                ImRect bounds = new ImRect();
                foreach (var child in symbolUi.ChildUis.Values)
                {
                    var rect = ImRect.RectWithSize(child.PosOnCanvas, child.Size);

                    if (!hasChildren)
                    {
                        bounds = rect;
                        hasChildren = true;
                    }
                    else
                    {
                        bounds.Add(rect);
                    }
                }

                var maxBoundsSize = MathF.Max(bounds.GetSize().X, bounds.GetSize().Y);
                var opacity = MathUtils.RemapAndClamp(maxBoundsSize, 200, 1000, 0, 1);

                if (hasChildren && opacity > 0)
                {
                    const float padding = 5;

                    var mapMin = widgetPos + Vector2.One * padding;
                    var mapSize = widgetSize - Vector2.One * padding * 2;

                    var boundsMin = bounds.Min;
                    var boundsSize = bounds.GetSize();
                    var boundsAspect = boundsSize.X / boundsSize.Y;

                    var mapAspect = mapSize.X / mapSize.Y;

                    if (boundsAspect > mapAspect)
                    {
                        mapSize.Y = mapSize.X / boundsAspect;
                    }
                    else
                    {
                        mapSize.X = mapSize.Y * boundsAspect;
                    }

                    foreach (var annotation in symbolUi.Annotations.Values)
                    {
                        var rect = ImRect.RectWithSize(annotation.PosOnCanvas, annotation.Size);
                        var min = (rect.Min - boundsMin) / boundsSize * mapSize + mapMin;
                        var max = (rect.Max - boundsMin) / boundsSize * mapSize + mapMin;
                        dl.AddRectFilled(min, max, annotation.Color.Fade(0.1f * opacity));
                    }

                    foreach (var child in symbolUi.ChildUis.Values)
                    {
                        var rect = ImRect.RectWithSize(child.PosOnCanvas, child.Size);
                        var min = (rect.Min - boundsMin) / boundsSize * mapSize + mapMin;
                        var max = (rect.Max - boundsMin) / boundsSize * mapSize + mapMin;

                        var fadedColor = UiColors.MiniMapItems.Fade(0.5f * opacity);
                        dl.AddRectFilled(min, max, fadedColor);
                    }

                    // Draw View Area
                    var viewMinInCanvas = canvas.InverseTransformPositionFloat(Vector2.Zero);
                    var viewMaxInCanvas = canvas.InverseTransformPositionFloat(canvas.WindowSize);

                    var min2 = (viewMinInCanvas - boundsMin) / boundsSize * mapSize + mapMin;
                    var max2 = (viewMaxInCanvas - boundsMin) / boundsSize * mapSize + mapMin;

                    dl.AddRect(min2, max2, UiColors.MiniMapItems.Fade(opacity));

                    var mousePos = ImGui.GetMousePos();
                    var normalizedMousePos = (mousePos - widgetPos - Vector2.One * padding) / mapSize;
                    var mousePosInCanvas = bounds.Min + bounds.GetSize() * normalizedMousePos;
                        
                    // Dragging
                    ImGui.InvisibleButton("##map", widgetSize);
                    if (ImGui.IsItemActive())
                    {
                        var scope = canvas.GetTargetScope();
                        scope.Scroll = mousePosInCanvas - (viewMaxInCanvas - viewMinInCanvas) / 2;
                        canvas.SetTargetScope(scope);
                    }

                    if (ImGui.IsItemHovered() && ImGui.GetIO().MouseWheel != 0)
                    {
                        var posInScreen = canvas.TransformPositionFloat(mousePosInCanvas);
                        canvas.ZoomWithMouseWheel(new MouseState()
                                                      {
                                                          Position = posInScreen, 
                                                          ScrollWheel  =ImGui.GetIO().MouseWheel
                                                      }, out _);
                    }
                }
            }
        }

        ImGui.EndChild();
    }

    public static void DrawProjectControlToolbar(ProjectView components)
    {
        TimeControls.HandleTimeControlActions();
        if (!UserSettings.Config.ShowToolbar)
            return;

        ImGui.SetCursorPos(
                           new Vector2(
                                       ImGui.GetWindowContentRegionMin().X+1,
                                       ImGui.GetWindowContentRegionMax().Y - TimeControls.ControlSize.Y-1));
        ImGui.BeginChild("TimeControls", Vector2.Zero, false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        {
            var icon = components.TimeLineCanvas.FoldingHeight.UsingCustomTimelineHeight ? Icon.ChevronDown : Icon.ChevronUp;
            if (CustomComponents.IconButton(icon, TimeControls.ControlSize, CustomComponents.ButtonStates.Dimmed))
            {
                components.TimeLineCanvas.FoldingHeight.Toggle();
                UserSettings.Config.ShowTimeline = true;
            }

            ImGui.SameLine();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            TimeControls.DrawTimeControls(components.TimeLineCanvas, components.CompositionInstance);
            ImGui.PopStyleVar();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
            components.GraphImageBackground.DrawToolbarItems();
            ImGui.PopStyleVar();
        }
        ImGui.EndChild();
    }
}