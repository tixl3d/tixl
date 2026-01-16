using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.space;

[Guid("D7C6E980-6766-4F83-A32C-677E3713E8A6")]
internal sealed class TwistField : Instance<TwistField>
                                 , IGraphNodeOp
{
    [Output(Guid = "0125A816-2885-4E83-97F9-357E24F8AFC3")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public TwistField()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
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
        c.Globals["opTwist"] = """
                               void opTwist(inout float3 p, float k) {
                                 k/= (180 / 3.14157892);
                                   float c = cos(k * p.z);
                                   float s = sin(k * p.z);
                                   float2x2  m = float2x2(c,-s,s,c);
                                   p = float3(mul(m,p.xy), p.z);
                               }
                               """;
    }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var axi = _axisCodes[(int)_axis];
        c.AppendCall($"opTwist(p{c}.{axi}, {ShaderNode}Amount);");
    }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.w *= {ShaderNode}StepFactor; ");
    }
    
    private AxisTypes _axis;

    private readonly string[] _axisCodes =
        [
            "yzx",
            "xzy",
            "xyz",
        ];

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    [Input(Guid = "84DD59CE-4726-4604-AFEE-92FFDEC0E3F5")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
    
    [GraphParam]
    [Input(Guid = "4935F569-8D23-495D-BB87-9A39E5B3981E")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "195F4642-52F7-4C95-9127-07F6175549F5", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();
    
    [GraphParam]
    [Input(Guid = "AD43A4C4-3439-46C5-986D-C654A56D296A")]
    public readonly InputSlot<float> StepFactor = new();
}