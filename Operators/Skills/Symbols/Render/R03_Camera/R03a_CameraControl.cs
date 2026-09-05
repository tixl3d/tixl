namespace Skills.Render.R03_Camera;

[Guid("90aa02da-3d97-4e56-ab8f-7bf79d4bcd95")]
internal sealed class R03a_CameraControl :Instance<R03a_CameraControl>{
    [Output(Guid = "fb0ae9f8-a290-410a-8cee-9e5fb2ab72aa")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}