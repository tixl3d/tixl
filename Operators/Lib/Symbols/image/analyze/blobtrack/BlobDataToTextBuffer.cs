using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Utils;
using Lib.Utils;

namespace Lib.image.analyze.blobtrack;

[Guid("9C4E7D2A-6B1E-4F3C-9A5D-8E2F1A3C5B4D")]
internal sealed class BlobDataToTextBuffer : Instance<BlobDataToTextBuffer>
{
    [Output(Guid = "2B3C4D5E-6F70-4A80-B9C0-D1E2F3A4B5C6")]
    public readonly Slot<Buffer> Buffer = new();

    [Output(Guid = "3C4D5E6F-7081-4B90-A0D1-E2F3A4B5C6D7")]
    public readonly Slot<int> VertexCount = new();

    [Input(Guid = "B97C5F9F-76E5-49A8-96A1-9B5E3F2FE2AD")]
    public readonly InputSlot<BufferWithViews> BlobData = new();

    [Input(Guid = "76D42773-DDBD-497A-8C17-927B09CCC6A9")]
    public readonly InputSlot<string> LabelPrefix = new("ID:");

    [Input(Guid = "64A3A4F5-420C-414D-80EF-5E0F33BB726F", MappedType = typeof(IdFormats))]
    public readonly InputSlot<int> IdFormat = new();

    [Input(Guid = "F4885527-C56F-4040-AD4E-7D5351B19D08", MappedType = typeof(LabelContentModes))]
    public readonly InputSlot<int> LabelContent = new();

    [Input(Guid = "3737C853-81F0-4FD9-9F82-207C2FE1A856")]
    public readonly InputSlot<Vector4> LabelColor = new(Vector4.One);

    [Input(Guid = "3C39D125-8CB4-4E84-A51D-D1C88721C740")]
    public readonly InputSlot<float> LabelSize = new(0.5f);

    [Input(Guid = "9D4E7F2A-6B1E-4F3C-9A5D-8E2F1A3C5B4E")]
    public readonly InputSlot<string> FontPath = new("Examples:fonts/Roboto-Black.fnt");

    [Input(Guid = "8D4E7F2A-6B1E-4F3C-9A5D-8E2F1A3C5B4F")]
    public readonly InputSlot<float> Spacing = new(0.0f);

    [Input(Guid = "7D4E7F2A-6B1E-4F3C-9A5D-8E2F1A3C5B40")]
    public readonly InputSlot<float> LineHeight = new(1.0f);

    [Input(Guid = "6D4E7F2A-6B1E-4F3C-9A5D-8E2F1A3C5B41")]
    public readonly InputSlot<float> Scale = new(1.0f);

    [Input(Guid = "5D4E7F2A-6B1E-4F3C-9A5D-8E2F1A3C5B42")]
    public readonly InputSlot<float> AspectRatio = new(1.0f);

    [Input(Guid = "4E4F5A6B-7C8D-4E9F-A0B1-C2D3E4F5A6B7")]
    public readonly InputSlot<Vector2> Offset = new(Vector2.Zero);

    public enum IdFormats
    {
        Decimal,
        HexadecimalUpper,
        HexadecimalLower,
        Binary,
    }

    public enum LabelContentModes
    {
        IdAndNumber,
        IdOnly,
        NumberOnly,
    }

    public BlobDataToTextBuffer()
    {
        _fontResource = new Resource<BmFontDescription>(FontPath, OnFileChanged);
        _fontResource.AddDependentSlots(Buffer);
        Buffer.UpdateAction += Update;
        VertexCount.UpdateAction += Update;
    }

    private bool OnFileChanged(FileResource file, BmFontDescription currentValue, out BmFontDescription newValue, out string failureReason)
    {
        if (BmFontDescription.TryInitializeFromFile(file.AbsolutePath, out newValue))
        {
            failureReason = null;
            return true;
        }

        failureReason = "Failed to load font from file";
        return false;
    }

    private void Update(EvaluationContext context)
    {
        var blobData = BlobData.GetValue(context);
        if (blobData == null || blobData.Buffer == null || blobData.Buffer.Description.StructureByteStride < BlobInfo.Stride)
        {
            Buffer.Value = null;
            VertexCount.Value = 0;
            return;
        }

        if (!_fontResource.TryGetValue(context, out var font))
        {
            Buffer.Value = null;
            VertexCount.Value = 0;
            return;
        }

        var blobs = ReadBlobs(blobData);
        var blobCount = blobs.Length;
        if (blobCount == 0)
        {
            Buffer.Value = null;
            VertexCount.Value = 0;
            return;
        }

        var prefix = LabelPrefix.GetValue(context) ?? string.Empty;
        var idFormat = IdFormat.GetValue(context);
        var contentMode = (LabelContentModes)LabelContent.GetValue(context);
        var color = LabelColor.GetValue(context);
        var rawSize = LabelSize.GetValue(context);
        var spacing = Spacing.GetValue(context);
        var lineHeight = LineHeight.GetValue(context);
        var scale = Scale.GetValue(context);
        var aspect = AspectRatio.GetValue(context);
        if (aspect < 0.0001f)
            aspect = 1.0f;

        var textureWidth = font.BmFont.Common.ScaleW;
        var textureHeight = font.BmFont.Common.ScaleH;

        // Convert the label's font size (in px at the 1080p reference, like TextSprites) into
        // overlay world units. The overlay world spans 2*halfHeight (= 2*scale) vertically.
        var viewHeightInWorldUnits = 2.0f * scale;
        var scaleFactor = (rawSize / font.BmFont.Info.Size) * (viewHeightInWorldUnits / 1080f);
        var size = (float)scaleFactor;

        var halfWidth = scale * aspect;
        var halfHeight = scale;

        // Offset in the same 1080p-reference-px units as LabelSize; convert px->world units.
        var offset = Offset.GetValue(context) * scaleFactor;

        EnsureCapacity(blobCount * 32);

        var outputIndex = 0;
        for (var i = 0; i < blobCount; i++)
        {
            var blob = blobs[i];
            var worldX = (blob.CenterX - 0.5f) * 2.0f * halfWidth;
            var worldY = (0.5f - blob.CenterY) * 2.0f * halfHeight;
            var boxTop = worldY - blob.Height * 2.0f * halfHeight * 0.5f;

            var label = BuildLabelText(blob.Id, contentMode, prefix, idFormat);
            if (string.IsNullOrEmpty(label))
                continue;

            outputIndex = LayoutLabel(font, label, worldX + offset.X, boxTop + offset.Y, size, spacing, lineHeight, color,
                                      textureWidth, textureHeight, outputIndex);
        }

        var vertexCount = outputIndex * 6;
        if (outputIndex == 0)
        {
            Buffer.Value = null;
            VertexCount.Value = 0;
            return;
        }

        EnsureCapacity(outputIndex);

        // The generic SetupStructuredBuffer writes the WHOLE array into a stream sized only
        // for sizeInBytes bytes. _bufferContent is overallocated, so it must be sliced to exactly
        // the used glyph count first - otherwise WriteRange overflows the DataStream and corrupts
        // the native heap (STATUS_HEAP_CORRUPTION).
        var usedBuffer = _bufferContent.Length == outputIndex
                             ? _bufferContent
                             : _bufferContent[..outputIndex];
        ResourceManager.SetupStructuredBuffer(usedBuffer, outputIndex * StructStride, StructStride, ref _gpuBuffer);
        if (_gpuBuffer != null)
            _gpuBuffer.DebugName = nameof(BlobDataToTextBuffer);

        Buffer.Value = _gpuBuffer;
        VertexCount.Value = vertexCount;
    }

    private int LayoutLabel(BmFontDescription font, string text, float anchorX, float anchorY,
                            float size, float characterSpacing, float lineHeight, Vector4 color,
                            float textureWidth, float textureHeight, int outputIndex)
    {
        var cursorX = 0f;
        var cursorY = 0f;

        for (var idx = 0; idx < text.Length; idx++)
        {
            var c = text[idx];
            if (c == '\n')
            {
                cursorY -= font.BmFont.Common.LineHeight * lineHeight;
                cursorX = 0;
                continue;
            }

            if (!font.InfoForCharacter.TryGetValue(c, out var charInfo))
                continue;

            float sizeWidth = charInfo.Width * size;
            float sizeHeight = charInfo.Height * size;
            var x = anchorX + (cursorX + charInfo.XOffset) * size;
            var y = anchorY + (cursorY - charInfo.YOffset) * size;

            if (charInfo.Width != 1 || charInfo.Height != 1)
            {
                _bufferContent[outputIndex] = new BufferLayout
                {
                    Position = new Vector3(x, y, 0),
                    CharHeight = sizeHeight,
                    Orientation = Quaternion.Identity,
                    AspectRatio = sizeWidth / sizeHeight,
                    Color = color,
                    UvMinMax = new Vector4(
                        charInfo.X / textureWidth,
                        charInfo.Y / textureHeight,
                        (charInfo.X + charInfo.Width) / textureWidth,
                        (charInfo.Y + charInfo.Height) / textureHeight),
                    Id = (uint)outputIndex,
                    LineNumber = 0,
                    Offset = new Vector2(charInfo.XOffset, charInfo.YOffset),
                };
                outputIndex++;
            }

            cursorX += charInfo.XAdvance + characterSpacing;
        }

        return outputIndex;
    }

    private void EnsureCapacity(int required)
    {
        if (_bufferContent != null && _bufferContent.Length >= required)
            return;
        _bufferContent = new BufferLayout[Math.Max(required, _bufferContent?.Length ?? 0) * 2 + 1];
    }

    private static string BuildLabelText(int id, LabelContentModes contentMode, string prefix, int idFormat)
    {
        var number = FormatId(id, idFormat);
        return contentMode switch
        {
            LabelContentModes.IdOnly => prefix,
            LabelContentModes.NumberOnly => number,
            _ => prefix + number,
        };
    }

    private static string FormatId(int id, int idFormat) => (IdFormats)idFormat switch
    {
        IdFormats.Decimal => id.ToString(),
        IdFormats.HexadecimalUpper => id.ToString("X"),
        IdFormats.HexadecimalLower => id.ToString("x"),
        IdFormats.Binary => Convert.ToString(id, 2),
        _ => id.ToString(),
    };

    private static BlobInfo[] ReadBlobs(BufferWithViews blobData)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;
        var buffer = blobData.Buffer;
        var stride = buffer.Description.StructureByteStride;
        var elementCount = buffer.Description.SizeInBytes / stride;
        if (elementCount <= 0)
            return Array.Empty<BlobInfo>();

        using var staging = new SharpDX.Direct3D11.Buffer(device, new BufferDescription
        {
            Usage = ResourceUsage.Staging,
            SizeInBytes = buffer.Description.SizeInBytes,
            StructureByteStride = stride,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.BufferStructured,
        });

        context.CopyResource(buffer, staging);
        var dataBox = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            using var stream = new DataStream(dataBox.DataPointer, dataBox.RowPitch, true, false);
            return stream.ReadRange<BlobInfo>(elementCount);
        }
        finally
        {
            context.UnmapSubresource(staging, 0);
        }
    }

    private Resource<BmFontDescription> _fontResource;
    private BufferLayout[] _bufferContent = Array.Empty<BufferLayout>();
    private Buffer _gpuBuffer;

    private const int StructStride = 21 * 4;

    [StructLayout(LayoutKind.Explicit, Size = StructStride)]
    private struct BufferLayout
    {
        [FieldOffset(0)]
        public Vector3 Position;

        [FieldOffset(3 * 4)]
        public float CharHeight;

        [FieldOffset(4 * 4)]
        public float AspectRatio;

        [FieldOffset(5 * 4)]
        public Quaternion Orientation;

        [FieldOffset(9 * 4)]
        public Vector4 Color;

        [FieldOffset(13 * 4)]
        public Vector4 UvMinMax;

        [FieldOffset(17 * 4)]
        public uint Id;

        [FieldOffset(18 * 4)]
        public uint LineNumber;

        [FieldOffset(19 * 4)]
        public Vector2 Offset;
    }
}
