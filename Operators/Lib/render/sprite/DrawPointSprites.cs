using T3.Core.Utils;

namespace Lib.render.sprite;

[Guid("9d494c7e-f94c-477f-aeb3-7fa70788f225")]
internal sealed class DrawPointSprites : Instance<DrawPointSprites>
{
        [Output(Guid = "d08f138b-e440-4b2a-a8f1-60dc412d872b")]
        public readonly Slot<Command> Output = new();

        [Input(Guid = "32a76e06-db29-40e0-9a4e-29ad88c2f62b")]
        public readonly InputSlot<BufferWithViews> Points = new();

        [Input(Guid = "d815c639-d412-4266-81df-f5889a87b4f6")]
        public readonly InputSlot<BufferWithViews> SpriteBuffer = new();

    [Input(Guid = "08c37dd9-155c-4fb4-a9d0-f29ddd7da9b0")]
        public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "c694946c-37b6-4792-8fc5-4204ede69f90")]
        public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "cf7692c9-d6bd-4e93-b89c-14ab507b6da5")]
        public readonly InputSlot<float> Size = new();

    [Input(Guid = "c0fdacd9-a887-488a-842d-46d015dae0bb", MappedType = typeof(ScaleFXModes))]
        public readonly InputSlot<int> ScaleFactor = new();

    [Input(Guid = "12e1244c-efb7-4bfd-a13c-f94552f153ca")]
        public readonly InputSlot<bool> UsePointScale = new();

    [Input(Guid = "926bdbc2-ee14-4125-8886-18f445766ef3")]
        public readonly InputSlot<float> AlphaCutOff = new();

    [Input(Guid = "9df7e923-76d2-4e98-af72-3a73b47de960")]
        public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "fecc8310-208e-45de-a43b-bf547663e743", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new();

    private enum ScaleFXModes
        {
            None = 0,
            F1 = 1,
            F2 = 2,
        }
}