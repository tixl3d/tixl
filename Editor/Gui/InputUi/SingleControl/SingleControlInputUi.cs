using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.SingleControl;

public abstract class SingleControlInputUi<T> : InputValueUi<T>
{
    protected abstract bool DrawSingleEditControl(string name, ref T value);
 
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref T value, bool readOnly)
    {
        bool valueModified = DrawSingleEditControl(name, ref value);
 
        InputEditStateFlags inputEditStateFlags = InputEditStateFlags.Nothing;
        inputEditStateFlags |= ImGui.IsItemClicked() ? InputEditStateFlags.Started : InputEditStateFlags.Nothing;
        inputEditStateFlags |= valueModified ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
        inputEditStateFlags |= ImGui.IsItemDeactivatedAfterEdit() ? InputEditStateFlags.Finished : InputEditStateFlags.Nothing;
 
        return inputEditStateFlags;
    }
}