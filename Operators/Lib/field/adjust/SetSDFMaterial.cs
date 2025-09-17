using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.adjust;

[Guid("88926602-4694-4632-9fd7-04c8d6ddd728")]
internal sealed class SetSDFMaterial : Instance<SetSDFMaterial>
                                     , IGraphNodeOp
{
    [Output(Guid = "51c8b9dd-9798-44e6-a9d0-0baecfc9c9a5")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public SetSDFMaterial()
    {
        ShaderNode = new ShaderGraphNode(this, null, SdfField, ColorField);
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
        var subContextIndex = c.ContextIdStack.Count;

        var inputNodeIndex = 0;
        
        // Return base distance (required for blending)
        if (SdfField.HasInputConnections)
        {
            if (ShaderNode.InputNodes.Count <= inputNodeIndex)
            {
                Log.Debug($"undefined inputField node at index {inputNodeIndex}", this);
            }
            else
            {
                ShaderNode.InputNodes[inputNodeIndex]?.CollectEmbeddedShaderCode(c);
                inputNodeIndex++;
            }
        }

        // TODO: This should be extracted into method
        c.AppendCall($"if(p{c}.w > 0.5 && p{c}.w < 1.5) {{");
        c.Indent();

        if (ColorField.HasInputConnections)
        {
            c.PushContext(subContextIndex, "albedo");
            var subContextId = c.ToString();
            if (ShaderNode.InputNodes.Count <= inputNodeIndex)
            {
                Log.Debug($"undefined inputField node at index {inputNodeIndex}", this);
            }
            else
            {
                ShaderNode.InputNodes[inputNodeIndex]?.CollectEmbeddedShaderCode(c);
            }

            c.PopContext();
            c.AppendCall($"f{c}.rgb = f{subContextId}.rgb * {ShaderNode}Color.rgb;");
            //inputNodeIndex++;
        }
        else
        {
            c.AppendCall($"f{c} = float4({ShaderNode}Color.rgb, f{c}.w);");
        }
        
        c.Unindent();
        c.AppendCall("}");

        return true;
    }
    
    
    [Input(Guid = "7c656067-ef12-4990-b094-7f8160a242d1")]
    public readonly InputSlot<ShaderGraphNode> SdfField = new();

    [Input(Guid = "93D0EE54-B2F6-41EA-BBAA-EF06BCA1F1A0")]
    public readonly InputSlot<ShaderGraphNode> ColorField = new();

    [GraphParam]
    [Input(Guid = "D2A64234-C7EF-424B-822C-ADDCE0B84E69")]
    public readonly InputSlot<Vector4> Color = new();
}