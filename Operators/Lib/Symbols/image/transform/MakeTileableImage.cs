namespace Lib.image.transform;

[Guid("1e29f81b-0c05-4784-b3ac-c475ce510159")]
internal sealed class MakeTileableImage : Instance<MakeTileableImage>
{

    [Output(Guid = "970d1691-47f4-43ce-937d-cad6a9c13922")]
    public readonly Slot<Texture2D> Selected = new Slot<Texture2D>();

    [Input(Guid = "f09d6911-dd5f-4dac-a475-3ea291f04dfe")]
    public readonly InputSlot<Texture2D> ImageA = new InputSlot<Texture2D>();

    [Input(Guid = "e0f2826f-867d-461b-b1ec-95d30d592539")]
    public readonly InputSlot<float> EdgeFallOff = new InputSlot<float>();

    [Input(Guid = "0cf0b6ec-93e4-4e08-939c-5588267c3991")]
    public readonly InputSlot<int> TilingMode = new InputSlot<int>();

        [Input(Guid = "0fef83cc-01c5-4e4a-a716-5bbdf9d916a0")]
        public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();

}