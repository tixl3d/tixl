using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.texture;

[Guid("edfc71fc-2d54-4226-b819-0340bb1fdd65")]
internal sealed class Raster3dField : Instance<Raster3dField>
,IGraphNodeOp
{
    [Output(Guid = "096ef8a1-c5bb-4cc1-a4a9-fdc8ab1214ae")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public Raster3dField()
    {
        ShaderNode = new ShaderGraphNode(this, null);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["Common"] = ShaderGraphIncludes.Common;
        c.Globals["CommonHgSdf"] = ShaderGraphIncludes.CommonHgSdf;
        
        c.Globals["fRaster3d"] = """
                                 float fRaster3d(float3 p, float3 center, float3 size, float lineWidth, float feather) 
                                 {
                                     float3 q = mod(p / size - center, 1) - 0.5;
                                     float distanceToEdge = vmax(abs(q));
                                     float line2 = smoothstep(lineWidth / 2 + feather, lineWidth / 2 - feather, distanceToEdge);
                                     return line2;
                                 }
                                 """;
    }
    

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var n = ShaderNode;
        c.AppendCall($"f{c}.rgb = lerp({n}ColorA.rgb, {n}ColorB.rgb, fRaster3d(p{c}.xyz, {n}Offset, {n}Scale, {n}LineWidth, {n}Feather));"); 
    }

    [GraphParam]
    [Input(Guid = "D3D51C3C-9DD7-4F9B-849D-59E94ABFF605")]
    public readonly InputSlot<Vector4> ColorA = new();

    [GraphParam]
    [Input(Guid = "21092A7F-01B8-47B4-BA37-C0B1DC6AFFC4")]
    public readonly InputSlot<Vector4> ColorB = new();
    
    [GraphParam]
    [Input(Guid = "3938188b-41ba-4efe-b7e1-9720d2e58cd4")]
    public readonly InputSlot<Vector3> Offset = new();

    [GraphParam]
    [Input(Guid = "9b324ca4-2116-489d-a829-8348a9984235")]
    public readonly InputSlot<Vector3> Scale = new();

    [GraphParam]
    [Input(Guid = "606584e3-2c4a-432f-a7a4-c3093a34685e")]
    public readonly InputSlot<float> LineWidth = new();
    
    [GraphParam]
    [Input(Guid = "1108EC25-8D77-4C0B-8F17-5308EA017DF4")]
    public readonly InputSlot<float> Feather = new();
}