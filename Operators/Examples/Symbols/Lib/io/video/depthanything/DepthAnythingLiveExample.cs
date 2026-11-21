using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video.depthanything;

[Guid("7e3a2f91-8b4c-4d27-a516-3c9e2b8f70da")]
internal sealed class DepthAnythingLiveExample : Instance<DepthAnythingLiveExample>
{
    [Output(Guid = "b9f2c4a8-1d7d-4e67-9a28-f5c3e1d2b640")]
    public readonly Slot<Texture2D> Depth = new();

    [Input(Guid = "cf2e5d9a-417b-4c86-9e3f-2b71d4a50e13")]
    public readonly InputSlot<string> StreamUrl = new("rtsp://10.0.0.189:8554/rpi_webcam");
}
