#nullable enable
using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf._;

[Guid("77bfe126-5ea6-4254-ba46-2dd9f77d7a70")]
internal sealed class ExecuteHeightmapSdf : Instance<ExecuteHeightmapSdf>
,IGraphNodeOp
{
    [Output(Guid = "c48b4b20-b70a-44cf-94d3-4abf5bccf830")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public ExecuteHeightmapSdf()
    {
        ShaderNode = new ShaderGraphNode(this);

        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
        _srv = SdfImageSrv.GetValue(context);
        if (_srv == null || _srv.IsDisposed)
            _srv = null;

        // Get all parameters to clear operator dirty flag
    }
    
    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Definitions.Append($$"""
                                   float sdHeightField{{ShaderNode}}(
                                       float3 p,                         // sample position in world space
                                       float heightScale,                // world units per height unit
                                       float2 uvScale,                   // world xz -> uv scale
                                       float2 uvOffset,                  // uv offset
                                       float maxSlope,                    // max slope (Lipschitz constant) in world units
                                       float maxWorldY 
                                   )
                                   {
                                       // Convert world-space xz to UV
                                       float2 uv = p.xz * uvScale + uvOffset;
                                   
                                       // Sample height from texture and scale to world units
                                       float heightValue = {{ShaderNode}}SdfImage.SampleLevel(WrappedSampler, uv, 0) * heightScale;
                                   
                                       // clamp to avoid artifacts above maxWorldY
                                       heightValue = min(heightValue, maxWorldY);
                                       
                                       float delta = p.y - heightValue;
                                       return delta / sqrt(1.0 + maxSlope * maxSlope);
                                   }
                                   """);
    }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        c.AppendCall($"""
                       f{c}.w = sdHeightField{ShaderNode}(
                                 p{c}.xyz, 
                                 {ShaderNode}Scale,
                                 {ShaderNode}UvScale,
                                 {ShaderNode}UvOffset,
                                 {ShaderNode}MaxSlope,
                                 {ShaderNode}MaxHeight
                                 );
                             f{c}.xyz = p{c}.w < 0.5 ?  p{c}.xyz : 1;     
                       """);
        //c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
        return true;
    }

    void IGraphNodeOp.AppendShaderResources(ref List<ShaderGraphNode.SrvBufferReference> list)
    {
        if (_srv == null)
            return;

        // Skip if already added
        foreach (var x in list)
        {
            if (x.Srv == _srv)
                return;
        }

        list.Add(new ShaderGraphNode.SrvBufferReference($"Texture2D<float> {ShaderNode}SdfImage", _srv));
    }

    private ShaderResourceView? _srv;


    [Input(Guid = "868219cf-a768-4aec-8e6a-871c06a8541f")]
    public readonly InputSlot<ShaderResourceView> SdfImageSrv = new();
    
    [GraphParam]
    [Input(Guid = "22f0f06f-703e-4aac-9799-aec26667e1ff")]
    public readonly InputSlot<float> Scale = new();
    
    [GraphParam]
    [Input(Guid = "120ead3f-30d0-44a0-9ef8-a66d17fd977a")]
    public readonly InputSlot<Vector2> UvScale = new();

    [GraphParam]
    [Input(Guid = "486A018A-0C0C-4077-B9D1-F5F1700E6C6A")]
    public readonly InputSlot<Vector2> UvOffset = new();

    [GraphParam]
    [Input(Guid = "96DF0CC2-3BF1-45AA-BEEF-584C73D127CC")]
    public readonly InputSlot<float> MaxSlope = new();
    
    [GraphParam]
    [Input(Guid = "72246279-47F9-42E4-8714-FBA34B687238")]
    public readonly InputSlot<float> MaxHeight = new();
}