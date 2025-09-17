namespace Examples.user.pixtur.GlobeControlTest;

[Guid("61f2cf41-b520-4830-bc59-0d9b1d226274")]
public class GlobeControlTest : Instance<GlobeControlTest>
{
    [Output(Guid = "f39e3eda-7a9e-4336-ab81-df2cd2a1c844")]
    public readonly Slot<Texture2D> ImgOutput = new();


}