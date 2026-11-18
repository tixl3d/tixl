using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Skills._{
    [Guid("be19804d-c269-4266-84b0-091f4f23c24a")]
    internal sealed class _ExampleContent :Instance<_ExampleContent>    {
        [Output(Guid = "b0c6c3cc-200d-4de1-9088-7be166de433f")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();

        [Input(Guid = "fcd39ec2-c822-43f1-b79a-4266cde3787b")]
        public readonly InputSlot<int> Type = new InputSlot<int>();


    }
}

