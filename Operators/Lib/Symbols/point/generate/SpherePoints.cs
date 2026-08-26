namespace Lib.point.generate;

[Guid("1a241222-200b-417d-a8c7-131e3b48cc36")]
internal sealed class SpherePoints : Instance<SpherePoints>
{

    [Output(Guid = "c20f4675-6387-45da-b14f-8d0a3af5e672")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    // public RadialGPoints()
    // {
    //     OutBuffer.TransformableOp = this;
    // }
    //
    // System.Numerics.Vector3 ITransformable.Translation { get => Center.Value; set => Center.SetTypedInputValue(value); }
    // System.Numerics.Vector3 ITransformable.Rotation { get => System.Numerics.Vector3.Zero; set { } }
    // System.Numerics.Vector3 ITransformable.Scale { get => System.Numerics.Vector3.One; set { } }
    //
    // public Action<ITransformable, EvaluationContext> TransformCallback { get => OutBuffer.TransformCallback; set => OutBuffer.TransformCallback = value; }
        
        
    [Input(Guid = "0b42b3e6-a6fd-4edc-88b1-d91f9c775023")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "0bdc6243-3e52-4b1a-b070-731ed27388c6")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "21140fe1-9fb5-4a79-b03a-7deac242fba2")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "813df416-a783-433c-9645-921c885c9840")]
    public readonly InputSlot<float> StartAngle = new();

    [Input(Guid = "15716b21-9905-4c1e-8330-06afc72552a5")]
    public readonly InputSlot<float> Scatter = new();
}