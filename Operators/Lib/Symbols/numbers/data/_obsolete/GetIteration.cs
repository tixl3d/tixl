namespace Lib.numbers.data._obsolete;

[Guid("4c088b67-be47-4599-bd64-5f277abb0113")]
internal sealed class GetIteration : Instance<GetIteration>
{
    [Output(Guid = "c3a199dc-993c-4f43-a4aa-f355e6584a64", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Progress = new();

    [Output(Guid = "821FD3F7-2956-44E0-9412-6D33D1EDB016", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Index = new();

    public GetIteration()
    {
        Progress.UpdateAction += Update;
        Index.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (context.IteratedList == null || context.IteratedList.NumElements == 0)
            return;


        Index.Value = context.IteratedListIndex;
        Progress.Value = (float)context.IteratedListIndex/(context.IteratedList.NumElements - 1);
    }
}