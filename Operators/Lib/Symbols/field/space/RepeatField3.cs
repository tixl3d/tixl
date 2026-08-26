using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.space;

[Guid("8d8345b5-56b6-4220-8845-510bc59db871")]
internal sealed class RepeatField3 : Instance<RepeatField3>
,IGraphNodeOp
{
    [Output(Guid = "54fdc1c9-11bd-4d14-b0c3-b25ace547c23")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public RepeatField3()
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

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["Common"] = ShaderGraphIncludes.Common;

        c.Globals["pMod3"] = """
                                 // https://mercury.sexy/hg_sdf/
                                 void pMod3(inout float3 p, float3 size) {
                                 p = mod(p + size*0.5, size) - size*0.5;
                                 }
                                 """;
    }
    
    void IGraphNodeOp.GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"pMod3(p{c}.xyz, {ShaderNode}Size);");
    }
    

    [Input(Guid = "e264a846-934a-4821-9e85-badf8422a82b")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [GraphParam]
    [Input(Guid = "DD664F46-055E-4BF2-A9D4-317A4CCD2BCF")]
    public readonly InputSlot<Vector3> Size = new();
}