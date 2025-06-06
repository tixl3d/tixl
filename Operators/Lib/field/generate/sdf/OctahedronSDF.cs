using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("fdb85ece-201b-49dc-899b-f044a98ac414")]
internal sealed class OctahedronSDF : Instance<OctahedronSDF>
,IGraphNodeOp
{
    [Output(Guid = "1cda6720-72cb-4aeb-a665-2fc35d787539")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public OctahedronSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
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
        c.Globals["fsdOctahedron"] = """
                                      float fsdOctahedron(float3 p, float3 center, float s, float ra) {
                                          p = abs(p);
                                          float m = p.x + p.y + p.z - s;
                                          float3 r = 3.0 * p - m;
                                          float3 o = min(r, 0.0);
                                          o = max(r*2.0 - o*3.0 + (o.x+o.y+o.z), 0.0);
                                          return length(p - s*o/(o.x+o.y+o.z))-ra;
                                      }
                                      """;
    }
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.w = fsdOctahedron(p{c}.xyz, {ShaderNode}Center, {ShaderNode}Size, {ShaderNode}EdgeRadius);"); 

    }
    
    [GraphParam]
    [Input(Guid = "2811b696-3d09-46f7-84ac-045eb7fe82b0")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "23012866-4820-4072-8cd8-82d6bdedf13d")]
    public readonly InputSlot<float> Size = new();

    [GraphParam]
    [Input(Guid = "921ec8f2-cd50-459b-8f0a-990de5deea64")]
    public readonly InputSlot<float> EdgeRadius = new();
}