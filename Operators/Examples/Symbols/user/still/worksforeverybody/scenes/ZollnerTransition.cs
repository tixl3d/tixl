namespace Examples.user.still.worksforeverybody.scenes;

[Guid("77b8cf1b-2bd7-4bfb-9f22-57d613560186")]
internal sealed class ZollnerTransition : Instance<ZollnerTransition>
{
    [Output(Guid = "1426e023-78b1-4800-acf0-7bca954671ef")]
    public readonly Slot<Texture2D> TextureOutput = new();


}