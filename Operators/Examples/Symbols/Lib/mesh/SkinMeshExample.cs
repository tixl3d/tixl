using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh{
    [Guid("c437039d-6678-4036-bafd-4497e681679c")]
    internal sealed class SkinMeshExample : Instance<SkinMeshExample>
    {
        [Output(Guid = "19b2b57f-1343-4e6f-89c7-5e800b1273fb")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

