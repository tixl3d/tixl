namespace Skills.Render.R01_MovingThings;

[Guid("4f25fbc7-e5c1-470e-940a-b6f2e3c2afc6")]
internal sealed class R01h_Repeating :Instance<R01h_Repeating>{
    [Output(Guid = "ae157c00-9149-42f2-a8c5-8af2f135c7ef")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}