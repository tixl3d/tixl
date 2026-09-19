namespace Examples.user.pixtur.examples;

[Guid("cf1fdb46-72c0-4e3c-a00d-edef25ae955a")]
public class FluidFireLogo : Instance<FluidFireLogo>
{
    [Output(Guid = "80df5695-1d1e-46f3-ac5f-89a4294317d7")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}