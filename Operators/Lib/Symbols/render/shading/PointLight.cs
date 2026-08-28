namespace Lib.render.shading;

[Guid("9c67a8c8-839f-4f67-a949-08cb38b9dffd")]
internal sealed class PointLight : Instance<PointLight>, ITransformable
{
    [Output(Guid = "32b87a4d-bef3-4646-be76-8f8224ebd5c2")]
    public readonly TransformCallbackSlot<Command> Output = new();

    public PointLight()
    {
        Output.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
        
    [Input(Guid = "55dc52d8-51a6-497a-9624-b118e0e27c65")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "f6d96a01-dc90-49c7-9152-a6a42bb05218")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "98155900-1bb9-427a-9c4e-0988fec806cd")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "ff3442c5-95c8-4bd6-a492-cb4a9a597ea1")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "f3ca8d13-4e24-4718-a59c-6a1b9a2a3c04")]
    public readonly InputSlot<bool> IsEnabled = new();
        
    [Input(Guid = "B5EE1E4B-3C8C-48DF-BBCF-AAC614DE6EC9")]
    public readonly InputSlot<GizmoVisibility> ShowGizmo = new();
        
    [Input(Guid = "3babb43d-afe6-4c34-a4c6-950d1e3971cc")]
    public readonly InputSlot<float> GizmoSize = new();

    [Input(Guid = "d6f6838c-4b36-41a8-86c5-1b1fe5dcece1")]
    public readonly InputSlot<float> Decay = new();

    [Input(Guid = "7d6e2f4b-cd5b-446d-b63e-1fd8780bf0fd")]
    public readonly InputSlot<float> Range = new InputSlot<float>();

}