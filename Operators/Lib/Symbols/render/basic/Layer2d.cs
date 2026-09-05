using T3.Core.Utils;

namespace Lib.render.basic;

[Guid("d8c5330f-59b5-4907-b845-a02def3042fa")]
internal sealed class Layer2d : Instance<Layer2d>, ITransformable
{
    [Output(Guid = "e4a8d926-7abd-4d2a-82a1-b7d140cb457f")]
    public readonly TransformCallbackSlot<Command> Output = new();

    public Layer2d()
    {
        Output.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "2a95ac54-5ef7-4d3c-a90b-ecd5b422bddc")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "a384be77-c5fc-47b3-9ec3-960db9f9bae9")]
        public readonly InputSlot<System.Numerics.Vector2> Position = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "4ac0a4d8-367c-4ece-9c1d-7abfbb2bfe27")]
        public readonly InputSlot<float> PositionZ = new InputSlot<float>();

        [Input(Guid = "da0941c9-c700-4552-9d8a-799bf7a08826")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "4618d8e0-2718-4373-a071-88ec1843b0c8")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "38f34034-b36f-4351-84e1-1a4f96e03fc6")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "1d9ccc5d-bed4-4d07-b664-0903442e4f58", MappedType = typeof(ScaleModes))]
        public readonly InputSlot<int> ScaleMode = new InputSlot<int>();

        [Input(Guid = "ed4f8c30-7b71-4649-97e6-710a718039b0")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "c8d0e44e-acd1-4045-b565-e1ce0b722e70", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "a83bdfb1-c92d-4844-9e43-8ce09959fae9")]
        public readonly InputSlot<bool> EnableDepthTest = new InputSlot<bool>();

        [Input(Guid = "022f1959-a62b-49de-817a-3930bc8ec32b")]
        public readonly InputSlot<bool> EnableDepthWrite = new InputSlot<bool>();

        [Input(Guid = "b646098a-b62c-44dd-b6f7-f8d4821b0c12")]
        public readonly InputSlot<SharpDX.Direct3D11.Filter> Filtering = new InputSlot<SharpDX.Direct3D11.Filter>();

    public enum ScaleModes
    {
        FitHeight,
        FitWidth,
        FitBoth,
        Cover,
        Stretch,
        MatchPixelResolution,
    }
        
}