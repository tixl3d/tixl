namespace Lib.render.@_;

[Guid("393da0ad-00ef-4a9c-bd53-9314bb16b08b")]
internal sealed class LenseFlareHoop : Instance<LenseFlareHoop>
{
    [Output(Guid = "8ee4b3e9-0e1a-4e8e-8fa8-db515c7f24e9")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "f545c6e8-cf08-4f24-b96c-b20d8a5e0e7d")]
    public readonly InputSlot<int> LightIndex = new();

    [Input(Guid = "3e99b9bf-03f9-46f0-ada4-0b0f55913014")]
    public readonly InputSlot<float> Distance = new();

    [Input(Guid = "2f69b30a-cf3d-42b3-855e-e44300797523")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "6b3e0b32-6df9-4356-a019-5dfa4693ce56")]
    public readonly InputSlot<float> RandomizeSize = new();

    [Input(Guid = "89744c45-4963-4266-a395-af24c51babdc")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "70c2be48-d896-4312-a98f-029077a35af4")]
    public readonly InputSlot<Vector4> RandomizeColor = new();

    [Input(Guid = "a1518a4f-322b-4ea5-9af6-2c535c6c589d")]
    public readonly InputSlot<int> RandomSeed = new();

    [Input(Guid = "d8f07d6b-f053-4348-972c-819e4a043faa")]
    public readonly InputSlot<Vector2> PositionFactor = new();

    [Input(Guid = "03cafdc8-c009-418a-a24a-526a358d5cf3")]
    public readonly InputSlot<int> FxZoneMode = new();

    [Input(Guid = "9447da2e-f9b6-4fc9-a523-c7db580d272b")]
    public readonly InputSlot<Vector2> EdgeFxZone = new();

    [Input(Guid = "c822325b-c19c-4a69-a9a6-7a2b2d09264d")]
    public readonly InputSlot<Vector2> InnerFxZone = new();

    [Input(Guid = "49bce40f-13cd-4210-8102-6cc74308be52")]
    public readonly InputSlot<float> FxZoneScale = new();

    [Input(Guid = "9de875e5-218f-49a8-9fe3-3330abb87e83")]
    public readonly InputSlot<float> FxZoneBrightness = new();

    [Input(Guid = "35f1811e-93f3-46ed-8f3f-8fb7bfef2969")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "bed4f907-fcc8-4818-9688-9427bab72028")]
    public readonly InputSlot<float> AspectRatio = new();

    [Input(Guid = "e349ffc0-60db-4cbb-a188-4c267c22c233")]
    public readonly InputSlot<float> Noise = new();

    [Input(Guid = "0e755f29-0902-427a-8154-44ceacc99796")]
    public readonly InputSlot<float> Segments = new();

    [Input(Guid = "9eb87ac9-a129-4bce-88dc-27d76f02a37e")]
    public readonly InputSlot<float> SegmentWidth = new();


}