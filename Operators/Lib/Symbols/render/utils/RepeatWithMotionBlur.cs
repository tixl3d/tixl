namespace Lib.render.utils;

[Guid("603c68a7-77e8-4b64-b4f3-d4423e654a38")]
internal sealed class RepeatWithMotionBlur : Instance<RepeatWithMotionBlur>
{

    [Output(Guid = "c8d4473b-2c94-413b-bd7c-2110c2b4a4aa")]
    public readonly Slot<Command> Output2 = new();

    [Input(Guid = "d64ec438-a6a5-4f8e-bd49-56abd4f245a0")]
    public readonly InputSlot<Command> SubGraph = new();

    [Input(Guid = "e819088b-9494-417b-bacd-8e4444472ed1")]
    public readonly InputSlot<int> Passes = new();

    [Input(Guid = "e39b807e-c1c1-41c8-8f13-a409d5ace983")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "7cd6fef4-d98b-444e-b821-abef39778564")]
    public readonly InputSlot<float> FadeAlpha = new();

}