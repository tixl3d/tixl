namespace Lib.flow.testing{
    [Guid("b4a9f19a-bbb0-4d65-8d4a-560460e2505c")]
    internal sealed class AllTests : Instance<AllTests>
    {
        [Output(Guid = "f9ddc7da-91fa-4a7c-b59d-d1ba8fc5eb74")]
        public readonly Slot<string> Result = new Slot<string>();


    }
}

