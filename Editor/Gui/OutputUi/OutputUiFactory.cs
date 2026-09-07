using T3.Core.Compilation;

namespace T3.Editor.Gui.OutputUi;

public static class OutputUiFactory
{
    public static readonly GenericFactory<IOutputUi> Instance = new(typeof(ValueOutputUi<>));
}