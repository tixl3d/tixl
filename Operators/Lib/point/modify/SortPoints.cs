using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.point.modify{
    [Guid("b5008d45-dbda-4f76-b7e9-e05357688d6c")]
    internal sealed class SortPoints :Instance<SortPoints>    {

        [Input(Guid = "5543e38e-5146-4b2b-831e-4cadded777e7")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "3123b47e-d18c-4d58-a4b1-a80fc37ebac4")]
        public readonly InputSlot<Object> CameraReference = new InputSlot<Object>();

        [Input(Guid = "959158f6-06db-487a-8c56-e47033536828")]
        public readonly InputSlot<float> SortingSpeed = new InputSlot<float>();

        [Input(Guid = "6834aef5-adbd-47ae-92b8-5526001b2b75")]
        public readonly InputSlot<bool> Ascending = new InputSlot<bool>();

        [Output(Guid = "629d9b6a-67bf-4b95-bb9f-acc8297da07c")]
        public readonly Slot<T3.Core.DataTypes.BufferWithViews> Output = new Slot<T3.Core.DataTypes.BufferWithViews>();

        [Output(Guid = "68e9614a-0fbf-4b5f-bbc5-a2289e6de5c3")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> DebugView = new Slot<T3.Core.DataTypes.Texture2D>();

    }
}

