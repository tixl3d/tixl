namespace Examples.user.pixtur.vj;

[Guid("a9e36415-58b3-4e2c-b42a-757000d5e337")]
public class VJUralt : Instance<VJUralt>
{
    [Output(Guid = "c4484c6f-8a38-4cea-b71f-bbff12718054")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}