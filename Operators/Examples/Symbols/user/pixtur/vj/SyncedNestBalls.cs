namespace Examples.user.pixtur.vj;

[Guid("ff6d6856-c287-4f2c-9a9e-7dda8b4c2921")]
public class SyncedNestBalls : Instance<SyncedNestBalls>
{
    [Output(Guid = "44cef765-9aa9-48fd-89a8-79155dfb33ea")]
    public readonly Slot<Command> Output = new();


}