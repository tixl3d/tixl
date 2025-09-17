namespace Examples.user.pixtur.fc4;

[Guid("995cc9f3-b23a-42fb-ac0d-3f1037e3aeaa")]
public class FC4_Memory : Instance<FC4_Memory>
{
    [Output(Guid = "5fb3fdc7-dd8e-4e48-a802-04cf3487e5eb")]
    public readonly Slot<Texture2D> ImgOutput = new();


}