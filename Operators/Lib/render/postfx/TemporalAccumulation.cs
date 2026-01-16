using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.render.postfx{
    [Guid("196ad0dc-035a-4fb6-8871-a8f3c9cdbef5")]
    internal sealed class TemporalAccumulation : Instance<TemporalAccumulation>
    {
        [Output(Guid = "2332fcce-5392-4f6c-befc-3c67ca494916")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


        [Input(Guid = "0ecb558b-d9a1-4c6f-9097-9f558d002df8")]
        public readonly InputSlot<Texture2D> Texture = new InputSlot<Texture2D>();

        [Input(Guid = "b0eece89-6c27-48d6-87c7-62d04469ab2e")]
        public readonly InputSlot<float> FeedbackAmount = new InputSlot<float>();

    }
}

