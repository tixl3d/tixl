namespace Examples.user.pixtur.vj;

[Guid("3ec672b7-f794-49a8-a5e1-e04c927f2ac5")]
public class AgroVisuals : Instance<AgroVisuals>
{
    [Output(Guid = "27b913ca-2ac8-4565-830b-c2cbb4783939")]
    public readonly Slot<Texture2D> Output = new();


}