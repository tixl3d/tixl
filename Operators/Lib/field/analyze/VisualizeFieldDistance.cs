namespace Lib.field.analyze;

[Guid("89dd9ee0-8754-4e4c-abdc-74425c39dcc2")]
public class VisualizeFieldDistance : Instance<VisualizeFieldDistance>, ITransformable
{
    [Output(Guid = "a6fb7868-37ea-48c8-921a-aa097cca885c")]
    public readonly TransformCallbackSlot<Command> DrawCommand = new();

    public VisualizeFieldDistance()
    {
        DrawCommand.TransformableOp = this;
    }        
    
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    
        [Input(Guid = "cce60e8d-3254-4838-8120-d0c777f73b93")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "92d5e264-3842-4a8b-a9c0-2cd016189ea6")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "8f8cad00-c152-47e6-8a4c-55f88ab985e2")]
        public readonly InputSlot<System.Numerics.Vector3> Rotation = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "701747e9-fdf5-40ea-bbf5-4f4aa71c1d02")]
        public readonly InputSlot<System.Numerics.Vector2> Size = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "48359be0-336b-48da-954d-69c9cf3d69e4")]
        public readonly InputSlot<System.Numerics.Vector4> LineColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "0abd3f44-27f3-4f4c-a56b-e1fbfcb32cb6")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "819710f4-6f09-4e23-8bee-be6f9c777e06")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "5fc455af-f66a-4f04-8cc2-0498ee479f26")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();
}