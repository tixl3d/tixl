namespace Examples.Lib.image.use{
    [Guid("62df21a2-901e-4cb9-a5ed-84d555860852")]
    internal sealed class VideoDeviceInputExample : Instance<VideoDeviceInputExample>
    {
        [Output(Guid = "e435738b-84fa-4f32-822d-0315d8a8455d")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

