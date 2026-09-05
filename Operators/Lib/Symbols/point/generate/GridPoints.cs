namespace Lib.point.generate;

[Guid("e46b50ce-c84f-46bb-b8e7-dea2a64a1bdc")]
internal sealed class GridPoints : Instance<GridPoints>
,ITransformable
{

    [Output(Guid = "ac93edb1-0c2f-44ed-b8f1-07d8cb65c9ca")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

        
    public GridPoints()
    {
        OutBuffer.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "b071b537-5ee5-4bb6-bee5-b1e50183d93f")]
        public readonly InputSlot<int> CountX = new InputSlot<int>();

        [Input(Guid = "3a8a5f10-62ff-41e5-9aba-c265308cf508")]
        public readonly InputSlot<int> CountY = new InputSlot<int>();

        [Input(Guid = "761d5fce-a563-48e5-8adb-7eca1bc3a7cc")]
        public readonly InputSlot<int> CountZ = new InputSlot<int>();

        [Input(Guid = "a68007ea-ca2f-4a16-85f6-2cbfcd87f4d8", MappedType = typeof(SizeModes))]
        public readonly InputSlot<int> SizeMode = new InputSlot<int>();

        [Input(Guid = "6c383241-6807-4156-99ad-aa4dd22dfaf1")]
        public readonly InputSlot<System.Numerics.Vector3> Size = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "2e3f0337-c7c1-4dff-9a84-ab7e5fbc3d18")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "88793605-2c2a-43fb-9826-55db1152a716")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "c647e9b7-352c-4724-bede-49f634bd9cc8")]
        public readonly InputSlot<System.Numerics.Vector3> Pivot = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "92561930-2660-4b52-8002-4b9d4cdb3784", MappedType = typeof(Tilings))]
        public readonly InputSlot<int> Tiling = new InputSlot<int>();

        [Input(Guid = "bd0ebb71-65f0-477b-8352-20790919f751")]
        public readonly InputSlot<float> PointScale = new InputSlot<float>();

        [Input(Guid = "fb12b9be-fc8a-4c68-bd40-f6a0f04f3c38")]
        public readonly InputSlot<float> F1 = new InputSlot<float>();

        [Input(Guid = "48aae87f-0e7a-47aa-bd67-b4739a587a3d")]
        public readonly InputSlot<float> F2 = new InputSlot<float>();

        [Input(Guid = "bb24860b-8e08-4c98-8519-386c3f45c4d9")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "a3653d07-18b7-491d-8b19-dfb21c4a0683")]
        public readonly InputSlot<System.Numerics.Vector3> OrientationAxis = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "beb3eb7c-716c-421f-9d91-4d37c472bbbe")]
        public readonly InputSlot<float> OrientationAngle = new InputSlot<float>();

        [Input(Guid = "0bdd788b-e0ce-4f15-877c-6b8e3f0749dc")]
        public readonly InputSlot<float> W = new InputSlot<float>();

    private enum SizeModes
    {
        Cell,
        Bounds,
    }
        
    private enum Tilings
    {
        Cartesian,
        Triangular,
        HoneyCombs,
        Diagonal,
    }
}