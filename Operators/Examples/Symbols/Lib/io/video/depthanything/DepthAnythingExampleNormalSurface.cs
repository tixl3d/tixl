using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video.depthanything;

[Guid("5e2b0f2a-f01f-4a2f-a7a3-91ea37fa39db")]
internal sealed class DepthAnythingExampleNormalSurface : Instance<DepthAnythingExampleNormalSurface>
{
    [Output(Guid = "05611821-ee33-48f4-9bb6-ed3bda9196b9")]
    public readonly Slot<Texture2D> Normals = new();

    [Output(Guid = "1e61ff87-bd39-4781-82e7-31691bb6dbd9")]
    public readonly Slot<Texture2D> Positions = new();

    [Input(Guid = "c0e59cd2-6944-45cf-8168-b23d6a1e0227")]
    public readonly InputSlot<string> ImagePath = new();

}
