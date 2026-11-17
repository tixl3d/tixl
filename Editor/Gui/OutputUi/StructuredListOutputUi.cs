#nullable enable
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
        return new StructuredListOutputUi()
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
                // Ops like [ReadPointsFromGpu] output a new list per read; an unchanged reference means nothing changed.
                _previousList = list != _shownList ? _shownList : null;
                _shownList = list;

                var modified = TableList.Draw(list, _previousList);
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

    private StructuredList? _shownList;
    private StructuredList? _previousList;
}