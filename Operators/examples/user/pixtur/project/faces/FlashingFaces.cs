namespace Examples.user.pixtur.project.faces;

[Guid("f487a6ae-62ed-4f35-9ca8-22f6a25fc2cc")]
public class FlashingFaces : Instance<FlashingFaces>
{
    [Output(Guid = "92c2800f-e565-4b7a-bf7d-83117d26f8af")]
    public readonly Slot<Texture2D> Output = new();


}