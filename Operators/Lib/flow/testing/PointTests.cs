namespace Lib.flow.testing{
    [Guid("9eab73c1-bcca-4a10-8af8-7aa13649a9b6")]
    internal sealed class PointTests : Instance<PointTests>
    {
        [Output(Guid = "a0f103c6-1d04-4391-97d7-2b9651bad1c3")]
        public readonly Slot<string> Result = new Slot<string>();


    }
}

