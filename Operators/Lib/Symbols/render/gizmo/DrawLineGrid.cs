namespace Lib.render.gizmo;

[Guid("296dddbd-250b-4801-a039-bcb3cd555774")]
internal sealed class DrawLineGrid : Instance<DrawLineGrid>
{
    [Output(Guid = "d542b5bf-5e9b-4beb-8cbf-f2fff294423f")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "5767a3be-9ac1-4ad5-9529-aea00d3143ea")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "0480e529-b790-4c6f-a993-2efdbfda35e4")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "a4ea3140-3397-4989-98ee-3cf02d11f242")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "0b8c7835-078c-4990-8db5-edccd26018c9")]
    public readonly InputSlot<int> BlendMod = new InputSlot<int>();

    [Input(Guid = "5e7da35a-6537-4fc1-9f23-faab82c8eeaa")]
    public readonly InputSlot<Int2> Segments = new InputSlot<Int2>();

        [Input(Guid = "80736d45-85dd-4f9e-bc93-97c8c4e9c362")]
        public readonly InputSlot<int> Orientation = new InputSlot<int>();

        [Input(Guid = "ee938995-46a3-49dd-a791-d15f4045310b")]
        public readonly InputSlot<bool> ShowAxis = new InputSlot<bool>();

        [Input(Guid = "2679515c-72d3-4cae-a14c-b68f007dfb7c")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

}