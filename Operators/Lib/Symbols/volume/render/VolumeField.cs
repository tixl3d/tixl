using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using T3.Core.DataTypes.ShaderGraph;

namespace Lib.volume.render;

/// Samples a 3D texture baked from a volume ([VolumeToTexture3D]) as a field for [RaymarchField] - this is how volumes get raymarched with TiXL's existing field pipeline. Map the bounds outputs 1:1 from [VolumeToTexture3D].
[Guid("cd200763-633f-513e-8f24-d983b95d9ff0")]
internal sealed class VolumeField : Instance<VolumeField>, IGraphNodeOp
{
    [Output(Guid = "436a5543-a0e8-506d-891e-8ef353c96151")]
    public readonly Slot<ShaderGraphNode> Result = new();


    public VolumeField()
    {
        ShaderNode = new ShaderGraphNode(this, null);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var srv = Texture.GetValue(context)?.Srv;
        if (srv != null && srv.IsDisposed)
            srv = null;

        // The generated shader only declares the 3D texture in AppendShaderResources. A bake that
        // finishes after the first compile was skipped there, so flag the code for regeneration
        // when the texture appears (or is replaced) — otherwise RaymarchField keeps compiling a
        // shader that samples an undeclared texture.
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
        // The texture is only declared in AppendShaderResources, and only once a bake has produced
        // one. Before that the sampling body would reference an undeclared identifier and fail the
        // whole shader compile, so report "outside everywhere" until the texture appears - the same
        // guard [VdbDensityField] uses for its buffer.
        if (_srv == null)
        {
            c.Globals[$"volumeSample{ShaderNode}"] = $$"""
                                                      float volumeSample{{ShaderNode}}(float3 p, float3 boundsMin, float3 boundsMax, float valueScale, float valueOffset, float outsideValue)
                                                      {
                                                          return outsideValue;
                                                      }
                                                      """;
            return;
        }

        c.Globals[$"volumeSample{ShaderNode}"] = $$"""
                                                  float volumeSample{{ShaderNode}}(float3 p, float3 boundsMin, float3 boundsMax, float valueScale, float valueOffset, float outsideValue)
                                                  {
                                                      float3 uvw = saturate((p - boundsMin) / max(boundsMax - boundsMin, 1e-5));
                                                      float value = {{ShaderNode}}VolumeTex.SampleLevel(ClampedSampler, uvw, 0) * valueScale + valueOffset;

                                                      // Outside the bounds the clamped edge sample plus the distance to the box never overshoots the volume.
                                                      float3 outsideDelta = max(boundsMin - p, p - boundsMax);
                                                      float outsideDistance = length(max(outsideDelta, 0.0));
                                                      if (outsideDistance > 0)
                                                          return min(outsideDistance + max(value, 0.0), outsideValue);

                                                      return value;
                                                  }
                                                  """;
    }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        c.AppendCall($"f{c}.w = volumeSample{ShaderNode}(p{c}.xyz, {ShaderNode}BoundsMin, {ShaderNode}BoundsMax, {ShaderNode}ValueScale, {ShaderNode}ValueOffset, {ShaderNode}OutsideValue);");
        c.AppendCall($"f{c}.xyz = p{c}.w < 0.5 ? p{c}.xyz : 1;"); // local space, as every field node provides
        return true;
    }

    void IGraphNodeOp.AppendShaderResources(ref List<ShaderGraphNode.SrvBufferReference> list)
    {
        if (_srv == null)
            return;

        foreach (var x in list)
        {
            if (x.Srv == _srv)
                return;
        }

        list.Add(new ShaderGraphNode.SrvBufferReference($"Texture3D<float> {ShaderNode}VolumeTex", _srv));
    }

    private ShaderResourceView _srv;

    [Input(Guid = "eefa5651-27d2-5347-99fb-850697ea913b")]
    public readonly InputSlot<Texture3dWithViews> Texture = new();

    [GraphParam]
    [Input(Guid = "ac832ebe-2283-57ec-851b-a173ff449a36")]
    public readonly InputSlot<Vector3> BoundsMin = new();

    [GraphParam]
    [Input(Guid = "5f30e9a1-da69-5421-8438-d1270a3467a2")]
    public readonly InputSlot<Vector3> BoundsMax = new();

    [GraphParam]
    [Input(Guid = "9a1ecbca-e38e-537f-8f38-837e0218c951")]
    public readonly InputSlot<float> ValueScale = new();

    [GraphParam]
    [Input(Guid = "8c4b78e6-abe0-547c-b243-3f3c8e100c38")]
    public readonly InputSlot<float> ValueOffset = new();

    [GraphParam]
    [Input(Guid = "de0f63b7-05c2-5cb1-817e-d4f8bfe2222b")]
    public readonly InputSlot<float> OutsideValue = new();

}