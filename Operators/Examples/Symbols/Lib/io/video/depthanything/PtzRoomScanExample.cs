using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video.depthanything;

[Guid("5d2f8c14-7a39-4e6b-b1c0-9e4a2d7f3b58")]
internal sealed class PtzRoomScanExample : Instance<PtzRoomScanExample>
{
    [Output(Guid = "8052cb47-ad6c-419e-e4f3-c17d50a26e8b")]
    public readonly Slot<Command> Cloud = new();

    [Input(Guid = "6e30a925-8b4a-4f7c-c2d1-af5b3e804c69")]
    public readonly InputSlot<string> Address = new("10.0.0.111");

    [Input(Guid = "7f41ba36-9c5b-408d-d3e2-b06c4f915d7a")]
    public readonly InputSlot<bool> Run = new(true);
}
