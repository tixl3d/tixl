namespace Lib.image.fx.stylize;

[Guid("aaa64b7b-abe4-499e-ae29-a4d3df561c33")]
internal sealed class ScreenCloseUp : Instance<ScreenCloseUp>
{
    [Output(Guid = "9cd53ad3-17f8-44eb-8b54-684b9ba4732d")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "7bb8d127-dd59-4d53-ad5d-934700da8926")]
    public readonly InputSlot<Texture2D> Texture2d = new();

        [Input(Guid = "c5e1e00e-448f-4623-a121-c1d0ba56a15f")]
        public readonly InputSlot<float> Zoom = new InputSlot<float>();

        [Input(Guid = "a0cd657e-115d-4522-9937-0bfc68657133")]
        public readonly InputSlot<System.Numerics.Vector2> Target = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "7b40b40e-5524-4458-8a79-1a9012fb8fcb")]
        public readonly InputSlot<System.Numerics.Vector2> Tilt = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "f4b8ed2c-e5ba-4441-8bee-9824c9793444")]
        public readonly InputSlot<float> FogStrength = new InputSlot<float>();

        [Input(Guid = "4b2ea8f2-dfe3-4e59-a140-5d5427e5c1ce")]
        public readonly InputSlot<float> Glossy = new InputSlot<float>();

        [Input(Guid = "ec096545-6cc7-45b0-8eb6-2100e52d3815")]
        public readonly InputSlot<float> DepthOfField = new InputSlot<float>();

        [Input(Guid = "e10c3708-9696-4d15-81b5-93eb4026937b")]
        public readonly InputSlot<System.Numerics.Vector4> BackgroundColor = new InputSlot<System.Numerics.Vector4>();

}