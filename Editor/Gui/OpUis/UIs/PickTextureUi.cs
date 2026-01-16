#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class PickTextureUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("29e289be-e735-4dd4-8826-5e434cc995fa")]
        internal readonly InputSlot<int> Index = null!;

        [BindInput("6C935163-1729-4DF0-A981-610B4AA7C6A3")]
        internal readonly MultiInputSlot<Texture2D> Inputs = null!;        
    }

    internal static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                    ImDrawListPtr drawList,
                                                    ImRect screenRect,
                                                    ScalableCanvas canvas,
                                                    ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;
        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var canvasScaleY = canvas.Scale.Y;

        var font = Fonts.FontNormal;
        var fontSize = Fonts.FontNormal.FontSize * canvasScaleY * .9f;
        var labelColor = WidgetElements.GetPrimaryLabelColor(canvasScaleY);

        // Current index
        var isAnimated = instance.Parent?.Symbol.Animator.IsInputSlotAnimated(data.Index) ?? false;
        var indexIsConnected = data.Index.HasInputConnections;
        var currentValue = (isAnimated || indexIsConnected)
                               ? data.Index.Value
                               : data.Index.TypedInputValue.Value;
        
        var margin = 4.0f * canvasScaleY;
        var buttonSpacing = 4.0f * canvasScaleY;
        var spaceForIndex = 35.0f * canvasScaleY;

        var connections = data.Inputs.GetCollectedTypedInputs(forceRefresh: true);
        var currentCount = connections?.Count ?? 0;
     
        if (connections != null && currentCount > 0)
        {
            ImGui.PushID(instance.SymbolChildId.GetHashCode());
            ImGui.PushClipRect(screenRect.Min, screenRect.Max, true);
            // Calculate layout
            var workingRect = screenRect;
            //workingRect.Expand(-margin);
            workingRect.Min.X += margin;
            workingRect.Min.Y += margin*2;
            workingRect.Max.Y -= margin*2;

            var buttonAreaHeight = workingRect.GetHeight();

            if (indexIsConnected)
            {
                buttonAreaHeight -= spaceForIndex;
            }
            var buttonHeight = (buttonAreaHeight - (buttonSpacing * (connections.Count - 1))) / connections.Count;

            // Draw buttons
            var buttonMinY = workingRect.Min.Y;
            var buttonMinX = workingRect.Min.X;
            var buttonWidth = workingRect.GetWidth();

            for (var i = 0; i < connections.Count; i++)
            {
                var srcSlot = connections[i];
                var label = $"#{i}";

                var srcInstance = srcSlot?.Parent;
                if (srcInstance != null)
                {
                    if (!string.IsNullOrWhiteSpace(srcInstance.SymbolChild.Name))
                    {
                        label = srcInstance.SymbolChild.Name;
                    }
                    else if (!string.IsNullOrWhiteSpace(srcInstance.Symbol.Name))
                    {
                        label = srcInstance.Symbol.Name;
                    }
                }

                var buttonY = buttonMinY + i * (buttonHeight + buttonSpacing);
                var buttonRect = new ImRect(
                    new Vector2(buttonMinX, buttonY),
                    new Vector2(buttonMinX + buttonWidth, buttonY + buttonHeight)
                );

                var isActive = (i == currentValue.Mod(connections.Count));
                var isHovered = ImGui.IsWindowHovered() && buttonRect.Contains(ImGui.GetMousePos());

                // Determine button color
                var buttonColor = ColorVariations.OperatorOutline.Apply(UiColors.ColorForTextures);
               
                if (isActive)
                {
                    buttonColor = UiColors.BackgroundActive.Rgba;
                }
                else if (isHovered && !indexIsConnected)
                {
                    buttonColor = UiColors.BackgroundActive.Fade(0.3f).Rgba;
                }

                // Draw button background
                drawList.AddRectFilled(buttonRect.Min, buttonRect.Max, buttonColor);

                // Draw button text (left-aligned)
                var textPadding = margin;
                var textPos = new Vector2(buttonRect.Min.X + textPadding, buttonRect.GetCenter().Y - fontSize / 2);
                drawList.AddText(font, fontSize, textPos, labelColor, label);

                // Handle click
                if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !indexIsConnected)
                {
                    data.Index.SetTypedInputValue(i);
                    data.Index.DirtyFlag.ForceInvalidate();
                }

            }
            // Draw multi-input region indicator
            var anchorColor = ColorVariations.OperatorOutline.Apply(UiColors.ColorForTextures);
            DrawMultiInputRegion(drawList, workingRect, buttonAreaHeight, canvasScaleY, anchorColor);
            // Draw current index text if connected

            if (indexIsConnected)
            {
                var indexText = $"Index: {currentValue}";
                var titlePos = new Vector2(workingRect.Min.X + margin, workingRect.Max.Y - buttonHeight/2);
                drawList.AddText(font, fontSize, titlePos, labelColor, indexText);
            }
            ImGui.PopClipRect();
            ImGui.PopID();
            return OpUi.CustomUiResult.Rendered
                 | OpUi.CustomUiResult.PreventOpenSubGraph
                 | OpUi.CustomUiResult.PreventInputLabels
                 | OpUi.CustomUiResult.AllowThumbnail
                 | OpUi.CustomUiResult.PreventTooltip;
        }
        else
        {
            return OpUi.CustomUiResult.None;
        }
    }

    private static void DrawMultiInputRegion(ImDrawListPtr drawList, ImRect workingRect, float regionHeight, float canvasScaleY, Color color)
    {
        var regionMinX = workingRect.Min.X - 4.0f * canvasScaleY;
        var regionMinY = workingRect.Min.Y - 4.0f * canvasScaleY;
        var regionWidth = 4.0f * canvasScaleY;
        regionHeight += 8 * canvasScaleY;

        // Define quad points directly without offsets
        var p1 = new Vector2(regionMinX, regionMinY);                    // Top-left
        var p2 = new Vector2(regionMinX + regionWidth, regionMinY + regionWidth); // Top-right (diagonal)
        var p3 = new Vector2(regionMinX + regionWidth, regionMinY + regionHeight - regionWidth); // Bottom-right (diagonal)
        var p4 = new Vector2(regionMinX, regionMinY + regionHeight);     // Bottom-left
        drawList.AddQuadFilled(p1, p2, p3, p4, color);

      //  drawList.AddCircleFilled(p3, 1.0f * canvasScaleY, UiColors.StatusWarning, 8); // Debug disc
    }
}