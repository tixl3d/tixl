namespace Skills.Image.I02_Gradients;

[Guid("0534ef90-6c31-4c9c-bf3a-f0f558fa61c9")]
internal sealed class I02c_GradientOffset : Instance<I02c_GradientOffset>
{
    [Output(Guid = "e0d084f0-65f5-4d20-8862-498a0ca199c2")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}