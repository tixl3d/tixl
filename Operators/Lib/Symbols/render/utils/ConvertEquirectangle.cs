namespace Lib.render.utils;

[Guid("c6014c28-c6ab-4b4e-b6bf-0cee92fb4b40")]
internal sealed class ConvertEquirectangle : Instance<ConvertEquirectangle>
{
    [Output(Guid = "000b79eb-b390-4b6b-9fdc-b99f12bc308d")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    [Input(Guid = "57ce1074-7971-4542-95c8-86f58ed75c7d")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "07d45e2f-75dd-455c-b8fe-b96ab2f830a2")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();

}