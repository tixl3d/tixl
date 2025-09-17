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

    private void Update(EvaluationContext context)
    {
        try
        {
            var matrixParams = Vec4Params.GetCollectedTypedInputs();
            var floatParams = Params.GetCollectedTypedInputs();

            var floatParamCount = floatParams.Count;
            var vec4ArrayLength = matrixParams.Count;

            var totalFloatCount = floatParamCount + vec4ArrayLength * 4 * 4;

            var arraySize = totalFloatCount; //Note (vux) alignment handled on buffer creation
            //note : luckily in .net 10 that eventually goes to the stack
            var array = new float[arraySize];

            if (array.Length == 0)
                return;

            var totalFloatIndex = 0;

            foreach (var aInput in matrixParams)
            {
                var aaa = aInput.GetValue(context);
                foreach (var vec4 in aaa)
                {
                    array[totalFloatIndex++] = vec4.X;
                    array[totalFloatIndex++] = vec4.Y;
                    array[totalFloatIndex++] = vec4.Z;
                    array[totalFloatIndex++] = vec4.W;
                }
            }

            // Add Floats
            for (var floatIndex = 0; floatIndex < floatParamCount; floatIndex++)
            {
                array[totalFloatIndex++] = floatParams[floatIndex].GetValue(context);
            }

            Params.DirtyFlag.Clear();
            Vec4Params.DirtyFlag.Clear();

            var device = ResourceManager.Device;

            var size = sizeof(float) * array.Length;

            if (ResourceUtils.GetDynamicConstantBuffer(device, ref Buffer.Value, size))
            {
                Buffer.Value.DebugName = nameof(FloatsToBuffer); // no need to copy string every frame if constant
            }

            ResourceUtils.WriteDynamicBufferData<float>(device.ImmediateContext, Buffer.Value, array.AsSpan());
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