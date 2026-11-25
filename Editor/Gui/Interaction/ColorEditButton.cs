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
            _scaledDragOffset = 0;
            _isHdrUnlocked = new Color(color).V > 1;
        }

        else if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            edited |= InputEditStateFlags.Started;
            _previousColor = color;
            _scaledDragOffset = 0;
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

            AccumulateScaledDrag();
            color.W = (_previousColor.W - _scaledDragOffset / Height).Clamp(0, 1);

            if(_modifiedSlider)
                edited |= InputEditStateFlags.Modified;
        }

        var showBrightnessSlider = ImGui.IsMouseDragging(ImGuiMouseButton.Right) && ImGui.GetID(string.Empty) == _rightClickedItemId;
        if (showBrightnessSlider)
        {
            FrameStats.Current.OpenedPopUpName = "ColorBrightnessSlider";
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            if (MathF.Abs(ImGui.GetMouseDragDelta(ImGuiMouseButton.Right).Y) > UserSettings.Config.ClickThreshold /2f)
            {
                _modifiedSlider = true;
            }

            AccumulateScaledDrag();
            color = GetBrightnessOrHdrColor(out var pullAboveBar);
            DrawBrightnessAndHdrSlider(color, pCenter, pullAboveBar);
            if(_modifiedSlider)
                edited |= InputEditStateFlags.Modified;
        }

        return edited;
    }

    private static bool _modifiedSlider = false;

    /// <summary>
    /// Dragging up with the right mouse button raises the lightness. Pulling on past the top of the bar by the
    /// color picker's unlock distance continues into HDR brightness with the picker's cubic value curve,
    /// keeping hue and saturation. A drag that starts on an HDR color is unlocked from the beginning.
    /// </summary>
    private static Vector4 GetBrightnessOrHdrColor(out float pullAboveBar)
    {
        var previous = new Color(_previousColor);
        var previousV = previous.V;
        var startedInHdr = previousV > 1;

        // An HDR color is mapped onto the bar via its V=1 version, so the drag stays continuous at the top.
        var lightnessBase = previous;
        if (startedInHdr)
            lightnessBase.V = 1;

        var baseHsl = lightnessBase.AsHsl;

        // Position on a virtual bar: 0..1 is the lightness (scaled for HDR starts), above 1 the cube root of the HDR value.
        var startPos = startedInHdr ? MathF.Pow(previousV.Clamp(1, ColorEditPopup.MaxHdrValue), 1 / 3f) : baseHsl.Z;
        var pos = startPos - _scaledDragOffset / Height;
        pullAboveBar = (pos - 1) * Height;

        if (pullAboveBar >= ColorEditPopup.HdrUnlockDistance * T3Ui.UiScaleFactor)
            _isHdrUnlocked = true;

        if (pos > 1 && _isHdrUnlocked)
        {
            var hdrColor = lightnessBase;
            hdrColor.V = MathF.Pow(pos, 3).Clamp(1, ColorEditPopup.MaxHdrValue);
            return hdrColor.Rgba;
        }

        var lightness = startedInHdr ? pos.Clamp(0, 1) * baseHsl.Z : pos.Clamp(0, 1);
        return Color.ColorFromHsl(baseHsl.X, baseHsl.Y, lightness, _previousColor.W).Rgba;
    }

    /// <summary>
    /// The slider is a tape that moves with the mouse and is read at the swatch center, so the bright end sits
    /// at the bottom and the HDR range continues below it.
    /// </summary>
    private static void DrawBrightnessAndHdrSlider(Vector4 color, Vector2 pCenter, float pullAboveBar)
    {
        var drawList = ImGui.GetForegroundDrawList();
        var hdrValue = new Color(color).V;
        var isHdr = hdrValue > 1 && pullAboveBar > 0;
        var barPos = 1 + pullAboveBar / Height;
        VerticalColorSlider(color, pCenter, isHdr ? barPos : barPos.Clamp(0, 1));

        var barLeft = pCenter.X + 15;
        var labelPos = new Vector2(barLeft + SliderBarWidth + 6, pCenter.Y - ImGui.GetTextLineHeight() / 2);
        if (isHdr)
        {
            var opaqueColor = color;
            opaqueColor.W = 1;
            var stripTop = new Vector2(barLeft, pCenter.Y - pullAboveBar);
            var stripBottom = new Vector2(barLeft + SliderBarWidth, pCenter.Y + HdrStripOvershoot * T3Ui.UiScaleFactor);
            drawList.AddRectFilledMultiColor(stripTop, stripBottom,
                                             ImGui.ColorConvertFloat4ToU32(opaqueColor),
                                             ImGui.ColorConvertFloat4ToU32(opaqueColor),
                                             ImGui.ColorConvertFloat4ToU32(UiColors.ForegroundFull),
                                             ImGui.ColorConvertFloat4ToU32(UiColors.ForegroundFull));
            drawList.AddRectFilled(new Vector2(barLeft - 15, pCenter.Y), new Vector2(barLeft + SliderBarWidth, pCenter.Y + 1),
                                   UiColors.BackgroundFull);
            DrawLabelWithBackdrop(drawList, labelPos, UiColors.Text, $"× {hdrValue:0.00}");
        }
        else if (pullAboveBar > 0)
        {
            // Signals that pulling on unlocks HDR: the label brightens as the pull approaches the unlock distance.
            var progress = (pullAboveBar / (ColorEditPopup.HdrUnlockDistance * T3Ui.UiScaleFactor)).Clamp(0, 1);
            DrawLabelWithBackdrop(drawList, labelPos, Color.Mix(UiColors.TextMuted, UiColors.ForegroundFull, progress), "HDR");
        }
    }

    private static void DrawLabelWithBackdrop(ImDrawListPtr drawList, Vector2 pos, Color color, string label)
    {
        var padding = new Vector2(4, 1) * T3Ui.UiScaleFactor;
        drawList.AddRectFilled(pos - padding, pos + ImGui.CalcTextSize(label) + padding, UiColors.BackgroundFull.Fade(0.8f),
                               2 * T3Ui.UiScaleFactor);
        drawList.AddText(pos, color, label);
    }

    private const float HdrStripOvershoot = 40;
    private const int SliderBarWidth = 10;

    internal static void VerticalColorSlider(Vector4 color, Vector2 pCenter, float valuePos)
    {
        const int barWidth = SliderBarWidth;
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

    /// <summary>
    /// Accumulates the vertical mouse movement scaled by the current precision factor, so
    /// toggling SHIFT mid-drag changes sensitivity without jumping the value.
    /// </summary>
    private static void AccumulateScaledDrag()
    {
        var precisionMode = ImGui.GetIO().KeyShift;
        _scaledDragOffset += ImGui.GetIO().MouseDelta.Y * (precisionMode ? PrecisionDragFactor : 1f);

        if (precisionMode)
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.GetForegroundDrawList().AddText(ImGui.GetMousePos() + new Vector2(10, 10) * T3Ui.UiScaleFactor,
                                                  UiColors.Gray, "×0.1");
            ImGui.PopFont();
        }
    }

    private static int Height => (int)(200 * T3Ui.UiScaleFactor);
    private const float PrecisionDragFactor = 0.1f;
    private static float _scaledDragOffset;
    private static uint _rightClickedItemId;
    private static Vector4 _previousColor;

    /** Set once the current right-drag started on an HDR color or was pulled past the unlock distance. */
    private static bool _isHdrUnlocked;
}