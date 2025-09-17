using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("a54e0946-71d0-4985-90bc-184cdb1b6b34")]
internal sealed class TorusSDF : Instance<TorusSDF>
                               , IGraphNodeOp, ITransformable
{
    [Output(Guid = "14cd4d1f-0b9b-43c4-93cc-d730c137cee8")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public TorusSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); // Needed for Gizmo support
        var axis = Axis.GetEnumValue<AxisTypes>(context);
        
        var templateChanged = axis != _axis;
        if (templateChanged)
        {
            _axis = axis;
            ShaderNode.FlagCodeChanged();
        }
       
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["fTorus"]
            = """
              float fTorus(float3 p, float radius, float thickness) {
                  float2 q = float2(length(p.xy) - radius, p.z);
                  return length(q) - thickness;
              }
              """;
    }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var a = _axisCodes0[(int)_axis];
       
        c.AppendCall($"f{c}.w = fTorus(p{c}.{a} - {ShaderNode}Center.{a}, {ShaderNode}Radius, {ShaderNode}Thickness);");
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }

    private readonly string[] _axisCodes0 =
        [
            "yzx",
            "xzy",
            "xyz",
        ];

    private AxisTypes _axis;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    [GraphParam]
    [Input(Guid = "dbc72bd7-6191-4145-a69f-d17b3808b3ab")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "5fe2ab92-f8e5-400d-b5a3-197f20570d6f")]
    public readonly InputSlot<float> Radius = new();

    [GraphParam]
    [Input(Guid = "6a392bc1-2adf-4a50-bb3f-5d4f2a63bf0b")]
    public readonly InputSlot<float> Thickness = new();

    [Input(Guid = "522A9640-CA8C-47E6-AD36-5C316A9092AE", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();
}