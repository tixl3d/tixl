namespace Lib.io.dmx.helpers{
    [Guid("f9c60c86-1ee0-4fa9-bcdb-19ef8abe9922")]
    internal sealed class VisualizeSpotLights : Instance<VisualizeSpotLights>
    {
        [Output(Guid = "79f0a428-8064-4998-b387-05c2d3505775")]
        public readonly Slot<Command> Output = new Slot<Command>();


        [Input(Guid = "4e0b3a38-9459-4479-9f9d-2be3538f1475")]
        public readonly InputSlot<BufferWithViews> GPoints = new InputSlot<BufferWithViews>();

        [Input(Guid = "269f9604-9ded-42cf-a543-bb114fb75420")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "238dac3a-f24a-4f65-a3ae-6a13d4ffcace")]
        public readonly InputSlot<bool> ShowBody = new InputSlot<bool>();

        [Input(Guid = "7fc925f9-2f0a-4146-94f7-d48a7abd80bf")]
        public readonly InputSlot<bool> VisualizeAxis = new InputSlot<bool>();

        [Input(Guid = "7ae45928-69dc-47a1-8302-77b9a50f57bf")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

    }
}

