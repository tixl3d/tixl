namespace Skills.Basics.B01_Welcome;

[Guid("4f9eb54f-1b81-4a6b-a842-f80c423e5843")]
internal sealed class B01e_SpeedItUp : Instance<B01e_SpeedItUp>
{
    [Output(Guid = "bfa820e6-ac48-41de-9303-d07d004744e1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}