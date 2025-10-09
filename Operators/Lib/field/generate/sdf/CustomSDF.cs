using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("637d00e4-ab63-4fe3-8e63-1e206c728841")]
internal sealed class CustomSDF : Instance<CustomSDF>
                                , IGraphNodeOp
{
    [Output(Guid = "1aaaf637-a2f1-4706-909e-fa4fb102619d")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public CustomSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);

        var code = DistanceFunction.GetValue(context);
        var defines = AdditionalDefines.GetValue(context);

        var templateChanged = code != _code || defines != _defines;
        if (!templateChanged)
            return;

        _code = code;
        _defines = defines;
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Definitions.AppendLine("""
                                 #ifndef mod
                                 #define mod(x, y) ((x) - (y) * floor((x) / (y)))
                                 #endif
                                 """);
        
        c.Definitions.Append(_defines);
        c.Definitions.AppendLine();

        c.Definitions.AppendLine($"float dCustom{ShaderNode}(float3 p, float3 Offset, float A, float B, float C)\n{{");
        c.Definitions.Append(_code);
        c.Definitions.AppendLine();
        c.Definitions.AppendLine("}");
    }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var n = ShaderNode;
        c.AppendCall($"f{c}.w = dCustom{ShaderNode}(p{c}.xyz, {n}Offset, {n}A, {n}B, {n}C);");
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }

    private string _code = string.Empty;
    private string _defines = string.Empty;
    
    [GraphParam]
    [Input(Guid = "64f1812f-7ebd-4231-8a6a-0bbc302bfaff")]
    public readonly InputSlot<Vector3> Offset = new();

    [GraphParam]
    [Input(Guid = "3c366d34-c398-410e-972b-d8cc2baffddb")]
    public readonly InputSlot<float> A = new();

    [GraphParam]
    [Input(Guid = "874ae9c8-5835-4d0c-9bef-253ac75d19b2")]
    public readonly InputSlot<float> B = new();

    [GraphParam]
    [Input(Guid = "56e5d5ec-ec59-4ea0-85c1-1eca3dcb5790")]
    public readonly InputSlot<float> C = new();

    [Input(Guid = "BDE89B93-224C-4A3F-85AB-D85B0401C02A")]
    public readonly InputSlot<string> DistanceFunction = new InputSlot<string>();

    [Input(Guid = "48A0699D-1207-4E18-AB9B-4DA7F77CC7AA")]
    public readonly InputSlot<string> AdditionalDefines = new InputSlot<string>();
}