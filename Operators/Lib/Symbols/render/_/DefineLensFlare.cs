using T3.Core.Utils;

namespace Lib.render.@_;

[Guid("947ad81e-47da-46c3-9b1d-8e578174d876")]
internal sealed class DefineLensFlare : Instance<DefineLensFlare>
{
    [Output(Guid = "B26730FF-B1FF-40A7-91AF-B10026ED4C32", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<StructuredList> OutBuffer = new();

    public DefineLensFlare()
    {
        OutBuffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var worldToClipSpace = Matrix4x4.Multiply(context.WorldToCamera, context.CameraToClipSpace);
        //Matrix worldToView = context.WorldToCamera * context.CameraProjection;
        //var worldToClipSpace = context.WorldToCamera
        //var viewToWorld = Matrix.Invert(worldToClipSpace);

            
        var brightness = Brightness.GetValue(context);

        var color = Color.GetValue(context);
        var randomizeColor = RandomizeColor.GetValue(context);
        var size = Size.GetValue(context);
        var randomizeSize = RandomizeSize.GetValue(context);
        var stretch = Stretch.GetValue(context);
        var distanceFromLight = OffsetTowardsCenter.GetValue(context);
        var spread = Spread.GetValue(context);
        var randomizeSpread = RandomizeSpread.GetValue(context);
        var positionFactor = DistributeSpread.GetValue(context);
        var randomizePosition = RandomizePosition.GetValue(context);

        var mixPointLightColor = MixPointLightColor.GetValue(context);

        var referencedLightIndex = LightIndex.GetValue(context);

        var innerFxZone = InnerFxZone.GetValue(context);
        var edgeFxZone = EdgeFxZone.GetValue(context);
        var zoneFxScale = FxZoneScale.GetValue(context);
        var zoneFxBrightness = FxZoneBrightness.GetValue(context);

        var matteBoxZone = MattBoxZone.GetValue(context);

        var rand = new Random(RandomSeed.GetValue(context));
        var fxZoneMode = (ZoneFxModes)FxZoneMode.GetValue(context);

        var rotation = Rotation.GetValue(context);
        var rotationSpread = RotationSpread.GetValue(context);

        var rotateTowards = (Categories)RotateTowards.GetValue(context);
        var spriteCount = SpriteCount.GetValue(context).Clamp(0, 1000);

        var startLightIndex = 0;
        var endLightIndex = context.PointLights.Count;
        var textureStart = TextureIndex.GetValue(context);


        _tempList.Clear();

        if (!(brightness > 0.00001f))
        {
            OutBuffer.Value = null;
            return;
        }

        if (referencedLightIndex >= 0)
        {
            startLightIndex = referencedLightIndex;
            endLightIndex = referencedLightIndex + 1;
        }

        var aspectRatio = (float)context.RequestedResolution.Width / (float)context.RequestedResolution.Height;

        for (int lightIndex = startLightIndex; lightIndex < endLightIndex; lightIndex++)
        {
            var pointLight = context.PointLights.GetPointLight(lightIndex);
            var lightPosDx = new Vector4(pointLight.Position, 1);

            var posInViewDx = Vector4.Transform(lightPosDx, worldToClipSpace);
            posInViewDx /= posInViewDx.W;

            // Ignore light sources behind
            var hideFactor = posInViewDx.Z < 0 ? 0 : 1;

            posInViewDx /= posInViewDx.W;
            var lightPosInView2D = new Vector2(posInViewDx.X, posInViewDx.Y);

            if (spriteCount != _sprites.NumElements)
            {
                _sprites = new StructuredList<Sprite>(spriteCount);
            }

            // Render Planes
            for (var i = 0; i < spriteCount; ++i)
            {
                var f = spriteCount <= 1 ? 0 : ((float)i / (spriteCount - 1) - 0.5f);
                var positionOnLine = (float)((-distanceFromLight
                                              + f * spread * 2
                                              + randomizeSpread * (rand.NextDouble() - 0.5) + 1));

                Vector2 objectScreenPos = lightPosInView2D * positionOnLine * positionFactor + (new Vector2(1, 1) - positionFactor) * lightPosInView2D;

                objectScreenPos += new Vector2((float)(randomizePosition.X * (rand.NextDouble() - 0.5)),
                                               (float)(randomizePosition.Y * (rand.NextDouble() - 0.5)));

                var sizeWithRandom = size * (float)(1.0 + randomizeSize * (rand.NextDouble() - 0.5)) / 0.2f;

                var colorWithLight =
                    new
                        Vector4((color.X + randomizeColor.X * (float)(rand.NextDouble() - 0.5) * 4) * MathUtils.Lerp(1f, pointLight.Color.X, mixPointLightColor),
                                (color.Y + randomizeColor.Y * (float)(rand.NextDouble() - 0.5) * 4) *
                                MathUtils.Lerp(1f, pointLight.Color.Y, mixPointLightColor),
                                (color.Z + randomizeColor.Z * (float)(rand.NextDouble() - 0.5) * 4) *
                                MathUtils.Lerp(1f, pointLight.Color.Z, mixPointLightColor),
                                color.W * (1 - randomizeColor.W * (float)(rand.NextDouble() * 2)));
                var spriteColor = Vector4.Clamp(colorWithLight, Vector4.Zero, new Vector4(100, 100, 100, 1));

                var triggerPosition = fxZoneMode == ZoneFxModes.Lights
                                          ? lightPosInView2D
                                          : objectScreenPos;

                var d = GetDistanceToEdge(triggerPosition);
                var cInnerZone = MathUtils.SmootherStep(innerFxZone.Y, innerFxZone.X, 1 - d);
                var cEdgeZone = MathUtils.SmootherStep(edgeFxZone.X, edgeFxZone.Y, 1 - d);
                var cMatteBox = MathUtils.SmootherStep(matteBoxZone.Y, matteBoxZone.X, 1 - d);

                var totalTriggerAmount = (cInnerZone + cEdgeZone) * cMatteBox;

                sizeWithRandom *= (1 + zoneFxScale * totalTriggerAmount).Clamp(0, 100);

                var brightnessEffect = (zoneFxBrightness * totalTriggerAmount).Clamp(0, 100);
                spriteColor.X += brightnessEffect;
                spriteColor.Y += brightnessEffect;
                spriteColor.Z += brightnessEffect;
                spriteColor.W = ((spriteColor.W + brightnessEffect)).Clamp(0, 1);

                spriteColor.W *= cMatteBox * pointLight.Color.W;

                // This might actually be a good idea. Maybe we should do this later..
                // Fade with incoming alpha from FlatShaders and Materials
                //color.W *= materialAlpha;

                var spriteRotation = rotation;

                switch (rotateTowards)
                {
                    case Categories.Object:
                        break;
                    case Categories.Light:
                        spriteRotation -=
                            (float)(Math.Atan2((objectScreenPos.X - lightPosInView2D.X) * aspectRatio, objectScreenPos.Y - lightPosInView2D.Y) +
                                    MathF.PI) * (180 / MathF.PI);
                        break;
                    case Categories.ScreenCenter:
                        spriteRotation -= (float)(Math.Atan2(objectScreenPos.X, objectScreenPos.Y) + MathF.PI) * 180f / MathF.PI;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                    
                spriteColor.W *= brightness;

                _tempList.Add(new Sprite
                                  {
                                      PosInClipSpace = objectScreenPos,
                                      Size = sizeWithRandom * stretch * hideFactor,
                                      Color = spriteColor,
                                      RotationDeg = spriteRotation + f * rotationSpread * 180,
                                      UvMin = Vector2.Zero,
                                      UvMax = Vector2.One,
                                      TextureIndex = (int)(rand.NextDouble() * 65535) //Fixme: implement correct number
                                  });
            }
        }

        // Copy to structured array
        if (_tempList.Count != _sprites.NumElements)
        {
            _sprites = new StructuredList<Sprite>(_tempList.Count);
        }

        for (var spriteIndex = 0; spriteIndex < _tempList.Count; spriteIndex++)
        {
            _sprites.TypedElements[spriteIndex] = _tempList[spriteIndex];
        }

        OutBuffer.Value = _sprites;
    }

    private static float GetDistanceToEdge(Vector2 posInClipSpace)
    {
        var p = (posInClipSpace / 2 + Vector2.One * 0.5f);
        var dToRight = 1 - p.X;
        var dToLeft = p.X;
        var dToUp = p.Y;
        var dToBottom = 1 - p.Y;

        var d = MathF.Min(dToLeft, dToRight);
        d = MathF.Min(d, dToUp);
        d = MathF.Min(d, dToBottom);
        d *= 2;

        return d;
    }

    private readonly List<Sprite> _tempList = new(100);

    [StructLayout(LayoutKind.Explicit, Size = SizeInBytes)]
    public struct Sprite
    {
        [FieldOffset(0 * 4)]
        public Vector2 PosInClipSpace;

        [FieldOffset(2 * 4)]
        public Vector2 Size;

        [FieldOffset(4 * 4)]
        public float RotationDeg;

        [FieldOffset(5 * 4)]
        public Vector4 Color;

        [FieldOffset(9 * 4)]
        public Vector2 UvMin;

        [FieldOffset(11 * 4)]
        public Vector2 UvMax;

        [FieldOffset(13 * 4)]
        public int TextureIndex;
            
        [FieldOffset(14 * 4)]
        public Vector2 __padding;

        private const int SizeInBytes = 16 * 4;
    }

    private StructuredList<Sprite> _sprites = new(10);
        
    private enum ZoneFxModes
    {
        Off,
        Objects,
        Lights,
    }

    [Input(Guid = "50FDA27A-BD94-4438-9CEF-0AAA16FAE12B")]
    public readonly InputSlot<int> LightIndex = new();

    [Input(Guid = "59BA312F-C74F-4E76-8830-57C2EFFA0FF1")]
    public readonly InputSlot<int> SpriteCount = new();

    [Input(Guid = "9F85D1F1-37D2-4F7E-9EFD-70CB415C4CEE")]
    public readonly InputSlot<float> Brightness = new();


    [Input(Guid = "792E9B7E-9094-4BF9-8AED-D8B3FCDEE358")]
    public readonly InputSlot<float> Spread = new();
        
    [Input(Guid = "F21DBEFD-EA54-4901-A806-B1A2C5EE140F")]
    public readonly InputSlot<float> OffsetTowardsCenter = new();

    [Input(Guid = "BC1D9FDC-EA07-4C0D-BE2D-02FA955F9E5A")]
    public readonly InputSlot<float> RandomizeSpread = new();

    [Input(Guid = "1C250003-CF16-44DF-9A5E-F9FCF331617C")]
    public readonly InputSlot<Vector2> DistributeSpread = new();

    [Input(Guid = "D314C572-71C7-4A67-921B-DF369817DD4A")]
    public readonly InputSlot<Vector2> RandomizePosition = new();

        
    [Input(Guid = "CCF77198-7682-4F6B-96F2-986E6827A4DF")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "2A1285B1-63EA-46A5-8D96-B7BA33EDD88B")]
    public readonly InputSlot<float> RandomizeSize = new();

    [Input(Guid = "C8347CA9-C700-4195-A23F-0F220F5823E2")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "000A17CA-36A2-43EF-8FDA-314366C9E204")]
    public readonly InputSlot<float> Rotation = new();

    [Input(Guid = "93728B1F-CBFA-4065-9DE1-BF8641FADE7E")]
    public readonly InputSlot<float> RotationSpread = new();

    // [Input(Guid = "7244CC40-8F0A-4381-80A3-EB818E262C88", MappedType = typeof(ColorSources))]
    // public readonly InputSlot<int> ColorSource = new();
        
    [Input(Guid = "205ED310-E01C-4B0C-9C24-E404476CE036", MappedType = typeof(Categories))]
    public readonly InputSlot<int> RotateTowards = new();
        
    [Input(Guid = "D6554B75-F320-4E8B-BCB0-6B484C29F6D3")]
    public readonly InputSlot<float> MixPointLightColor = new();
        
    [Input(Guid = "7D9DA46C-2D1F-48F8-BDC5-BB7E29C363C7")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "53351CF3-71A5-4CB3-AD81-60ABC7718D4B")]
    public readonly InputSlot<Vector4> RandomizeColor = new();

    // [Input(Guid = "77EAC715-D2EE-4BD5-93F5-1E9E7119A0E6")]
    // public readonly InputSlot<Vector2> TextureCells = new();

    [Input(Guid = "C1B5F49F-3538-48AA-8D16-92D48FCF08CB", MappedType = typeof(ZoneFxModes))]
    public readonly InputSlot<int> FxZoneMode = new();

    [Input(Guid = "1C11CB25-05F8-4422-AA16-DB57E8CD2E0B")]
    public readonly InputSlot<Vector2> EdgeFxZone = new();

    [Input(Guid = "00ED2D51-4CF0-43D6-ADAA-CE42E5EB8439")]
    public readonly InputSlot<Vector2> InnerFxZone = new();


    [Input(Guid = "0D98C14C-3F62-47E2-8FE8-A85208D9C02D")]
    public readonly InputSlot<float> FxZoneScale = new();

    [Input(Guid = "520EE127-F542-4AD8-A6EA-4A24A70ADE4D")]
    public readonly InputSlot<float> FxZoneBrightness = new();

    [Input(Guid = "BE4366C5-9E1C-430A-8F34-F31321A7DF2C")]
    public readonly InputSlot<Vector2> MattBoxZone = new();

    [Input(Guid = "9CFFFB1A-675E-410C-96DA-C02BD6B3A81A")]
    public readonly InputSlot<int> RandomSeed = new();

    [Input(Guid = "A5AD260C-9341-4E43-923C-86F12974DF97")]
    public readonly InputSlot<Int2> TextureIndex = new();

    private enum Categories
    {
        Object,
        Light,
        ScreenCenter,
    }
}