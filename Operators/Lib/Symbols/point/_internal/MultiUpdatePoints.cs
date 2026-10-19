namespace Lib.point._internal;

[Guid("62c89469-7194-486a-87cd-c3e6bc0cf5d2")]
internal sealed class MultiUpdatePoints : Instance<MultiUpdatePoints>
{
    [Output(Guid = "5556392D-8884-4922-B5AD-AFA474E36C02")]
    public readonly Slot<BufferWithViews> Result = new();
        
        
    public MultiUpdatePoints()
    {
        Result.UpdateAction += Update;
    }


    private void Update(EvaluationContext context)
    {
        var collectedInputs = PointBuffers.CollectedInputs;
        BufferWithViews first = null;
        BufferWithViews last = null;
        var connectedIndex = 0;
        for (var index = 0; index < collectedInputs.Count; index++)
        {
            var buffer = collectedInputs[index].GetValue(context);
            if (buffer == null)
                continue;

            if (first == null)
            {
                first = buffer;
            }
            else if (buffer != first)
            {
                Log.Warning($"Inconsistent buffers connected to MultiUpdatePoints on index {connectedIndex}", this);
            }

            last = buffer;
            connectedIndex++;
        }

        PointBuffers.DirtyFlag.Clear();
        Result.Value = last;
    }


    [Input(Guid = "7CAA2F8C-64E1-4941-BD1C-8BDC89584BA7")]
    public readonly MultiInputSlot<BufferWithViews> PointBuffers = new();
}