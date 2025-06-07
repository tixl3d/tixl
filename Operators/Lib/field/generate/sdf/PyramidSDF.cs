using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("5b8604d0-65f4-45c9-9e85-4a044005a778")]
internal sealed class PyramidSDF : Instance<PyramidSDF>
,IGraphNodeOp
{
    [Output(Guid = "566667bd-e78b-4971-b567-3e1c3ea68701")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public PyramidSDF()
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
        c.Globals["fPyramid"] = """
                                      float fPyramid(float3 p, float3 center, float h, float r) {
                                                                    
                                          p -= center;
                                          float m2 = h*h + 0.25;
                                          p.xz = abs(p.xz);
                                          p.xz = (p.z>p.x) ? p.zx : p.xz;
                                          p.xz -= 0.5;
                                          float3 q = float3( p.z, h*p.y - 0.5*p.x, h*p.x + 0.5*p.y);
                                          float s = max(-q.x,0.0);
                                          float t = clamp( (q.y-0.5*p.z)/(m2+0.25), 0.0, 1.0 );
                                          float a = m2*(q.x+s)*(q.x+s) + q.y*q.y;
                                          float b = m2*(q.x+0.5*t)*(q.x+0.5*t) + (q.y-m2*t)*(q.y-m2*t);
                                          float d2 = min(q.y,-q.x*m2-q.y*0.5) > 0.0 ? 0.0 : min(a,b);
                                          return sqrt((d2+q.z*q.z)/m2 ) * sign(max(q.z,-p.y)) - r;
                                      }
                                      """;
        var a = _axisCodes0[(int)_axis];
        c.AppendCall($"f{c}.w = fPyramid(p{c}.{a}, {ShaderNode}Center.{a}, {ShaderNode}Height, {ShaderNode}Rounding);"); 
       // c.AppendCall($"f{c}.xyz = p{c}.xyz;");
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
    public readonly InputSlot<float> Height = new();

    [GraphParam]
    [Input(Guid = "5b3e69cf-cb55-4428-b78c-76f4f22cf2ac")]
    public readonly InputSlot<float> Rounding = new();
    
    [Input(Guid = "09320506-1e5c-4da8-b522-9f89c143277f", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();

}