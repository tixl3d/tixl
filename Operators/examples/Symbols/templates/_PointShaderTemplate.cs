namespace Examples.templates;

[Guid("0db659a4-d0ba-4d23-acac-aea5ba5b57dc")]
internal sealed class _PointShaderTemplate : Instance<_PointShaderTemplate>
{

    [Output(Guid = "30ecabbb-4efe-487a-9eba-e371c9d23662")]
    public readonly Slot<T3.Core.DataTypes.BufferWithViews> Output = new();

    [Input(Guid = "bc8395c2-f04b-4be3-b5a1-81f4ba5760dc")]
    public readonly InputSlot<System.Numerics.Vector3> Position = new();

    [Input(Guid = "fb959bb4-559b-4205-b85f-62307d73ab3a")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "77c021ed-6a7c-47c0-a327-49c2a055633e")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new();


    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
        WorldSpace,
    }
}