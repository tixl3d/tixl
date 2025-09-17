namespace Examples.user.pixtur.project.climatewatch.helpers;

[Guid("487dad15-abb2-4d8f-a66a-520af9739684")]
public class CW_ResourceIcon : Instance<CW_ResourceIcon>
{
    [Output(Guid = "2a942a7d-3e98-4c15-9736-675095c714c6")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "56bc4d62-4938-4173-86d5-1f1bd3ea6a43")]
    public readonly InputSlot<string> ImagePath = new();

    [Input(Guid = "e49a4ccf-31aa-4168-9b28-b034dbc0848a")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "a03886ac-21a6-4c3e-bedd-a3c1a60f17ab")]
    public readonly InputSlot<System.Numerics.Vector2> Position = new();

    [Input(Guid = "fc81bd61-8f6b-49a6-ba35-b95949e1748c")]
    public readonly InputSlot<int> Index = new();

    [Input(Guid = "a74f1b5b-c09c-42a1-8c6d-279aaf6a5e39")]
    public readonly InputSlot<string> Label = new();

}