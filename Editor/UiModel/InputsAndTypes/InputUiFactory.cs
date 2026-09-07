using T3.Core.Compilation;
using T3.Editor.Gui.InputUi.SimpleInputUis;

namespace T3.Editor.UiModel.InputsAndTypes;

internal static class InputUiFactory
{
    public static readonly GenericFactory<IInputUi> Instance = new(typeof(ValueInputUi<>), typeof(EnumInputUi<>));
}