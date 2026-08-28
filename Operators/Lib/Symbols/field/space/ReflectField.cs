using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.space;

[Guid("1a933eb2-a89b-4694-ae04-60d86779cf28")]
internal sealed class ReflectField : Instance<ReflectField>
,IGraphNodeOp
{
    [Output(Guid = "47166966-b53a-4b85-ac62-a6f956b7de23")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public ReflectField()
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
        c.Globals["pReflect"] = """
                                // https://mercury.sexy/hg_sdf/
                                // Reflect space at a plane
                                void pReflect(inout float3 p, float3 planeNormal, float offset) {
                                	float t = dot(p, planeNormal)+offset;
                                	if (t < 0) {
                                		p = p - (2*t)*planeNormal;
                                	}
                                	//return sgn(t);
                                }
                                """;
    }
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"pReflect(p{c}.xyz, normalize({ShaderNode}PlaneNormal), {ShaderNode}Offset);");
    }
    
    [Input(Guid = "c617549d-b67d-454c-91f5-bc1d6e9a66b7")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [GraphParam]
    [Input(Guid = "AFD3834B-9EF0-4D7A-B0A3-A93CB4193D40")]
    public readonly InputSlot<Vector3> PlaneNormal = new();
    
    [GraphParam]
    [Input(Guid = "D184A675-A5AB-42F9-B3F9-D275B0C7BEE8")]
    public readonly InputSlot<float> Offset = new();
}