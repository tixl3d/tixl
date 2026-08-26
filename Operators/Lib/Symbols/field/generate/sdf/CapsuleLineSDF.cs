using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("752e4358-05e0-4651-a147-f141138f5244")]
internal sealed class CapsuleLineSDF : Instance<CapsuleLineSDF>
                                     , IGraphNodeOp, ITransformable
{
    [Output(Guid = "1d6a73c3-1a6a-452a-a4a2-ddee308aa0ae")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => EndPoint;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public CapsuleLineSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); // Needed for Gizmo support
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.Globals["fCapsule"] = """
                                float fCapsule(float3 p, float3 a, float3 b, float r) {
                                    float3 pa = p - a;
                                    float3 ba = b - a;
                                    float h = clamp( dot(pa,ba)/dot(ba,ba), 0.0, 1.0 );
                                    return length( pa - ba*h ) - r;
                                } 
                                """;

        c.AppendCall($"f{c}.w = fCapsule(p{c} - {ShaderNode}Center, {ShaderNode}StartingPoint, {ShaderNode}EndPoint, {ShaderNode}Thickness);");
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
    }

    [GraphParam]
    [Input(Guid = "50A47EA6-A941-4FF4-9B87-14DDD2791FE4")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "ace2fa64-382c-4386-b623-5b1051f4bcf4")]
    public readonly InputSlot<Vector3> StartingPoint = new();

    [GraphParam]
    [Input(Guid = "0B4FDE07-5439-4DBD-AF37-32E5C6DC9315")]
    public readonly InputSlot<Vector3> EndPoint = new();

    [GraphParam]
    [Input(Guid = "808C44FF-44E2-4A22-91F1-F459F298E6EB")]
    public readonly InputSlot<float> Thickness = new();
}