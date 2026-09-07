using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

internal sealed class ColorListOutputUi : OutputUi<List<Vector4>>
{
    public override IOutputUi Clone()
    {
        return new ColorListOutputUi()
        {
            OutputDefinition = OutputDefinition,
            PosOnCanvas = PosOnCanvas,
            Size = Size
        };
    }
        
    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is Slot<List<Vector4>> typedSlot)
        {
            var colors = typedSlot.Value;
            if (colors == null)
            {
                ImGui.TextUnformatted("NULL");
                return;
            }
                
            ImGui.BeginChild("ScrollableList");
                
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted("Count: "+colors.Count);
            ImGui.PopFont();

            for (var index = 0; index < colors.Count; index++)
            {
                var s = colors[index];
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
                ImGui.TextUnformatted($"#{index}   ");
                ImGui.PopStyleVar();
                    
                ImGui.SameLine();
                ImGui.SetCursorPosX(50);
                //ImGui.TextUnformatted(s);
                ImGui.ColorButton("" + index, s, ImGuiColorEditFlags.DisplayRGB);
            }

            //var outputString = string.Join("\n", strings);
            ImGui.EndChild();
        }
        else
        {
            Debug.Assert(false);
        }
    }
}