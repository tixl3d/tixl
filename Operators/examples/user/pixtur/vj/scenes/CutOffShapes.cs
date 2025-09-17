namespace Examples.user.pixtur.vj.scenes;

[Guid("49bbae57-da13-4fe6-b28b-8878d63c1fee")]
public class CutOffShapes : Instance<CutOffShapes>
{
    [Output(Guid = "f22f02f7-1703-4e1e-8237-1d609b8f09d7")]
    public readonly Slot<Command> Output = new Slot<Command>();


}