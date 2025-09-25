using Lib.Utils;
using T3.Core.Utils;

namespace Lib.render.@_;

[Guid("c5707b79-859b-4d53-92e0-cbed53aae648")]
internal sealed class _RenderFontBuffer : Instance<_RenderFontBuffer>
{
    [Output(Guid = "3D2F53A3-F1F0-489B-B20B-BADB09CDAEBE")]
    public readonly Slot<Buffer> Buffer = new();

    [Output(Guid = "A0ECA9CE-35AA-497D-B5C9-CDE52A7C8D58")]
    public readonly Slot<int> VertexCount = new();
    public _RenderFontBuffer()
    {
        _fontResource = new Resource<BmFontDescription>(Filepath, OnFileChanged);
        _fontResource.AddDependentSlots(Buffer);
        Buffer.UpdateAction += UpdateMesh;
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

    // Cached state (hot path avoids work if none changed)
    private string _lastText = string.Empty;
    private float _lastSize, _lastSpacing, _lastLineHeight;
    private Vector2 _lastPosition;
    private Vector4 _lastColor;
    private int _lastHAlign, _lastVAlign;
    private BmFontDescription _lastFontRef;
    private bool _bufferDirty = true;

    private BufferLayout[] _bufferContent = Array.Empty<BufferLayout>();
    private int _lastVertexCount;

    private static readonly int HorizontalAlignCount = Enum.GetValues(typeof(BmFontDescription.HorizontalAligns)).Length;
    private static readonly int VerticalAlignCount   = Enum.GetValues(typeof(BmFontDescription.VerticalAligns)).Length;

    private void UpdateMesh(EvaluationContext context)
    {
        if (!_fontResource.TryGetValue(context, out var font))
            return;

        var text = Text.GetValue(context);
        if (string.IsNullOrEmpty(text))
            text = " ";

        // Inputs
        var rawSize        = Size.GetValue(context);
        var spacing        = Spacing.GetValue(context);
        var lineHeight     = LineHeight.GetValue(context);
        var position       = Position.GetValue(context);
        var color          = Color.GetValue(context);
        var hAlignInt      = HorizontalAlign.GetValue(context).Clamp(0, HorizontalAlignCount - 1);
        var vAlignInt      = VerticalAlign.GetValue(context).Clamp(0, VerticalAlignCount - 1);

        // Change detection
        bool fontChanged       = !ReferenceEquals(font, _lastFontRef);
        bool textChanged       = !string.Equals(text, _lastText, StringComparison.Ordinal);
        bool formattingChanged =
            _lastSize       != rawSize    ||
            _lastSpacing    != spacing    ||
            _lastLineHeight != lineHeight ||
            _lastPosition   != position   ||
            _lastColor      != color      ||
            _lastHAlign     != hAlignInt  ||
            _lastVAlign     != vAlignInt;

        if (fontChanged || textChanged || formattingChanged)
        {
            _lastFontRef   = font;
            _lastText      = text;
            _lastSize      = rawSize;
            _lastSpacing   = spacing;
            _lastLineHeight= lineHeight;
            _lastPosition  = position;
            _lastColor     = color;
            _lastHAlign    = hAlignInt;
            _lastVAlign    = vAlignInt;

            // Ensure capacity (grow only)
            if (_bufferContent.Length < text.Length)
                _bufferContent = new BufferLayout[text.Length];

            // Rebuild CPU buffer
            _lastVertexCount = RebuildBuffer(
                font,
                text,
                rawSize,
                spacing,
                lineHeight,
                position,
                color,
                (BmFontDescription.HorizontalAligns)hAlignInt,
                (BmFontDescription.VerticalAligns)vAlignInt);

            _bufferDirty = true;
        }

        // Upload only when dirty or buffer missing
        if (_bufferDirty || Buffer.Value == null)
        {
            ResourceManager.SetupStructuredBuffer(_bufferContent, ref Buffer.Value);
            if (Buffer.Value != null)
                Buffer.Value.DebugName = nameof(_RenderFontBuffer);
            _bufferDirty = false;
        }

        VertexCount.Value = _lastVertexCount;
    }

    private int RebuildBuffer(BmFontDescription font,
                              string text,
                              float rawSize,
                              float characterSpacing,
                              float lineHeight,
                              Vector2 position,
                              Vector4 color,
                              BmFontDescription.HorizontalAligns horizontalAlign,
                              BmFontDescription.VerticalAligns verticalAlign)
    {
        // Line count without allocations
        int numLinesInText = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') numLinesInText++;

        float textureWidth  = font.BmFont.Common.ScaleW;
        float textureHeight = font.BmFont.Common.ScaleH;

        // Scale to match 1080p 72DPI pt sizes (as in original)
        var scaleFactor = 1.0 / font.BmFont.Info.Size * 0.00185;
        var size = (float)(rawSize * scaleFactor);

        float cursorX = 0;
        float cursorY = 0;
        const float sdfWidth = 5f; // heuristic

        switch (verticalAlign)
        {
            case BmFontDescription.VerticalAligns.Top:
                cursorY = font.BmFont.Common.Base * (1 + sdfWidth / font.BmFont.Info.Size);
                break;
            case BmFontDescription.VerticalAligns.Middle:
                cursorY = font.BmFont.Common.LineHeight * lineHeight * (numLinesInText - 1) / 2
                          + font.BmFont.Common.LineHeight / 2f
                          + font.BmFont.Common.Base * (sdfWidth / font.BmFont.Info.Size);
                break;
            case BmFontDescription.VerticalAligns.Bottom:
                cursorY = font.BmFont.Common.LineHeight * lineHeight * numLinesInText;
                break;
        }

        int outputIndex = 0;
        int currentLineCharacterCount = 0;
        int lineNumber = 0;
        int lastChar = 0;

        for (int idx = 0; idx < text.Length; idx++)
        {
            char c = text[idx];

            if (c == '\n')
            {
                AdjustLineAlignment(horizontalAlign, cursorX, characterSpacing, size, currentLineCharacterCount, outputIndex);
                cursorY -= font.BmFont.Common.LineHeight * lineHeight;
                cursorX = 0;
                currentLineCharacterCount = 0;
                lastChar = 0;
                lineNumber++;
                continue;
            }

            if (!font.InfoForCharacter.TryGetValue(c, out var charInfo))
            {
                lastChar = 0;
                continue;
            }

            if (lastChar != 0)
            {
                int key = lastChar | c; // kept as in original
                if (font.KerningForPairs.TryGetValue(key, out var kerning))
                    cursorX += kerning;
            }

            float sizeWidth  = charInfo.Width  * size;
            float sizeHeight = charInfo.Height * size;
            float x = position.X + (cursorX + charInfo.XOffset) * size;
            float y = position.Y + ((cursorY - charInfo.YOffset)) * size;

            if (charInfo.Width != 1 || charInfo.Height != 1)
            {
                _bufferContent[outputIndex] = new BufferLayout
                {
                    Position    = new Vector3(x, y, 0),
                    CharHeight  = sizeHeight,
                    Orientation = Quaternion.Identity,
                    AspectRatio = sizeWidth / sizeHeight,
                    Color       = color,
                    UvMinMax    = new Vector4(
                                    charInfo.X / textureWidth,
                                    charInfo.Y / textureHeight,
                                    (charInfo.X + charInfo.Width) / textureWidth,
                                    (charInfo.Y + charInfo.Height) / textureHeight),
                    Id          = (uint)outputIndex,
                    LineNumber  = (uint)lineNumber,
                    Offset      = new Vector2(charInfo.XOffset, charInfo.YOffset),
                };

                outputIndex++;
            }

            currentLineCharacterCount++;
            cursorX += charInfo.XAdvance + characterSpacing;
            lastChar = c;
        }

        // Align last line
        AdjustLineAlignment(horizontalAlign, cursorX, characterSpacing, size, currentLineCharacterCount, outputIndex);

        // Return vertex count (6 per quad)
        return outputIndex * 6;
    }

    private void AdjustLineAlignment(BmFontDescription.HorizontalAligns horizontalAlign,
                                     float cursorX,
                                     float characterSpacing,
                                     float size,
                                     int currentLineCharacterCount,
                                     int outputIndex)
    {
        switch (horizontalAlign)
        {
            case BmFontDescription.HorizontalAligns.Center:
                OffsetLineCharacters((cursorX / 2 - characterSpacing / 2) * size, currentLineCharacterCount, outputIndex);
                break;
            case BmFontDescription.HorizontalAligns.Right:
                OffsetLineCharacters(cursorX * size, currentLineCharacterCount, outputIndex);
                break;
        }
    }

    private void OffsetLineCharacters(float offset, int currentLineCharacterCount, int outputIndex)
    {
        for (int backIndex = 0; backIndex <= currentLineCharacterCount; backIndex++)
        {
            int index0 = outputIndex - backIndex;
            if (index0 < 0 || index0 >= _bufferContent.Length)
                continue;

            _bufferContent[index0].Position.X -= offset;
        }
    }

    private Resource<BmFontDescription> _fontResource;

    [StructLayout(LayoutKind.Explicit, Size = StructSize)]
    public struct BufferLayout
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

        private const int StructSize = 21 * 4;
    }
        
    // Inputs ----------------------------------------------------
    [Input(Guid = "F2DD87B1-7F37-4B02-871B-B2E35972F246")]
    public readonly InputSlot<string> Text = new();

    [Input(Guid = "E827FDD1-20CA-473C-99EE-B839563690E9")]
    public readonly InputSlot<string> Filepath = new();

    [Input(Guid = "1CDE902D-5EAA-4144-B579-85F54717356B")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "5008E9B4-083A-4494-8F7C-50FE5D80FC35")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "E05E143E-8D4C-4DE7-8C9C-7FA7755009D3")]
    public readonly InputSlot<float> Spacing = new();

    [Input(Guid = "9EB4E13F-0FE3-4ED9-9DF1-814F075A05DA")]
    public readonly InputSlot<float> LineHeight = new();

    [Input(Guid = "C4F03392-FF7E-4B4A-8740-F93A581B2B6B")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "FFD2233A-8F3E-426B-815B-8071E4C779AB")]
    public readonly InputSlot<float> Slant = new();

    [Input(Guid = "14829EAC-BA59-4D31-90DC-53C7FC56CC30")]
    public readonly InputSlot<int> VerticalAlign = new();

    [Input(Guid = "E43BC887-D425-4F9C-8A86-A32C761DE0CC")]
    public readonly InputSlot<int> HorizontalAlign = new();        
}