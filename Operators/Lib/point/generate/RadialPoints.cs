namespace Lib.point.generate;

[Guid("3352d3a1-ab04-4d0a-bb43-da69095b73fd")]
internal sealed class RadialPoints : Instance<RadialPoints>
                           ,ITransformable
{

    [Output(Guid = "d7605a96-adc6-4a2b-9ba4-33adef3b7f4c")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public RadialPoints()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "b654ffe2-d46e-4a62-89b3-a9692d5c6481")]
        public readonly InputSlot<int> Count = new InputSlot<int>();

        [Input(Guid = "acce4779-56d6-47c4-9c52-874fca91a3a1")]
        public readonly InputSlot<float> Radius = new InputSlot<float>();

        [Input(Guid = "13cbb509-f90c-4ae7-a9d3-a8fc907794e3")]
        public readonly InputSlot<float> OffsetRadius = new InputSlot<float>();

        [Input(Guid = "ca84209e-d821-40c6-b23c-38fc4bbd47b0")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "6df5829e-a534-4620-bcd5-9324f94b4f54")]
        public readonly InputSlot<System.Numerics.Vector3> Axis = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "f6986f68-851b-4cd1-ae59-bf189aa1698e")]
        public readonly InputSlot<System.Numerics.Vector3> OffsetCenter = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "5a3347a2-ba87-4b38-a1a8-94bd0ef70f48")]
        public readonly InputSlot<float> StartAngle = new InputSlot<float>();

        [Input(Guid = "94b2a118-f760-4043-933c-31283e6e7006")]
        public readonly InputSlot<float> Rotations = new InputSlot<float>();

        [Input(Guid = "ef8d1fe2-8470-4113-8d20-40a92d0dab97")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "76124db6-4b89-4d7c-bd25-2ebf95b1c141")]
        public readonly InputSlot<bool> CloseCircleLine = new InputSlot<bool>();

        [Input(Guid = "bf66627b-9228-4763-99be-5196ad9c8eb3")]
        public readonly InputSlot<System.Numerics.Vector2> Scale = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "56b8aabc-2abf-49c7-ba1a-945a7bcf0c53")]
        public readonly InputSlot<System.Numerics.Vector2> F1 = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "5e0ff702-687e-4892-9a86-29f356d7f6bd")]
        public readonly InputSlot<System.Numerics.Vector2> F2 = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "5f5394b4-b23e-41bf-8089-b5c063623e66")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "01a62754-7629-487d-a43a-f0cd2fbfafce")]
        public readonly InputSlot<System.Numerics.Vector3> OrientationAxis = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "cd917c3d-489e-4e4d-b5dc-eacc846d82ef")]
        public readonly InputSlot<float> OrientationAngle = new InputSlot<float>();

        [Input(Guid = "acabe2d8-f34c-45cd-b488-2f95e76c23d7", MappedType = typeof(OrientationModes))]
        public readonly InputSlot<int> OrientationMode = new InputSlot<int>();

        private enum OrientationModes
        {
            Classic,
            AlignedToCurvature,
        }
}