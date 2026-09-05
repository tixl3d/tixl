using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.point.generate{
    [Guid("24848671-a827-45f0-84f2-52efccd4fbd5")]
    internal sealed class PointInfoLines : Instance<PointInfoLines>
    {
        [Output(Guid = "eec67714-4eda-4410-afee-2076026191c7")]
        public readonly Slot<BufferWithViews> OutBuffer = new Slot<BufferWithViews>();


        [Input(Guid = "9054a475-05ba-47c6-a105-84cf55f3fdb8")]
        public readonly MultiInputSlot<BufferWithViews> Input = new MultiInputSlot<BufferWithViews>();

        [Input(Guid = "d039c1b4-77ca-4264-bf2c-6dddd4aaf831")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "75585b6b-67b9-43b1-bb6d-14e069ab29df")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "012bd45d-ecb4-49f5-970d-0d64d162bb6a", MappedType = typeof(ShowInfoModes))]
        public readonly InputSlot<int> ShownInfo = new InputSlot<int>();

        [Input(Guid = "4e557506-ebf0-4b3f-b70f-2054fe1cf14c")]
        public readonly InputSlot<bool> AsBillboards = new InputSlot<bool>();

        [Input(Guid = "88987121-8e52-4515-8d46-f2e7f493a47a")]
        public readonly InputSlot<int> Digits = new InputSlot<int>();

        [Input(Guid = "abbbfdba-c25d-4c8f-b5cf-9fb52853896a")]
        public readonly InputSlot<int> Precision = new InputSlot<int>();

        private enum ShowInfoModes
        {
            Position,
            Color,
            FX,
        }

    }
}

