namespace Skills.Basics.B01_Welcome;

[Guid("123a1ff1-2935-4d9a-880f-897a7f8885ad")]
internal sealed class B01d_ConnectingValues : Instance<B01d_ConnectingValues>
{
    [Output(Guid = "23bc598c-e87d-4993-9e09-b4676e302e61")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}