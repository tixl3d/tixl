using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.field.use{
    [Guid("f9a3cbf2-a32a-4940-84ca-de11250d02cc")]
    internal sealed class SdfReflectionLinePointsExample : Instance<SdfReflectionLinePointsExample>
    {
        [Output(Guid = "2550d2e7-2cb8-4da8-86da-383e1117877a")]
        public readonly Slot<Texture2D> Result = new Slot<Texture2D>();


    }
}

