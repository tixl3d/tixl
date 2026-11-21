using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video.depthanything;

[Guid("2f6a8c41-93d7-4b58-a2e9-7c1d5b3f6e84")]
internal sealed class DepthAnythingSplatExample : Instance<DepthAnythingSplatExample>
{
    [Output(Guid = "8d4e7a2c-6b39-4f15-92d8-5a3c7e1f4b96")]
    public readonly Slot<Texture2D> Splat = new();

    [Input(Guid = "4a9e2c7f-8b31-4d65-9a24-6e5f8c3b1d70")]
    public readonly InputSlot<string> StreamUrl = new("rtsp://10.0.0.189:8554/rpi_webcam");
}
