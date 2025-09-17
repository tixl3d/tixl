namespace Lib.flow.testing{
    [Guid("cf9a082f-490c-4b59-8a9a-eee161420d06")]
    internal sealed class DemoProjectTests : Instance<DemoProjectTests>
    {
        [Output(Guid = "771dbcd5-b6a0-4888-a11b-f8cc71c362d4")]
        public readonly Slot<string> Result = new Slot<string>();


    }
}

