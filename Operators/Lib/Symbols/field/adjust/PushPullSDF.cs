#nullable enable
using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.adjust;

[Guid("0ff18a32-c1e9-406d-b3e3-23700e2453a0")]
internal sealed class PushPullSDF : Instance<PushPullSDF>
,IGraphNodeOp
{
    [Output(Guid = "ffbb6690-d57e-4dea-8aa7-fbf0101b9c96")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public PushPullSDF()
    {
        ShaderNode = new ShaderGraphNode(this, null, SdfField, AmountField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);

    }

    public ShaderGraphNode ShaderNode { get; }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        var fields = ShaderNode?.InputNodes;
        if (fields == null || fields.Count == 0)
            return true;
        
        var inputField = fields.Count > 0 ? fields[0] : null;
        var amountField = fields.Count > 1 ? fields[1] : null;
        
        inputField?.CollectEmbeddedShaderCode(c);
        
        c.AppendCall("{");
        c.Indent();

        if (amountField !=null)
        {
            c.PushContext(c.ContextIdStack.Count, "amount");
            var subContextId = c.ToString();
            amountField.CollectEmbeddedShaderCode(c);
            c.PopContext();
            
            c.AppendCall($"f{c}.w += f{subContextId}.r *{ShaderNode}Amount;");
            c.AppendCall($"f{c}.w /= 1 + {ShaderNode}StepScale;");
        }
        else
        {
            c.AppendCall($"f{c}.w += {ShaderNode}Amount;");
        }
        
        c.Unindent();
        c.AppendCall("}");

        return true;
    }
    
    
    [Input(Guid = "c57d91b6-a26a-4e69-be1f-dc04f86594b6")]
    public readonly InputSlot<ShaderGraphNode> SdfField = new();

    [Input(Guid = "91C09FAC-0B50-4F62-97CA-18461801B442")]
    public readonly InputSlot<ShaderGraphNode> AmountField = new();
    
    [GraphParam]
    [Input(Guid = "9cd1120d-81fa-43ab-b023-c462fe651ffb")]
    public readonly InputSlot<float> Amount = new();
    
    [GraphParam]
    [Input(Guid = "463149FA-B53B-4D43-B4E7-57DCB045BBA0")]
    public readonly InputSlot<float> StepScale = new();    
}