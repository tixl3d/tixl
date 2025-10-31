using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("fdb85ece-201b-49dc-899b-f044a98ac414")]
internal sealed class OctahedronSDF : Instance<OctahedronSDF>
                                    , ITransformable
                                    , IGraphNodeOp

{
    [Output(Guid = "1cda6720-72cb-4aeb-a665-2fc35d787539")]
    public readonly Slot<ShaderGraphNode> Result = new();

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public OctahedronSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); //needed for Gizmo
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    //Octahedron SDF by TheTurk (check comments): https://www.shadertoy.com/view/wsSGDG
    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["fsdOctahedron"] = """
                                      float fsdOctahedron(float3 p, float3 center, float s, float ra) {
                                          p -= center;
                                          p = abs(p);
                                          float m = (p.x + p.y + p.z - s) / 3.0;
                                          float3 o = p - m;
                                          float3 k = min(o, 0.0);
                                          o = o + (k.x + k.y + k.z) * 0.5 - k * 1.5;
                                          o = clamp(o, 0.0, s); 
                                          return length(p - o) * sign(m) - ra;
                                      }
                                      """;
    }
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.w = fsdOctahedron(p{c}.xyz, {ShaderNode}Center, {ShaderNode}Size, {ShaderNode}EdgeRadius);");
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); 
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