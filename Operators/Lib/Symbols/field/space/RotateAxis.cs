using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.space;

[Guid("7d9e1b37-5b44-40a3-bd81-281397e76e1a")]
internal sealed class RotateAxis : Instance<RotateAxis>
,IGraphNodeOp
{
    [Output(Guid = "b1730fd1-dbf9-4415-b2ad-e53b3e9c7a96")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public RotateAxis()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);

        var axis = Axis.GetEnumValue<AxisTypes>(context);

        var templateChanged = axis != _axis;
        if (!templateChanged)
            return;

        _axis = axis;

        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["pRotateAxis"] = """
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
        var axi = _axisCodes0[(int)_axis];
        c.AppendCall($"pRotateAxis(p{c}.{axi}, {ShaderNode}Rotation / 180 * 3.141578);");
    }


    private readonly string[] _axisCodes0 =
        [
            "zy",
            "zx",
            "yx",
        ];

    private AxisTypes _axis;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    [Input(Guid = "6204058c-ab34-46ad-af85-db3eeb718970")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [GraphParam]
    [Input(Guid = "5e037357-1a5c-4011-98ed-df061ea890ac")]
    public readonly InputSlot<float> Rotation = new();

    [Input(Guid = "c4ed2ce7-8f83-4999-ae7a-d3bd53ac1ab9", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();
}
