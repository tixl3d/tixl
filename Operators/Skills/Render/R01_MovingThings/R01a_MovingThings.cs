namespace Skills.Render.R01_MovingThings;

[Guid("ed778d74-6c42-4f64-bd0e-bb4953b260bf")]
internal sealed class R01a_MovingThings :Instance<R01a_MovingThings>{
    [Output(Guid = "2af0f74f-c0ee-4dcf-9f0e-9859055ec12a")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}