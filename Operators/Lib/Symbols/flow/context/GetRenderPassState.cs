using T3.Core.Utils;

namespace Lib.flow.context;

[Guid("a54c36f9-aa5c-4c0b-824c-f61981802f10")]
internal sealed class GetRenderPassState : Instance<GetRenderPassState>
{
    /** Stable values: saved graphs store the number, so new entries are only appended. */
    private enum PassValues
    {
        VelocityRequested = 0,
        RepeatPass = 1,
        CubeFace = 2,
        IsShadowPass = 3,
        SubframeCount = 4,
    }

    /** Re-read on every pull: one op instance can run in several passes per frame (a shadow map and the main image). */
    [Output(Guid = "ff5b0eca-419d-4e4e-bf77-102c2065115b", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<int> Value = new();

    public GetRenderPassState()
    {
        Value.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Value.Value = Select.GetEnumValue<PassValues>(context) switch
                          {
                              PassValues.VelocityRequested => context.RenderPass.VelocityRequested ? 1 : 0,
                              PassValues.RepeatPass        => context.RenderPass.RepeatPass,
                              PassValues.CubeFace          => context.RenderPass.CubeFace,
                              PassValues.IsShadowPass      => context.RenderPass.IsShadowPass ? 1 : 0,
                              PassValues.SubframeCount     => context.RenderPass.SubframeCount,
                              _                            => 0,
                          };
    }

    [Input(Guid = "06e25694-70eb-4fa6-9f78-10fa215a2a80", MappedType = typeof(PassValues))]
    public readonly InputSlot<int> Select = new();
}
