namespace Examples.user.pixtur.vj;

[Guid("8f1bc36a-0206-4c35-900d-ced5dd30481b")]
public class VJDeadlineAnimals : Instance<VJDeadlineAnimals>
{
    [Output(Guid = "e1a1bfcb-fb3c-4bc5-a335-14311122a45d")]
    public readonly Slot<Texture2D> Output = new();


}