#nullable enable
namespace Lib.render.postfx;

/// <summary>
/// Hands an effect's result the render bundle of its source image, for effects that keep the scene's geometry (lighting,
/// anti-aliasing, color grading), so the next effect in the chain still finds depth, normals, velocity and camera.
/// </summary>
[Guid("24ff7992-4d49-46e9-81bf-58442684a895")]
internal sealed class AttachTextureBundle : Instance<AttachTextureBundle>
{
    [Output(Guid = "fc1c0611-1bb4-4111-95e3-1c8c99994291")]
    public readonly Slot<Texture2D?> Output = new();

    public AttachTextureBundle()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        var bundle = BundleFrom.GetValue(context)?.Bundle;

        // The result belongs to the effect's own render target, which leaves the bundle alone unless told to attach one.
        if (texture != null && bundle != null)
            texture.Bundle = bundle;

        Output.Value = texture;
        Output.DirtyFlag.Clear();
    }

    [Input(Guid = "a78e187e-ecbd-4dc1-b5de-d69ea5c8c42b")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "31caea05-a9ef-43e8-a75a-e8d2b27f303e")]
    public readonly InputSlot<Texture2D> BundleFrom = new();
}
