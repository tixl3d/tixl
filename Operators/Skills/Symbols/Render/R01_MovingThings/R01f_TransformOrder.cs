namespace Skills.Render.R01_MovingThings;

[Guid("00fa848b-2e06-4c43-b823-570c61c85fef")]
internal sealed class R01f_TransformOrder :Instance<R01f_TransformOrder>{
    [Output(Guid = "5e20950a-caed-40dd-a58d-680c428f6b7d")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}