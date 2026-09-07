using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OpUis.UIs;

public static class DataListUi
{
    public static OpUi.CustomUiResult DrawChildUi(Instance instance, ImDrawListPtr drawList, ImRect screenRect, Vector2 canvasScale)
    {
        return OpUi.CustomUiResult.None;
    }
/*
    public static OpUi.CustomUiResult DrawChildUi(Instance instance, ImDrawListPtr drawList, ImRect selectableScreenRect, Vector2 canvasScale)
    {
        if (!(instance is DataList dataList)
            || !ImGui.IsRectVisible(selectableScreenRect.Min, selectableScreenRect.Max))
            return DefaultResult;

        var innerRect = selectableScreenRect;
        innerRect.Expand(-7);
        if (innerRect.GetHeight() < 1)
            return DefaultResult;

        drawList.AddRectFilled(innerRect.Min, innerRect.Max, UiColors.Gray);
            
        var list = dataList.InputList.Value;
        if (list == null)
        {
            return DefaultResult;
        }
            
        ImGui.SetCursorScreenPos(innerRect.Min);
            
        var modified =global::T3.Editor.Gui.TableView.TableList.Draw(list, innerRect.GetSize());
        if (modified)
        {
            dataList.
                InputList.DirtyFlag.Invalidate();
            dataList.Result.DirtyFlag.Invalidate();
        }

        return DefaultResult;
    }
        
    private const OpUi.CustomUiResult  DefaultResult =             OpUi.CustomUiResult.Rendered
                                                                             | OpUi.CustomUiResult.PreventTooltip
                                                                             | OpUi.CustomUiResult.PreventOpenSubGraph
                                                                             | OpUi.CustomUiResult.PreventInputLabels
                                                                             | OpUi.CustomUiResult.PreventOpenParameterPopUp;
*/
}