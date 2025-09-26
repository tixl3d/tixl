#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
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

        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        ImGui.PushClipRect(screenRect.Min, screenRect.Max, true);

        var canvasScaleY = canvas.Scale.Y;
        var font = Fonts.FontBold;
        var labelColor = WidgetElements.GetPrimaryLabelColor(canvasScaleY);

        // Current index
        var isAnimated = instance.Parent?.Symbol.Animator.IsInputSlotAnimated(data.Index) ?? false;
        var currentValue = (isAnimated || data.Index.HasInputConnections)
                               ? data.Index.Value
                               : data.Index.TypedInputValue.Value;

        var connections = data.Inputs.GetCollectedTypedInputs();
        if (connections != null && connections.Count > 0)
        {
            // Calculate layout
            var margin = 4.0f * canvasScaleY;
            var buttonSpacing = 5.0f * canvasScaleY;
            var workingRect = screenRect;
            workingRect.Expand(-margin);

            // Reserve space for title
            var titleHeight = font.FontSize + 12.0f * canvasScaleY;
            var buttonAreaHeight = workingRect.GetHeight() - titleHeight;
            var buttonHeight = (buttonAreaHeight - (buttonSpacing * (connections.Count - 1))) / connections.Count;
            buttonHeight = Math.Max(16.0f * canvasScaleY, buttonHeight);

            // Draw title
            var titleText = !string.IsNullOrWhiteSpace(instance.SymbolChild.Name)
                ? $"{instance.SymbolChild.Name}: {currentValue}"
                : $"PickTexture: {currentValue}";

            var titlePos = workingRect.Min + new Vector2(2.0f * canvasScaleY, 2.0f * canvasScaleY);
            drawList.AddText(font, font.FontSize, titlePos, labelColor, titleText);

            // Draw buttons
            var buttonTop = workingRect.Min.Y + titleHeight;
            var buttonLeft = workingRect.Min.X;
            var buttonWidth = workingRect.GetWidth();
            //buttonWidth -= 5.0f * canvasScaleY; // reserve space for the animated icon
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

                var buttonY = buttonTop + i * (buttonHeight + buttonSpacing);
                var buttonRect = new ImRect(
                    new Vector2(buttonLeft, buttonY),
                    new Vector2(buttonLeft + buttonWidth, buttonY + buttonHeight)
                );

                var isActive = (i == currentValue % connections.Count);
                var isHovered = ImGui.IsWindowHovered() && buttonRect.Contains(ImGui.GetMousePos());

                // Determine button color
                uint buttonColor;
                if (isActive)
                {
                    buttonColor = UiColors.BackgroundActive;
                }
                else if (isHovered && !data.Index.HasInputConnections)
                {
                    buttonColor = UiColors.BackgroundHover;
                }
                else
                {
                    buttonColor = UiColors.BackgroundButton;
                }

                // Draw button background
                drawList.AddRectFilled(buttonRect.Min, buttonRect.Max, buttonColor);
                // drawList.AddRect(buttonRect.Min, buttonRect.Max, UiColors.Text, 0.0f, ImDrawFlags.None, 1.0f);

                // Draw button text (left-aligned)
                var textPadding = 8.0f * canvasScaleY;
                var textPos = new Vector2(buttonRect.Min.X + textPadding, buttonRect.GetCenter().Y - font.FontSize / 2);
                drawList.AddText(font, font.FontSize, textPos, labelColor, label);

                // Handle click
                if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !data.Index.HasInputConnections)
                {
                    data.Index.TypedInputValue.Value = i;
                    data.Index.DirtyFlag.ForceInvalidate();
                }
            }     
        }
        else
        {
            // No connections - just show title
            var titleText = !string.IsNullOrWhiteSpace(instance.SymbolChild.Name)
                ? instance.SymbolChild.Name
                : $"PickTexture: {currentValue}";

            var titlePos = screenRect.Min + new Vector2(8.0f * canvasScaleY, 8.0f * canvasScaleY);
            drawList.AddText(font, font.FontSize, titlePos, labelColor, titleText);
        }

        ImGui.PopClipRect();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
             | OpUi.CustomUiResult.PreventOpenSubGraph
             | OpUi.CustomUiResult.PreventInputLabels
             | OpUi.CustomUiResult.AllowThumbnail
             | OpUi.CustomUiResult.PreventTooltip;
    }
}