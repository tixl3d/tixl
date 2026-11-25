using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.field.use{
    [Guid("f1e1a3b7-94e1-4b0c-a340-8ec2b84fb09d")]
    internal sealed class JumpFloodFillExample : Instance<JumpFloodFillExample>
    {
        [Output(Guid = "5595e576-13f5-401a-b7f1-6e4d2039155e")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

