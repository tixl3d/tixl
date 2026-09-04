using T3.Core.Operator;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.ListInputs;

internal sealed class ColorListInputUi : ListInputValueUi<List<Vector4>>
{
    public override IInputUi Clone()
    {
        return new ColorListInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy
                   };
    }
    
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref List<Vector4> list, bool readOnly)
    {
        return DrawListInputControl(input, ref list);
    }
}