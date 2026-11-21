using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.ai;

[Guid("fe71eba4-ba4d-4fc3-b6bf-3cb4a33cdbcc")]
internal sealed class StreamDiffusionExample : Instance<StreamDiffusionExample>
{
    [Output(Guid = "a9370a92-822e-434f-98e3-90691a7e44f5")]
    public readonly Slot<Texture2D> Image = new();

    [Input(Guid = "669328d8-66ff-4fba-a23a-99aa97bc12f6")]
    public readonly InputSlot<string> ModelPath = new();

    [Input(Guid = "e5bf45ec-137e-4d91-acff-7fd5298d9665")]
    public readonly InputSlot<string> Prompt = new();

}
