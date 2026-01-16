using System.Globalization;
using ImGuiNET;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// A small helper that helps to layout item blocks in a flexible column grid.
/// </summary>
/// <remarks>
/// Using ImGui's Columns would require a pre-pass to calculate the count and width of columns.
/// This method basically sets the cursor-position to "fake" a similar thing. Obviously this
/// can't be nested.
/// </remarks>
internal static class ColumnLayout
{
    internal static void StartLayout(int wrapCount = 10)
    {
        _wrapLineCount = wrapCount;
        _layoutStartPosY = ImGui.GetCursorPosY();
        _currentColumnWidth = 0;
    }

    internal static void StartGroupAndWrapIfRequired(int lineCountInGroup)
    {
        var y = ImGui.GetCursorPosY() - _layoutStartPosY;
        var isFirstBlock = y < 10;
        if (isFirstBlock)
            return;

        var requiredHeight = y + ImGui.GetFrameHeight() * lineCountInGroup;
        var wrapHeight = _wrapLineCount * ImGui.GetFrameHeight();
        if (requiredHeight < wrapHeight)
            return;

        // Start next column
        ImGui.SetCursorPosY(_layoutStartPosY);
        ImGui.Indent(_currentColumnWidth + Padding);
        _currentColumnWidth = 0;
    }

    internal static void ExtendWidth(float itemWidth)
    {
        _currentColumnWidth = MathF.Max(_currentColumnWidth, itemWidth);
    }

    private static  int _wrapLineCount = 10;
    private static float _layoutStartPosY;
    private static float _currentColumnWidth;
    private static float Padding => 30 * T3Ui.UiScaleFactor;
}

/// <summary>
/// Sometimes ImGui's methods to automatically resize windows (like popups) to their correct size doesn't work
/// out of the box. In these this class be used "measure" the required content size by calling <see cref="ExtendToLastItem"/>
/// after each draw call. Before creating the window you can then use <see cref="GetLastAndReset"/> to get the size of the last frame.
/// In most scenarios, the frame delay is not noticeable. 
/// </summary>
internal static class WindowContentExtend
{
    internal static void ExtendToLastItem()
    {
        _currentExtend = Vector2.Max(_currentExtend, ImGui.GetItemRectMax() - ImGui.GetWindowPos()) + new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
    }
    
    internal static void ExtendToLastItem(int width)
    {
        _currentExtend = Vector2.Max(_currentExtend, new Vector2(width, ImGui.GetItemRectMax().Y - ImGui.GetWindowPos().Y + ImGui.GetScrollY()));
    }

    internal static Vector2 GetLastAndReset()
    {
        var lastExtend = _currentExtend;
        _currentExtend = Vector2.Zero;
        return lastExtend;
    }

    private static Vector2 _currentExtend = Vector2.Zero;
}