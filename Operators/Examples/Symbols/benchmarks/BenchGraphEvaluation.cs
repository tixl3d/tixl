namespace Examples.benchmarks;

[Guid("a4055ada-4668-5205-b5e1-e45b032e754d")]
internal sealed class BenchGraphEvaluation : Instance<BenchGraphEvaluation>
{
    [Output(Guid = "59f13348-26f2-59c1-adde-93403c908497")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "b2699554-87c0-5438-b21f-1fe46d0096b0")]
    public readonly InputSlot<int> Load = new();
}
