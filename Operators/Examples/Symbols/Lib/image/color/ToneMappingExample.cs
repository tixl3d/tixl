using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.image.color{
    [Guid("242c2f29-a00b-4e33-aba7-be57c60aa8e2")]
    internal sealed class ToneMappingExample : Instance<ToneMappingExample>
    {
        [Output(Guid = "f97b5bf6-29d3-4105-9cb8-2799ea2082fb")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

