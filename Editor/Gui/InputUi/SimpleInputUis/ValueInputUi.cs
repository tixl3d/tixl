using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

internal sealed class ValueInputUi<T> : InputValueUi<T>
{
    public override IInputUi Clone()
    {
        return new ValueInputUi<T>
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy,
                       Size = Size
                   };
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref T value, bool readOnly)
    {
        ImGui.TextUnformatted(""); // Print an empty text to force layout to next line
        return InputEditStateFlags.Nothing;
    }

    protected override void DrawReadOnlyControl(string name, ref T value)
    {
        ImGui.Button($"{name}", new Vector2(-1,0));
        //ImGui.Text($"{name}"); // Print an empty text to force layout to next line
    }
}