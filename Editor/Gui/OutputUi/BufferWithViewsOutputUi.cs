using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

internal sealed class BufferWithViewsOutputUi : OutputUi<BufferWithViews>
{
    public override IOutputUi Clone()
    {
        return new BufferWithViewsOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        var type = typeof(BufferWithViews);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(type.Namespace);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        ImGui.TextUnformatted(type.Name);

        if (slot is not Slot<BufferWithViews> bufferSlot 
            || bufferSlot.Value?.Buffer == null || bufferSlot.Value.Srv == null)
                
        {
            ImGui.TextUnformatted("Undefined");
            return;
        }

        try
        {
            ImGui.Dummy(new Vector2(5,5));
            var elementCount = bufferSlot.Value.Srv.Description.Buffer.ElementCount;
            var totalSize = bufferSlot.Value.Buffer.Description.SizeInBytes;
            ImGui.TextUnformatted($"{elementCount} × {totalSize/elementCount}");
            ImGui.TextUnformatted($"{totalSize} bytes");
        }
        catch (Exception e)
        {
            ImGui.TextUnformatted("Can't access buffer: " + e.Message);
        }
    }
}