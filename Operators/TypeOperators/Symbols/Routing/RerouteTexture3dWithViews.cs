namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Texture3dWithViews through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("543cde35-4c5f-427b-80f2-1ebebd020f27")]
public sealed class RerouteTexture3dWithViews : Instance<RerouteTexture3dWithViews>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "fa12bb05-4871-4633-be20-19e2eaaa486f")]
    public readonly Slot<T3.Core.DataTypes.Texture3dWithViews> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "0ac48bc4-a4af-4836-8bd0-078efc4d42d9")]
    public readonly InputSlot<T3.Core.DataTypes.Texture3dWithViews> Input = new();
}
