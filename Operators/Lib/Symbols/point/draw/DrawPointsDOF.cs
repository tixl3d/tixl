using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("dcd04bb7-4531-40ab-bb18-26a3bc269dc4")]
internal sealed class DrawPointsDOF : Instance<DrawPointsDOF>
{
    [Output(Guid = "48a1480e-888e-453b-ae2b-af9279c87c40")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "4d131da0-3a0b-4eb8-8040-24a5bd50bbbb")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "74ab516a-f694-45ba-af2c-4f7c7171a560")]
        public readonly InputSlot<float> PointSize = new InputSlot<float>();

        [Input(Guid = "aa874a05-b142-43c0-8db0-1bd6d57ecf19", MappedType = typeof(ScaleFXModes))]
        public readonly InputSlot<int> ScaleFactor = new InputSlot<int>();

        [Input(Guid = "dfe41042-6dad-46f7-9778-4de5d90a1400")]
        public readonly InputSlot<bool> UsePointsScale = new InputSlot<bool>();

        [Input(Guid = "ad390566-379e-499b-bec8-5b8d2360cc90")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "98cb7c7b-97c4-4352-87d7-8222f500c56b", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "9d0e1f78-cf04-4b3e-ab48-d81907650ace")]
        public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

        [Input(Guid = "539014bb-a672-4eaa-9c98-467b1f6ec81f")]
        public readonly InputSlot<float> FadeNearest = new InputSlot<float>();

        [Input(Guid = "f95cb054-e723-4ff1-bc6c-9b6efe7f5168")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "79f7f963-ea12-4ad2-ac4c-2e3ac6a8ed49")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "37e8a9f7-8496-4fca-90e9-b5ffc0cdf25e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "7f568085-7c31-4017-a1bc-638aa9f3a1ae")]
        public readonly InputSlot<System.Numerics.Vector2> DofShape = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "7e15e9be-6ec9-4975-af1d-9a3642bb5c70")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> InputField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "e285bab3-01cd-46ec-8f22-aa5ac6d5f5dd")]
        public readonly InputSlot<int> SortBucketCount = new InputSlot<int>();

        [Input(Guid = "8ae89721-6a8d-4d55-b742-736226dfa3a5")]
        public readonly InputSlot<float> BucketSize = new InputSlot<float>();
        
        private enum ScaleFXModes
        {
            None = 0,
            F1 = 1,
            F2 = 2,
        }
}