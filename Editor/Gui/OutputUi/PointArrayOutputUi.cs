using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace T3.Editor.Gui.OutputUi;

internal sealed class PointArrayOutputUi : OutputUi<Point[]>
{
    public override IOutputUi Clone()
    {
        return new PointArrayOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }
        
    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is Slot<Point[]> typedSlot)
        {
            var v = typedSlot.Value;

            if (v == null)
                return;

            for (var index = 0; index < v.Length && index < 50; index++)
            {
                var position = v[index].Position;
                ImGui.TextUnformatted($"{position}");
            }
        }
        else
        {
            Debug.Assert(false);
        }
    }
}