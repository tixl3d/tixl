using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.video{
    [Guid("aee0986e-1bb3-4f3a-a475-c12c7abec143")]
    internal sealed class TrackMarkersExample : Instance<TrackMarkersExample>
    {
        [Output(Guid = "ab37d762-df46-46ed-ad26-146cd1611720")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

