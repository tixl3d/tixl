namespace Skills.Image.I02_Gradients;

[Guid("3442c4dd-fb4d-406c-b696-4a2830398395")]
internal sealed class I02b_GradientRotate : Instance<I02b_GradientRotate>
{
    [Output(Guid = "c97be600-8507-41bc-a65c-fc7e315d0894")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}