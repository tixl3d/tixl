namespace Lib.numbers.vec3;

[Guid("e9dc80e1-8898-4b77-b515-bf6d589302de")]
internal sealed class Vector3Gizmo : Instance<Vector3Gizmo>, ITransformable
{
    [Output(Guid = "75ca1280-f3af-4c08-b19b-90b0eff05fbd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
        
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
    
    public Vector3Gizmo()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var gizmoChanged = ShowGizmo.DirtyFlag.IsDirty;
        
        if (gizmoChanged)
        {
            Result.DirtyFlag.Trigger = ShowGizmo.GetValue(context) 
                                           ? DirtyFlagTrigger.Animated 
                                           : DirtyFlagTrigger.None;
        }
        
        TransformCallback?.Invoke(this, context);
        Result.Value = Position.GetValue(context);
    }
        
    [Input(Guid = "a2025ddf-423f-4683-a4ce-ec8c7bda42ee")]
    public readonly InputSlot<Vector3> Position = new();
    
    [Input(Guid = "059CEEAA-8683-4D6E-ACA3-C95D411663F0")]
    public readonly InputSlot<bool> ShowGizmo = new();
}