using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video.depthanything;

[Guid("90f1f8ad-201d-4471-9c8a-b5cf1faf9317")]
internal sealed class DepthAnythingExample : Instance<DepthAnythingExample>
{
    [Output(Guid = "e20fba13-866f-40e1-8399-c292b801b861")]
    public readonly Slot<Texture2D> Depth = new();

    [Input(Guid = "ce32cdf5-9929-403a-abdf-ba537cdb06c3")]
    public readonly InputSlot<string> ImagePath = new();

}
