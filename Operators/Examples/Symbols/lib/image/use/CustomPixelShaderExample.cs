using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.image.use{
    [Guid("af263c08-4f22-45a4-ad8c-caeb813c237b")]
    internal sealed class CustomPixelShaderExample : Instance<CustomPixelShaderExample>
    {

        [Output(Guid = "c18cb083-0743-45ae-bb29-b2fa23803418")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> ColorBuffer = new Slot<T3.Core.DataTypes.Texture2D>();

    }
}

