using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.ShaderGraph;

namespace Lib.volume.fields;

/// Exposes a NanoVDB level set as a signed-distance field, so TiXL's existing field operators work on it. Feed [Grid] from [LoadNanoVdb] or [VolumeToNanoVdb], and map [BoundsMin]/[BoundsMax] from the same operator's world bounds. The result plugs into [RaymarchField], [FieldVolumeForce], [FieldDistanceForce], [SampleFieldPoints], [SDFToColor] and anything else that consumes a scalar field.
[Guid("d8f932c2-dde0-4c24-a073-99de3829f91a")]
internal sealed class VdbToSdfField : Instance<VdbToSdfField>, IGraphNodeOp
{
    [Output(Guid = "fa70337b-0461-4d3e-aecf-7230c37f3154")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public VdbToSdfField()
    {
        ShaderNode = new ShaderGraphNode(this, null);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var srv = Grid.GetValue(context)?.Srv;
        if (srv != null && srv.IsDisposed)
            srv = null;

        // Only AppendShaderResources declares the buffer. A grid that arrives after the first
        // compile was skipped there, so the code has to be regenerated when it appears.
        if (!ReferenceEquals(srv, _srv))
        {
            _srv = srv;
            ShaderNode.FlagCodeChanged();
        }

        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals[VdbFieldCode.CommonKey] = VdbFieldCode.CommonInclude;
        c.Globals[VdbFieldCode.OutsideKey] = VdbFieldCode.OutsideBoxFunction;
        c.Globals[$"vdbSdf{ShaderNode}"] = $$"""
                                             float vdbSdf{{ShaderNode}}(StructuredBuffer<uint> g, float3 p, float3 boundsMin, float3 boundsMax, float scale, float offset, float outsideValue)
                                             {
                                                 VdbGridInfo info = VdbReadGridInfo(g);
                                                 VdbLeafCache cache = VdbNewLeafCache();
                                                 float value = VdbSampleTrilinear(g, info, p, cache) * scale + offset;
                                                 return VdbFieldOutside(p, boundsMin, boundsMax, value, outsideValue);
                                             }
                                             """;
    }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        // Before the grid arrives the resource list does not declare the buffer, so emitting the
        // call here would reference an undeclared identifier and fail the whole shader compile.
        // Report "outside everywhere" until there is something to sample; the resource appears in
        // the same pass that brings the real call back.
        if (_srv == null)
        {
            c.AppendCall($"f{c}.w = {ShaderNode}OutsideValue;");
            c.AppendCall($"f{c}.xyz = 1;");
            return true;
        }

        c.AppendCall($"f{c}.w = vdbSdf{ShaderNode}({ShaderNode}Grid, p{c}.xyz, {ShaderNode}BoundsMin, {ShaderNode}BoundsMax, {ShaderNode}ValueScale, {ShaderNode}ValueOffset, {ShaderNode}OutsideValue);");
        c.AppendCall($"f{c}.xyz = p{c}.w < 0.5 ? p{c}.xyz : 1;"); // local space, as every field node provides
        return true;
    }

    void IGraphNodeOp.AppendShaderResources(ref List<ShaderGraphNode.SrvBufferReference> list)
    {
        if (_srv == null)
            return;

        foreach (var existing in list)
        {
            if (existing.Srv == _srv)
                return;
        }

        list.Add(new ShaderGraphNode.SrvBufferReference($"StructuredBuffer<uint> {ShaderNode}Grid", _srv));
    }

    private ShaderResourceView _srv;

    [Input(Guid = "5893924c-b57a-4807-bf4a-d498a105a371")]
    public readonly InputSlot<BufferWithViews> Grid = new();

    [GraphParam]
    [Input(Guid = "5e761feb-f214-4c33-9d36-90c88c3f1b6f")]
    public readonly InputSlot<Vector3> BoundsMin = new();

    [GraphParam]
    [Input(Guid = "41e6db16-5a41-4f49-adbd-45b1cda2d0ac")]
    public readonly InputSlot<Vector3> BoundsMax = new();

    [GraphParam]
    [Input(Guid = "bedfc0b1-99d3-4d38-8a28-e0bace94f3f8")]
    public readonly InputSlot<float> ValueScale = new();

    [GraphParam]
    [Input(Guid = "794b9793-e02f-484f-be18-3b1e07b62839")]
    public readonly InputSlot<float> ValueOffset = new();

    [GraphParam]
    [Input(Guid = "7c49a500-4dfe-48bb-9ace-77a74d9d0a94")]
    public readonly InputSlot<float> OutsideValue = new();
}
