using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.sdf;

[Guid("f393bb49-bea5-42f7-a437-c6896bdfd7d8")]
internal sealed class BoxFrameSDF : Instance<BoxFrameSDF>
,ITransformable
,IGraphNodeOp
{
    [Output(Guid = "10bd32e8-f7a1-4efa-9659-83313c2bf5a6")]
    public readonly Slot<ShaderGraphNode> Result = new();

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => Size;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
    
    public BoxFrameSDF()
    {
        ShaderNode = new ShaderGraphNode(this);
        
        ShaderNode.AdditionalParameters = [
                new ShaderGraphNode.Parameter("float3", "CombinedScale", Vector3.One),
            ];        
        
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var combinedScale = Size.GetValue(context) * UniformScale.GetValue(context) /2;
        ShaderNode.AdditionalParameters[0].Value = combinedScale;
        
        TransformCallback?.Invoke(this, context);
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }


    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["fBoxFrame"] = """
                                      float fBoxFrame(float3 p, float3 center, float3 b, float e) {
                                           p = abs(p-center  )-b;
                                           float3 q = abs(p+e)-e;
                                      return min(min(
                                          length(max(float3(p.x,q.y,q.z),0.0))+min(max(p.x,max(q.y,q.z)),0.0),
                                          length(max(float3(q.x,p.y,q.z),0.0))+min(max(q.x,max(p.y,q.z)),0.0)),
                                          length(max(float3(q.x,q.y,p.z),0.0))+min(max(q.x,max(q.y,p.z)),0.0));
                                      }
                                      """;
    }
    
    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.w = fBoxFrame(p{c}.xyz, {ShaderNode}Center, {ShaderNode}CombinedScale, {ShaderNode}Thickness);");
        c.AppendCall($"f{c}.xyz = p.w < 0.5 ?  p{c}.xyz : 1;"); // save local space
    }

    [GraphParam]
    [Input(Guid = "c811ef83-e183-4b14-a1e6-3ea2fd840596")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "993a74e6-87fc-4193-9dc1-8cd9447a1c49")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "a53ab420-7bcf-4d98-8440-abcf00d7db19")]
    public readonly InputSlot<float> UniformScale = new();

    [GraphParam]
    [Input(Guid = "26389c32-410e-4240-9cf7-9ed0908b9cf8")]
    public readonly InputSlot<float> Thickness = new();
}