namespace Lib.render.gizmo;

[Guid("348652c3-abf5-4fe9-873b-89d1acaaf0ff")]
internal sealed class Locator : Instance<Locator>, ITransformable
{
    [Output(Guid = "357c4c25-2b08-4470-84b1-9707a3d8e56e")]
    public readonly TransformCallbackSlot<Command> Output = new();

    [Output(Guid = "7599cf50-cbb6-49ce-bb41-9f709b593b0b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Pos = new();
        
    [Output(Guid = "0986BEB0-16AB-4836-9071-87052AB19602", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> DistanceToCamera = new ();
        
    public Locator()
    {
        Output.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "53aeef4f-37f8-40a6-b552-cb35f5fb887c")]
    public readonly InputSlot<Vector3> Position = new InputSlot<Vector3>();

    [Input(Guid = "43f63f2d-72f0-4d73-9311-fba7e4e32b31")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "d7b88971-a4fb-4761-a2a2-0abeffd01552")]
    public readonly InputSlot<float> Thickness = new InputSlot<float>();

    [Input(Guid = "2a66b445-49c2-43fd-857f-71224d8bcb39")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "f7a4cab5-6095-4b7f-98d0-faea091dda29")]
    public readonly InputSlot<string> Label = new InputSlot<string>();

    [Input(Guid = "fc366e00-4980-4b3e-85a3-b83bff210458")]
    public readonly InputSlot<GizmoVisibility> Visibility = new InputSlot<GizmoVisibility>();

}