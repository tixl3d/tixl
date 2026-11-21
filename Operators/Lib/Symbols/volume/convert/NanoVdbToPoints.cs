using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Graphics.Compat;
using ComputeShaderT3 = T3.Core.DataTypes.ComputeShader;

namespace Lib.volume.convert;

/// Turns the active voxels of a NanoVDB grid into TiXL points on the GPU. Point [Grid] to the buffer of [LoadNanoVdb] or [VolumeToNanoVdb], and a [ComputeShader] at Lib:shaders/volume/nanovdb-to-points.hlsl. The output feeds [DrawPoints] or any other point operator. This never reads the grid back to the CPU, so it scales with the grid instead of with the volume.
[Guid("cb09f697-7b35-4f7c-b1bd-243cfd102137")]
internal sealed class NanoVdbToPoints : Instance<NanoVdbToPoints>
{
    [Output(Guid = "3bd4066b-b0ad-4d86-af8c-50b77ab487a9")]
    public readonly Slot<BufferWithViews> Points = new();

    public NanoVdbToPoints()
    {
        Points.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var grid = Grid.GetValue(context);
        var shader = Shader.GetValue(context);

        var threshold = Threshold.GetValue(context);
        var maxPoints = Math.Clamp(MaxPoints.GetValue(context), 0, MaxAllowedPoints);
        var maxLeaves = Math.Max(0, MaxLeaves.GetValue(context));

        if (grid?.Srv == null || shader == null || maxPoints == 0 || maxLeaves == 0)
        {
            Points.Value = null;
            return;
        }

        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;

        try
        {
            // Output: MaxPoints * 64 bytes of Point, with an SRV and a UAV because both are needed.
            const int pointStride = 64;
            ResourceManager.SetupStructuredBuffer(maxPoints * pointStride, pointStride, ref _points.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_points.Buffer, ref _points.Srv);
            ResourceManager.CreateStructuredBufferUav(_points.Buffer, UnorderedAccessViewBufferFlags.None, ref _points.Uav);

            // Counter: one uint, zeroed before the append pass so the fill pass knows the real count.
            _counterData[0] = 0;
            ResourceManager.SetupStructuredBuffer(_counterData, sizeof(uint), sizeof(uint), ref _counter.Buffer);
            ResourceManager.CreateStructuredBufferUav(_counter.Buffer, UnorderedAccessViewBufferFlags.None, ref _counter.Uav);

            EnsureParamsBuffer();

            var csStage = deviceContext.ComputeShader;
            csStage.Set(shader);
            csStage.SetConstantBuffer(0, _paramsBuffer);
            csStage.SetShaderResource(0, grid.Srv);
            csStage.SetUnorderedAccessView(0, _points.Uav);
            csStage.SetUnorderedAccessView(1, _counter.Uav);

            // Pass 0: one group of 512 threads per leaf; the shader skips leaves past the real count.
            SetParams(threshold, maxPoints, pass: 0, maxLeaves);
            deviceContext.Dispatch(maxLeaves, 1, 1);

            // Pass 1: mark everything past the counter as a separator so the points end where the
            // real data ends. Dispatched even when the append filled the buffer, which is harmless.
            SetParams(threshold, maxPoints, pass: 1, maxLeaves);
            deviceContext.Dispatch(maxPoints / PointsPerGroup + 1, 1, 1);

            Points.Value = _points;
        }
        catch (Exception e)
        {
            Log.Warning($"NanoVdbToPoints failed: {e.Message}", this);
            Points.Value = null;
        }
        finally
        {
            // Leaving resources bound to the device poisons whatever dispatches next.
            var csStage = deviceContext.ComputeShader;
            csStage.SetShaderResource(0, null);
            csStage.SetUnorderedAccessView(0, null);
            csStage.SetUnorderedAccessView(1, null);
            csStage.SetConstantBuffer(0, null);
        }

        // The output was rebuilt from the inputs, so let the graph cache this result.
        Grid.DirtyFlag.Clear();
        Shader.DirtyFlag.Clear();
    }

    private void EnsureParamsBuffer()
    {
        if (_paramsBuffer != null)
            return;

        var description = new BufferDescription
                              {
                                  Usage = ResourceUsage.Default,
                                  SizeInBytes = ParamsSizeInBytes,
                                  BindFlags = BindFlags.ConstantBuffer,
                              };
        using var data = new DataStream(ParamsSizeInBytes, true, true);
        _paramsBuffer = new Buffer(ResourceManager.Device, data, description);
        _paramsData = new DataStream(ParamsSizeInBytes, true, true);
    }

    /// <summary>Writes the pass constants. Both passes share one buffer, so each is set before its dispatch.</summary>
    private void SetParams(float threshold, int maxPoints, int pass, int maxLeaves)
    {
        _params[0].Threshold = threshold;
        _params[0].MaxPoints = (uint)maxPoints;
        _params[0].Pass = (uint)pass;
        _params[0].MaxLeaves = (uint)maxLeaves;

        _paramsData.Position = 0;
        _paramsData.WriteRange(_params);
        _paramsData.Position = 0;

        var box = new DataBox(_paramsData.DataPointer, 0, 0);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(box, _paramsBuffer, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _points.Dispose();
            _counter.Dispose();
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _paramsBuffer);
            _paramsData?.Dispose();
            _paramsData = null;
        }

        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Params
    {
        public float Threshold;
        public uint MaxPoints;
        public uint Pass;
        public uint MaxLeaves;
    }

    private const int ParamsSizeInBytes = 16;   // constant buffers are bound in 16-byte units
    private const int MaxAllowedPoints = 8 * 1024 * 1024;
    private const int PointsPerGroup = 512;     // must match [numthreads] in nanovdb-to-points.hlsl

    private readonly Params[] _params = new Params[1];
    private readonly uint[] _counterData = new uint[1];
    private readonly BufferWithViews _points = new();
    private readonly BufferWithViews _counter = new();
    private Buffer _paramsBuffer;
    private DataStream _paramsData;

    [Input(Guid = "6c01c2c6-c3ec-4f59-93ee-a4654b218f2e")]
    public readonly InputSlot<BufferWithViews> Grid = new();

    [Input(Guid = "e3952b20-aba1-44fa-a6d6-64d61a5085e4")]
    public readonly InputSlot<ComputeShaderT3> Shader = new();

    [Input(Guid = "4db54dc7-b227-433a-8442-56fe0a3374df")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "d998b43d-d4b5-4029-90ec-6c90eb14a1b2")]
    public readonly InputSlot<int> MaxPoints = new();

    [Input(Guid = "c48d37c8-ea1d-4207-9bad-0dd8d0bffe94")]
    public readonly InputSlot<int> MaxLeaves = new();
}
