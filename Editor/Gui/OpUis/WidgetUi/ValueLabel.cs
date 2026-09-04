using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.WidgetUi;

public static class ValueLabel
{
    public static bool Draw(ImDrawListPtr drawList, ImRect screenRect, Vector2 alignment, InputSlot<float> inputSlot)
    {
        if (screenRect.GetHeight() < 15)
            return false;
            
        var modified = false;
        //var value = (double)inputSlot.TypedInputValue.Value;
        var value = (double)inputSlot.GetCurrentValue();
            // // (inputSlot.HasInputConnections) 
            //             ? (double)inputSlot.Value 
            //             :(double)inputSlot.TypedInputValue.Value;
            
        var valueText = $"{value:G5}";
        var hashCode = inputSlot.GetHashCode();
        ImGui.PushID(hashCode);
            
        var editingUnlocked = ImGui.GetIO().KeyCtrl || _jogDialValue != null;
        var highlight = editingUnlocked;
            
        // InputGizmo
        {
            var labelSize = screenRect.GetSize() / 4 + Vector2.One * 4;
            var space = screenRect.GetSize() - labelSize;
            var position = screenRect.Min + space * alignment - Vector2.One * 2;
            ImGui.SetCursorScreenPos(position);
            if (editingUnlocked)
            {
                ImGui.InvisibleButton("button", labelSize);
                    
                //double value2 = inputSlot.TypedInputValue.Value;
                var restarted = false;
                if (ImGui.IsItemActivated() && ImGui.GetIO().KeyCtrl)
                {
                    _jogDailCenter = ImGui.GetIO().MousePos;
                    _jogDialValue = inputSlot;
                    restarted = true;
                }
                    
                if (ImGui.IsItemActive() || !ImGui.IsAnyItemActive())
                {
                    drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), _hoverRegionColor);
                }
                else
                {
                    highlight = false;
                }

                if (_jogDialValue == inputSlot)
                {
                    if (ImGui.IsItemActive())
                    {
                        SingleValueEdit.DrawValueEditMethod(ref value,  restarted, _jogDailCenter,double.NegativeInfinity, double.PositiveInfinity, false, false, 0.025f);
                        inputSlot.SetTypedInputValue((float)value);
                        modified = true;
                    }
                    else
                    {
                        _jogDialValue = null;
                    }
                }
            }
        }
            
        // Draw aligned label
        if(!string.IsNullOrEmpty(valueText)){
            ImGui.PushFont(Fonts.FontSmall);
            var labelSize = ImGui.CalcTextSize(valueText);
            var space = screenRect.GetSize() - labelSize;
            var position = screenRect.Min + space * alignment;
            drawList.AddText(MathUtils.Floor(position), highlight ? UiColors.WidgetValueTextHover : UiColors.WidgetValueText, valueText);
            ImGui.PopFont();
        }            
        ImGui.PopID();
        return modified;
    }

    private static readonly Color _hoverRegionColor = new(0, 0, 0, 0.2f);
    private static Vector2 _jogDailCenter;
    private static InputSlot<float> _jogDialValue;        
}