using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.render.sprite{
    [Guid("23cb02f6-6622-47ef-8772-4fe5ae1d09dc")]
    internal sealed class TextSpritesExample : Instance<TextSpritesExample>
    {
        [Output(Guid = "acdedfe5-7a22-4ccd-82bc-61628b3eecf7")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();

        [Input(Guid = "76724dd9-ddfb-4dce-bcda-90a397f2316c")]
        public readonly InputSlot<string> String = new InputSlot<string>();


    }
}

