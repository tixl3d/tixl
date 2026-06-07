using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.image.fx.blur{
    [Guid("45d12ccb-3da8-41e5-85fd-61d7abfaa92d")]
    internal sealed class ChromaBlur : Instance<ChromaBlur>
    {
        [Output(Guid = "b5ba0ee4-b4ee-47b1-87c1-5fc7864bc604")]
        public readonly Slot<Texture2D> TextureOutput = new Slot<Texture2D>();


        [Input(Guid = "3e6e1e6d-ea12-4cc6-b2db-b901d0743f99")]
        public readonly InputSlot<Texture2D> ImageA = new InputSlot<Texture2D>();

        [Input(Guid = "8507382b-0baf-47a0-8ca4-2a3a51d7aa99")]
        public readonly InputSlot<int> BlurLevels = new InputSlot<int>();

    }
}

