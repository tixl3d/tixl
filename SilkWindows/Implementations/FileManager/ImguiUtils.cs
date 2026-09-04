using System.Numerics;
using ImGuiNET;

namespace SilkWindows.Implementations.FileManager;

internal static class ImguiUtils
{
    public static Vector2 GetButtonSize(string text, bool useSpacing = true)
    {
        var style = ImGui.GetStyle();
        return ImGui.CalcTextSize(text) + style.ItemInnerSpacing * (useSpacing ? 1 : 0) + GetButtonInnerPadding();
    }
    
    private static Vector2 GetButtonInnerPadding() => ImGui.GetStyle().FramePadding * 2;
}