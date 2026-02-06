#nullable enable

using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Logging;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace T3.Core.Resource;

/// <summary>
/// Asynchronously reads back GPU structured buffers over multiple frames using a rotating set of staging buffers.
/// </summary>
public sealed class StructuredBufferReadAccess : IDisposable
{
    public sealed class ReadRequestItem
    {
        internal int RequestIndex;
        public required StructuredBufferReadAccess BufferReadAccess;
        public required OnReadComplete OnSuccess;
        public required Buffer CpuAccessBuffer;
        public required int ElementCount;
        public required int StructureByteStride;

        internal bool IsReady => RequestIndex == BufferReadAccess._frameCounter - (BufferCount - 2);
        internal bool IsObsolete => RequestIndex < BufferReadAccess._frameCounter - (BufferCount - 2);
    }

    public delegate void OnReadComplete(ReadRequestItem readItem, IntPtr dataPointer, DataStream dataStream);

    /// <summary>
    /// Readback will take several frames because it takes a while until the frames are
    /// downloaded from the gpu. <see cref="Update"/> needs to be called once a frame.
    /// </summary>
    public void Update()
    {
        _frameCounter++;

        while (_readRequests.Count > 0 && _readRequests[0].IsObsolete)
        {
            Log.Debug("Removing obsolete buffer read request");
            _readRequests.RemoveAt(0);
        }

        if (_readRequests.Count == 0 || !_readRequests[0].IsReady)
            return;

        var request = _readRequests[0];
        var context = ResourceManager.Device.ImmediateContext;

        var dataBox = context.MapSubresource(
            request.CpuAccessBuffer,
            0,
            MapMode.Read,
            MapFlags.None,
            out var dataStream);

        try
        {
            request.OnSuccess(request, dataBox.DataPointer, dataStream);
        }
        finally
        {
            context.UnmapSubresource(request.CpuAccessBuffer, 0);
            _readRequests.RemoveAt(0);
        }
    }

    public bool InitiateRead(Buffer sourceBuffer, int elementCount, int structureByteStride, OnReadComplete onSuccess)
    {
        if (sourceBuffer == null! || sourceBuffer.IsDisposed)
            return false;

        var sizeInBytes = elementCount * structureByteStride;
        PrepareCpuAccessBuffers(sizeInBytes, structureByteStride);

        var cpuAccessBuffer = _cpuAccessBuffers[_frameCounter % BufferCount];
        ResourceManager.Device.ImmediateContext.CopyResource(sourceBuffer, cpuAccessBuffer);

        _readRequests.Add(new ReadRequestItem
        {
            BufferReadAccess = this,
            RequestIndex = _frameCounter,
            CpuAccessBuffer = cpuAccessBuffer,
            ElementCount = elementCount,
            StructureByteStride = structureByteStride,
            OnSuccess = onSuccess,
        });

        return true;
    }

    private void PrepareCpuAccessBuffers(int sizeInBytes, int structureByteStride)
    {
        if (_cpuAccessBuffers.Count > 0 &&
            _cpuAccessBuffers[0].Description.SizeInBytes == sizeInBytes &&
            _cpuAccessBuffers[0].Description.StructureByteStride == structureByteStride)
            return;

        DisposeBuffers();
        if (_readRequests.Count > 0)
        {
            Log.Debug($"Discarding {_readRequests.Count} buffer frames with outdated format");
            _readRequests.Clear();
        }

        var desc = new BufferDescription
        {
            Usage = ResourceUsage.Staging, // vs. Default
            SizeInBytes = sizeInBytes, 
            BindFlags = BindFlags.None, // BindFlags.UnorderedAccess | BindFlags.ShaderResource
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = structureByteStride
        };

        for (int i = 0; i < BufferCount; i++)
        {
            try
            {
                var buffer = new Buffer(ResourceManager.Device, desc);
                _cpuAccessBuffers.Add(buffer);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to setup readback buffer " + e.Message);
            }
        }
    }

    public void ClearQueue()
    {
        _readRequests.Clear();
    }

    private void DisposeBuffers()
    {
        foreach (var buffer in _cpuAccessBuffers)
        {
            buffer.Dispose();
        }
        _cpuAccessBuffers.Clear();
    }

    public void Dispose()
    {
        DisposeBuffers();
    }

    private const int BufferCount = 3;
    private readonly List<Buffer> _cpuAccessBuffers = new(BufferCount);
    private readonly List<ReadRequestItem> _readRequests = new(BufferCount);
    private int _frameCounter;
}
