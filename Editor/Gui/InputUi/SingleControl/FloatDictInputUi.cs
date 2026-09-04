using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.SingleControl;

public sealed class FloatDictInputUi : SingleControlInputUi<Dict<float>>
{
    public override IInputUi Clone()
    {
        return new FloatDictInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy
                   };
    }

    protected override bool DrawSingleEditControl(string name, ref Dict<float> list)
    {
        if (list != null)
        {
            var outputString = string.Join(", ", list);
            ImGui.TextUnformatted($"{outputString}");
        }
        return false;
    }

    protected override void DrawReadOnlyControl(string name, ref Dict<float> list)
    {
        var outputString = (list == null) ? "NULL" :  string.Join(", ", list);
        ImGui.TextUnformatted($"{outputString}");
    }
}