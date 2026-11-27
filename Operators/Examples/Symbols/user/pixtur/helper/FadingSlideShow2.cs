namespace Examples.user.pixtur.helper;

[Guid("7eb80902-dd6f-4887-bb99-68741bff6f69")]
internal sealed class FadingSlideShow2 :Instance<FadingSlideShow2>{
    [Output(Guid = "47db5687-3dd6-4dd5-91f5-f05f64fcd0cd")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "40bdf568-b7af-4f3d-8efd-61c881513a5d")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "2a91c3ee-a826-4e27-bd46-263d9bd3d74c")]
    public readonly InputSlot<float> IndexAndFraction = new();

    [Input(Guid = "42a0b6fe-6020-40e7-bc03-0d64a438f565")]
    public readonly InputSlot<float> BlendSpeed = new();

    [Input(Guid = "1ca4011c-3640-43c3-845e-740b8df29caa")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "4eb8d3ff-3eb1-4dde-9dea-af74a7aa74dc")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "97b374b0-5332-4a5b-a5aa-a34c97ac2986")]
    public readonly InputSlot<float> RandomOffset = new();

    [Input(Guid = "9a9a6de8-34c6-4d9a-a0b3-1e6dcae2538c")]
    public readonly InputSlot<string> FolderWithImages = new();

    [Input(Guid = "d4fc1aba-083c-4a53-a7f8-4663f5ccdbe4")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "0122a821-0ac6-4c19-9e6f-10d7f149df07")]
    public readonly InputSlot<Vector4> BackgroundColor = new();

    [Input(Guid = "81d4275e-0031-48be-95f3-0f9355f76ab4")]
    public readonly InputSlot<int> ScaleMode = new();

    [Input(Guid = "ad0f5f3b-3407-4029-8cd9-5441a45dbc51")]
    public readonly InputSlot<bool> TriggerUpdate = new InputSlot<bool>();

    [Input(Guid = "989caa58-3d5c-4ea3-8507-e746a3bfe689")]
    public readonly InputSlot<int> FadeType = new InputSlot<int>();

}