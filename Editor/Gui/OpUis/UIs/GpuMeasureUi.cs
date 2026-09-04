using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

public static class GpuMeasureUi
{
    public static OpUi.CustomUiResult DrawChildUi(Instance instance, ImDrawListPtr drawList, ImRect screenRect, Vector2 canvasScale)
    {
        return OpUi.CustomUiResult.None;
    }
/*

    public static OpUi.CustomUiResult DrawChildUi(Instance instance, ImDrawListPtr drawList, ImRect selectableScreenRect, Vector2 canvasScale)
    {
        if (!(instance is GpuMeasure measureInstance))
            return OpUi.CustomUiResult.None;

        var symbolChild = instance.SymbolChild;
        ImGui.PushClipRect(selectableScreenRect.Min, selectableScreenRect.Max, true);

        float h = selectableScreenRect.GetHeight();
        var font = h > 50 ? Fonts.FontLarge : h > 25 ? Fonts.FontNormal : Fonts.FontSmall;

        var radius = measureInstance.LastMeasureInMs * 5;
        if (radius > 2)
        {
            drawList.AddCircleFilled(selectableScreenRect.GetCenter(), radius, _color);
        }

        ImGui.PushFont(font);
        ImGui.SetCursorScreenPos(selectableScreenRect.Min + new Vector2(10,0));
        ImGui.BeginGroup();
        if (!string.IsNullOrWhiteSpace(symbolChild.Name))
        {
            ImGui.TextUnformatted(symbolChild.Name);
        }

        ImGui.TextUnformatted($"{measureInstance.LastMeasureInMicroSeconds}µs");
        ImGui.EndGroup();
        ImGui.PopFont();

        ImGui.PopClipRect();
        return OpUi.CustomUiResult.Rendered;
    }

    private static Color _color = new(0.8f, 0.6f, 0.2f, 0.2f);
    */
}