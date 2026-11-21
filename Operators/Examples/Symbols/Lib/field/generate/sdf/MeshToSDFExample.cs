using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.field.generate.sdf{
    [Guid("7aeb2189-969f-4edf-90d4-6d9e41ef1120")]
    internal sealed class MeshToSDFExample : Instance<MeshToSDFExample>
    {
        [Output(Guid = "7c75e16f-98d7-45bf-920f-ac4171c2e5c6")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}
