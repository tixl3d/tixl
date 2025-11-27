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
            _lastConnectionCount = Inputs.GetCollectedTypedInputs()?.Count ?? 0;
        }

        [BindInput("29e289be-e735-4dd4-8826-5e434cc995fa")]
        internal readonly InputSlot<int> Index = null!;

        [BindInput("6C935163-1729-4DF0-A981-610B4AA7C6A3")]
        internal readonly MultiInputSlot<Texture2D> Inputs = null!;

        private int _lastConnectionCount;

        internal bool CheckAndUpdateConnectionCount()
        {
            var currentConnections = Inputs.GetCollectedTypedInputs();
            var currentCount = currentConnections?.Count ?? 0;

            if (currentCount != _lastConnectionCount)
            {
                _lastConnectionCount = currentCount;
                Index.DirtyFlag.ForceInvalidate();
                return true;
            }

            return false;
        }
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

        // Check for connection count changes and force invalidation if needed
        data.CheckAndUpdateConnectionCount();

        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        ImGui.PushClipRect(screenRect.Min, screenRect.Max, true);

        var canvasScaleY = canvas.Scale.Y;
        var font = Fonts.FontBold;
        var labelColor = WidgetElements.GetPrimaryLabelColor(canvasScaleY);

        // Current index
        var isAnimated = instance.Parent?.Symbol.Animator.IsInputSlotAnimated(data.Index) ?? false;
        var indexIsConnected = data.Index.HasInputConnections;
        var currentValue = (isAnimated || indexIsConnected)
                               ? data.Index.Value
                               : data.Index.TypedInputValue.Value;


        var fontSize = Fonts.FontNormal.FontSize * canvasScaleY * .9f;
        var margin = 4.0f * canvasScaleY;
        var buttonSpacing = 6.0f * canvasScaleY;
        var spaceForIndex = 35.0f * canvasScaleY;

        var connections = data.Inputs.GetCollectedTypedInputs();

        if (connections != null && connections.Count > 0)
        {
            // Calculate layout

            var workingRect = screenRect;
            workingRect.Expand(-margin);

            var buttonAreaHeight = workingRect.GetHeight();
            if (indexIsConnected)
            {
                buttonAreaHeight = workingRect.GetHeight() - spaceForIndex;
            }
            var buttonHeight = (buttonAreaHeight - (buttonSpacing * (connections.Count - 1))) / connections.Count;
            buttonHeight = Math.Max(16.0f * canvasScaleY, buttonHeight);

            // Draw buttons
            var buttonTop = workingRect.Min.Y;
            var buttonLeft = workingRect.Min.X;
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

                var buttonY = buttonTop + i * (buttonHeight + buttonSpacing);
                var buttonRect = new ImRect(
                    new Vector2(buttonLeft, buttonY),
                    new Vector2(buttonLeft + buttonWidth, buttonY + buttonHeight)
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
                var textPadding = 4.0f * canvasScaleY;
                var textPos = new Vector2(buttonRect.Min.X + textPadding, buttonRect.GetCenter().Y - fontSize / 2);
                drawList.AddText(font, fontSize, textPos, labelColor, label);

                // Handle click
                if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !indexIsConnected)
                {
                    data.Index.SetTypedInputValue(i);
                    data.Index.DirtyFlag.ForceInvalidate();
                }
                var anchorColor = ColorVariations.OperatorOutline.Apply(UiColors.ColorForTextures);
                // Draw multi-input region indicator
                DrawMultiInputRegion(drawList, workingRect, buttonAreaHeight, canvasScaleY, anchorColor);
            }
            // Draw current index text if connected

            if (indexIsConnected)
            {
                var indexText = $"Index: {currentValue}";
                var titlePos = new Vector2(workingRect.Min.X + 4.0f * canvasScaleY, workingRect.Min.Y + buttonAreaHeight + buttonSpacing * 2);
                drawList.AddText(font, fontSize, titlePos, labelColor, indexText);
            }
        }
        else
        {
            // No connections - just show title
            var titleText = !string.IsNullOrWhiteSpace(instance.SymbolChild.Name)
                ? instance.SymbolChild.Name
                : $"PickTexture";

            var titlePos = screenRect.Min + new Vector2(8.0f * canvasScaleY, 8.0f * canvasScaleY);
            drawList.AddText(font, fontSize, titlePos, labelColor, titleText);
        }

        ImGui.PopClipRect();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
             | OpUi.CustomUiResult.PreventOpenSubGraph
             | OpUi.CustomUiResult.PreventInputLabels
             | OpUi.CustomUiResult.AllowThumbnail
             | OpUi.CustomUiResult.PreventTooltip;
    }

    private static void DrawMultiInputRegion(ImDrawListPtr drawList, ImRect workingRect, float regionHeight, float canvasScaleY, Color color)
    {
        var regionLeft = workingRect.Min.X - 4.0f * canvasScaleY;
        var regionTop = workingRect.Min.Y - 4.0f * canvasScaleY;
        var regionWidth = 4.0f * canvasScaleY;
        regionHeight += 8 * canvasScaleY;

        // Define quad points directly without offsets
        var p1 = new Vector2(regionLeft, regionTop);                    // Top-left
        var p2 = new Vector2(regionLeft + regionWidth, regionTop + regionWidth); // Top-right (diagonal)
        var p3 = new Vector2(regionLeft + regionWidth, regionTop + regionHeight - regionWidth); // Bottom-right (diagonal)
        var p4 = new Vector2(regionLeft, regionTop + regionHeight);     // Bottom-left

        drawList.AddQuadFilled(p1, p2, p3, p4, color);
    }
}