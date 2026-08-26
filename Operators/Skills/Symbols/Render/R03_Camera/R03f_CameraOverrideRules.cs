namespace Skills.Render.R03_Camera;

[Guid("b8ff7779-3de5-4930-b9a7-26f1b3436479")]
internal sealed class R03f_CameraOverrideRules :Instance<R03f_CameraOverrideRules>{
    [Output(Guid = "8c7e5903-ce7e-45e7-b9e8-d3a33247be27")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}