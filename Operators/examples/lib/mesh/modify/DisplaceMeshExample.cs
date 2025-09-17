using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh.modify{
    [Guid("6726bfb3-79cf-46d2-8052-32933e3940be")]
    internal sealed class DisplaceMeshExample : Instance<DisplaceMeshExample>
    {
        [Output(Guid = "7ced0db6-97b0-4459-8bc0-26df7a2fbb70")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

