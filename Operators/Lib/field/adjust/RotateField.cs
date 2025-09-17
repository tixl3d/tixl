using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.adjust;

[Guid("6ca6420a-4c05-4c33-81a3-1a9c4872e29d")]
internal sealed class RotateField : Instance<RotateField>
,IGraphNodeOp
{
    [Output(Guid = "7c83325a-5aca-4bb0-865f-5de4d4908f26")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public RotateField()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        
        ShaderNode.AdditionalParameters = [
                new ShaderGraphNode.Parameter("float3", "RotateRad", Vector3.Zero),
            ];        
        
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.AdditionalParameters[0].Value = Rotation.GetValue(context) * MathUtils.ToRad;
        
        ShaderNode.Update(context);
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["pRotateXYZ"] = """
                                   // Rotate around a coordinate axis (i.e. in a plane perpendicular to that axis) by angle <a>.
                                   // Read like this: R(p.xz, a) rotates "x towards z".
                                   // This is fast if <a> is a compile-time constant and slower (but still practical) if not.
                                   void pRotateAxis(inout float2 p, float a) {
                                    p = cos(a)*p + sin(a) * float2(p.y, -p.x);
                                   }
                                   """;
    }
    
    void IGraphNodeOp.GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"""
                      pRotateAxis(p{c}.zy, {ShaderNode}RotateRad.x);
                      pRotateAxis(p{c}.zx, {ShaderNode}RotateRad.y);
                      pRotateAxis(p{c}.yx, {ShaderNode}RotateRad.z);
                      """);
    }
    
    [Input(Guid = "2e930b97-159d-4e29-ac28-1ff5c796820f")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [Input(Guid = "B9BC095A-084A-4616-A3AA-9616E82BA03B")]
    public readonly InputSlot<Vector3> Rotation = new();
}
