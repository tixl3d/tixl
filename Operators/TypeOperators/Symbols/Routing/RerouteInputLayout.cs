namespace Types.Routing;

// Passes SharpDX.Direct3D11.InputLayout through the .t3 connection; no C# evaluator is needed.
[Guid("4a4de885-dd67-4647-8148-3dc8c70db02c")]
public sealed class RerouteInputLayout : Instance<RerouteInputLayout>, IRerouteNode
{
    [Output(Guid = "e4114fb9-3afe-4f2b-b7b9-7cb2859b56ac")]
    public readonly Slot<SharpDX.Direct3D11.InputLayout> Output = new();

    [Input(Guid = "5e3c45a1-12b1-4a87-8a3e-c9daea1c36a6")]
    public readonly InputSlot<SharpDX.Direct3D11.InputLayout> Input = new();
}
