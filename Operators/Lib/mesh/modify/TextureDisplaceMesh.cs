namespace Lib.mesh.modify;

[Guid("a368035f-2697-4ba5-a7bd-484eeb54c39b")]
internal sealed class TextureDisplaceMesh : Instance<TextureDisplaceMesh> ,ITransformable
{
    // [Output(Guid = "5092c4a1-8c57-42ef-834e-f4c50876a8ed")]
    // public readonly TransformCallbackSlot<T3.Core.DataTypes.BufferWithViews> OutBuffer = new TransformCallbackSlot<T3.Core.DataTypes.BufferWithViews>();

    [Output(Guid = "006AB203-705B-433E-ACBB-A51F9046F6D2")]
    public readonly TransformCallbackSlot<MeshBuffers> DisplacedMesh = new();

    public TextureDisplaceMesh()
    {
        DisplacedMesh.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "c965e214-9454-4b89-bc9c-4fe8dd97ff55")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "c4363e87-16fc-499b-8dcd-51dde1f079f6")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "ffc625ab-290e-4949-b22d-44c19a1f9cc4")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "24123f92-e11a-4918-8de2-0bc67c3d458b")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "88b8edf0-021c-40c0-b0f2-9edad50ab1ba")]
        public readonly InputSlot<System.Numerics.Vector3> TextureRotate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "163a697e-bb71-471c-adf5-1f449d036de7")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "e70ca887-655c-49a4-8fde-3cdd3a5ca3af")]
        public readonly InputSlot<System.Numerics.Vector3> AmountDistribution = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "0b4d328e-aaa4-48a5-b63d-95e3d9959274")]
        public readonly InputSlot<int> RotationSpace = new InputSlot<int>();

        [Input(Guid = "02dff73c-273e-4d4c-b6d1-47523673a6a8")]
        public readonly InputSlot<float> RotationLookupDistance = new InputSlot<float>();

        [Input(Guid = "89e061a1-878e-4731-9ef6-2440ca46e9b2")]
        public readonly InputSlot<bool> UseVertexSelection = new InputSlot<bool>();

        [Input(Guid = "e14d4065-e4d7-4137-8907-a198bba8665f")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> TextureMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "a218aab7-c638-4987-a64c-4543b52555ee")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

        [Input(Guid = "81f729bd-9e50-4f44-9b96-8792da2aa0b0")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new InputSlot<T3.Core.DataTypes.Texture2D>();


    private enum Attributes
    {
        NotUsed =0,
        For_X = 1,
        For_Y =2,
        For_Z =3,
        For_W =4,
        Rotate_X =5,
        Rotate_Y =6 ,
        Rotate_Z =7,
    }

    private enum Modes
    {
        Add,
        Multiply,
    }
        
    private enum Spaces
    {
        Object,
        Point,
    }

}