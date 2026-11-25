namespace Lib.render.shading;

[Guid("8e9ddaba-ef3f-4268-a8c4-e72ef9e45aed")]
internal sealed class DirectionalLight : Instance<DirectionalLight>, ITransformable
{
    [Output(Guid = "b4ad9dad-a492-4bc6-afb3-33a10595bc2e")]
    public readonly TransformCallbackSlot<Command> Output = new();

    public DirectionalLight()
    {
        Output.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    IInputSlot ITransformable.AimTargetInput => Target;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "70bfb720-55ff-4936-b3f5-f894b3a4fa71")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "a67fdfb7-f18e-44b8-aee8-d112070f5f98")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "d49db40d-47d7-4a07-be8a-5a29ed0cd2a1")]
    public readonly InputSlot<Vector3> Target = new();

    [Input(Guid = "314cf0cf-3804-4a48-83b1-1e065aa1006f")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "343cf9ba-aad6-423a-a341-0fb95bd458da")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "6fce9306-80c8-4c94-bb60-bdcd2ff6dd73")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "ca8eaafd-94ee-431e-8f33-d7234a3258e7")]
    public readonly InputSlot<GizmoVisibility> ShowGizmo = new();

    [Input(Guid = "1637524b-a338-4bf7-bc30-ee48f9d9d4f9")]
    public readonly InputSlot<float> GizmoSize = new();
}
