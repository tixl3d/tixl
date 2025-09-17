using T3.Core.Rendering;

// SharpDX.Direct3D11.Buffer;
//using Utilities = T3.Core.Utils.Utilities;

namespace Lib.numbers.@int.process;

[Guid("2eb20a76-f8f7-49e9-93a5-1e5981122b50")]
internal sealed class IntsToBuffer : Instance<IntsToBuffer>
{
    [Output(Guid = "f5531ffb-dbde-45d3-af2a-bd90bcbf3710")]
    public readonly Slot<Buffer> Result = new();

    public IntsToBuffer()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var intParams = Params.GetCollectedTypedInputs();
        var intParamCount = intParams.Count;

        var arraySize = (intParamCount / 4 + (intParamCount % 4 == 0 ? 0 : 1)) * 4; // always 16byte slices for alignment
        var array = new int[arraySize];

        if (array.Length == 0)
        {
            Params.DirtyFlag.Clear();    
            return;
        }
            
        for (var intIndex = 0; intIndex < intParamCount; intIndex++)
        {
            array[intIndex] = intParams[intIndex].GetValue(context);
        }

        Params.DirtyFlag.Clear();

        var device = ResourceManager.Device;
        var size = sizeof(int) * array.Length;

        if (ResourceUtils.GetDynamicConstantBuffer(device, ref Result.Value, size))
        {
            Result.Value.DebugName = nameof(IntsToBuffer); // no need to copy string every frame if constant
        }

        ResourceUtils.WriteDynamicBufferData<int>(device.ImmediateContext, Result.Value, array.AsSpan());
    }


    [Input(Guid = "49556D12-4CD1-4341-B9D8-C356668D296C")]
    public readonly MultiInputSlot<int> Params = new();

}