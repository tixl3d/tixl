namespace Lib.point.sim.experimental;

[Guid("f925e9b9-5c7a-4fbf-9572-b11fe2d54d6c")]
internal sealed class ApplyRandomWalk : Instance<ApplyRandomWalk>
{

    [Output(Guid = "22ce9fc5-d23d-4141-8a81-c07ad824ed5e")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "4b3bb61b-a9f6-4c53-9af7-707984f7ff18")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "7ce4b12c-224c-41ad-a42d-770df1ee4a67")]
    public readonly InputSlot<bool> TriggerStep = new();

    [Input(Guid = "351e1da3-a8ab-4625-b7c6-0c14703a94d9")]
    public readonly InputSlot<float> StepWidth = new();

    [Input(Guid = "a5f770ae-7f1d-4b45-93e9-d31ba7f62932")]
    public readonly InputSlot<float> TurnAngle = new();

    [Input(Guid = "23a45ab7-7f40-4f6d-91ff-5ecaf9ffe221")]
    public readonly InputSlot<float> StepRatio = new();

    [Input(Guid = "e0326849-5e5b-41fb-b53e-0a2c0eafca12")]
    public readonly InputSlot<float> TurnRatio = new();

    [Input(Guid = "065cafa7-8da9-428b-aef7-5ce7f969f293")]
    public readonly InputSlot<float> RandomStepWidth = new();

    [Input(Guid = "b70f51c3-b64f-417e-a22f-e9f790e0259a")]
    public readonly InputSlot<float> RandomRotateAngle = new();

    [Input(Guid = "87b7a45e-dad8-4d5b-93af-7ed743ac8237")]
    public readonly InputSlot<Vector2> AreaEdgeRange = new();

    [Input(Guid = "b4cabe59-4166-490c-be8d-12eb9d5fe3c4")]
    public readonly InputSlot<Vector2> AreaCenter = new();

    [Input(Guid = "2b7fd9ed-4d08-4d0e-8258-41c7606c182f")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "99430d88-24b4-4906-9cd0-2031f587ef5b")]
    public readonly InputSlot<bool> IsEnabled = new();
}