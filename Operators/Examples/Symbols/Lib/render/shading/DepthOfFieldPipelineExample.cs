namespace Examples.Lib.render.shading;

[Guid("06a14a28-85b7-45a1-b885-2cc695c0e61d")]
 internal sealed class DepthOfFieldPipelineExample : Instance<DepthOfFieldPipelineExample>
{
    [Output(Guid = "ed15f212-2354-459d-91ce-6cbfe278c809")]
    public readonly Slot<Command> Output = new Slot<Command>();


}