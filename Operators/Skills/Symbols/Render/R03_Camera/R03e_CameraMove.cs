namespace Skills.Render.R03_Camera;

[Guid("53f827df-47e8-460b-94d4-e924219b8f98")]
internal sealed class R03e_CameraMove :Instance<R03e_CameraMove>{
    [Output(Guid = "df49c430-20aa-40d0-bbe8-eae8d126d76a")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}