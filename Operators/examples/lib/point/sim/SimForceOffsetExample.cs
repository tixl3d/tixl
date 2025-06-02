using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.sim{
    [Guid("6e43fb51-1974-4020-a5fa-934de5bf04af")]
    internal sealed class SimForceOffsetExample : Instance<SimForceOffsetExample>
    {
        [Output(Guid = "46311adb-030a-4729-aaa7-4caf45494c98")]
        public readonly Slot<Texture2D> ImgOutput = new Slot<Texture2D>();


    }
}

