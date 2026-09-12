namespace Types.Routing;

// Passes T3.Core.DataTypes.PixelShader through the .t3 connection; no C# evaluator is needed.
[Guid("dc440a5c-796e-475c-9c21-4b2f23a68509")]
public sealed class ReroutePixelShader : Instance<ReroutePixelShader>, IRerouteNode
{
    [Output(Guid = "a5e178d4-b5fd-4443-ba4b-1e4ce26ec081")]
    public readonly Slot<T3.Core.DataTypes.PixelShader> Output = new();

    [Input(Guid = "85ee34ae-96e6-4e42-8cb7-07f17108d35e")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> Input = new();
}
