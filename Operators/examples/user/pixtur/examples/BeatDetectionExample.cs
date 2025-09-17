namespace Examples.user.pixtur.examples;

[Guid("9bf84a1e-e676-4b5d-9554-99cd26a9ccc1")]
public class BeatDetectionExample : Instance<BeatDetectionExample>
{
    [Output(Guid = "299efba2-9ec6-492e-809b-3597fd7fe04b")]
    public readonly Slot<Command> Output = new();


}