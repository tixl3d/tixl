namespace Lib.image.use;

[Guid("2d62dd4b-9597-4569-a09e-495abf880e34")]
internal sealed class DepthBufferAsGrayScale : Instance<DepthBufferAsGrayScale>
{
    [Output(Guid = "bbb34e6c-ac3d-40e3-959d-124ea0bcac3d")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "20f33d70-7599-4e71-993c-43464410182a")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "482e7931-cc65-49bd-ac56-8724b83a4e6a")]
    public readonly InputSlot<Vector2> NearFarRange = new();

    [Input(Guid = "41f15d72-577a-4e13-bfec-e60443930fd1")]
    public readonly InputSlot<Vector2> OutputRange = new();

    [Input(Guid = "379bee6d-ba65-4a53-9d9e-8dded21f351a")]
    public readonly InputSlot<bool> ClampOutput = new();

    [Input(Guid = "05359bf0-eef3-4322-b5cd-8df009e6a236")]
    public readonly InputSlot<int> Mode = new();

}