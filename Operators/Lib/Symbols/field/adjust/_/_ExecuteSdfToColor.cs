#nullable enable
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.adjust._;

[Guid("5b8daf99-7a4d-4491-a50f-403cb598bca4")]
internal sealed class _ExecuteSdfToColor : Instance<_ExecuteSdfToColor>
,IGraphNodeOp
{
    [Output(Guid = "d127f876-6f4f-4a44-a547-1bba00693d12")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public _ExecuteSdfToColor()
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
        
        
        var mapping = Mapping.GetEnumValue<MappingModes>(context);
        var templateChanged = mapping != _mapping;
        if (templateChanged)
        {
            _mapping = mapping;
            ShaderNode.FlagCodeChanged();
            Log.Debug("Template changed", this);
        }
        
        // Get all parameters to clear operator dirty flag
        InputField.DirtyFlag.Clear();
    }


    public ShaderGraphNode ShaderNode { get; }

    
    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals[nameof(ShaderGraphIncludes.IncludeBiasFunctions)] = ShaderGraphIncludes.IncludeBiasFunctions;
        c.Globals[nameof(ShaderGraphIncludes.Common)] = ShaderGraphIncludes.Common; // For mod
    }
    
    
    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        if (ShaderNode.InputNodes.Count == 0)
            return true;

        var inputNode = ShaderNode.InputNodes[0];
        if (inputNode == null)
            return true;

        inputNode.CollectEmbeddedShaderCode(c);
        
        var mapFunction =  _mapping switch {
            MappingModes.Centered  => $"(f{c}.w + {ShaderNode}Range / 2) / {ShaderNode}Range - {ShaderNode}Offset / ({ShaderNode}Range * 0.5f);",
            MappingModes.FromStart => $"f{c}.w / {ShaderNode}Range - {ShaderNode}Offset;", 
            MappingModes.PingPong  => $"abs(mod((2 * f{c}.w - 2 * {ShaderNode}Offset * {ShaderNode}Range - 1) / {ShaderNode}Range, 2) - 1);",  
            MappingModes.Repeat    => $"mod(f{c}.w / {ShaderNode}Range - 0.5 - {ShaderNode}Offset,1);", 
            _                      => throw new ArgumentOutOfRangeException()
                       };


        // Will be clamped by sampler 
        c.AppendCall($"""
                      float _t{c} = {mapFunction}
                      _t{c} = ApplyGainAndBias(_t{c},{ShaderNode}GainAndBias);
                      f{c} = {ShaderNode}RemapGradient.SampleLevel(ClampedSampler, float2(_t{c}, 0.5),0);
                      """);
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
    
    private MappingModes _mapping;

    private enum MappingModes
    {
        Centered,
        FromStart,
        PingPong,
        Repeat,
    }
    
    [Input(Guid = "82a13817-aa0f-43c5-b165-0a6a936a69b1")]
    public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> InputField = new();

    [Input(Guid = "6c5a8f58-e0a7-4f6f-b061-4a0a5cf1b1a8")]
    public readonly InputSlot<ShaderResourceView> GradientSrv = new();
    
    [Input(Guid = "CC38A4F4-DB1E-4E93-A37B-1C1202B06B0B")]
    public readonly InputSlot<int> Mapping = new();
    
    [GraphParam]
    [Input(Guid = "9EC874C0-468B-4953-B256-326D217DEBD0")]
    public readonly InputSlot<float> Range = new();
    
    [GraphParam]
    [Input(Guid = "BBF50909-089B-4146-9D1A-98F0C5A417FE")]
    public readonly InputSlot<float> Offset = new();

    [GraphParam]
    [Input(Guid = "98722B10-E72F-4440-B1F4-02D3F9740D0F")]
    public readonly InputSlot<Vector2> GainAndBias = new();

}