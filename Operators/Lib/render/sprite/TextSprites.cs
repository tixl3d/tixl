#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.render.sprite;

[Guid("1a6a58ea-c63a-4c99-aa9d-aeaeb01662f4")]
internal sealed class TextSprites : Instance<TextSprites>
{
    [Output(Guid = "89685AC6-6A97-403C-8334-E685A4CCCDA0")]
    public readonly Slot<BufferWithViews> PointBuffer = new();

    [Output(Guid = "0BEA15C8-329A-4705-BC11-12EEF4F1A70A")]
    public readonly Slot<BufferWithViews> SpriteBuffer = new();

    [Output(Guid = "5BB66419-9FA8-4BD0-8476-D389E9EC78D5")]
    public readonly Slot<Texture2D> Texture = new();

    private Resource<Texture2D>? _texture;
    private ShaderResourceView? _textureSrv;
    private readonly Resource<BmFontDescription> _bmFont;
    public TextSprites()
    {
        _bmFont = new Resource<BmFontDescription>(Filepath, TryGenerateFont);
        _bmFont.AddDependentSlots(SpriteBuffer);
        _bmFont.AddDependentSlots(PointBuffer);
        SpriteBuffer.UpdateAction += Update;
        PointBuffer.UpdateAction += Update;
    }

    private bool TryGenerateFont(FileResource file, BmFontDescription? currentValue, [NotNullWhen(true)]out BmFontDescription? newValue, [NotNullWhen(false)]out string? failureReason)
    {
        var absolutePath = file.AbsolutePath;
        if (BmFontDescription.TryInitializeFromFile(absolutePath, out var potentialValue))
        {
            var fileExtension = Path.GetExtension(absolutePath);
            var imageFilePath = absolutePath.Replace(fileExtension, ".png");
            if (TryUpdateTexture(imageFilePath))
            {
                newValue = potentialValue;
                failureReason = string.Empty;
                return true;
            }

            newValue = null;
            failureReason = $"Could not load texture from file '{imageFilePath}'";
            return false;
        }

        failureReason = $"Could not load font from file '{file.AbsolutePath}'";
        newValue = null;
        return false;
    }

    private void Update(EvaluationContext context)
    {
        UpdateMesh(context); 

        // Prevent multiple evaluation because previously fetched SRV will be disposed
        SpriteBuffer.DirtyFlag.Clear();
        PointBuffer.DirtyFlag.Clear();
    }
        
    private bool TryUpdateTexture(string imageFilePath)
    {
        if (_texture != null)
        {
            _texture.Dispose();
            _texture = null;
            Texture.Value = null!;
        }
            
        _texture = ResourceManager.CreateTextureResource(imageFilePath, this);

        var success = _texture.Value != null;
        if (success)
        {
            var tex = _texture.Value;
            if (tex != null)
            {
                tex.Name = imageFilePath;
                UpdateTexture(tex);
            }
        }
            
        Texture.DirtyFlag.Clear();
        return success;

        void UpdateTexture(Texture2D texture)
        {
            if (Texture.Value == texture)
            {
                return;
            }

            Texture.Value = texture;
            texture.CreateShaderResourceView(ref _textureSrv, "TextSpritesSRV");

            try
            {
                if (_textureSrv != null)
                    ResourceManager.Device.ImmediateContext.GenerateMips(_textureSrv);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to generate mipmaps for texture {texture.Name}:" + e);
            }
        }
    }

    private void UpdateMesh(EvaluationContext context)
    {
        var text = Text.GetValue(context) ?? string.Empty;

        var bmFont = _bmFont.GetValue(context);
        if (bmFont == null)
        {
            Log.Warning("Can't generate text sprites without valid BMFont definition", this);
            return;
        }

        var lineNumber = 0;

        var offsetBaseLine = OffsetBaseLine.GetValue(context);
        var horizontalAlign = (BmFontDescription.HorizontalAligns)HorizontalAlign.GetValue(context)
                                                                                 .Clamp(0,
                                                                                        Enum.GetValues(typeof(BmFontDescription.HorizontalAligns)).Length -
                                                                                        1);
        var verticalAlign =
            (BmFontDescription.VerticalAligns)VerticalAlign.GetValue(context).Clamp(0, Enum.GetValues(typeof(BmFontDescription.VerticalAligns)).Length - 1);

        var characterSpacing = Spacing.GetValue(context);
        var lineHeight = LineHeight.GetValue(context);
        const float viewHeightInT3Units = 2;
        var textSize = Size.GetValue(context);

        var scaleFactor =  (textSize / bmFont.BmFont.Info.Size) * (viewHeightInT3Units / 1080f); // from cursor space to t3 units  
        var textPosition = Position.GetValue(context);

        var numLinesInText = text.Split('\n').Length;

        var color = Color.GetValue(context);
        float textureWidth = bmFont.BmFont.Common.ScaleW;
        float textureHeight = bmFont.BmFont.Common.ScaleH;
        float cursorX = 0;
        float cursorY = 0;
        var verticalCenterOffset = bmFont.Padding.Up + bmFont.BmFont.Common.Base + bmFont.Padding.Down - bmFont.BmFont.Info.Size / 2f;

        switch (verticalAlign)
        {
            case BmFontDescription.VerticalAligns.Top:
                // an ugly approximation to the original text implementation
                cursorY = bmFont.BmFont.Common.Base * (1 + bmFont.Padding.Up / bmFont.BmFont.Info.Size) - verticalCenterOffset;
                break;
                
            case BmFontDescription.VerticalAligns.Middle:
                cursorY = bmFont.BmFont.Common.LineHeight * lineHeight * (numLinesInText - 1) / 2;
                break;
                
            case BmFontDescription.VerticalAligns.Bottom:
                cursorY = bmFont.BmFont.Common.LineHeight * lineHeight * (numLinesInText) - (verticalCenterOffset - offsetBaseLine);
                break;
        }
            
        _sprites.Clear();
        _points.Clear();

        var outputIndex = 0;
        var currentLineCharacterCount = 0;
        var lastCharId = 0;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                AdjustLineAlignment();

                cursorY -= bmFont.BmFont.Common.LineHeight * lineHeight;
                cursorX = 0;
                currentLineCharacterCount = 0;
                lastCharId = 0;
                lineNumber++;
                continue;
            }

            if (!bmFont.InfoForCharacter.TryGetValue(c, out var charInfo))
            {
                lastCharId = 0;
                continue;
            }

            cursorX += bmFont.GetKerning(lastCharId, charInfo.Id) *0;



            var charTopLeft = new Vector2(cursorX + charInfo.XOffset,
                                          cursorY + charInfo.YOffset);

            var center = textPosition + new Vector3(
                                                    charTopLeft.X + charInfo.Width/2f,
                                                    cursorY,
                                                    0
                                                   ) * scaleFactor;

            var pivot = new Vector2(0,
                                    charInfo.YOffset + charInfo.Height / 2f - verticalCenterOffset - offsetBaseLine
                                   );
            pivot *= scaleFactor;

            _sprites.Add(
                             new Sprite
                                 {
                                     Width = charInfo.Width * scaleFactor,
                                     Height = charInfo.Height * scaleFactor,
                                     Color = color,
                                     UvMin = new Vector2(charInfo.X / textureWidth, charInfo.Y / textureHeight),
                                     UvMax = new Vector2((charInfo.X + charInfo.Width) / textureWidth, (charInfo.Y + charInfo.Height) / textureHeight),
                                     Pivot = pivot,
                                     CharIndex = (uint)outputIndex,
                                     CharIndexInLine = (uint)currentLineCharacterCount,
                                     LineIndex = (uint)lineNumber,
                                     Extra = 0
                                 });

            _points.Add(new Point
                            {
                                Position = center,
                                F1 = 1,
                                Orientation = Quaternion.Identity,
                                F2 = 1,
                                Scale = Vector3.One,
                                Color = color,
                            });
            outputIndex++;
            currentLineCharacterCount++;

            cursorX += charInfo.XAdvance;
            cursorX += characterSpacing;
            lastCharId = charInfo.Id;
        }

        AdjustLineAlignment();

        var pointBuffer= new BufferWithViews();
        ResourceManager.SetupStructuredBuffer( _points.Count > 0 ? _points.ToArray() : _nonPoints, ref pointBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(pointBuffer.Buffer, ref pointBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(pointBuffer.Buffer, UnorderedAccessViewBufferFlags.None,ref pointBuffer.Uav);
        PointBuffer.Value?.Dispose();
        PointBuffer.Value = pointBuffer;
            
        var spriteBuffer = new BufferWithViews();
        ResourceManager.SetupStructuredBuffer( _sprites.Count > 0 ? _sprites.ToArray() : _nonSprite, ref spriteBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(spriteBuffer.Buffer, ref spriteBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(spriteBuffer.Buffer, UnorderedAccessViewBufferFlags.None,ref spriteBuffer.Uav);
        SpriteBuffer.Value?.Dispose();
        SpriteBuffer.Value = spriteBuffer;
            
            
        void AdjustLineAlignment()
        {
            switch (horizontalAlign)
            {
                case BmFontDescription.HorizontalAligns.Center:
                    OffsetLineCharacters((cursorX / 2 - characterSpacing / 2) * scaleFactor, currentLineCharacterCount, outputIndex);
                    break;
                case BmFontDescription.HorizontalAligns.Right:
                    OffsetLineCharacters(cursorX * scaleFactor, currentLineCharacterCount, outputIndex);
                    break;
            }
        }
    }

    private readonly Sprite[] _nonSprite = { new()
                                                 {
                                                     Height = 0,
                                                     Width =  0,
                                                             
                                                 } };
        
    private readonly Point[] _nonPoints = { new()
                                                {
                                                    Position = Vector3.Zero,
                                                    F1= float.NaN,
                                                    Orientation =  Quaternion.Identity,
                                                    Color = Vector4.One,
                                                    F2 = 1,
                                                    Scale = Vector3.One,
                                                } };

    private void OffsetLineCharacters(float offset, int currentLineCharacterCount, int outputIndex)
    {
        for (var backIndex = 0; backIndex <= currentLineCharacterCount; backIndex++)
        {
            var index0 = outputIndex - backIndex;
            if (index0 < 0 || index0 >= _points.Count)
                continue;

            var p = _points[index0];
            p.Position.X -= offset;
            _points[index0] = p;
        }
    }

    private readonly List<Sprite> _sprites = new(100);
    private readonly List<Point> _points = new(100);

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

    [Input(Guid = "77A79A37-6151-4A35-BE4D-415A91B0E651")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "14829EAC-BA59-4D31-90DC-53C7FC56CC30", MappedType = typeof(BmFontDescription.VerticalAligns))]
    public readonly InputSlot<int> VerticalAlign = new();

    [Input(Guid = "E43BC887-D425-4F9C-8A86-A32C761DE0CC", MappedType = typeof(BmFontDescription.HorizontalAligns))]
    public readonly InputSlot<int> HorizontalAlign = new();

    [Input(Guid = "7094D22F-DCF9-4FD0-9570-7243E3284CF4")]
    public readonly InputSlot<float> OffsetBaseLine = new();
}