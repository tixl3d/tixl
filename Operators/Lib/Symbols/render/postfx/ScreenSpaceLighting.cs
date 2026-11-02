namespace Lib.render.postfx;

[Guid("f2a60c18-9d47-4b13-8e5a-c7b31d04e692")]
internal sealed class ScreenSpaceLighting : Instance<ScreenSpaceLighting>
{
    [Output(Guid = "76c6f273-504a-5ec4-a478-667277e75d42")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "c3e5a849-3ebd-5d73-b126-9061fe017779")]
    public readonly Slot<Texture2D> Occlusion = new();

    [Output(Guid = "48ed1801-2123-576c-bf4b-6c27214e32ac")]
    public readonly Slot<Texture2D> Reflections = new();

    [Input(Guid = "954e8e61-963b-5ab2-b6af-e6ebd979c67b")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "51bda255-4188-55e2-a6e9-9e2f2f53c7c0")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "b14dc3e3-8d42-586a-b06f-1b7ecbe783de")]
    public readonly InputSlot<Texture2D> NormalMap = new();

    [Input(Guid = "6fd50e91-9ae7-5378-b8e3-688f4d212a78")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "e1f4deec-22fd-5652-a3c3-37b46a5eab6c")]
    public readonly InputSlot<float> OcclusionIntensity = new();

    [Input(Guid = "385129ec-175f-5821-ac8b-ca4bb2d0b287")]
    public readonly InputSlot<float> OcclusionRadius = new();

    [Input(Guid = "2cb78d66-f7d9-5118-88cb-295c76e01ccf")]
    public readonly InputSlot<float> OcclusionThickness = new();

    [Input(Guid = "69fe1eda-0a88-524f-b5ac-8872e33d9eb7")]
    public readonly InputSlot<int> RaysPerPixel = new();

    [Input(Guid = "045b64b3-c3c6-55be-b124-c7ed34776ecc")]
    public readonly InputSlot<int> StepsPerRay = new();

    [Input(Guid = "2d098fe1-9c9e-5e3f-a910-5494b7138083")]
    public readonly InputSlot<float> ColorBleeding = new();

    [Input(Guid = "04f0e86f-c9b2-55ce-a278-bb790b07050f")]
    public readonly InputSlot<float> ReflectionIntensity = new();

    [Input(Guid = "0ba63e58-f702-5800-a992-96696cc44784")]
    public readonly InputSlot<float> Reflectivity = new();

    [Input(Guid = "bbcf597d-db03-5380-acab-6223b7ed1338")]
    public readonly InputSlot<float> Roughness = new();

    [Input(Guid = "398af543-56c1-580f-9355-ad83c8737982")]
    public readonly InputSlot<float> MaxDistance = new();

    [Input(Guid = "5da494ec-cdf2-5d07-b15d-75540ff2c63b")]
    public readonly InputSlot<float> ReflectionThickness = new();

    [Input(Guid = "e6d9b567-dbcf-5ce3-9862-cd1003bbee90")]
    public readonly InputSlot<int> MaxSteps = new();

    [Input(Guid = "797e795f-9700-5bf9-9674-d2bd39356f3c")]
    public readonly InputSlot<float> EdgeFade = new();

    [Input(Guid = "9f05525d-3b27-528f-bc16-e2ab9a230e1e")]
    public readonly InputSlot<float> BlendFactor = new();

    [Input(Guid = "f5c3947d-c580-5739-b123-d5f7ff6a8ef3")]
    public readonly InputSlot<int> MaxAccumulatedFrames = new();

    [Input(Guid = "863dafd8-10c4-571f-bddc-648857c595cc")]
    public readonly InputSlot<float> NeighborhoodClamp = new();

    [Input(Guid = "53f228d1-c7b1-5778-aa52-03c29e16cbc7")]
    public readonly InputSlot<float> DepthRejection = new();

    [Input(Guid = "2a9d43a4-a3b9-58aa-bb9e-9a1dc0534fad")]
    public readonly InputSlot<bool> Reset = new();
}
