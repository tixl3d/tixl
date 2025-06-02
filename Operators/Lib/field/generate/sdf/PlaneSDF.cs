using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("883e01f5-44ee-4724-9e6e-f885255c17e5")]
internal sealed class PlaneSDF : Instance<PlaneSDF>
                               , IGraphNodeOp
{
    [Output(Guid = "82527072-beb2-492f-b737-faf9ed454e3f")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public PlaneSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
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
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var a = _axisCodes0[(int)_axis];
        c.AppendCall($"f{c}.w = p{c}.{a} + {ShaderNode}Center.{a};");
    }

    private readonly string[] _axisCodes0 =
        [
            "x",
            "y",
            "z",
        ];

    private AxisTypes _axis;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    [GraphParam]
    [Input(Guid = "76D4C422-399A-4B68-B0EA-5D2D2F54A667")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "DEB91DB5-CEFB-4E0D-9244-D44BC6A21985")]
    public readonly InputSlot<Vector2> Size = new();

    [Input(Guid = "99FCA01A-E0A6-49DC-9535-EBDE0F5F8744", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();
}