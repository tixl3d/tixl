namespace Lib.image.use;

[Guid("b5b6c046-3c8e-478a-b423-899872c2e1c4")]
internal sealed class _KeepPreviousFrame_Old1 : Instance<_KeepPreviousFrame_Old1>
{
    [Output(Guid = "4cf4e43b-0f1f-41f7-9ba3-acab3b1160cb")]
    public readonly Slot<Texture2D> TextureA = new();

    [Output(Guid = "edc79491-f0c1-47c6-bc70-8014ebdb1a7a")]
    public readonly Slot<Texture2D> TextureB = new();


    [Input(Guid = "216dca25-9ba2-4cbb-b05a-e74befafaf37")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "b25d483f-1fdf-4d76-974c-8e781a405914")]
    public readonly InputSlot<bool> Enable = new();

    [Input(Guid = "7f255460-3a71-42e7-a372-629d39433ae8")]
    public readonly InputSlot<bool> HasFrameChanged = new();

}