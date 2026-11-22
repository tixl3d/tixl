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
        // Switches generated code, so it is not a shader parameter.
        var overrideMaterial = OverrideMaterial.GetValue(context);
        if (overrideMaterial != _overrideMaterial)
        {
            _overrideMaterial = overrideMaterial;
            ShaderNode.FlagCodeChanged();
        }

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

        // [RaymarchField] reads roughness, metal and specular in pass 2 and how much of each is not set in pass 3.
        if (_overrideMaterial)
        {
            c.AppendCall($"else if(p{c}.w > 1.5 && p{c}.w < 2.5) {{ f{c}.rgb = float3({ShaderNode}Roughness, {ShaderNode}Metal, {ShaderNode}Specular); }}");
            c.AppendCall($"else if(p{c}.w > 2.5) {{ f{c}.rgb = 0; }}");
        }

        return true;
    }

    private bool _overrideMaterial;
    
    
    [Input(Guid = "7c656067-ef12-4990-b094-7f8160a242d1")]
    public readonly InputSlot<ShaderGraphNode> SdfField = new();

    [Input(Guid = "93D0EE54-B2F6-41EA-BBAA-EF06BCA1F1A0")]
    public readonly InputSlot<ShaderGraphNode> ColorField = new();

    [GraphParam]
    [Input(Guid = "D2A64234-C7EF-424B-822C-ADDCE0B84E69")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "c12b2d75-b7e2-4470-b18d-378d52cd0601")]
    public readonly InputSlot<bool> OverrideMaterial = new();

    [GraphParam]
    [Input(Guid = "f9750728-9baf-493f-8a40-03674e66789a")]
    public readonly InputSlot<float> Roughness = new();

    [GraphParam]
    [Input(Guid = "627b6724-654a-4418-a400-2d08e8f08c7b")]
    public readonly InputSlot<float> Metal = new();

    [GraphParam]
    [Input(Guid = "f0476922-420a-4f6c-bda8-1f1e9ad3090b")]
    public readonly InputSlot<float> Specular = new();
}