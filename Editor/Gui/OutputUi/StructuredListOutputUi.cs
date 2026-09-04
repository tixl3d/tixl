using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.TableView;

namespace T3.Editor.Gui.OutputUi;

internal sealed class StructuredListOutputUi : OutputUi<StructuredList>
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
        if (slot is Slot<StructuredList> typedSlot)
        {
            var list = typedSlot.Value;
            if (list == null)
            {
                ImGui.TextUnformatted("NULL?");
            }
            else
            {
                var modified = TableList.Draw(typedSlot.Value);
                if (modified)
                {
                    typedSlot.DirtyFlag.Invalidate();
                }
            }
        }
        else
        {
            Debug.Assert(false);
        }
    }
}