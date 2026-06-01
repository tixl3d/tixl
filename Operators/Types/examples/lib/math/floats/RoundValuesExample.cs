using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;

namespace T3.Operators.Types.Id_f5c2bf4a_18ca_4301_ab72_edd809e74f4e
{
    public class RoundValuesExample : Instance<RoundValuesExample>
    {

        [Output(Guid = "2d2af358-b6c2-4714-8722-fc182a391f3b")]
        public readonly Slot<SharpDX.Direct3D11.Texture2D> ImgOutput = new Slot<SharpDX.Direct3D11.Texture2D>();

    }
}

