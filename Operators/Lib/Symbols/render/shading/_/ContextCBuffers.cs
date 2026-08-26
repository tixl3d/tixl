namespace Lib.render.shading.@_;

[Guid("d32a5484-880c-41d4-88ea-6ee1a3e61f0b")]
internal sealed class ContextCBuffers : Instance<ContextCBuffers>
{
    [Output(Guid = "d4171c74-5a90-4fe9-8334-10f9701c284c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer> FogParameters = new();

    [Output(Guid = "5cb8c86e-c3a6-434c-b30a-a107121436b2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer> PointLights = new();

    public ContextCBuffers()
    {
        FogParameters.UpdateAction += Update;
        PointLights.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        FogParameters.Value = context.FogParameters;
        PointLights.Value = context.PointLights.ConstBuffer;
    }
}