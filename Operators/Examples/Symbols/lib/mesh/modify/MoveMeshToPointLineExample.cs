using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh.modify{
    [Guid("29da7486-cee4-40cd-a708-96b1a84c4f9c")]
    internal sealed class MoveMeshToPointLineExample : Instance<MoveMeshToPointLineExample>
    {
        [Output(Guid = "4bb4d23c-f5d8-4db2-b694-8a822cc14464")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

