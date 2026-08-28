namespace Lib.render.postfx;

[Guid("e1c294b5-1ea8-435e-a437-26d280d3c2f4")]
internal sealed class GodRays : Instance<GodRays>
{
    [Output(Guid = "28bf4abe-e9a9-4302-bcca-67a6957b43a7")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "c94e4bb6-d6fc-4184-bd71-3563f4416413")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "bba257ae-e45d-43b6-9905-52e526583978")]
    public readonly InputSlot<Texture2D> DepthBuffer = new InputSlot<Texture2D>();

    [Input(Guid = "b64c4ece-8b59-4b32-a5fe-b99006286987")]
    public readonly InputSlot<Object> CameraReference = new InputSlot<Object>();

    [Input(Guid = "58c08101-ac16-4aed-aa84-259853734116")]
    public readonly InputSlot<Vector3> LightPosition = new InputSlot<Vector3>();

    [Input(Guid = "6063bb2b-2255-4a0b-a222-7b8dd8397918")]
    public readonly InputSlot<Vector4> RayColor = new InputSlot<Vector4>();

    [Input(Guid = "2f365f2a-8a2e-472c-af15-9352b54e2009")]
    public readonly InputSlot<Vector4> OriginalColor = new InputSlot<Vector4>();

    [Input(Guid = "59568136-2903-4fd5-a504-8a6308a2a2dc")]
    public readonly InputSlot<int> Samples = new InputSlot<int>();

    [Input(Guid = "500549e0-aa60-4555-b82a-a829568959ff")]
    public readonly InputSlot<float> CenterIntensity = new InputSlot<float>();

    [Input(Guid = "c59b6588-9e39-49ed-99b7-632e5f2b3bf4")]
    public readonly InputSlot<float> RayIntensity = new InputSlot<float>();

    [Input(Guid = "ea737eeb-e936-48c0-aaa7-8247fea95228")]
    public readonly InputSlot<float> Decay = new InputSlot<float>();

    [Input(Guid = "6d985988-3107-4480-8852-f17fe1c6d002")]
    public readonly InputSlot<float> ShiftDepth = new InputSlot<float>();

    [Input(Guid = "b9ca7291-1f78-47e2-8ba9-35ccb282833c")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "71acc14a-e26a-4412-9556-d88e0ec70e63")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "2e487d56-cc02-42d6-978e-aeb776516118")]
    public readonly InputSlot<int> BlurSamples = new InputSlot<int>();

    [Input(Guid = "9529d9b2-40ca-4f30-9142-1338dc4e07ff")]
    public readonly InputSlot<float> BlurSize = new InputSlot<float>();

    [Input(Guid = "5129404e-54c5-4c5a-9736-69361bb3077d")]
    public readonly InputSlot<float> BlurOffset = new InputSlot<float>();
}