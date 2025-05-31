using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.lib.numbers.floats{
    [Guid("4ce0b357-d71e-4d99-a7f4-3c5b00e6e67c")]
    internal sealed class EaseExample : Instance<EaseExample>
    {
        [Output(Guid = "3a31dcc0-fd2c-4d1c-8e8a-7b04cf374962")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

