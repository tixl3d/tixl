using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.use;

[Guid("48136477-cb34-416c-a968-7c9d82da9d9b")]
internal sealed class SdfToVector : Instance<SdfToVector>
                                  , IGraphNodeOp
{
    [Output(Guid = "8055c3e2-0830-4795-ad60-16d8716209fc")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public SdfToVector()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
        ShaderNode.FlagCodeChanged();
    }

    public ShaderGraphNode ShaderNode { get; }
    
    /// <remarks>
    /// This method is more complex than might be obvious:
    /// - We need to sample the field at 4 + 1 positions.
    /// - But we _only_ want to include incoming shader code once.
    /// - The only solution is to define and wrap the shader code inside a method.
    /// - But the incoming shader code must be collected in THIS call through CollectEmbeddedShaderCode()
    /// - To separate the sub shader code for our method, we have to introduce a stack of StringBuilders to 
    ///   push and keep the previous StringBuilders and then pop and convert the temporary ones into a string.
    /// - This result can then be used to define an instance method with that code...
    /// - which can then be called from the actual shader lines.
    ///
    /// This hasn't been extensively tested. But it should be.
    /// </remarks>
    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        var fields = ShaderNode.InputNodes;
        if (fields.Count == 0)
            return true;

        c.PushMethod("GetDistance");
        
        var inputField = fields[0];
        inputField?.CollectEmbeddedShaderCode(c);
        
        var nestedCode = c.PopMethod();

        c.Definitions.Append($$"""
                               float {{c}}GetSdfDistance(float3 p3) 
                               {
                                   float4 p{{c}} = float4(p3, 0);
                                   float4 f{{c}} = 1;
                                   {{nestedCode}}
                                   return f{{c}}.w;
                               } 
                               """);
        
        var offset = $"{ShaderNode}LookUpDistance";
        
        c.AppendCall($"""
                          f{c}.xyz = normalize(
                              {c}GetSdfDistance(p{c}.xyz + float3({offset}, -{offset}, -{offset})) * float3(1, -1, -1) +
                              {c}GetSdfDistance(p{c}.xyz + float3(-{offset}, {offset}, -{offset})) * float3(-1, 1, -1) +
                              {c}GetSdfDistance(p{c}.xyz + float3(-{offset}, -{offset}, {offset})) * float3(-1, -1, 1) +
                              {c}GetSdfDistance(p{c}.xyz + float3({offset}, {offset}, {offset})) * float3(1, 1, 1));
                              f{c}.w = GetSdfDistance(p{c}.xyz);
                       """);
        return true;
    }
    
    [Input(Guid = "d6664c35-040a-4021-ac37-aa7eaaae2b65")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();

    [GraphParam]
    [Input(Guid = "2ea69efd-a01b-4a7f-8f87-31c8dfefcad5")]
    public readonly InputSlot<float> LookUpDistance = new();
}