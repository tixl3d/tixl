namespace Skills.Render.R03_Camera;

[Guid("06de71db-842d-4f73-971c-ce9dd6437aea")]
internal sealed class R03d_CameraZoom :Instance<R03d_CameraZoom>{
    [Output(Guid = "1081da99-5e88-4d71-9233-2c4acd5953f9")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}