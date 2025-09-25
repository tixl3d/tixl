using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("724da755-2d0c-42ab-8335-8c88ec5fb078")]
internal sealed class FloatsToBuffer : Instance<FloatsToBuffer>
{
    [Output(Guid = "f5531ffb-dbde-45d3-af2a-bd90bcbf3710")]
    public readonly Slot<Buffer> Buffer = new();

    public FloatsToBuffer()
    {
        Buffer.UpdateAction += Update;
    }
    private float[] _uploadBuffer = [];

    private void Update(EvaluationContext context)
    {
        try
        {
            var matrixParams = Vec4Params.GetCollectedTypedInputs();
            var floatParams = Params.GetCollectedTypedInputs();

            var floatParamCount = floatParams.Count;
            var vec4ArrayLength = matrixParams.Count;

            var totalFloatCount = floatParamCount + vec4ArrayLength * 4 * 4;
            if (totalFloatCount == 0)
                return;

            // Ensure buffer large enough
            if (_uploadBuffer.Length < totalFloatCount)
                _uploadBuffer = new float[totalFloatCount];

            var totalFloatIndex = 0;

            // Fill matrices
            foreach (var aInput in matrixParams)
            {
                var mat = aInput.GetValue(context);
                foreach (var vec4 in mat)
                {
                    _uploadBuffer[totalFloatIndex++] = vec4.X;
                    _uploadBuffer[totalFloatIndex++] = vec4.Y;
                    _uploadBuffer[totalFloatIndex++] = vec4.Z;
                    _uploadBuffer[totalFloatIndex++] = vec4.W;
                }
            }

            // Fill floats
            for (var floatIndex = 0; floatIndex < floatParamCount; floatIndex++)
            {
                _uploadBuffer[totalFloatIndex++] = floatParams[floatIndex].GetValue(context);
            }

            Params.DirtyFlag.Clear();
            Vec4Params.DirtyFlag.Clear();

            var device = ResourceManager.Device;
            var size = sizeof(float) * totalFloatCount;

            if (ResourceUtils.GetDynamicConstantBuffer(device, ref Buffer.Value, size))
            {
                Buffer.Value.DebugName = nameof(FloatsToBuffer);
            }

            // Upload only the part we filled
            ResourceUtils.WriteDynamicBufferData<float>(
                                                        device.ImmediateContext, Buffer.Value,
                                                        _uploadBuffer.AsSpan(0, totalFloatCount));
        }
        catch (Exception e)
        {
            Log.Warning("Failed to setup shader parameters:" + e.Message);
        }
    }
        
    [Input(Guid = "914EA6E8-ABC6-4294-B895-8BFBE5AFEA0E")]
    public readonly MultiInputSlot<Vector4[]> Vec4Params = new();

    [Input(Guid = "49556D12-4CD1-4341-B9D8-C356668D296C")]
    public readonly MultiInputSlot<float> Params = new();

}