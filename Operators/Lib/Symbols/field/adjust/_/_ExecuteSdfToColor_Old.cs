#nullable enable
using T3.Core.DataTypes.ShaderGraph;


namespace Lib.field.adjust._;

[Guid("c0045cbb-7eaf-438e-872e-9e0ba08040a4")]
internal sealed class _ExecuteSdfToColor_Old : Instance<_ExecuteSdfToColor_Old>
,IGraphNodeOp
{
    [Output(Guid = "8325c723-06f0-400a-adf2-a494ea161def")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public _ExecuteSdfToColor_Old()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);

        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Parent == null)
        {
            Log.Warning("Can't initialized SDF to color without parent");
            return;
        }

        // Override to ensure unique prefix id
        ShaderNode.InstanceForPrefixId = Parent;

        ShaderNode.Update(context);
        _srv = GradientSrv.GetValue(context);
        if (_srv == null || _srv.IsDisposed)
            _srv = null;

        // Get all parameters to clear operator dirty flag
        InputField.DirtyFlag.Clear();
    }

    public ShaderGraphNode ShaderNode { get; }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        if (ShaderNode.InputNodes.Count == 0)
            return true;

        var inputNode = ShaderNode.InputNodes[0];
        if (inputNode == null)
            return true;

        inputNode.CollectEmbeddedShaderCode(c);

        // Will be clamped by shader
        c.AppendCall($"float _t{c} = f{c}.w/({ShaderNode}Range.y-{ShaderNode}Range.x) + {ShaderNode}Range.x;");
        c.AppendCall($"f{c} = {ShaderNode}RemapGradient.SampleLevel(ClampedSampler, float2(_t{c}, 0.5),0);");
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

        list.Add(new ShaderGraphNode.SrvBufferReference($"Texture2D<float4> {ShaderNode}RemapGradient", _srv));
    }

    private ShaderResourceView? _srv;

    [Input(Guid = "4a095164-ec1d-4aa6-abb8-7cca0107b45a")]
    public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> InputField = new();

    [Input(Guid = "D4B35B39-659E-4367-A69B-FF77CA74AD5A")]
    public readonly InputSlot<ShaderResourceView> GradientSrv = new();

    [GraphParam]
    [Input(Guid = "BDF570A0-E8F5-4379-93D9-618B53C4ED36")]
    public readonly InputSlot<Vector2> Range = new();
}