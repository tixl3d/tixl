#nullable enable
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.space._;

[Guid("296c774b-1cf0-4e37-9c22-7ac4fd5d78e5")]
internal sealed class ExecuteRepeatFieldAtPoints : Instance<ExecuteRepeatFieldAtPoints>
,IGraphNodeOp
{
    [Output(Guid = "b246c7f7-04dd-4632-aff8-fa0a2c03af4f")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public ExecuteRepeatFieldAtPoints()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);

        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var combineMethod = CombineMethod.GetEnumValue<CombineMethods>(context);
        if (combineMethod != _combineMethod)
        {
            _combineMethod = combineMethod;
            ShaderNode.FlagCodeChanged();
        }
        
        var buffer = Points.GetValue(context);
        _count = buffer?.Srv?.Description != null ? buffer.Srv.Description.Buffer.ElementCount : 0;

        if (buffer != null && buffer.Srv != null)
        {
            _srv = buffer.Srv.IsDisposed ? null : buffer.Srv;
        }
        else
        {
            _srv = null;
        }
        
        ShaderNode.Update(context);
        
        // Get all parameters to clear operator dirty flag
        InputField.DirtyFlag.Clear();
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        // This is initialized in ComputePointTransformMatrix.hlsl
        c.ResourceTypes["PointMatrix"] = """
                                 struct PointTransform
                                 {
                                     float4x4 WorldToPointObject;
                                     float4 PointColor;
                                 };
                                 """;
        
        c.Globals[nameof(ShaderGraphIncludes.GetColorBlendFactor)] = ShaderGraphIncludes.GetColorBlendFactor;
        
        // Register global method
        switch (_combineMethod)
        {

            case CombineMethods.UnionRound:
                c.Globals["fOpUnionRound"]
                    = """
                       // The "Round" variant uses a quarter-circle to join the two objects smoothly:
                       float fOpUnionRound(float a, float b, float r) {
                           float2 u = max(float2(r - a,r - b), 0);
                           return max(r, min (a, b)) - length(u);
                       }
                      """;
                break;
            case CombineMethods.UnionSoft:
                c.Globals["fOpUnionSoft"] = """
                                            float fOpUnionSoft(float a, float b, float r) {
                                            	float e = max(r - abs(a - b), 0);
                                            	return min(a, b) - e*e*0.25/r;
                                            }
                                            """;
                break;
        }
    }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        const int maxSteps = 100;
        
        var fields = ShaderNode.InputNodes;
        if (fields.Count == 0)
            return true;

        var inputField = fields[0];

        c.AppendCall($"float4 pStart{c} = p{c};");
        c.AppendCall($"float4 fLoop{c} = float4(1,1,1,99999);");
        c.AppendCall($"for(int i{c} = 0; i{c} < {_count} && i{c} < {maxSteps}; i{c}++) {{");
        c.Indent();
        {
            c.AppendCall($"f{c} = float4(1,1,1,9999);");
            
            c.AppendCall($"p{c}.xyz = mul(float4(pStart{c}.xyz,1), {ShaderNode}PointTransforms[i{c}].WorldToPointObject).xyz;");
            
            inputField?.CollectEmbeddedShaderCode(c);
            
            // Multiply Point color
            c.AppendCall($"f{c}.rgb *= {ShaderNode}PointTransforms[i{c}].PointColor.rgb;");
            c.AppendCall($"fLoop{c}.rgb = lerp(fLoop{c}.rgb, f{c}.rgb, GetColorBlendFactor(fLoop{c}.w, f{c}.w, {ShaderNode}K ));");
            
            // Combine initial value with new value...
            switch (_combineMethod)
            {
                case CombineMethods.Union:
                    c.AppendCall($"fLoop{c}.w = min(f{c}.w, fLoop{c}.w);");
                    break;
                
                case CombineMethods.UnionSoft:
                    c.AppendCall($"fLoop{c}.w = fOpUnionSoft(fLoop{c}.w, f{c}.w, {ShaderNode}K);");
                    break;

                case CombineMethods.UnionRound:
                    c.AppendCall($"fLoop{c}.w = fOpUnionRound(fLoop{c}.w, f{c}.w, {ShaderNode}K);");
                    break;
            }
        }
        c.Unindent();
        c.AppendCall("}");
        c.AppendCall($"f{c} = fLoop{c};");
        return true;
    }

    void IGraphNodeOp.AppendShaderResources(ref List<ShaderGraphNode.SrvBufferReference> list)
    {
        if (_srv == null)
            return;

        // Skip if already added
        foreach (var x in list)
        {
            if (x.Srv == _srv)
                return;
        }

        list.Add(new ShaderGraphNode.SrvBufferReference($"StructuredBuffer<PointTransform> {ShaderNode}PointTransforms", _srv));
    }
    
    private int _count;
    private ShaderResourceView? _srv; // This will later be passed on to the shader Stage by GenerateShaderCode
    private CombineMethods _combineMethod;

    private enum CombineMethods
    {
        Union,
        UnionSoft,
        UnionRound,
    }

    [Input(Guid = "bb4e6ad8-5941-4218-9e4b-4ba402be7ed4")]
    public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> InputField = new();

    [Input(Guid = "1E5288D2-C2AE-4A1D-AD69-FE63D32A00C6")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new();
    
    [GraphParam]
    [Input(Guid = "9E4F5916-722D-4C4B-B1CA-814958A5B836")]
    public readonly InputSlot<float> K = new();
    
    [Input(Guid = "4648E514-B48C-4A98-A728-3EBF9BCFA0B7", MappedType = typeof(CombineMethods))]
    public readonly InputSlot<int> CombineMethod = new();
    
    
}