using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh.modify{
    [Guid("11187aad-c763-48ed-9d5f-216fae079285")]
    internal sealed class Warp2dMeshExample : Instance<Warp2dMeshExample>
    {
        [Output(Guid = "d6a2a928-e454-415d-8b51-8af51ea80060")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

