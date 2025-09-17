namespace Examples.user.pixtur.dailies;

[Guid("b01a8336-6dcc-45cd-a86b-a0880772f9a9")]
public class DirectionalBlurExample : Instance<DirectionalBlurExample>
{
    [Output(Guid = "623bd104-bf6f-416b-b9c0-d8022e87845e")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}