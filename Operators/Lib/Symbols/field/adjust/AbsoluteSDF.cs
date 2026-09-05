using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.adjust;

[Guid("5b911a78-8cad-4dbd-9f74-afccb0cb0787")]
internal sealed class AbsoluteSDF : Instance<AbsoluteSDF>, IGraphNodeOp
{
    [Output(Guid = "386b568b-0818-4d3e-adb7-d3adc41d3240")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public AbsoluteSDF()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"f{c}.w = abs(f{c}.w);");
    }

    [Input(Guid = "e60a6857-2be5-4236-b506-43c9e84b8d1e")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();
}