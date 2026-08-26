using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("cc6100a4-73ed-45e4-87ab-601e7956fb36")]
internal sealed class RotatedPlaneSDF : Instance<RotatedPlaneSDF>
,ITransformable
,IGraphNodeOp

{
    [Output(Guid = "b42a945e-9842-4f76-8f1f-bb73cfd14be4")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public RotatedPlaneSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); // Needed for Gizmo support
        ShaderNode.Update(context);
        
        ShaderNode.FlagCodeChanged();     
    }

    public ShaderGraphNode ShaderNode { get; }
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"""
                      f{c}.w = dot(p{c}.xyz - {ShaderNode}Center, normalize({ShaderNode}Normal));
                      f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;
                      """);
        //c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }

    
    [GraphParam]
    [Input(Guid = "1a154ff5-6fa7-4ee1-9b1e-99c5d47e9493")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "BFED14A7-7A97-4379-9419-222BB7A2BA57")]
    public readonly InputSlot<Vector3> Normal = new();
}