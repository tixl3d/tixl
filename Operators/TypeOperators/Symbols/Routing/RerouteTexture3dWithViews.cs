namespace Types.Routing;

// Passes T3.Core.DataTypes.Texture3dWithViews through the .t3 connection; no C# evaluator is needed.
[Guid("543cde35-4c5f-427b-80f2-1ebebd020f27")]
public sealed class RerouteTexture3dWithViews : Instance<RerouteTexture3dWithViews>, IRerouteNode
{
    [Output(Guid = "fa12bb05-4871-4633-be20-19e2eaaa486f")]
    public readonly Slot<T3.Core.DataTypes.Texture3dWithViews> Output = new();

    [Input(Guid = "0ac48bc4-a4af-4836-8bd0-078efc4d42d9")]
    public readonly InputSlot<T3.Core.DataTypes.Texture3dWithViews> Input = new();
}
