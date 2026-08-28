namespace Lib.flow;

[Guid("6e67d136-9285-48c6-a557-0a07361fc847")]
internal sealed class BlendScenes : Instance<BlendScenes>
{
    [Output(Guid = "bb6e9504-2e2c-413a-a455-5dd4ef41e3cb")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "998b7cbc-b54c-4e81-bc61-31a7e05ce8e0")]
    public readonly MultiInputSlot<Command> Scenes = new();

    [Input(Guid = "ce0e7890-b25c-45bd-8d7f-20865cc0a51a")]
    public readonly InputSlot<float> BlendFraction = new();


}