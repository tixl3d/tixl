namespace Lib.render.basic;

[Guid("cb89ed1d-03ea-4880-bfa0-1dd723e4bdab")]
internal sealed class FadingSlideShow : Instance<FadingSlideShow>
{
    [Output(Guid = "fd703cd6-ed0a-473b-9620-d5b5f5547774")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "51ba413f-1a7a-4860-8654-bdbd78a1bba4")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "61469462-04ef-4f75-bd58-2bd42b8da15a")]
    public readonly InputSlot<float> IndexAndFraction = new();

    [Input(Guid = "18aeda2b-ff90-4cf4-9665-c8c65a23cb5f")]
    public readonly InputSlot<float> BlendSpeed = new();

    [Input(Guid = "c3765d6d-4f45-4425-a86c-1cdd13eff296")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "8feb4ec2-acc6-4a46-852b-70c4f156f6de")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "651d479c-a3d4-4d18-bdcd-2400e2cdb56b")]
    public readonly InputSlot<float> RandomOffset = new();

    [Input(Guid = "ca8724d8-a362-4af4-bdf8-a80a1b0ef72e")]
    public readonly InputSlot<string> FolderWithImages = new();

    [Input(Guid = "3cb08250-213c-4fa6-b9a0-1c92bf01ad6e")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "cabb74b7-72c3-4916-8096-5890fc8e7023")]
    public readonly InputSlot<Vector4> BackgroundColor = new();

    [Input(Guid = "0f7305c9-4372-4e62-82ff-a377bcbffc12")]
    public readonly InputSlot<int> ScaleMode = new();

    [Input(Guid = "fcf2c4d0-4e36-473e-8c67-66cc1048dc06")]
    public readonly InputSlot<bool> TriggerUpdate = new InputSlot<bool>();

    [Input(Guid = "8b5fb93a-ee65-44d9-88be-66ee2e174385")]
    public readonly InputSlot<int> FadeType = new InputSlot<int>();

}