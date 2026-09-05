namespace Lib.render.shading;

[Guid("f3b66187-34b2-4018-8380-279f9f296ded")]
internal sealed class SetEnvironment : Instance<SetEnvironment>
{
    [Output(Guid = "1f8cbdfd-ffcd-4604-b4b4-5f1184daf138")]
    public readonly Slot<Command> Output = new();


    [Input(Guid = "e4482cab-f20c-4f9f-9179-c64944164509")]
    public readonly InputSlot<Command> SubTree = new();

    [Input(Guid = "5c042a08-74b3-4a6b-a420-2fcfa0fc01ee")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "c3c815fa-8672-4d99-99a7-986844f2fc45")]
    public readonly InputSlot<bool> UpdateLive = new();

    [Input(Guid = "71c54c8e-a95f-47e8-b126-0cdaa89ae49b")]
    public readonly InputSlot<float> Exposure = new();

    [Input(Guid = "4f573afe-8815-4fd3-a655-89ec40bf3c22")]
    public readonly InputSlot<bool> RenderBackground = new();

    [Input(Guid = "96094239-9d82-4a32-bbb0-e9da7f6501da")]
    public readonly InputSlot<float> BackgroundBlur = new();

    [Input(Guid = "650aa9a6-4aa6-4928-be76-3f1f825aa773")]
    public readonly InputSlot<Vector4> BackgroundColor = new();

    [Input(Guid = "0299761d-7397-4a2f-b591-81fadb404a92")]
    public readonly InputSlot<float> BackgroundDistance = new();

    [Input(Guid = "46d76d8a-5fb6-4138-a88b-950a2e5b8529")]
    public readonly InputSlot<float> QualityFactor = new InputSlot<float>();

        [Input(Guid = "486f4f09-2e4e-43b8-bfbc-2722e77d5dbd")]
        public readonly InputSlot<float> Orientation = new InputSlot<float>();

        [Input(Guid = "de6b9cb3-69ce-4b23-add6-a564648ec3a8", MappedType = typeof(Fallbacks))]
        public readonly InputSlot<int> Fallback = new InputSlot<int>();

        private enum Fallbacks
        {
            Studio,
            Cathedral,
            Black,
        }
}