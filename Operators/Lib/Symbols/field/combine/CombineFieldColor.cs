using System.Diagnostics;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

// ReSharper disable UnusedMember.Local

namespace Lib.field.combine;

[Guid("023fa572-a3e0-4dfc-9b42-79bc1f5c353e")]
internal sealed class CombineFieldColor : Instance<CombineFieldColor>
,IGraphNodeOp
{
    [Output(Guid = "d3293cf6-1e83-4902-958b-b694ede26641")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public CombineFieldColor()
    {
        ShaderNode = new ShaderGraphNode(this, InputFields);
        Result.UpdateAction += Update;
        Result.Value = ShaderNode;
    }

    private void Update(EvaluationContext context)
    {
        var combineMethod = CombineMethod.GetEnumValue<CombineMethods>(context);
        if (combineMethod != _combineMethod)
        {
            _combineMethod = combineMethod;
            ShaderNode.FlagCodeChanged();
        }

        ShaderNode.Update(context);

        // Get all parameters to clear operator dirty flag
        InputFields.DirtyFlag.Clear();
    }
    
    void IGraphNodeOp.GetPostShaderCode(CodeAssembleContext cac, int inputIndex)
    {
        // Just pass along subcontext if not enough connected fields...
        if (ShaderNode.InputNodes.Count <= 1)
        {
            cac.AppendCall("// skipping combine with single or no input...");
            return;
        }

        Debug.Assert(cac.ContextIdStack.Count >= 2);

        var contextId = cac.ContextIdStack[^2];
        var subContextId = cac.ContextIdStack[^1];

        if (inputIndex == 0)
        {
            // Keep initial value
            cac.AppendCall($"f{contextId} = f{subContextId};");
        }
        else
        {
            // // Combine initial value with new value...
            switch (_combineMethod)
            {
                case CombineMethods.Mix:
                    cac.AppendCall($"f{contextId} = lerp(f{contextId},  f{subContextId}, {ShaderNode}K);"); 
                    break;
                
                case CombineMethods.Add:
                    cac.AppendCall($"f{contextId} = f{contextId} +  f{subContextId};"); 
                    break;
                
                case CombineMethods.Multiply:
                    cac.AppendCall($"f{contextId} = f{contextId} * f{subContextId};"); 
                    break;
            
            }
        }
    }

    public ShaderGraphNode ShaderNode { get; }

    private CombineMethods _combineMethod;

    private enum CombineMethods
    {
        Mix,
        Add,
        Multiply,
    }

    [Input(Guid = "494b16a6-320a-4197-b5ae-62dd8a697c7c")]
    public readonly MultiInputSlot<ShaderGraphNode> InputFields = new();

    [GraphParam]
    [Input(Guid = "b9a9ba6a-53b5-463d-8161-6677019e78ca")]
    public readonly InputSlot<float> K = new();

    [Input(Guid = "d17f87db-7500-4780-aece-d8400cfe4da0", MappedType = typeof(CombineMethods))]
    public readonly InputSlot<int> CombineMethod = new();
}