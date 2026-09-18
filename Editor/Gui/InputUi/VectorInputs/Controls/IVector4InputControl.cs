using T3.Core.Operator.Slots;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.VectorInputs.Controls;

/// <summary>A shared Vector4 renderer that receives input context per draw without retaining instance state.</summary>
internal interface IVector4InputControl
{
    InputEditStateFlags Draw(InputSlot<Vector4> inputSlot, ref Vector4 value);
}
