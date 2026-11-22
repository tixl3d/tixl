#nullable enable
using T3.Core.Rendering;

namespace Lib.render.postfx;

/// <summary>
/// Resolves the buffers and camera a post effect needs: an explicitly connected input wins, otherwise the value comes from
/// the render bundle carried by <see cref="Image"/> (see [RenderScene]).
/// </summary>
[Guid("879fa99c-f462-4c0d-957c-9243ac60335c")]
internal sealed class HandleTextureBundle : Instance<HandleTextureBundle>, IStatusProvider
{
    [Output(Guid = "eba59106-9028-4097-88c7-0cd462b45feb")]
    public readonly Slot<Texture2D?> Depth = new();

    [Output(Guid = "7fe1f79e-77cc-4d4e-8a10-8e451df964c8")]
    public readonly Slot<Texture2D?> Normal = new();

    [Output(Guid = "d41ff3e9-bcbb-492a-af2b-9fb340cd80a2")]
    public readonly Slot<Texture2D?> Velocity = new();

    [Output(Guid = "792e2626-6d07-4b58-a7c1-4109111b9d7c")]
    public readonly Slot<Texture2D?> BaseColor = new();

    [Output(Guid = "cfdbb2a7-5e0e-4750-88d6-bb690b43e054")]
    public readonly Slot<Texture2D?> Material = new();

    [Output(Guid = "bcb64c90-6771-43ae-85b8-bea3dd975f35")]
    public readonly Slot<object?> Camera = new();

    public HandleTextureBundle()
    {
        Depth.UpdateAction += Update;
        Normal.UpdateAction += Update;
        Velocity.UpdateAction += Update;
        BaseColor.UpdateAction += Update;
        Material.UpdateAction += Update;
        Camera.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var bundle = Image.GetValue(context)?.Bundle;

        // Inside a composite these inputs are always connected to the parent's, so a missing value is null, not unconnected.
        Depth.Value = DepthMap.GetValue(context) ?? bundle?.Depth;
        Normal.Value = NormalMap.GetValue(context) ?? bundle?.Normal;
        Velocity.Value = VelocityMap.GetValue(context) ?? bundle?.Velocity;
        BaseColor.Value = BaseColorMap.GetValue(context) ?? bundle?.BaseColor;
        Material.Value = MaterialMap.GetValue(context) ?? bundle?.Material;
        Camera.Value = CameraReference.GetValue(context) ?? bundle?.Camera;

        _missing = Depth.Value == null
                       ? "No depth: connect DepthMap or render the image with [RenderScene]."
                       : Camera.Value == null
                           ? "No camera: connect CameraReference or render the image with [RenderScene]."
                           : null;

        Depth.DirtyFlag.Clear();
        Normal.DirtyFlag.Clear();
        Velocity.DirtyFlag.Clear();
        BaseColor.DirtyFlag.Clear();
        Material.DirtyFlag.Clear();
        Camera.DirtyFlag.Clear();
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => _missing == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    public string? GetStatusMessage() => _missing;

    private string? _missing;

    [Input(Guid = "a08378f3-c1c8-47ee-9e2c-ce8e5f851661")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "23bd0152-e178-4323-97de-8a39df7974c7")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "62694908-74bd-46a2-88e5-7867af1fe55a")]
    public readonly InputSlot<Texture2D> NormalMap = new();

    [Input(Guid = "2d6f7aaa-5952-4bee-865f-11b7937cf1cc")]
    public readonly InputSlot<Texture2D> VelocityMap = new();

    [Input(Guid = "3f252509-193e-4c56-a0e7-5ca1ba26e7e0")]
    public readonly InputSlot<Texture2D> BaseColorMap = new();

    [Input(Guid = "04bba317-ef1b-4de3-a3a0-37f3163bd6d1")]
    public readonly InputSlot<Texture2D> MaterialMap = new();

    [Input(Guid = "2a205c54-81f2-4e80-9de5-ed7a98bb6fc8")]
    public readonly InputSlot<object> CameraReference = new();
}
