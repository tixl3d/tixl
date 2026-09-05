namespace Lib.image.transform;

[Guid("06621b4b-43be-4ef9-80d0-f1b36fa4dbd1")]
internal sealed class MirrorRepeat : Instance<MirrorRepeat>
{
    [Output(Guid = "7dc02a27-bc05-447f-9053-a44a60123980")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "25bdfb83-a61c-448c-a909-527f1bb73740")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "149a9d1c-b76e-4256-8f5d-a261923a9ae5")]
    public readonly InputSlot<float> RotateMirror = new InputSlot<float>();

    [Input(Guid = "8caa4f2b-bfab-455e-a594-09b0cfb500fc")]
    public readonly InputSlot<float> RotateImage = new InputSlot<float>();

    [Input(Guid = "e39e2fd3-6820-4353-b098-44127303bb51")]
    public readonly InputSlot<float> Width = new InputSlot<float>();

    [Input(Guid = "bb6c1508-1849-4c40-b609-06ddfe62a6ea")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "bb67b53b-2d0b-42f5-b2be-26c28f869b71")]
    public readonly InputSlot<float> OffsetEdge = new InputSlot<float>();

    [Input(Guid = "5a5fc5e7-4fec-4146-9183-161fff71ee97")]
    public readonly InputSlot<Vector2> Offsetimage = new InputSlot<Vector2>();

    [Input(Guid = "341ee76d-f431-40e8-ba14-aa4e0a1f10c4")]
    public readonly InputSlot<float> ShadeAmount = new InputSlot<float>();

    [Input(Guid = "5309f31b-bb18-4ec6-8a40-f1e3f83fb239")]
    public readonly InputSlot<Vector4> ShadeColor = new InputSlot<Vector4>();

    [Input(Guid = "6809a355-47fc-4e27-98a2-956dcc7f41ef")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();
}