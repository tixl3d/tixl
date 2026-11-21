using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.ai;

[Guid("dbfd4f61-ac7d-481d-8347-dda6068e6643")]
internal sealed class StreamDiffusionExampleV2Restyle : Instance<StreamDiffusionExampleV2Restyle>
{
    [Output(Guid = "19e6c4cb-0000-4e05-bbe0-9d07dd7fbfab")]
    public readonly Slot<Texture2D> Image = new();

    [Input(Guid = "8fcd83a5-892f-411c-8ea2-ca945b318607")]
    public readonly InputSlot<string> ModelPath = new();

    [Input(Guid = "3fd3401f-1b22-46de-a784-03f3dd3eb77e")]
    public readonly InputSlot<string> Prompt = new();

    [Input(Guid = "156ca01c-9dd5-4a89-962f-8a60868084d6")]
    public readonly InputSlot<string> ImagePath = new();

}
