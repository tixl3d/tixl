namespace Lib.image.use;

[Guid("01458940-287f-4d31-9906-998efa9a2641")]
internal sealed class NormalMap : Instance<NormalMap>
{
    [Output(Guid = "b1fa156b-a959-42f8-9a81-30a667d60554")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "3b04296a-14e5-40e0-91a2-eda0314b0490")]
    public readonly InputSlot<Texture2D> LightMap = new();

    [Input(Guid = "ab21289c-f91c-4991-a7e5-ecb0c0954f02")]
    public readonly InputSlot<float> Impact = new();

    [Input(Guid = "3e9bce35-7de1-42de-80e5-8eec29e92422")]
    public readonly InputSlot<float> SampleRadius = new();

    [Input(Guid = "b16de87a-4099-42fe-9a73-97d8fa112d4d")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "82464caa-a407-4f6d-a062-cef322d131f0")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "cf7e6f41-cc6e-46dd-8779-1273326a5a53", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "4d25b1db-daeb-4b5e-b2bc-8681f332b092")]
    public readonly InputSlot<Format> OutputFormat = new InputSlot<Format>();

        [Input(Guid = "b79b3334-bce6-44a0-aa8c-d2db6a45afff")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> TextureRepeat = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();
        
        
    private enum Modes
    {
        Gray_ToNormalizedRGB,
        Gray_ToNormalizedRGBSigned,
        Gray_ToAngleAndMagnitude,
        Red_ToRG_KeepBA,
    }
}