namespace Examples.user.still.worksforeverybody;

[Guid("3f7f8975-a7bb-47e6-96f7-061deb418e18")]
internal sealed class _WFEGameUi : Instance<_WFEGameUi>
{
    [Output(Guid = "9f6bd5f3-eb31-40de-894a-51d55b4e0fa3")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "acb35d19-baaf-4042-9664-7f784b697831")]
    public readonly Slot<System.Collections.Generic.List<string>> Scores = new();

    [Output(Guid = "6a689686-0ba5-430e-b3ca-8ddbfb2bacf7")]
    public readonly Slot<int> LastScore = new();

    [Input(Guid = "8d294996-7f87-424e-9869-12d383cb7869")]
    public readonly InputSlot<bool> IsActive = new();

    [Input(Guid = "dd0ca7a1-9fa1-4364-80a4-ecf0892fa66b")]
    public readonly InputSlot<int> SceneIndex = new();

    [Input(Guid = "cee4bca4-e9d4-493d-b4dd-56dd69d7fb3f")]
    public readonly InputSlot<float> PerfectTime = new();

    [Input(Guid = "84d0d959-0a87-4f8b-9470-d9117e2b365f")]
    public readonly InputSlot<bool> SaveScore = new();

}