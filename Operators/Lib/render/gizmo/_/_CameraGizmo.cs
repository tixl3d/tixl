namespace Lib.render.gizmo.@_;

[Guid("b601be85-fa85-4358-9541-7e97341bf6b3")]
internal sealed class _CameraGizmo : Instance<_CameraGizmo>
{
    [Output(Guid = "a9f5c041-525b-44f6-a5e4-39bf11456228")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "7fff0c87-f14b-4dbf-9101-8a746b9959e7")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "b2a6964d-849c-4b0d-882b-08d2376f1054")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();

        [Input(Guid = "d75731a1-6826-44bf-8f2b-a684ef36e48f")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();


}