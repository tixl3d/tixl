namespace Skills.Basics.B01_Welcome;

[Guid("c0065a1d-091a-4fc4-ad36-4430b90d29d4")]
internal sealed class B01a_Connection1 : Instance<B01a_Connection1>
{
    [Output(Guid = "38ba6678-687d-4c6d-821a-ecf2cdea75c5")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}