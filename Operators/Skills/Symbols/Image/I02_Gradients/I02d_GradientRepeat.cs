namespace Skills.Image.I02_Gradients;

[Guid("7cdd542a-39ab-4aec-bfdb-dffa86c0c18d")]
internal sealed class I02d_GradientRepeat : Instance<I02d_GradientRepeat>
{
    [Output(Guid = "5ac5edc2-9aca-4d5b-9382-84eb98a2c225")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}