namespace Types.Routing;

// Passes SharpDX.Direct3D11.SamplerState through the .t3 connection; no C# evaluator is needed.
[Guid("cbf73235-5b79-4ceb-9d40-81ae9e29ee6f")]
public sealed class RerouteSamplerState : Instance<RerouteSamplerState>, IRerouteNode
{
    [Output(Guid = "c0f8b77b-2916-4e49-a639-618a80486619")]
    public readonly Slot<SharpDX.Direct3D11.SamplerState> Output = new();

    [Input(Guid = "0e35dfda-9db9-4ef0-acde-9c52d1cd7475")]
    public readonly InputSlot<SharpDX.Direct3D11.SamplerState> Input = new();
}
