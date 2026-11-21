using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.ShaderGraph;

namespace Lib.volume.fields;

/// Exposes a NanoVDB fog volume as a signed-distance-like field: the output is [IsoValue] minus the sampled density, so dense regions read as "inside" to every operator that expects negative values inside a surface. Feed [Grid] from [LoadNanoVdb] or [VolumeToNanoVdb] and map the bounds from the same operator. Use this with [FieldVolumeForce] or [SampleFieldPoints] to let smoke push or colour particles.
[Guid("d3c3c4eb-791e-4897-9c8c-27e159090239")]
internal sealed class VdbDensityField : Instance<VdbDensityField>, IGraphNodeOp
{
    [Output(Guid = "9d4c046e-98a5-46a3-b694-dc383e6eb68e")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public VdbDensityField()
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
        c.Globals[$"vdbDensity{ShaderNode}"] = $$"""
                                                 float vdbDensity{{ShaderNode}}(StructuredBuffer<uint> g, float3 p, float3 boundsMin, float3 boundsMax, float scale, float offset, float isoValue, float outsideValue)
                                                 {
                                                     VdbGridInfo info = VdbReadGridInfo(g);
                                                     VdbLeafCache cache = VdbNewLeafCache();
                                                     float density = VdbSampleTrilinear(g, info, p, cache) * scale + offset;

                                                     // Density is high inside, so flipping it around the iso value gives the
                                                     // negative-inside convention the other field operators expect.
                                                     return VdbFieldOutside(p, boundsMin, boundsMax, isoValue - density, outsideValue);
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

        c.AppendCall($"f{c}.w = vdbDensity{ShaderNode}({ShaderNode}Grid, p{c}.xyz, {ShaderNode}BoundsMin, {ShaderNode}BoundsMax, {ShaderNode}ValueScale, {ShaderNode}ValueOffset, {ShaderNode}IsoValue, {ShaderNode}OutsideValue);");
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

    [Input(Guid = "3e457cb0-9b2c-475b-a616-e0a176c08d0c")]
    public readonly InputSlot<BufferWithViews> Grid = new();

    [GraphParam]
    [Input(Guid = "c0ecc90c-7529-45e4-b860-7d079452e4f3")]
    public readonly InputSlot<Vector3> BoundsMin = new();

    [GraphParam]
    [Input(Guid = "4d6b9f1a-8f2c-4a1e-9a3d-6b7c5e2d8f10")]
    public readonly InputSlot<Vector3> BoundsMax = new();

    [GraphParam]
    [Input(Guid = "8a2e5c31-7b4d-42f8-9c61-5d3a0e9b7c24")]
    public readonly InputSlot<float> ValueScale = new();

    [GraphParam]
    [Input(Guid = "2f7b9e04-1c5a-4d63-8e92-a4b7c0d1f368")]
    public readonly InputSlot<float> ValueOffset = new();

    [GraphParam]
    [Input(Guid = "6e1d8b47-3a92-4c05-b7e8-9f2a1d6c4b53")]
    public readonly InputSlot<float> IsoValue = new();

    [GraphParam]
    [Input(Guid = "b5c2a918-4e7f-4d0b-a361-8c9e2f5b7d04")]
    public readonly InputSlot<float> OutsideValue = new();
}
