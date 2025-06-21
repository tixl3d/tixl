using T3.Core.Operator;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.ListInputs;

internal sealed class IntListInputUi : ListInputValueUi<List<int>>
{
    public override IInputUi Clone()
    {
        return new IntListInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy
                   };
    }
    
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref List<int> list, bool readOnly)
    {
        return DrawListInputControl(input, ref list);
    }
}