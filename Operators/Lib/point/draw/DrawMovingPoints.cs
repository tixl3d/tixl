using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("ef938762-4341-42f4-b421-c14e162e159e")]
internal sealed class DrawMovingPoints : Instance<DrawMovingPoints>
{
    [Output(Guid = "bdde4e99-bce6-49eb-a8f9-05cdddf95c0b")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "5df235ec-5147-4e6b-bea3-8c2e76045c19")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "46559f84-2bcf-43e5-aae3-2f6ecf6a17f1")]
        public readonly InputSlot<float> PointSize = new InputSlot<float>();

        [Input(Guid = "7dae1a72-c664-400d-90df-bbcdc45d4fde", MappedType = typeof(ScaleFXModes))]
        public readonly InputSlot<int> ScaleFactor = new InputSlot<int>();

        [Input(Guid = "3a51a6e8-1beb-451b-8bba-f80501d9d48d")]
        public readonly InputSlot<bool> UsePointsScale = new InputSlot<bool>();

        [Input(Guid = "679b1d52-e806-468b-aa50-3a0a3b816f5a")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "ea88600c-6fc2-4f20-9969-cf3572bd011b", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "6d17692e-9d2b-4c20-bc0a-936b751a9a74")]
        public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

        [Input(Guid = "512596be-f344-44da-b210-c67068dbc1d4")]
        public readonly InputSlot<float> FadeNearest = new InputSlot<float>();

        [Input(Guid = "37a6d217-6042-4322-aadb-0b165982876d")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "c0aca5e3-1379-488d-a585-e981d49d9d1b")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "3f69d14e-29b4-4477-a80e-04d64050157e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "bb3706e6-9918-4500-a4ab-7e47a6c2b08b")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ColorField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "32aba77d-f7b6-4106-bb98-a7b654e21fc0")]
        public readonly InputSlot<float> VelocityJumpThreshold = new InputSlot<float>();

        [Input(Guid = "6f6087e6-b912-4431-a64f-36bf9061b9e8")]
        public readonly InputSlot<float> VelocityStretch = new InputSlot<float>();

        [Input(Guid = "768bc509-107f-48ab-a588-a36791edadb6")]
        public readonly InputSlot<float> VelocityThickness = new InputSlot<float>();
        
        private enum ScaleFXModes
        {
            None = 0,
            F1 = 1,
            F2 = 2,
        }
}