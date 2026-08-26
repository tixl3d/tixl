using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.sdf;

[Guid("1e1e65cd-3564-45e1-88f8-6cb4b4b18c5a")]
internal sealed class PrismSDF : Instance<PrismSDF>
                                        , ITransformable
                                        , IGraphNodeOp
{
    [Output(Guid = "e3d6161a-68bd-41e6-882c-5092a61fc449")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public PrismSDF()
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
        var sides = Sides.GetEnumValue<SidesType>(context) == SidesType._3 ? 3: 6;

        var templateChanged = axis != _axis || sides != _sides;
        
        if (!templateChanged)
            return;

        _axis = axis;
        _sides = sides;
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        switch (_sides)
        {
            case 3:
                c.Globals["fTriangularPrism"] = """
                                                float fTriangularPrism(float3 p, float r, float l) 
                                                {
                                                    float3 q = abs(p);
                                                    return max(q.z-l,max(q.x*0.866025+p.y*0.5,-p.y)-r*0.5);
                                                }
                                                """;
                break;
            case 6:
                c.Globals["fHexPrism"] = """
                                         // h is radius and length
                                         float fHexPrism(float3 p, float r, float l, float round) 
                                         { 
                                             const float3 k = float3(-0.8660254, 0.5, 0.57735);
                                         
                                             p = abs(p);
                                             p.xy -= 2.0*min(dot(k.xy, p.xy), 0.0)*k.xy;
                                             float2 d = float2(length(p.xy-float2(clamp(p.x,-k.z * r, k.z * r), r))*sign(p.y - r),p.z - l);
                                             return min(max(d.x,d.y),0.0) + length(max(d,0.0))-round;
                                         };
                                         """;
                break;
        }
    }
    
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var n = ShaderNode;
        var a = _axisCodes0[(int)_axis];
        
        switch (_sides)
        {
            case 3:
                c.AppendCall($"f{c}.w = fTriangularPrism(p{c}.{a} - {n}Center.{a}, {n}Radius * 0.5, {n}Length * 0.5);");
                break;
            
            case 6:
                c.AppendCall($"f{c}.w = fHexPrism(p{c}.{a} - {n}Center.{a}, {n}Radius *0.5, {n}Length * 0.5, {n}EdgeRadius);");
                break;
        }
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }
    

    private readonly string[] _axisCodes0 =
        [
            "yzx",
            "xzy",
            "xyz",
        ];

    private AxisTypes _axis;
    private int _sides = 3;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    private enum SidesType
    {
        _3,
        _6,
    }

    [GraphParam]
    [Input(Guid = "cfc6f8f3-94b4-46f6-9501-11d7b7916bb7")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "EF023A17-CD92-47C5-B2FD-4DB7AC4BEBAC")]
    public readonly InputSlot<float> Radius = new();

    [GraphParam]
    [Input(Guid = "DD33F05B-6C98-4740-A218-A0E8A4922AC3")]
    public readonly InputSlot<float> Length = new();
    
    [GraphParam]
    [Input(Guid = "356E85DD-BF81-4A1C-96A4-1D2983916A4B")]
    public readonly InputSlot<float> EdgeRadius = new();
    
    [Input(Guid = "B5E445DF-88D0-4B1F-A583-3C4EA83D6526", MappedType = typeof(SidesType))]
    public readonly InputSlot<int> Sides = new();

    [Input(Guid = "522A9640-CA8C-47E6-AD36-5C316A9092AE", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();


}