namespace Lib.point.modify;

[Guid("21b0a346-f214-449f-ae27-7bfbe5395d66")]
internal sealed class SetAttributesWithPointFields : Instance<SetAttributesWithPointFields>
{

    [Output(Guid = "1e4de159-c526-44b4-9d31-e1aec95b9bad")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "7485a1bc-0983-4348-a7d7-d723e92eecd9")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

    [Input(Guid = "11584b2f-3df4-4073-bcde-01e0fdaa3f5d")]
    public readonly InputSlot<float> Range = new InputSlot<float>();

    [Input(Guid = "a32d5d18-df0b-4fc2-9139-a68cc0449681")]
    public readonly InputSlot<float> OffsetRange = new InputSlot<float>();

    [Input(Guid = "3c70a819-d813-4ebd-9e5b-c185fac70ea2")]
    public readonly InputSlot<Vector2> BiasAndGain = new InputSlot<Vector2>();

    [Input(Guid = "46238904-91b9-47ab-bf90-9392fb034dc3")]
    public readonly InputSlot<float> Variation = new InputSlot<float>();

    [Input(Guid = "71ca8ac5-37ee-4b4c-aa04-9706e7923cff")]
    public readonly InputSlot<float> AffectColor = new InputSlot<float>();

    [Input(Guid = "8003ed74-aed6-4a2e-ae98-d293b023975f", MappedType = typeof(ColorModes))]
    public readonly InputSlot<int> ColorMode = new InputSlot<int>();

    [Input(Guid = "959d8403-74d0-4f8b-b2f8-28ba0d26a3e6")]
    public readonly InputSlot<Gradient> Gradient = new InputSlot<Gradient>();

    [Input(Guid = "9e69c4d8-3432-4afa-965f-55faffc35d32")]
    public readonly InputSlot<float> AffectPosition = new InputSlot<float>();

    [Input(Guid = "49bcec9b-d043-4f2d-a381-2697003268c9")]
    public readonly InputSlot<float> AffectOrientation = new InputSlot<float>();

    [Input(Guid = "f4eff9af-3f65-4603-9432-45c97978c92f")]
    public readonly InputSlot<Vector3> OrientationUpVector = new InputSlot<Vector3>();

    [Input(Guid = "ea732975-c8c7-4210-b2ea-0e73298c54d1")]
    public readonly InputSlot<float> AffectW = new InputSlot<float>();

    [Input(Guid = "f2cc5c36-1c99-421a-a253-1213317d9c98", MappedType = typeof(WModes))]
    public readonly InputSlot<int> WMode = new InputSlot<int>();

    [Input(Guid = "85e831f2-b72d-4a86-a4a8-52fda5dddabc")]
    public readonly InputSlot<bool> WCurveAffectsWeight = new InputSlot<bool>();

    [Input(Guid = "d3d92fe5-9a56-45f6-acc7-67370d744c0e")]
    public readonly InputSlot<Curve> WCurve = new InputSlot<Curve>();

    [Input(Guid = "cfc0c91e-4cb6-4408-a463-21209d7d4742")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();

    [Input(Guid = "df08c590-c398-4b92-aa09-ab57abc42aaf")]
    public readonly InputSlot<BufferWithViews> FieldPoints = new InputSlot<BufferWithViews>();

    private enum WModes
    {
        Set,
        Add,
        BlendWithOriginal,
    }
        
    private enum ColorModes
    {
        Add,
        Average,
        Blend,
    }
}