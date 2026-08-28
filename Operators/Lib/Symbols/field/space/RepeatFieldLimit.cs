using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.space;

[Guid("409a150a-83c7-4efd-80a0-67bf0345a398")]
internal sealed class RepeatFieldLimit : Instance<RepeatFieldLimit>
,IGraphNodeOp
{
    [Output(Guid = "0aa09119-e59c-437c-beaf-fd4b2b4ee3c6")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public RepeatFieldLimit()
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
        c.Globals["Common"] = ShaderGraphIncludes.Common;

        c.Globals["pModLimited2"] = """
                                    // https://mercury.sexy/hg_sdf/
                                    // Repeat only a few times: from indices <start> to <stop> (similar to above, but more flexible)
                                    float pModLimited2(inout float p, float size, float start, float stop) 
                                    {
                                        float halfsize = size*0.5;
                                        float c = floor((p + halfsize)/size);
                                        p = mod(p+halfsize, size) - halfsize;
                                        if (c > stop) { //yes, this might not be the best thing numerically.
                                    	    p += size*(c - stop);
                                    	    c = stop;
                                        }
                                        if (c <start) {
                                    	    p += size*(c - start);
                                    	    c = start;
                                        }
                                        return c;
                                    }
                                    """;
    }
    
    void IGraphNodeOp.GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var n = ShaderNode;
        c.AppendCall($"pModLimited2(p{c}.{_axisCodes0[(int)_axis]}, {n}Size, {n}Start, {n}Stop);");
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

    [Input(Guid = "073ac201-6be4-495c-9c6d-cfa26c9b1035")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();

    [Input(Guid = "06ea2b77-5c65-40b9-b9e8-6788786bd7db", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();

    [GraphParam]
    [Input(Guid = "3ec4e97c-f653-4b80-8a8e-3292d28dff13")]
    public readonly InputSlot<float> Size = new();
    
    [GraphParam]
    [Input(Guid = "B73FE274-CF9A-4950-A10C-88C9F22B4397")]
    public readonly InputSlot<float> Start = new();
    
    [GraphParam]
    [Input(Guid = "C72615BE-8123-4D3B-BC69-0F912B0FDC39")]
    public readonly InputSlot<float> Stop = new();
}