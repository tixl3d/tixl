using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.adjust;

[Guid("0707a816-d118-4419-84d6-7bdcbeee318d")]
internal sealed class InvertSDF : Instance<InvertSDF>
,IGraphNodeOp
{
    [Output(Guid = "7688fcf5-b225-4517-8bc0-9f8f5485d043")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public InvertSDF()
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
        c.AppendCall($"f{c}.w *=-1;");
    }
    
    [Input(Guid = "532b4f4f-59b3-4f1d-8bca-f61c3e6a9e16")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
}