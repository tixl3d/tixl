using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video.depthanything;

[Guid("6469d616-577c-4b31-a1f2-97992a72e8e9")]
internal sealed class DepthAnythingExampleParallaxMove : Instance<DepthAnythingExampleParallaxMove>
{
    [Output(Guid = "0b59d343-64b7-4e78-b225-db7cd046d746")]
    public readonly Slot<Texture2D> Parallax = new();

    [Input(Guid = "b023aa75-1400-4842-9523-eb0ab7567c36")]
    public readonly InputSlot<string> ImagePath = new();

}
