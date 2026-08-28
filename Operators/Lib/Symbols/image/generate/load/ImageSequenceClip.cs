namespace Lib.image.generate.load;

[Guid("927a77d5-7bf0-477b-b288-4bb48d4980cd")]
public class ImageSequenceClip : Instance<ImageSequenceClip>
{

    [Output(Guid = "5e6c968e-593a-40e5-8749-0a4cf8e13ef3")]
    public readonly TimeClipSlot<Command> Output = new();

    [Input(Guid = "A1E08FDE-2D9B-4A2D-BF09-A5E54AD8CFA0")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "5c510853-f87d-4ebf-be0a-78d06d970be2")]
    public readonly InputSlot<float> FrameRate = new();

    [Input(Guid = "25215a7e-1e38-4370-8845-cbf422a7eb92")]
    public readonly InputSlot<bool> TriggerUpdate = new();

    [Input(Guid = "7c4fbfe3-2cf4-4e23-9b3e-961927f20935")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();

}