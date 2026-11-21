using System;
using System.Runtime.InteropServices;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Graphics.Compat;
using ComputeShaderT3 = T3.Core.DataTypes.ComputeShader;

namespace Lib.volume.convert;

/// Draws the sparse structure of a NanoVDB grid as wireframe boxes, one per node of the selected level. Feed [Grid] from [LoadNanoVdb] or [VolumeToNanoVdb], add a [ComputeShader] at Lib:shaders/volume/nanovdb-bounds.hlsl, and draw the output with [DrawLines]. The fastest way to see whether bounds, transforms and node counts line up before trusting a raymarch.
[Guid("9bd1746d-a869-417a-8467-080652ddf718")]
internal sealed class DrawVdbBounds : Instance<DrawVdbBounds>
{
    [Output(Guid = "b9b80bf9-db91-4996-b295-ceeadde34273")]
    public readonly Slot<BufferWithViews> Points = new();

    public DrawVdbBounds()
    {
        Points.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var grid = Grid.GetValue(context);
        var shader = Shader.GetValue(context);

        var level = Math.Clamp(Level.GetValue(context), 0, 2);
        var maxNodes = Math.Max(0, MaxNodes.GetValue(context));
        var capacity = Math.Clamp(Capacity.GetValue(context), 0, MaxAllowedPoints);
        var color = Color.GetValue(context);

        if (grid?.Srv == null || shader == null || maxNodes == 0 || capacity < SlotsPerBox)
        {
            Points.Value = null;
            return;
        }

        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;

        try
        {
            const int pointStride = 64;
            ResourceManager.SetupStructuredBuffer(capacity * pointStride, pointStride, ref _points.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_points.Buffer, ref _points.Srv);
            ResourceManager.CreateStructuredBufferUav(_points.Buffer, UnorderedAccessViewBufferFlags.None, ref _points.Uav);

            _counterData[0] = 0;
            ResourceManager.SetupStructuredBuffer(_counterData, sizeof(uint), sizeof(uint), ref _counter.Buffer);
            ResourceManager.CreateStructuredBufferUav(_counter.Buffer, UnorderedAccessViewBufferFlags.None, ref _counter.Uav);

            EnsureParamsBuffer();
            SetParams(color, level, maxNodes, capacity);

            var csStage = deviceContext.ComputeShader;
            csStage.Set(shader);
            csStage.SetConstantBuffer(0, _paramsBuffer);
            csStage.SetShaderResource(0, grid.Srv);
            csStage.SetUnorderedAccessView(0, _points.Uav);
            csStage.SetUnorderedAccessView(1, _counter.Uav);

            // 64 threads per group, matching [numthreads] in nanovdb-bounds.hlsl.
            deviceContext.Dispatch(maxNodes / ThreadsPerGroup + 1, 1, 1);

            Points.Value = _points;
        }
        catch (Exception e)
        {
            Log.Warning($"DrawVdbBounds failed: {e.Message}", this);
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

    private void SetParams(Vector4 color, int level, int maxNodes, int capacity)
    {
        _params[0].Color = color;
        _params[0].Level = (uint)level;
        _params[0].MaxNodes = (uint)maxNodes;
        _params[0].Capacity = (uint)capacity;

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
        public Vector4 Color;
        public uint Level;
        public uint MaxNodes;
        public uint Capacity;
        public uint Padding;
    }

    private const int ParamsSizeInBytes = 32;
    private const int ThreadsPerGroup = 64;
    private const int SlotsPerBox = 36;                    // 12 edges, separator + 2 points each
    private const int MaxAllowedPoints = 8 * 1024 * 1024;

    private readonly Params[] _params = new Params[1];
    private readonly uint[] _counterData = new uint[1];
    private readonly BufferWithViews _points = new();
    private readonly BufferWithViews _counter = new();
    private Buffer _paramsBuffer;
    private DataStream _paramsData;

    [Input(Guid = "37de5282-2685-49d4-814e-fb0d2ad2ac1d")]
    public readonly InputSlot<BufferWithViews> Grid = new();

    [Input(Guid = "c1c55f26-1ce4-4ef0-b045-57582c4e82b8")]
    public readonly InputSlot<ComputeShaderT3> Shader = new();

    [Input(Guid = "1d104dc8-1b12-469f-a2b3-d9093d2634f0")]
    public readonly InputSlot<int> Level = new();

    [Input(Guid = "768eeb4d-4083-45f7-884e-a852c41f9146")]
    public readonly InputSlot<int> MaxNodes = new();

    [Input(Guid = "5c8c098a-4fb2-49cb-90f8-68ae87cca5c6")]
    public readonly InputSlot<int> Capacity = new();

    [Input(Guid = "f97533c9-c128-4126-b1f3-0ed9dece008a")]
    public readonly InputSlot<Vector4> Color = new();
}
