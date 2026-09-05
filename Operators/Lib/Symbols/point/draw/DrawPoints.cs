using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("ffd94e5a-bc98-4e70-84d8-cce831e6925f")]
internal sealed class DrawPoints : Instance<DrawPoints>
{
    [Output(Guid = "b73347d9-9d9f-4929-b9df-e2d6db722856")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "5df18658-ef86-4c0f-8bb4-4ac3fbbf9a33")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "414c8045-5086-4449-9d9a-03f28c3966b3")]
        public readonly InputSlot<float> PointSize = new InputSlot<float>();

        [Input(Guid = "e40aeedd-49fe-467c-b886-064a1024cef3", MappedType = typeof(ScaleFXModes))]
        public readonly InputSlot<int> ScaleFactor = new InputSlot<int>();

        [Input(Guid = "64e75fea-c07f-4cb1-8ac0-0f8d05362664")]
        public readonly InputSlot<bool> UsePointsScale = new InputSlot<bool>();

        [Input(Guid = "cc442161-e9ca-40ea-be3b-f87189d4e155")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "8fab9161-48d4-43b0-a18f-5942b7071a49", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "cce4048d-85b7-4d82-aec1-4379d1f0de61")]
        public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

        [Input(Guid = "3fbad175-6060-40f2-a675-bdae20107698")]
        public readonly InputSlot<float> FadeNearest = new InputSlot<float>();

        [Input(Guid = "814fc516-250f-4383-8f20-c2a358bbe4e1")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "7acc95ad-d317-42fc-97f8-85f48d7e516f")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "850e3a32-11ba-4ad2-a2b1-6164f077ddd6")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "fe99f40a-607a-4f62-9949-17f4715db859")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ColorField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();
        
        private enum ScaleFXModes
        {
            None = 0,
            F1 = 1,
            F2 = 2,
        }
}