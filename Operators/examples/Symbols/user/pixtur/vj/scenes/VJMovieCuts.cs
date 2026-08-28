namespace Examples.user.pixtur.vj.scenes;

[Guid("3570b49b-274c-4289-97fc-11a53b9184dc")]
public class VJMovieCuts : Instance<VJMovieCuts>
{
    [Output(Guid = "a0db06b3-6eca-46ae-b009-5082bba45225")]
    public readonly Slot<Command> Output = new Slot<Command>();


}