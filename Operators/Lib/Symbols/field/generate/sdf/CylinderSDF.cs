using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("67f21840-dc9a-4390-aa45-234ce81c8717")]
internal sealed class CylinderSDF : Instance<CylinderSDF>
                                        , ITransformable
                                        , IGraphNodeOp
{
    [Output(Guid = "e3690acf-e989-4327-8ee9-441bb48ac3a4")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public CylinderSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); // Needed for Gizmo support
        ShaderNode.Update(context);

        var axis = Axis.GetEnumValue<AxisTypes>(context);

        var templateChanged = axis != _axis;
        if (!templateChanged)
            return;

        _axis = axis;
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }

    //public void GetShaderCode(StringBuilder shaderStringBuilder, Dictionary<string, string> globals)
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.Globals["fRoundedCyl"] = """
                                      float fRoundedCyl(float3 p, float3 center, float ra, float rb, float h) {
                                          float2 d = float2(length (p.xz - center.xz) - 2.0*ra+rb, abs(p.y-center.y) - h);
                                          return min(max(d.x,d.y),0.0) + length(max(d,0.0)) - rb;
                                      }
                                      """;
        var a = _axisCodes0[(int)_axis];
        c.AppendCall($"f{c}.w = fRoundedCyl(p{c}.{a}, {ShaderNode}Center.{a}, {ShaderNode}Radius *0.5, {ShaderNode}Rounding, {ShaderNode}Height *0.5 );");
        // c.AppendCall($"f{c}.xyz = p{c}.xyz;");
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
    [Input(Guid = "29d6303f-34ba-4e1e-a910-3366104e26e3")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "04ce1364-f421-4bd1-8f45-60921c8605df")]
    public readonly InputSlot<float> Radius = new();
    
    [GraphParam]
    [Input(Guid = "8780e976-ae85-43fb-a822-def6ea21b407")]
    public readonly InputSlot<float> Height = new();

    [GraphParam]
    [Input(Guid = "c409c9a7-5c45-47f7-a49e-2b0fadcf7892")]
    public readonly InputSlot<float> Rounding = new();
    
    [Input(Guid = "1F65CD48-CAA9-4B25-925C-7CBCC12EABBC", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();

}