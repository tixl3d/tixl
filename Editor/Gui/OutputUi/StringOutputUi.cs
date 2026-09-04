using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.OutputUi;

internal sealed class StringOutputUi : OutputUi<string>
{
    public override IOutputUi Clone()
    {
        return new StringOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }
        
    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is Slot<string> typedSlot)
        {
            var stringValue = typedSlot.Value;
            if (stringValue == null)
            {
                ImGui.TextUnformatted("NULL");
                return;
            }
                
            ImGui.BeginChild("ScrollableList");
            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                EditorUi.Instance.SetClipboardText(stringValue);
                Log.Debug("Copied string to clipboard");
            }
                
            ImGui.Indent(10);
            int index = 0;
            foreach (var line in stringValue.Split('\n'))
            {
                index++;
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
                ImGui.TextUnformatted($"{index}   ");
                ImGui.PopStyleVar();
                    
                ImGui.SameLine();
                ImGui.SetCursorPosX(50);
                ImGui.PushFont(Fonts.FontBold);

                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                if (commentIndex == -1)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                    ImGui.TextUnformatted(line);
                    ImGui.PopStyleColor();
                }
                else
                {
                    ReadOnlySpan<char> codePart = line.AsSpan(0, commentIndex);
                    ReadOnlySpan<char> commentPart = line.AsSpan(commentIndex);

                    // Print code part in normal color
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                    ImGui.TextUnformatted(codePart);
                    ImGui.PopStyleColor();
                    
                    ImGui.SameLine();

                    // Print comment part in a different color
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextUnformatted(commentPart);
                    ImGui.PopStyleColor(1);
                }
                ImGui.PopFont();
                
                
            }

            ImGui.EndChild();
        }
        else
        {
            Debug.Assert(false);
        }
    }
}