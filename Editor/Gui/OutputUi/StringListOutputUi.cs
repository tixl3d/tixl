using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

internal sealed class StringListOutputUi : OutputUi<List<string>>
{
    public override IOutputUi Clone()
    {
        return new StringListOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }
        
    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is Slot<List<string>> typedSlot)
        {
            var strings = typedSlot.Value;
            if (strings == null)
            {
                ImGui.TextUnformatted("NULL");
                return;
            }
                
            ImGui.BeginChild("ScrollableList");
                
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted("Count: "+strings.Count);
            ImGui.PopFont();

            for (var index = 0; index < strings.Count; index++)
            {
                var s = strings[index];
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
                ImGui.TextUnformatted($"#{index}   ");
                ImGui.PopStyleVar();
                    
                ImGui.SameLine();
                ImGui.SetCursorPosX(50);
                ImGui.TextUnformatted(s);
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