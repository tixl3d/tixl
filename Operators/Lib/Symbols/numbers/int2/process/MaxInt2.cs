namespace Lib.numbers.int2.process;

[Guid("8e9a45db-a631-4c92-aea9-c252ea6e9708")]
internal sealed class MaxInt2 : Instance<MaxInt2>
{
    [Output(Guid = "1D58BFF5-0FDF-4A42-ABF6-22FD8B74237F")]
    public readonly Slot<Int2> MaxSize = new();

    public MaxInt2()
    {
            
        MaxSize.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        int maxWidth =0 ;
        int maxHeight = 0;
        //Result.Value = 0;
        foreach (var input in Sizes.GetCollectedTypedInputs())
        {
            var s = input.GetValue(context);
            maxWidth = Math.Max(maxWidth, s.Width);
            maxHeight = Math.Max(maxHeight, s.Height);
        }
        Sizes.DirtyFlag.Clear();

        MaxSize.Value = new Int2(maxWidth, maxHeight);
    }
        
        
    [Input(Guid = "3FE2016D-C4BC-42E1-A3D8-F8BC34CFCF32")]
    public readonly MultiInputSlot<Int2> Sizes = new();
}