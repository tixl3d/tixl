using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.space;

[Guid("1d9f133d-eba6-4b28-9dfd-08f6c5417ed6")]
internal sealed class RepeatPolar : Instance<RepeatPolar>
,IGraphNodeOp
{
    [Output(Guid = "de78d5d8-b232-44f6-ab18-cc765f81eb38")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public RepeatPolar()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);

        var axis = Axis.GetEnumValue<AxisTypes>(context);
        var mirror = Mirror.GetValue(context);

        var templateChanged = axis != _axis || mirror != _useMirror;
        if (!templateChanged)
            return;

        _axis = axis;
        _useMirror = mirror;
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["Common"] = ShaderGraphIncludes.Common;

        if (_useMirror)
        {
            c.Globals["pModPolarMirror"] = """
                                     // https://mercury.sexy/hg_sdf/
                                     void pModPolarMirror(inout float2 p, float repetitions, float offset) {
                                         float angle = 2.0 * PI / repetitions;
                                         float a = atan2(p.y, p.x) + angle / 2.0 + offset / (180.0 * PI);
                                         float r = length(p);
                                         float c = floor(a / angle);
                                         a = mod(a, angle) - angle / 2.0;
                                         
                                         // Flip every second repetition by mirroring the angle
                                         if (mod(c, 2.0) >= 1.0) {
                                             a = -a;
                                         }
                                         
                                         p = float2(cos(a), sin(a)) * r;
                                     }
                                     """;            
        }
        else
        {
            c.Globals["pModPolar"] = """
                                     // https://mercury.sexy/hg_sdf/
                                     void pModPolar(inout float2 p, float repetitions, float offset) {
                                         float angle = 2*PI/repetitions;
                                         float a = atan2(p.y, p.x) + angle/2. +  offset / (180 *PI);
                                         float r = length(p);
                                         float c = floor(a/angle);
                                         a = mod(a,angle) - angle/2.;
                                         p = float2(cos(a), sin(a))*r;
                                     }
                                     """;
        }
    }
    
    void IGraphNodeOp.GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        if (_useMirror)
        {
            c.AppendCall($"pModPolarMirror(p{c}.{_axisCodes0[(int)_axis]}, {ShaderNode}Repetitions, {ShaderNode}Offset);");
        }
        else
        {
            c.AppendCall($"pModPolar(p{c}.{_axisCodes0[(int)_axis]}, {ShaderNode}Repetitions, {ShaderNode}Offset);");
        }
    }


    private readonly string[] _axisCodes0 =
        [
            "zy",
            "zx",
            "yx",
        ];

    private AxisTypes _axis;
    private bool _useMirror;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    [Input(Guid = "7248C680-7279-4C1D-B968-3864CB849C77")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();

    [Input(Guid = "02E4130F-8A0C-4EFB-B75F-F7DA29CC95EB", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();

    [GraphParam]
    [Input(Guid = "b4c551a3-28c1-418a-83b4-ebdd61ed599c")]
    public readonly InputSlot<float> Repetitions = new();

    [GraphParam]
    [Input(Guid = "A0231B91-8AB8-4591-A3DA-3CD7F3980D2F")]
    public readonly InputSlot<float> Offset = new();
    
    [Input(Guid = "57F25302-EA5E-40AB-9C54-9D7C3411E467")]
    public readonly InputSlot<bool> Mirror = new();
}