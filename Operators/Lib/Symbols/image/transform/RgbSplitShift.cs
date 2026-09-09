using T3.Core.Utils;

namespace Lib.image.transform;

[Guid("66f2492d-9bd6-45e9-af12-c7b48c16c8e2")]
internal sealed class RgbSplitShift :Instance<RgbSplitShift>{
    [Output(Guid = "642faa41-fd77-42db-911c-edbe56267db5")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "65cb2ce4-1ce4-42b4-a830-4c4308b9012e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "95d4c527-3cdd-45d9-8cec-12ab96398c8a")]
        public readonly InputSlot<System.Numerics.Vector2> Center = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "35592f38-7109-4853-be37-5e086f0f5796")]
        public readonly InputSlot<System.Numerics.Vector2> R = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "939a5c15-4376-4f81-8bfc-d140ebb5f429")]
        public readonly InputSlot<System.Numerics.Vector2> G = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "435abe18-64b2-4032-b697-00514ad059a8")]
        public readonly InputSlot<System.Numerics.Vector2> B = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "c21412ed-708b-4136-84b6-b9c7665a2d10")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "04675fbd-234f-453b-bc06-1abb4e17b9af", MappedType = typeof(WrapModes))]
        public readonly InputSlot<int> WrapMode = new InputSlot<int>();

        [Input(Guid = "e4a3dad4-80a9-432a-8d48-5e1dcbecf1b0")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> FxTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

    private enum WrapModes
    {
        Wrap,
        Mirror,
        Clamp,
        Border,
        MirrorOnce,
    }
}