namespace Lib.render.shading.@_;

[Guid("83106ab0-9429-4197-98d8-365f6b4cf652")]
internal sealed class GetShadowParameters : Instance<GetShadowParameters>
{
    [Output(Guid = "4c2fb374-643d-492d-a2df-74b5182bef53", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Buffer> ParameterBuffer = new();

    [Output(Guid = "bbfae323-1e13-4b43-b33e-48ce98e92390", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<ShaderResourceView> ShadowMap = new();

    public GetShadowParameters()
    {
        ParameterBuffer.UpdateAction += UpdateParameterBuffer;
        ShadowMap.UpdateAction += UpdateShadowMap;
    }

    private void UpdateParameterBuffer(EvaluationContext context) => ParameterBuffer.Value = context.ShadowParameters;
    private void UpdateShadowMap(EvaluationContext context) => ShadowMap.Value = context.ShadowMap;
}
