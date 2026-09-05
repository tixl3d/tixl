using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh.modify{
    [Guid("bce9bc96-ff9b-443b-97f5-2286ddd30908")]
    internal sealed class BlendMeshToPointsExample : Instance<BlendMeshToPointsExample>
    {
        [Output(Guid = "70f34914-6f2a-4359-9e33-76b172702aeb")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

