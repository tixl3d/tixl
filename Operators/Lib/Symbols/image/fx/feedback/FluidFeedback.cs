namespace Lib.image.fx.feedback;

[Guid("f9d453d1-04d9-43ef-9189-50008f93bcc2")]
internal sealed class FluidFeedback : Instance<FluidFeedback>
{
    [Output(Guid = "b9baba42-18b6-4792-929d-bf628ce8a488")]
    public readonly Slot<Texture2D> ColorBuffer = new();

        [Input(Guid = "ebd35d33-dc73-46ae-a82c-2060d750018a")]
        public readonly MultiInputSlot<T3.Core.DataTypes.Command> Command = new MultiInputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "a7669abe-65c7-4745-97a6-d0d80f6a3150")]
        public readonly InputSlot<float> Displacement = new InputSlot<float>();

        [Input(Guid = "78b314b8-f9d2-4723-9b11-c07ba926db86")]
        public readonly InputSlot<float> Shade = new InputSlot<float>();

        [Input(Guid = "8060756f-72a4-490b-9677-872b70e73b3a")]
        public readonly InputSlot<float> BlurRadius = new InputSlot<float>();

        [Input(Guid = "41c080de-575f-4041-b605-8f55e4bcf797")]
        public readonly InputSlot<float> SampleDistance = new InputSlot<float>();

        [Input(Guid = "297a2220-0648-47d3-82a1-c1077a1326a4")]
        public readonly InputSlot<float> Twist = new InputSlot<float>();

        [Input(Guid = "98662eab-90b9-4af1-8ff3-ba6709b5038e")]
        public readonly InputSlot<float> Zoom = new InputSlot<float>();

        [Input(Guid = "859977e7-afaf-44b5-956a-d42da972330c")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "4109af01-5c9a-4a9f-af7f-87fbcdcece3d")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "806221f8-6e31-45ec-b62e-5baac6c1fd54")]
        public readonly InputSlot<float> DisplaceOffset = new InputSlot<float>();

        [Input(Guid = "51621e59-9bdd-4004-a053-d4637278bd92")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "1f09da31-b853-417c-abb1-39e1199a149f")]
        public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();

}