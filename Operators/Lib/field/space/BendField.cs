using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.space;

[Guid("c7ef5f64-2654-47a8-a2ab-30b28446b934")]
internal sealed class BendField : Instance<BendField>
                                , IGraphNodeOp
{
    [Output(Guid = "b4427b7d-f2b8-433f-af97-14c0181fb3d6")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public BendField()
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
        c.Globals["opBend"] = """
                            void opBend(inout float3 p, float k) {
                                k/= (180 / 3.14157892);
                                float c = cos(k * p.x);
                                float s = sin(k * p.x);
                                float2x2  m = float2x2(c, -s, s, c);
                                p = float3(mul(m,p.xz), p.y);
                            }
                            """;
    }
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var axi = _axisCodes0[(int)_axis];
        c.AppendCall($"opBend(p{c}.{axi}, {ShaderNode}Amount);");
    }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
    }
    
    private readonly string[] _axisCodes0 =
        [
            "zyx",
            "zxy",
            "yxz",
        ];

    private AxisTypes _axis;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }    
    
    [Input(Guid = "adaf8efd-47b3-4d4b-9102-d8a3c6a7e34a")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();

    [Input(Guid = "4930DE03-6A81-4403-BB79-5B0A14591F05", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();
    
    [GraphParam]
    [Input(Guid = "c0490245-8f7c-4972-8ded-736883b4e650")]
    public readonly InputSlot<float> Amount = new();
}