using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.DataTypes;
using T3.Core.Utils;

namespace Lib.image.analyze.blobtrack;

[Guid("21529EF6-584F-434B-9D25-1528C68652BB")]
internal sealed class BlobTrackOverlay : Instance<BlobTrackOverlay>
{
    [Output(Guid = "728FEF86-E670-4055-99F8-5834F33D4BEC")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Input(Guid = "D789C43E-8838-42C0-B21C-3FD9F2ECC436")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "5E6F7081-9293-4A84-B9C0-D1E2F3A4B5C7")]
    public readonly InputSlot<bool> ImageEnabled = new(true);

    [Input(Guid = "8B03FDC4-D0E6-4D1B-A9D2-17FAD5D75DDE")]
    public readonly InputSlot<BufferWithViews> BlobData = new();

    [Input(Guid = "76BFE731-103B-4C00-B874-852FB6AD4A3B")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "AA82C44C-E090-40A9-B37F-8C228EE3F305")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "3D1ACA69-F398-4596-A9C4-DEA5833BEB04")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "9152FFDF-DEF3-43D8-B918-895CAF7E91AC")]
    public readonly InputSlot<float> AspectRatio = new();

    [Input(Guid = "6F17A0B0-DC12-461A-92B3-342DF6A1BCDC")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "37BED1C9-B7CE-44B0-B1D0-32F17111E020")]
    public readonly InputSlot<bool> Clear = new();

    [Input(Guid = "6A9DA377-6403-42E4-AADD-069A15FA26EC")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "76D42773-DDBD-497A-8C17-927B09CCC6A9")]
    public readonly InputSlot<string> LabelPrefix = new("ID:");

    [Input(Guid = "64A3A4F5-420C-414D-80EF-5E0F33BB726F", MappedType = typeof(BlobDataToTextBuffer.IdFormats))]
    public readonly InputSlot<int> IdFormat = new();

    [Input(Guid = "F4885527-C56F-4040-AD4E-7D5351B19D08", MappedType = typeof(BlobDataToTextBuffer.LabelContentModes))]
    public readonly InputSlot<int> LabelContent = new();

    [Input(Guid = "3737C853-81F0-4FD9-9F82-207C2FE1A856")]
    public readonly InputSlot<Vector4> LabelColor = new(Vector4.One);

    [Input(Guid = "3C39D125-8CB4-4E84-A51D-D1C88721C740")]
    public readonly InputSlot<float> LabelSize = new(30f);

    [Input(Guid = "9D4E7F2A-6B1E-4F3C-9A5D-8E2F1A3C5B4E")]
    public readonly InputSlot<string> FontPath = new("Examples:fonts/Roboto-Black.fnt");

    [Input(Guid = "4E4F5A6B-7C8D-4E9F-A0B1-C2D3E4F5A6B7")]
    public readonly InputSlot<Vector2> LabelOffset = new(Vector2.Zero);
}
