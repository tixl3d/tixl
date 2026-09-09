using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.image.transform{
    [Guid("97ff0a3b-5387-4c52-8982-70d8514ba4a5")]
    internal sealed class RgbSplitShiftExample : Instance<RgbSplitShiftExample>
    {
        [Output(Guid = "a78b9677-9fa0-49ae-a52f-bfe622c6820a")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

