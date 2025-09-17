using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.space;

[Guid("7b81d100-b5a6-4a0e-8900-0d6418146b41")]
internal sealed class Translate : Instance<Translate>, ITransformable
,IGraphNodeOp
{
    [Output(Guid = "bf2ac5fa-f77c-4f7c-bffb-b2a97cf57971")]
    public readonly Slot<ShaderGraphNode> Result = new();

    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
    
    public Translate()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context);
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"p{c}.xyz -= {ShaderNode}Translation;");
    }
    
    [Input(Guid = "9aa6a5cd-4c0b-45c1-9a82-0f40f8aa43c2")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [GraphParam]
    [Input(Guid = "C872228D-E9CA-4DC1-B6C2-2CA130B2330B")]
    public readonly InputSlot<Vector3> Translation = new();
}