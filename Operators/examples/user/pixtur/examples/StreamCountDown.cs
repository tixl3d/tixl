namespace Examples.user.pixtur.examples;

[Guid("135fd07f-b9a1-47d9-acc5-c8a15ae7558a")]
public class StreamCountDown : Instance<StreamCountDown>
{
    [Output(Guid = "2151f4b3-34fe-4d55-9669-ec3aea47a44c")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}