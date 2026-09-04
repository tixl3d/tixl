#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.UiHelpers;

internal abstract class UiListHelpers
{
    internal static void AdvanceSelectedItem<T>(IList<T?> list, ref T? currentItem, int offset)
    {
        if (list.Count == 0)
        {
            currentItem = default;
            return;
        }

        var index = list.IndexOf(currentItem);
        if (index == -1)
        {
            currentItem = list[0];
        }

        var newIndex = WrapIndex(index, offset, list.Count);
        currentItem = list[newIndex];
    }


    private static int WrapIndex(int startIndex, int offset, int count)
    {
        if (count == 0)
            return 0;

        var wrappedIndex = (startIndex + offset) % count;
        return wrappedIndex < 0
                   ? count - 1
                   : wrappedIndex;
    }

    public static void ScrollToMakeItemVisible()
    {
        var windowSize = ImGui.GetWindowSize();
        var scrollTarget = ImGui.GetCursorPos();
        scrollTarget -= new Vector2(0, ImGui.GetFrameHeight() + 4); // adjust to start pos of previous item
        var scrollPos = ImGui.GetScrollY();

        if (scrollTarget.Y < scrollPos)
        {
            ImGui.SetScrollY(scrollTarget.Y);
        }
        else if (scrollTarget.Y + 20 > scrollPos + windowSize.Y)
        {
            ImGui.SetScrollY(scrollPos + windowSize.Y - 20);
        }
    }
}