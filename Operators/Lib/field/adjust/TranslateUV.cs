using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.adjust;

[Guid("75286fcf-cb35-4eea-994b-51d628c8cda7")]
internal sealed class TranslateUV : Instance<TranslateUV>
,IGraphNodeOp
{
    [Output(Guid = "d9c97f8e-ea1a-4394-b0d6-b868593e81c4")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public TranslateUV()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.xyz -= p.w < 0.5 ? {ShaderNode}Translation : 0;"); // save local space
        
    }
    
    [Input(Guid = "858e9f77-552e-4751-b5d2-40812414ff7f")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [GraphParam]
    [Input(Guid = "416b6a4b-78f9-422d-85fc-ce383b97f1ba")]
    public readonly InputSlot<Vector3> Translation = new();
}