namespace Lib.render.postfx;

[Guid("c3e7a1d4-5b82-4f06-9a3e-1d6c8b2f7e40")]
internal sealed class ScreenSpaceReflections : Instance<ScreenSpaceReflections>
{
    [Output(Guid = "4a9d2c17-8e35-4b60-a1f7-2c5e9d03b681")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "7b1e5f92-3c48-4d07-b6a2-9e4d1c8f3a05")]
    public readonly Slot<Texture2D> Reflections = new();

    [Input(Guid = "1f8c3a60-7d24-4e91-85b3-6a0d2e9c4f17")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "2a9d4b71-8e35-4fa2-96c4-7b1e3fad5028")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "3bae5c82-9f46-40b3-a7d5-8c2f40be6139")]
    public readonly InputSlot<Texture2D> NormalMap = new();

    [Input(Guid = "4cbf6d93-a057-41c4-b8e6-9d3051cf724a")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "5dc07ea4-b168-42d5-89f7-ae4162d0835b")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "6ed18fb5-c279-43e6-9a08-bf5273e1946c")]
    public readonly InputSlot<float> Reflectivity = new();

    [Input(Guid = "7fe290c6-d38a-44f7-ab19-c06384f2a57d")]
    public readonly InputSlot<float> Roughness = new();

    [Input(Guid = "f8c16b0a-537e-4cd4-b91b-a046c2d35f87")]
    public readonly InputSlot<float> HighlightThreshold = new();

    [Input(Guid = "80f3a1d7-e49b-4508-bc2a-d17495a3b68e")]
    public readonly InputSlot<float> MaxDistance = new();

    [Input(Guid = "9104b2e8-f5ac-4619-8d3b-e285a6b4c79f")]
    public readonly InputSlot<float> Thickness = new();

    [Input(Guid = "a215c3f9-06bd-472a-9e4c-f396b7c5d8a0")]
    public readonly InputSlot<int> MaxSteps = new();

    [Input(Guid = "b326d40a-17ce-483b-af5d-04a7c8d6e9b1")]
    public readonly InputSlot<float> EdgeFade = new();

    [Input(Guid = "c437e51b-28df-494c-806e-15b8d9e7fac2")]
    public readonly InputSlot<float> BlendFactor = new();

    [Input(Guid = "b48d27c6-1f3a-4e90-a5d7-6c02e8f91b43")]
    public readonly InputSlot<int> MaxAccumulatedFrames = new();

    [Input(Guid = "d548f62c-39e0-4a5d-917f-26c9eaf80bd3")]
    public readonly InputSlot<float> NeighborhoodClamp = new();

    [Input(Guid = "e659073d-4af1-4b6e-a280-37dafb091ce4")]
    public readonly InputSlot<float> DepthRejection = new();

    [Input(Guid = "f76a184e-5b02-4c7f-b391-48eb0c1a2df5")]
    public readonly InputSlot<bool> Reset = new();
}
