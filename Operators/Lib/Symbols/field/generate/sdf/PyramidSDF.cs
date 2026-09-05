using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("5b8604d0-65f4-45c9-9e85-4a044005a778")]
internal sealed class PyramidSDF : Instance<PyramidSDF>
                                    , ITransformable
                                    , IGraphNodeOp
{
    [Output(Guid = "566667bd-e78b-4971-b567-3e1c3ea68701")]
    public readonly Slot<ShaderGraphNode> Result = new();

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => Scale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public PyramidSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); //needed for Gizmo
        ShaderNode.Update(context);

        var axis = Axis.GetEnumValue<AxisTypes>(context);

        var templateChanged = axis != _axis;
        if (!templateChanged)
            return;

        _axis = axis;
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }
    //Pyramid SDF by TheTurk: https://www.shadertoy.com/view/Ntd3DX
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.Globals["fPyramid"] = """
                                      float fPyramid(float3 p, float3 center, float halfWidth, float halfDepth, float halfHeight, float ra) {
                                      p -= center;
                                      p.y += halfHeight;
                                      p.xz = abs(p.xz);
                                      float3 d1 = float3(max(p.x - halfWidth, 0.0), p.y, max(p.z - halfDepth, 0.0));
                                      float3 n1 = float3(0.0, halfDepth, 2.0 * halfHeight);
                                      float k1 = dot(n1, n1);
                                      float h1 = dot(p - float3(halfWidth, 0.0, halfDepth), n1) / k1;
                                      float3 n2 = float3(k1, 2.0 * halfHeight * halfWidth, -halfDepth * halfWidth);
                                      float m1 = dot(p - float3(halfWidth, 0.0, halfDepth), n2) / dot(n2, n2);
                                      float3 d2 = p - clamp(p - n1 * h1 - n2 * max(m1, 0.0), float3(0., 0., 0.), float3(halfWidth, 2.0 * halfHeight, halfDepth));
                                      float3 n3 = float3(2.0 * halfHeight, halfWidth, 0.0);
                                      float k2 = dot(n3, n3);
                                      float h2 = dot(p - float3(halfWidth, 0.0, halfDepth), n3) / k2;
                                      float3 n4 = float3(-halfWidth * halfDepth, 2.0 * halfHeight * halfDepth, k2);
                                      float m2 = dot(p - float3(halfWidth, 0.0, halfDepth), n4) / dot(n4, n4);    
                                      float3 d3 = p - clamp(p - n3 * h2 - n4 * max(m2, 0.0), float3(0., 0., 0.), float3(halfWidth, 2.0 * halfHeight, halfDepth));
                                      float d = sqrt(min(min(dot(d1, d1), dot(d2, d2)), dot(d3, d3)));
                                      return (max(max(h1, h2), -p.y) < 0.0 ? -d : d) - ra;
                                      }
                                      """;
        var a = _axisCodes0[(int)_axis];
        c.AppendCall($"f{c}.w = fPyramid(p{c}.{a}, {ShaderNode}Center.{a}, {ShaderNode}Scale.x * {ShaderNode}UniformScale, {ShaderNode}Scale.z * {ShaderNode}UniformScale, {ShaderNode}Scale.y * {ShaderNode}UniformScale, {ShaderNode}Rounding);"); 
        //c.AppendCall($"f{c}.xyz = p{c}.xyz;");
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }
    
    public void GetPostShaderCode(CodeAssembleContext cac, int inputIndex)
    {
    }

    private readonly string[] _axisCodes0 =
       [
            "yxz",
            "xyz",
            "xzy",
        ];

    private AxisTypes _axis;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }
    
    [GraphParam]
    [Input(Guid = "92c03574-31e5-4ce2-9a76-ecab5f64d732")]
    public readonly InputSlot<Vector3> Center = new();
    
    [GraphParam]
    [Input(Guid = "8d5c22bb-2910-4415-9bf8-ab304e35a5b2")]
    public readonly InputSlot<Vector3> Scale = new();

    [GraphParam]
    [Input(Guid = "471d9c3b-ba5e-4b73-93cf-fb8ba76bf70e")]
    public readonly InputSlot<float> UniformScale = new();

    [GraphParam]
    [Input(Guid = "5b3e69cf-cb55-4428-b78c-76f4f22cf2ac")]
    public readonly InputSlot<float> Rounding = new();
    
    [Input(Guid = "09320506-1e5c-4da8-b522-9f89c143277f", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();

}