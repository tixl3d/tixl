using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.fuzzy{
    [Guid("3ff59cf2-33c3-4dd3-bcf8-83b88f8282e1")]
    internal sealed class ExampleCustomPixelShader : Instance<ExampleCustomPixelShader>
    {

        [Output(Guid = "f6705c92-2d04-4e94-93f5-f0c26d0d4cdc")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> Output = new Slot<T3.Core.DataTypes.Texture2D>();

    }
}

