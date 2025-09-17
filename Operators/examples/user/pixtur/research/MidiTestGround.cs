namespace Examples.user.pixtur.research;

[Guid("db162ead-71a4-4835-95c8-6a719511314e")]
public class MidiTestGround : Instance<MidiTestGround>
{
    [Output(Guid = "c7d58d1d-5444-4e89-975e-0b2ebb9a90cf")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}