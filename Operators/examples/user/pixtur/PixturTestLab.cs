namespace Examples.user.pixtur;

[Guid("a4f8b7e9-a52f-41b6-a63b-d30e5ba77825")]
public class PixturTestLab : Instance<PixturTestLab>
{
    [Output(Guid = "486eb33f-111d-4d6e-b81e-9f2ca16cb729")]
    public readonly Slot<Texture2D> Output = new();


}