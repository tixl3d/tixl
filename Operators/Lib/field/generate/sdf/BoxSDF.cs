using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("860da1cd-b341-4bc5-965a-4a9c295831f4")]
internal sealed class BoxSDF : Instance<BoxSDF>
,IGraphNodeOp
{
    [Output(Guid = "9153c53c-0b19-4ce4-b086-e448d78ef032")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public BoxSDF()
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
        c.Globals["fRoundedRect"] = """
                                      float fRoundedRect(float3 p, float3 center, float3 size, float r) {
                                          float3 q = abs(p- center) - size + r;
                                          return length(max(q,0.0)) + min(max(q.x,max(q.y,q.z)),0.0) - r;
                                      }
                                      """;
    }
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.w = fRoundedRect(p{c}.xyz, {ShaderNode}Center, {ShaderNode}Size * {ShaderNode}UniformScale * 0.5, {ShaderNode}EdgeRadius);"); 
        //c.AppendCall($"f{c}.xyz = p{c}.xyz;");
    }

    [GraphParam]
    [Input(Guid = "951b2983-1359-41e4-8fb0-8d97c50ed8d6")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "C4EF07B4-853B-48D4-9ADE-C93EE849071A")]
    public readonly InputSlot<Vector3> Size = new();

    [GraphParam]
    [Input(Guid = "734179fa-aaf8-46d0-b827-e71555dad6a0")]
    public readonly InputSlot<float> UniformScale = new();

    [GraphParam]
    [Input(Guid = "787e5d70-0aba-400f-8616-6ece6c5895bc")]
    public readonly InputSlot<float> EdgeRadius = new();
}