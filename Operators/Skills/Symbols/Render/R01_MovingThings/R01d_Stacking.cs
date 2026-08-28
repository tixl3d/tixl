namespace Skills.Render.R01_MovingThings;

[Guid("93ce5fa6-b055-47e7-bd6f-5c5f7eb5362f")]
internal sealed class R01d_Stacking :Instance<R01d_Stacking>{
    [Output(Guid = "2cc138cf-420f-412a-94df-6d3c7378882d")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}