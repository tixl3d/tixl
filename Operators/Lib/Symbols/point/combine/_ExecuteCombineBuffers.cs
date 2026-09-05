#nullable enable
using SharpDX;
using SharpDX.Direct3D11;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.point.combine;

[Guid("56f7cf15-678d-4527-a328-8666a80882d0")]
public sealed class _ExecuteCombineBuffers : Instance<_ExecuteCombineBuffers>
{
    [Output(Guid = "d6770718-842e-441d-a5f6-db9b2a20839b")]
    public readonly Slot<BufferWithViews?> Output = new();

    public _ExecuteCombineBuffers()
    {
        Output.UpdateAction += Update;
    }

    private record struct InputBufferStatus(bool IsDirty, BufferWithViews Buffer, int StartIndex, int Length);

    private readonly List<InputBufferStatus> _inputBuffers = [];

    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct IndexDataForPass
    {
        public int StartIndex;
        public int Length;
        private Vector2 _padding;  // pad to 16 bytes
    }

    private void Update(EvaluationContext context)
    {
        // Fetch inputs and prepare
        var connections = InputBuffers.GetCollectedTypedInputs();

        _computeShader = ComputeShader.GetValue(context);

        if (connections.Count == 0 || _computeShader == null)
        {
            Output.Value = null;
            return;
        }

        // Compute total size
        var totalLength = 0;
        var usedStride = -1;
        _inputBuffers.Clear();
        for (var connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
        {
            var input = connections[connectionIndex];

            var isDirty = input.DirtyFlag.IsDirty;
            var bufferWithViews = input.GetValue(context);

            if (bufferWithViews is not { Srv.IsDisposed: false })
                continue;

            var inputStride = bufferWithViews.Buffer.Description.StructureByteStride;

            if (usedStride == -1)
            {
                usedStride = inputStride;
            }
            else if (inputStride != usedStride)
            {
                Log.Warning($"Ignoring buffer #{connectionIndex} not matching stride of {usedStride}. Has {inputStride} bytes", this);
                continue;
            }

            //var sizeInBytes = bufferWithViews.Srv.Description.Buffer.ElementCount * inputStride;
            //var length = sizeInBytes / 4; // number of uints
            var length = bufferWithViews.Srv.Description.Buffer.ElementCount;
            _inputBuffers.Add(new InputBufferStatus
                                  {
                                      IsDirty = isDirty,
                                      Length = length, // as count of uints
                                      StartIndex = totalLength,
                                      Buffer = bufferWithViews
                                  });

            totalLength += length;
        }

        var device = ResourceManager.Device;

        // Setup or update target buffer 
        ResourceManager.SetupStructuredBuffer(totalLength * usedStride, usedStride, ref _resultBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_resultBuffer.Buffer, ref _resultBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(_resultBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _resultBuffer.Uav);

        // Setup pass parameter buffer (can be re-used for all passes)
        if (_passParamBuffer == null)
            SetupPassParamBuffer(0, 0);

        var csStage = device.ImmediateContext.ComputeShader;
        csStage.Set(_computeShader);
        csStage.SetConstantBuffer(0, _passParamBuffer);
        csStage.SetUnorderedAccessView(0, _resultBuffer.Uav);
        
        foreach (var bufferState in _inputBuffers)
        {
            csStage.SetShaderResources(0, 1, bufferState.Buffer.Srv);

            SetupPassParamBuffer(bufferState.StartIndex, bufferState.Length);

            // Dispatch the shader
            const int threadGroupSizeX = 256;
            device.ImmediateContext.Dispatch(bufferState.Length / threadGroupSizeX + 1, 1, 1);
            
            // Unbind pass
            csStage.SetShaderResource(0, null);
        }

        Output.Value = _resultBuffer;
        
        // Unbind resources
        csStage.SetUnorderedAccessView(0, null);
        csStage.SetConstantBuffer(0, null);
        
        // Make sure the op can be cached
        InputBuffers.DirtyFlag.Clear();
    }

    private void SetupPassParamBuffer(int startIndex, int length)
    {
        _passData[0].StartIndex = startIndex;
        _passData[0].Length = length;

        using var data = new DataStream(PassParamSizeInBytes, true, true);
        data.WriteRange(_passData);
        data.Position = 0;

        if (_passParamBuffer == null)
        {
            var bufferDesc = new BufferDescription
                                 {
                                     Usage = ResourceUsage.Default,
                                     SizeInBytes = PassParamSizeInBytes,
                                     BindFlags = BindFlags.ConstantBuffer
                                 };
            _passParamBuffer = new Buffer(ResourceManager.Device, data, bufferDesc);
        }
        else
        {
            ResourceManager.Device
                           .ImmediateContext
                           .UpdateSubresource(new DataBox(data.DataPointer, 0, 0),
                                              _passParamBuffer,
                                              0);
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;
        
        Utilities.Dispose(ref _passParamBuffer);
        Utilities.Dispose(ref _resultBuffer);
    }
    
    private const int PassParamSizeInBytes = 16; // min required
    private Buffer? _passParamBuffer;
    private BufferWithViews _resultBuffer = new();
    private readonly IndexDataForPass[] _passData = new IndexDataForPass[1];
    private SharpDX.Direct3D11.ComputeShader? _computeShader;

    [Input(Guid = "c8a5769e-2536-4caa-8380-22fbeed1ef12")]
    public readonly MultiInputSlot<BufferWithViews> InputBuffers = new();

    [Input(Guid = "d91e52f2-52c6-4533-ac14-f5b2ce8b4c0f")]
    public readonly InputSlot<T3.Core.DataTypes.ComputeShader> ComputeShader = new();
}