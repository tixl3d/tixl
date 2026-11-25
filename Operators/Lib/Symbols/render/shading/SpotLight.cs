namespace Lib.render.shading;

[Guid("9dc0b2de-1f79-4d8b-8d87-6f3764875220")]
internal sealed class SpotLight :Instance<SpotLight>,ITransformable
{
    [Output(Guid = "77faf1c9-34b1-4e6b-9cae-74a523225b10")]
    public readonly TransformCallbackSlot<Command> Output = new();

    public SpotLight()
    {
        Output.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    IInputSlot ITransformable.AimTargetInput => Target;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
        
    [Input(Guid = "31b0cc5b-2351-4b49-8af6-ad74bc793ad9")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "49e9bdd3-6930-41a1-8c25-f94a24dcd61a")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "c10e20ae-ceb8-47d4-bb93-5ac38368210f")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "f3c26d77-de8a-4383-a24b-ab5d1063f62a")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "9c4db6b3-bccf-4045-ad8e-cf200244aa9d")]
    public readonly InputSlot<bool> IsEnabled = new();
        
    [Input(Guid = "a93b2a2d-676c-4dad-a2fc-1433bce4662b")]
    public readonly InputSlot<GizmoVisibility> ShowGizmo = new();
        
    [Input(Guid = "ddf6d27a-4e9b-4097-944f-f1042ef60f3f")]
    public readonly InputSlot<float> GizmoSize = new();

    [Input(Guid = "86c6c87c-5e8d-4e59-b047-2eec0a55059a")]
    public readonly InputSlot<float> Decay = new();

    [Input(Guid = "6889c5f5-3431-4800-ac75-8128b23ce57f")]
    public readonly InputSlot<float> Range = new InputSlot<float>();

        [Input(Guid = "1acd0b18-378a-4337-8089-4e07ddc79989")]
        public readonly InputSlot<System.Numerics.Vector3> Target = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "0d4c855d-4122-4f68-8cd4-a8c30cfa850f")]
        public readonly InputSlot<float> ConeAngle = new InputSlot<float>();

}