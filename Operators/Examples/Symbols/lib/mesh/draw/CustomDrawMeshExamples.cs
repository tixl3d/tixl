using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh.draw{
    [Guid("b436c16e-df18-44ae-bdf6-5fd2d39f4d71")]
    internal sealed class CustomDrawMeshExamples : Instance<CustomDrawMeshExamples>
    {
        [Output(Guid = "790cb6f6-1d90-4f16-a4ec-d9cb88788406")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

