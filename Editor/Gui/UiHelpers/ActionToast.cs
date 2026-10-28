#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.Layouts;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// A short message, right-aligned in the main menu bar over the log line (at the bottom of the main window when the
/// menu bar is hidden), that tells what a keyboard action did when its result isn't
/// obvious, or teaches a variant of it. One message at a time; a new one replaces it. Hovering keeps it visible.
/// The text uses the <see cref="KeyHintText"/> markup: "{Output shows Blur}  hold [P] to only peek".
/// Tips are messages that teach a related feature: they stop once the feature was used (<see cref="MarkLearned"/>)
/// or after a few appearances. What was seen is kept in <see cref="ToastSettings"/>.
/// </summary>
internal static class ActionToast
{
    /// <param name="icon">Tells it apart from the warnings and errors usually shown in that place</param>
    internal static void Show(string markup, Icon icon = Icon.Tip)
    {
        if (!Settings.Enabled)
            return;

        _icon = icon;
        KeyHintText.Parse(markup, _segments);
        _shownTime = ImGui.GetTime();
        _isVisible = true;
    }

    /// <summary>
    /// Shows a tip unless its feature was already used or it was shown <paramref name="maxTimes"/> times.
    /// </summary>
    internal static void ShowTip(string tipId, string markup, int maxTimes = DefaultMaxTipShowCount)
    {
        if (!Settings.Enabled || Settings.LearnedTips.Contains(tipId))
            return;

        Settings.TipShowCounts.TryGetValue(tipId, out var showCount);
        if (showCount >= maxTimes)
            return;

        Settings.TipShowCounts[tipId] = showCount + 1;
        Show(markup);
    }

    /** The user has used the feature a tip teaches, so it isn't shown anymore */
    internal static void MarkLearned(string tipId)
    {
        Settings.LearnedTips.Add(tipId);
    }

    internal static void ResetTips()
    {
        Settings.LearnedTips.Clear();
        Settings.TipShowCounts.Clear();
    }

    private static ToastSettings Settings => UserSettings.Config.Toasts;

    /// <summary>
    /// Called each frame the menu bar is drawn, so the toast can sit over the log line.
    /// </summary>
    /// <param name="logMessageMinX">Where the log message starts; the toast covers it completely</param>
    /// <param name="textTop">Top of the menu bar's text, so the toast's text lines up with the menu labels</param>
    internal static void SetMenuBarArea(ImRect area, float reservedRight, float logMessageMinX, float textTop)
    {
        _menuBarTextTop = textTop;
        _menuBarArea = area;
        _menuBarReservedRight = reservedRight;
        _logMessageMinX = logMessageMinX;
        _menuBarFrame = ImGui.GetFrameCount();
    }

    internal static void Draw()
    {
        if (!_isVisible)
            return;

        var time = ImGui.GetTime();
        var elapsed = time - _shownTime;
        if (elapsed > FadeInSec + VisibleSec + FadeOutSec)
        {
            _isVisible = false;
            return;
        }

        var opacity = (float)Math.Min(elapsed / FadeInSec, Math.Clamp((FadeInSec + VisibleSec + FadeOutSec - elapsed) / FadeOutSec, 0, 1));

        var scale = T3Ui.UiScaleFactor;
        var lineHeight = ImGui.GetTextLineHeight();
        var iconSize = MathF.Round(lineHeight);
        var iconAdvance = iconSize + IconGapPx * scale;

        // The icon is drawn left of the text and counts as part of its width
        var textWidth = KeyHintText.GetWidth(_segments) + iconAdvance;
        Vector2 min, max, textPos;
        // Focus mode hides the menu bar, so the toast floats at the bottom there
        var isInMenuBar = !LayoutHandling.FocusMode && _menuBarFrame >= ImGui.GetFrameCount() - 1;
        if (isInMenuBar)
        {
            // Right-aligned in the menu bar, covering the whole log line so no longer message shows through
            var paddingX = PaddingXPx * scale;
            max = new Vector2(MathF.Floor(_menuBarArea.Max.X - _menuBarReservedRight), MathF.Floor(_menuBarArea.Max.Y));
            var minX = MathF.Min(max.X - textWidth - 2 * paddingX, _logMessageMinX - paddingX);
            min = new Vector2(MathF.Floor(minX), MathF.Floor(_menuBarArea.Min.Y));
            var textTop = float.IsNaN(_menuBarTextTop) ? (min.Y + max.Y - lineHeight) / 2 : _menuBarTextTop;
            textPos = new Vector2(MathF.Floor(max.X - textWidth - paddingX), MathF.Floor(textTop));
        }
        else
        {
            var padding = new Vector2(PaddingXPx, PaddingYPx) * scale;
            var size = new Vector2(textWidth, lineHeight) + 2 * padding;
            var viewport = ImGui.GetMainViewport();
            min = new Vector2(MathF.Floor(viewport.WorkPos.X + (viewport.WorkSize.X - size.X) / 2),
                              MathF.Floor(viewport.WorkPos.Y + viewport.WorkSize.Y - size.Y - BottomMarginPx * scale));
            max = min + new Vector2(MathF.Round(size.X), MathF.Round(size.Y));
            textPos = min + padding;
        }

        // Reading it keeps it there
        if (new ImRect(min, max).Contains(ImGui.GetMousePos()))
            _shownTime = time - FadeInSec;

        // Without an argument this would be the current window's viewport, which isn't the main one in focus mode
        var drawList = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        if (isInMenuBar)
        {
            // Part of the bar: its own background, no frame
            drawList.AddRectFilled(min, max, UiColors.BackgroundGaps.Fade(opacity));
        }
        else
        {
            drawList.AddRectFilled(min, max, UiColors.BackgroundPopup.Fade(opacity), RoundingPx * scale);
            drawList.AddRect(min, max, UiColors.ForegroundFull.Fade(0.12f * opacity), RoundingPx * scale);
        }
        var iconPos = new Vector2(MathF.Floor(textPos.X), MathF.Floor(textPos.Y + (lineHeight - iconSize) / 2));
        Icons.DrawIconAtScreenPosition(_icon, iconPos, new Vector2(iconSize), drawList, UiColors.TextMuted.Fade(opacity));
        KeyHintText.Draw(drawList, textPos + new Vector2(iconAdvance, 0), _segments, opacity: opacity, isProminent: true);
    }

    private const int DefaultMaxTipShowCount = 5;
    private const double FadeInSec = 0.12;
    private const float IconGapPx = 6;
    private const double VisibleSec = 2.5;
    private const double FadeOutSec = 0.4;
    private const float PaddingXPx = 12;
    private const float PaddingYPx = 7;
    private const float RoundingPx = 6;
    private const float BottomMarginPx = 60;

    private static readonly List<KeyHintText.Segment> _segments = new(6);
    private static double _shownTime;
    private static Icon _icon = Icon.Tip;
    private static ImRect _menuBarArea;
    private static float _menuBarReservedRight;
    private static float _logMessageMinX = float.PositiveInfinity;
    private static float _menuBarTextTop = float.NaN;
    private static int _menuBarFrame = -10;
    private static bool _isVisible;
}
