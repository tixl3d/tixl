using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh{
    [Guid("786ad268-9a76-4675-a755-6c2922fb3916")]
    internal sealed class DrawMeshChunksAtPointsExample : Instance<DrawMeshChunksAtPointsExample>
    {
        [Output(Guid = "760a21b6-b8de-4968-8a5b-d3ac2a7e3938")]
        public readonly Slot<Texture2D> Result = new Slot<Texture2D>();


    }
}

