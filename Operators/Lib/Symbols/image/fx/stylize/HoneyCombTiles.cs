namespace Lib.image.fx.stylize;

[Guid("ee398291-674c-409b-aab8-9ca843ce6cef")]
internal sealed class HoneyCombTiles : Instance<HoneyCombTiles>
{
    [Output(Guid = "2c2d62c4-3735-40a4-bc97-eaecf7e4cb7f")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "452fb0e1-34c1-4042-9a7c-30bf8dd4cb3c")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "dc8c94e6-97d9-4aa5-9edd-5a8d01e4f7c4")]
    public readonly InputSlot<Vector4> Fill = new InputSlot<Vector4>();

    [Input(Guid = "165b0ad5-1fde-4f8d-871e-f1498a419887")]
    public readonly InputSlot<Vector4> Background = new InputSlot<Vector4>();

    [Input(Guid = "4ba0a4d2-44c8-4c8e-95d1-7b1fbda6975b")]
    public readonly InputSlot<Vector2> Center = new InputSlot<Vector2>();

    [Input(Guid = "0c227ab3-3394-40fb-9274-d857a2293d35")]
    public readonly InputSlot<float> Rotation = new InputSlot<float>();

    [Input(Guid = "00ffc593-d62e-4f40-8957-906b444b8f3b")]
    public readonly InputSlot<float> Divisions = new InputSlot<float>();

    [Input(Guid = "e635d5cd-8250-4722-a11a-ffe27a922f3e")]
    public readonly InputSlot<float> LineThickness = new InputSlot<float>();

    [Input(Guid = "228b8705-92c7-467b-ab0f-eee60a618c72")]
    public readonly InputSlot<float> MixOriginal = new InputSlot<float>();

    [Input(Guid = "03afbb91-a64f-48bc-a306-b378a068aca8")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();
}