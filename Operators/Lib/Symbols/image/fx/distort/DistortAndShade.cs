namespace Lib.image.fx.distort;

[Guid("8bede700-4e3e-42d8-8097-9744abdb8ad3")]
internal sealed class DistortAndShade : Instance<DistortAndShade>
{
    [Output(Guid = "5a639fac-b8e1-495b-a82f-e4877133b06f")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "824e1ad4-0d33-458f-aefe-f3780ab06529")]
    public readonly InputSlot<Texture2D> ImageA = new InputSlot<Texture2D>();

    [Input(Guid = "2596e8fb-00aa-4704-9978-a880c1016c18")]
    public readonly InputSlot<Texture2D> ImageB = new InputSlot<Texture2D>();

    [Input(Guid = "84cea304-79d9-4f8c-a171-584897b9b468")]
    public readonly InputSlot<float> Displacement = new InputSlot<float>();

    [Input(Guid = "3a3acfbd-dca7-4f8a-b862-90eae2bc41ca")]
    public readonly InputSlot<Vector2> Center = new InputSlot<Vector2>();

    [Input(Guid = "f942fc0d-ed5f-4594-8690-d464c5b12ed8")]
    public readonly InputSlot<float> Shading = new InputSlot<float>();

    [Input(Guid = "17c45c4e-590e-4de6-90c2-befe2f89831d")]
    public readonly InputSlot<Vector4> ShadeColor = new InputSlot<Vector4>();

}