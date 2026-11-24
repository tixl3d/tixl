namespace Examples.benchmarks;

[Guid("22a202a0-7fde-5c9d-a6fb-813d8264c82a")]
internal sealed class BenchPointProcessing : Instance<BenchPointProcessing>
{
    [Output(Guid = "eab8ac6b-f4c1-571e-9068-9676db4abc50")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "f8da3b29-d83f-5ea8-9688-3dd3e1eecca2")]
    public readonly InputSlot<int> Load = new();
}
