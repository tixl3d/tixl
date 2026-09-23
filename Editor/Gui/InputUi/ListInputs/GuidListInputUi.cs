using T3.Core.Operator;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.ListInputs;

internal sealed class GuidListInputUi : ListInputValueUi<List<Guid>>
{
    public override IInputUi Clone()
    {
        return new GuidListInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy
                   };
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref List<Guid> list, bool readOnly)
    {
        return DrawListInputControl(input, ref list);
    }
}
