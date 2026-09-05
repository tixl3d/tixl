namespace Skills.Render.R01_MovingThings;

[Guid("2461dc98-f2e0-40be-a3dd-c618cc281d63")]
internal sealed class R01b_Transform :Instance<R01b_Transform>{
    [Output(Guid = "1351cc9b-05e8-4ed5-963b-e7029a673cc9")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}