using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.field.use{
    [Guid("eb058346-b33f-4959-b470-6f36c6dfc4d6")]
    internal sealed class ApplyVectorFieldExample : Instance<ApplyVectorFieldExample>
    {
        [Output(Guid = "35af5c1e-f5fb-4b8b-8b83-f86298d9d01e")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

