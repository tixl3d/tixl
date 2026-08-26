namespace Skills.Render.R06_ShadingAndLight;

[Guid("bd866a72-9451-4e85-aa49-1a08b78ec87d")]
internal sealed class R06c_CameraandEnvironmentOrder :Instance<R06c_CameraandEnvironmentOrder>{
    [Output(Guid = "058e963a-f110-4592-b986-c4417844fb30")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}