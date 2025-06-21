using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("b85198a8-6475-45e1-b2d2-83ec8f59d6ed")]
internal sealed class CappedTorusSDF : Instance<CappedTorusSDF>
                                        , ITransformable
                                        , IGraphNodeOp
{
    [Output(Guid = "170b545f-c7c0-47d9-8b19-d8d8e8a3d2fa")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public CappedTorusSDF()
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
        c.Globals["fCappedTorus"] = """
                                    float fCappedTorus(float3 p, float3 size, float ra, float rb) 
                                    {
                                        float an = 2.5*(0.5+0.5*(size*1.1+3.0));
                                        float2 sc = float2(sin(an),cos(an));
                                    
                                        p.x = abs(p.x);
                                        float k = (sc.y*p.x>sc.x*p.y) ? dot(p.xy,sc) : length(p.xy);
                                        return sqrt(dot(p,p) + ra*ra - 2.0*ra*k ) - rb;
                                    }
                                    """;

        var n = ShaderNode;
        c.AppendCall($"f{c}.w = fCappedTorus(p{c}.xyz - {n}Center, {n}Fill, {n}Radius, {n}Thickness);");
    }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
    }


    [GraphParam]
    [Input(Guid = "f5a24e80-741a-49c5-9a0b-d7074e80940a")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "d37914dd-7bbd-47fa-ad90-9921621afb9e")]
    public readonly InputSlot<float> Fill = new();

    [GraphParam]
    [Input(Guid = "00cec9de-3be4-403f-8677-dcc2ef473708")]
    public readonly InputSlot<float> Radius = new();

    [GraphParam]
    [Input(Guid = "e1dc5cdc-24d2-409f-9ea6-6dd14fdb70d3")]
    public readonly InputSlot<float> Thickness = new();
}