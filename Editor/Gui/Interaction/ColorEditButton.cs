using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Interaction;

public static class ColorEditButton
{
    public static InputEditStateFlags Draw(ref Vector4 color, Vector2 size, bool triggerOpen = false)
    {
        var edited = InputEditStateFlags.Nothing;
            
        var buttonPosition = ImGui.GetCursorScreenPos();
        ImGui.ColorButton("##thumbnail", color, ImGuiColorEditFlags.AlphaPreviewHalf, size);

        if (MathUtils.HasHdrRange(color, out var intensity))
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var center = (min + max) / 2;
            ImGui.GetWindowDrawList().DrawTriangleUp(center, 
                                    Color.Black, 
                                    intensity *4);
        }

        if (ImGui.IsItemHovered())
        {
            T3Ui.DragFieldHovered = true;
        }
            
        // Don't use ImGui.IsItemActivated() to allow quick switching between color thumbnails
        if (triggerOpen || ImGui.IsItemHovered( ImGuiHoveredFlags.AllowWhenBlockedByPopup)
            && ImGui.IsMouseReleased(0)
            && ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length() < UserSettings.Config.ClickThreshold
            && !ImGui.IsPopupOpen(ColorEditPopup.PopupId)
           )
        {
            _previousColor = color;
            _modifiedSlider = false;
            ImGui.OpenPopup(ColorEditPopup.PopupId);
            ImGui.SetNextWindowPos(ImGui.GetItemRectMax() + new Vector2(4,10));
        }
            
        edited |= HandleQuickSliders(ref color, buttonPosition);
        edited |= ColorEditPopup.DrawPopup(ref color, _previousColor);
        return edited;
    }

    private static InputEditStateFlags HandleQuickSliders(ref Vector4 color, Vector2 buttonPosition)
    {
        var edited = InputEditStateFlags.Nothing;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            edited |= InputEditStateFlags.Started;
            _rightClickedItemId = ImGui.GetID(string.Empty);
            _previousColor = color;
        }
            
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            edited |= InputEditStateFlags.Started;
            _previousColor = color;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            _rightClickedItemId = 0;
                
            if(_modifiedSlider)
                edited |= InputEditStateFlags.Finished;
                
            _modifiedSlider = false;
        }
            
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if(_modifiedSlider)
                edited |= InputEditStateFlags.Finished;
                
            _modifiedSlider = false;
        }

        var pCenter = buttonPosition + Vector2.One * ImGui.GetFrameHeight() / 2;

        var showAlphaSlider = ImGui.IsMouseDragging(ImGuiMouseButton.Left) && ImGui.IsItemActive();
        if (showAlphaSlider)
        {
            var valuePos = color.W;
            VerticalColorSlider(color, pCenter, valuePos);
            if (MathF.Abs(ImGui.GetMouseDragDelta().Y) > UserSettings.Config.ClickThreshold / 2f)
                _modifiedSlider = true;

            color.W = (_previousColor.W - ImGui.GetMouseDragDelta().Y * DragFactor/ Height).Clamp(0, 1);
                
            if(_modifiedSlider)
                edited |= InputEditStateFlags.Modified;
        }

        var showBrightnessSlider = ImGui.IsMouseDragging(ImGuiMouseButton.Right) && ImGui.GetID(string.Empty) == _rightClickedItemId;
        if (showBrightnessSlider)
        {
            FrameStats.Current.OpenedPopUpName = "ColorBrightnessSlider";
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var hsb = new Color(color).AsHsl;
            var previousHsb = new Color(_previousColor).AsHsl;

            var valuePos = hsb.Z;
            VerticalColorSlider(color, pCenter, valuePos);

            if (MathF.Abs(ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Y) > UserSettings.Config.ClickThreshold /2f)
            {
                _modifiedSlider = true;
            }

            var newBrightness = (previousHsb.Z - ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Y * DragFactor/ Height).Clamp(0, 1);
            color = Color.ColorFromHsl(previousHsb.X, previousHsb.Y, newBrightness, _previousColor.W);
            if(_modifiedSlider)
                edited |= InputEditStateFlags.Modified;
        }

        return edited;
    }

    private static bool _modifiedSlider = false;

    internal static void VerticalColorSlider(Vector4 color, Vector2 pCenter, float valuePos)
    {
        const int barWidth = 10;
        var drawList = ImGui.GetForegroundDrawList();
        var pMin = pCenter + new Vector2(15, -Height * valuePos);
        var pMax = pMin + new Vector2(barWidth, Height);
        var area = new ImRect(pMin, pMax);
        drawList.AddRectFilled(pMin - Vector2.One, pMax + Vector2.One, new Color(0.1f, 0.1f, 0.1f));
        CustomComponents.FillWithStripes(drawList, area, 1);

        // Draw Slider
        var opaqueColor = color;
        opaqueColor.W = 1;
        var transparentColor = color;
        transparentColor.W = 0;
        drawList.AddRectFilledMultiColor(pMin, pMax,
                                         ImGui.ColorConvertFloat4ToU32(transparentColor),
                                         ImGui.ColorConvertFloat4ToU32(transparentColor),
                                         ImGui.ColorConvertFloat4ToU32(opaqueColor),
                                         ImGui.ColorConvertFloat4ToU32(opaqueColor));

        drawList.AddRectFilled(pCenter, pCenter + new Vector2(barWidth + 15, 1), UiColors.BackgroundFull);
    }

    private static int Height => (int)(200 * T3Ui.UiScaleFactor);
    private static float DragFactor = ImGui.GetIO().KeyShift ? 0.5f : 1f;
    private static uint _rightClickedItemId;
    private static Vector4 _previousColor;
}