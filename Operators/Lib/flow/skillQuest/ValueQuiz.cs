using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lib.flow.skillQuest
{
    [Guid("702f1f75-df90-44ca-b567-78ba9d1613b5")]
    internal sealed class ValueQuiz : Instance<ValueQuiz>
    {
        [Output(Guid = "6fbc9fd1-58c2-4b0b-accb-9850a8835120")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();

        [Input(Guid = "270bda80-7f81-4f31-adb4-9cd5e8aea693")]
        public readonly InputSlot<float> Yours = new InputSlot<float>();

        [Input(Guid = "c09a529a-dfe2-4bf8-86ce-369aeadbee4a")]
        public readonly InputSlot<float> Goal = new InputSlot<float>();

        [Input(Guid = "ba4714ca-594a-4fab-aa01-bf02d1164eb2")]
        public readonly InputSlot<System.Numerics.Vector2> DifferenceRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "fe12fa62-9207-4a6a-bbb3-4d57218094bf")]
        public readonly InputSlot<System.Numerics.Vector2> DisplayValueRange = new InputSlot<System.Numerics.Vector2>();
    }
}